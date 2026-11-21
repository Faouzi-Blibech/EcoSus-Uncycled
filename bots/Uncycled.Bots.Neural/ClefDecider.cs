using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// Asks Cloudflare Clef-flash (Workers AI) to choose what a bot says and votes: fixed-option questions
    /// in, a choice per question out, mapped to a catalogue phrase by a fixed table and grounded. Never
    /// writes text. Any failure (no budget, timeout, 401, 429, 5xx, bad answer, network) is answered by the
    /// rule decider for that call, and a rejected token switches the model off after one log line.
    /// The API token only ever goes in the Authorization header and is never logged.
    /// </summary>
    public sealed class ClefDecider : IMeetingDecider
    {
        public const string AccountEnv = "CLOUDFLARE_ACCOUNT_ID";
        public const string TokenEnv = "CLOUDFLARE_API_TOKEN";
        public const string SourceName = "clef";

        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

        /// <summary>
        /// A "wait" answer still counts as speak when the model's own speak probability is at least this. Measured on the real
        /// model (2026-11-21): it answers wait at 0.2-0.3 speak on ordinary table talk, so its argmax alone kept every bot silent.
        /// The talker's pacing (a try every 6-15 s, 3 lines at most) turns this into about one line per bot per meeting.
        /// </summary>
        public const double SpeakProbabilityFloor = 0.2;

        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _token;
        private readonly DailyBudget _budget;
        private readonly MeetingDecisionLog _log;
        private readonly TimeSpan _timeout;
        private int _authFailed;

        public ClefDecider(HttpClient http, string accountId, string apiToken, DailyBudget budget, MeetingDecisionLog log, TimeSpan timeout)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _url = "https://api.cloudflare.com/client/v4/accounts/" + Uri.EscapeDataString(accountId) + "/ai/run/@cf/cloudflare/clef-flash";
            _token = apiToken;
            _budget = budget ?? throw new ArgumentNullException(nameof(budget));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _timeout = timeout;
        }

        public string Name => SourceName;

        /// <summary>A decider from CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN; null when either is empty.</summary>
        public static ClefDecider? FromEnvironment(HttpClient http, DailyBudget budget, MeetingDecisionLog log, TimeSpan? timeout = null, Func<string, string?>? getEnv = null)
        {
            Func<string, string?> env = getEnv ?? Environment.GetEnvironmentVariable;
            string? account = env(AccountEnv);
            string? token = env(TokenEnv);
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            return new ClefDecider(http, account.Trim(), token.Trim(), budget, log, timeout ?? DefaultTimeout);
        }

        public async Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            if (Volatile.Read(ref _authFailed) != 0)
            {
                return Fallback(state, "auth_off", sw);
            }

            if (!_budget.TryConsume())
            {
                return Fallback(state, "budget", sw);
            }

            try
            {
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    limit.CancelAfter(_timeout);
                    using (var req = new HttpRequestMessage(HttpMethod.Post, _url))
                    {
                        req.Content = new StringContent(ClefRequest.Build(state), Encoding.UTF8, "application/json");
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                        using (HttpResponseMessage res = await _http.SendAsync(req, limit.Token).ConfigureAwait(false))
                        {
                            if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                            {
                                if (Interlocked.Exchange(ref _authFailed, 1) == 0)
                                {
                                    _log.Note("Clef-flash answered " + (int)res.StatusCode + " (token rejected): the model is off until the server restarts, bots use the rule path");
                                }

                                return Fallback(state, "http_" + (int)res.StatusCode, sw);
                            }

                            if (!res.IsSuccessStatusCode)
                            {
                                return Fallback(state, "http_" + (int)res.StatusCode, sw);
                            }

                            string json = await res.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
                            MeetingDecision? d = Parse(json, state);
                            if (d == null)
                            {
                                return Fallback(state, "bad_answer", sw);
                            }

                            d = Grounding.Check(d, state);
                            d.Source = SourceName;
                            d.LatencyMs = sw.ElapsedMilliseconds;
                            return d;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return Fallback(state, ct.IsCancellationRequested ? "late" : "timeout", sw);
            }
            catch (HttpRequestException)
            {
                return Fallback(state, "network", sw);
            }
            catch (Exception)
            {
                return Fallback(state, "error", sw);
            }
        }

        private MeetingDecision Fallback(MeetingState state, string reason, Stopwatch sw)
        {
            MeetingDecision d = RuleDecider.Decide(state);
            d.Reason = reason;
            d.LatencyMs = sw.ElapsedMilliseconds;
            return d;
        }

        // ---------- answer ----------

        private static string? Choice(JsonElement answers, string qid)
        {
            if (answers.TryGetProperty(qid, out JsonElement a) && a.ValueKind == JsonValueKind.Object
                && a.TryGetProperty("choice", out JsonElement c) && c.ValueKind == JsonValueKind.String)
            {
                return c.GetString();
            }

            return null;
        }

        /// <summary>The model's probability for "speak" (0 when the answer carries none).</summary>
        private static double SpeakProbability(JsonElement answers)
        {
            if (answers.TryGetProperty(ClefRequest.QSpeak, out JsonElement a) && a.ValueKind == JsonValueKind.Object
                && a.TryGetProperty("probabilities", out JsonElement p) && p.ValueKind == JsonValueKind.Object
                && p.TryGetProperty("speak", out JsonElement v) && v.ValueKind == JsonValueKind.Number)
            {
                return v.GetDouble();
            }

            return 0.0;
        }

        private static MeetingDecision? Parse(string json, MeetingState s)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("result", out JsonElement result)
                    || !result.TryGetProperty("answers", out JsonElement answers))
                {
                    return null;
                }

                // every choice must be one of the options that were sent; anything else is a bad answer
                // (the rule path decides), never a silent skip or silence
                if (s.Stage == MeetingStage.Voting)
                {
                    string? vote = Choice(answers, ClefRequest.QVote);
                    if (vote == null)
                    {
                        return null;
                    }

                    if (vote == ClefRequest.Skip)
                    {
                        return new MeetingDecision { SkipVote = true, Source = SourceName };
                    }

                    byte? voted = SeatOf(vote);
                    if (voted == null || voted == s.Self || !s.IsLiving(voted.Value))
                    {
                        return null;
                    }

                    return new MeetingDecision { Vote = voted, Source = SourceName };
                }

                string? speak = Choice(answers, ClefRequest.QSpeak);
                string? kind = Choice(answers, ClefRequest.QKind);
                string? seatKey = Choice(answers, ClefRequest.QSeat);
                string? zoneKey = Choice(answers, ClefRequest.QZone);
                if (speak == null || kind == null || seatKey == null || zoneKey == null)
                {
                    return null;
                }

                if ((speak != "speak" && speak != "wait") || Array.IndexOf(ClefRequest.Kinds, kind) < 0)
                {
                    return null;
                }

                byte? target = null;
                if (seatKey != ClefRequest.None)
                {
                    target = SeatOf(seatKey);
                    if (target == null || !s.IsLiving(target.Value))
                    {
                        return null;
                    }
                }

                string? zone = null;
                if (zoneKey != ClefRequest.None)
                {
                    if (!ZoneOnMap(s, zoneKey))
                    {
                        return null;
                    }

                    zone = zoneKey;
                }

                if (!(speak == "speak" || SpeakProbability(answers) >= SpeakProbabilityFloor) || kind == "quiet")
                {
                    return MeetingDecision.Quiet(SourceName);
                }

                return Phrase(kind, target, zone, s);
            }
        }

        /// <summary>The seat of an exact "seat_N" key, else null.</summary>
        private static byte? SeatOf(string? key)
        {
            if (key != null && key.StartsWith("seat_", StringComparison.Ordinal) && byte.TryParse(key.Substring(5), out byte seat) && ClefRequest.SeatKey(seat) == key)
            {
                return seat;
            }

            return null;
        }

        private static bool ZoneOnMap(MeetingState s, string zone)
        {
            foreach (string z in s.Zones)
            {
                if (z == zone)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The fixed table from the model's (kind, seat, zone) to a catalogue phrase; Grounding checks it next.</summary>
        public static MeetingDecision Phrase(string kind, byte? seat, string? zone, MeetingState s)
        {
            switch (kind)
            {
                case "accuse":
                    if (seat == null)
                    {
                        return MeetingDecision.Quiet(SourceName);
                    }

                    return zone == null
                        ? MeetingDecision.Say(SourceName, "sus", seat)
                        : MeetingDecision.Say(SourceName, AccuseId(s, seat.Value, zone), seat, zone);

                case "alibi":
                    return seat != null && seat != s.Self && zone != null
                        ? MeetingDecision.Say(SourceName, "with", seat, zone)
                        : MeetingDecision.Say(SourceName, "i_was", null, zone);

                case "question":
                    return seat != null ? MeetingDecision.Say(SourceName, "where", seat) : MeetingDecision.Quiet(SourceName);

                case "react":
                    if (s.AccusedInChat() && (seat == null || Accused(s, seat.Value)))
                    {
                        return MeetingDecision.Say(SourceName, "not_me");
                    }

                    if (seat == null)
                    {
                        return MeetingDecision.Quiet(SourceName);
                    }

                    return MeetingDecision.Say(SourceName, Accuses(s, seat.Value) ? "agree" : "doubt", seat);

                case "vote_talk":
                    return seat != null ? MeetingDecision.Say(SourceName, "vote", seat) : MeetingDecision.Say(SourceName, "skip_no_proof");

                default:
                    return MeetingDecision.Quiet(SourceName);
            }
        }

        /// <summary>saw_dump, saw_hit or near_pile from the evidence held about the seat; a polluter with none lies with saw_dump.</summary>
        private static string AccuseId(MeetingState s, byte seat, string zone)
        {
            string? other = null;
            foreach (EvidenceItem e in s.Evidence)
            {
                if (e.Seat != seat || e.Kind == EvidenceItem.MyZone)
                {
                    continue;
                }

                if (e.Zone == zone)
                {
                    return e.Kind;
                }

                other ??= e.Kind;
            }

            if (other != null)
            {
                return other;
            }

            return s.Role == Role.Polluter ? "saw_dump" : "sus";
        }

        private static bool Accused(MeetingState s, byte seat)
        {
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker == seat && a.Kind == "accuse" && a.Seat == s.Self)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Accuses(MeetingState s, byte seat)
        {
            foreach (ChatAct a in s.Chat)
            {
                if (a.Speaker == seat && a.Kind == "accuse")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
