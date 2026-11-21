using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;
using Uncycled.Bots.Neural;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class ClefDeciderTests
    {
        private const string Account = "acc123";
        private const string Token = "tok-secret";

        private static readonly string[] MapZones = { "depot", "market", "garden" };

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> _respond;

            public FakeHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> respond)
            {
                _respond = respond;
            }

            public List<string> Bodies { get; } = new List<string>();

            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

            public bool SawCancel { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
                lock (Bodies)
                {
                    Bodies.Add(body);
                    Requests.Add(request);
                }

                ct.Register(() => SawCancel = true);
                return await _respond(request, body, ct);
            }
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        private static string Answers(params (string qid, string choice)[] answers)
        {
            var sb = new StringBuilder("{\"success\":true,\"result\":{\"answers\":{");
            sb.Append(string.Join(",", answers.Select(a => "\"" + a.qid + "\":{\"choice\":\"" + a.choice + "\",\"probabilities\":{\"" + a.choice + "\":0.9},\"confidence\":0.9}")));
            sb.Append("}}}");
            return sb.ToString();
        }

        private sealed class Rig
        {
            public Rig(FakeHandler handler, int cap = 100, TimeSpan? timeout = null, Func<DateTime>? clock = null)
            {
                Handler = handler;
                Lines = new List<string>();
                Log = new MeetingDecisionLog(l => { lock (Lines) { Lines.Add(l); } });
                Budget = new DailyBudget(cap, clock ?? (() => new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc)));
                Decider = new ClefDecider(new HttpClient(handler), Account, Token, Budget, Log, timeout ?? ClefDecider.DefaultTimeout);
            }

            public FakeHandler Handler { get; }

            public List<string> Lines { get; }

            public MeetingDecisionLog Log { get; }

            public DailyBudget Budget { get; }

            public ClefDecider Decider { get; }

            public List<JsonElement> Records()
            {
                lock (Lines)
                {
                    return Lines.Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
                }
            }
        }

        private static MeetingState State(
            Role role = Role.Citizen,
            MeetingStage stage = MeetingStage.Discussion,
            byte[]? partners = null,
            IEnumerable<EvidenceItem>? evidence = null,
            Dictionary<byte, float>? suspicion = null)
        {
            return new MeetingState
            {
                MatchId = "ROOM1",
                Self = 0,
                Role = role,
                Partners = partners ?? new byte[0],
                Living = new byte[] { 0, 1, 2, 3, 4 },
                Stage = stage,
                StageLeft = 30f,
                Evidence = (evidence ?? new EvidenceItem[0]).ToList(),
                Chat = new List<ChatAct> { new ChatAct(2, "sus", "accuse", 3, null, 5f) },
                Suspicion = suspicion ?? new Dictionary<byte, float>(),
                Heat = new Dictionary<byte, float>(),
                Zones = MapZones,
            };
        }

        private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static string[] Keys(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        [Fact]
        public async Task Clef_BuildsTheCheckedRequestShape()
        {
            var handler = new FakeHandler((req, body, ct) =>
            {
                JsonElement q = Parse(body).GetProperty("questions");
                var answers = q.EnumerateObject().Select(p => (p.Name, p.Name == "vote" ? "skip" : p.Name == "speak" ? "wait" : p.Name == "kind" ? "quiet" : "none")).ToArray();
                return Task.FromResult(Json(Answers(answers)));
            });
            var rig = new Rig(handler);
            var evidence = new[] { new EvidenceItem(EvidenceItem.SawDump, 3, "depot", 50f) };

            await rig.Decider.DecideAsync(State(evidence: evidence), CancellationToken.None);

            HttpRequestMessage req0 = handler.Requests[0];
            Assert.Equal(HttpMethod.Post, req0.Method);
            Assert.Equal("https://api.cloudflare.com/client/v4/accounts/" + Account + "/ai/run/@cf/cloudflare/clef-flash", req0.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Equal("Bearer", req0.Headers.Authorization!.Scheme);
            Assert.Equal(Token, req0.Headers.Authorization.Parameter);

            JsonElement root = Parse(handler.Bodies[0]);
            Assert.Equal("clef-flash", root.GetProperty("model").GetString());
            JsonElement questions = root.GetProperty("questions");
            Assert.Equal(new[] { "kind", "seat", "speak", "zone" }, Keys(questions));
            foreach (JsonProperty p in questions.EnumerateObject())
            {
                Assert.Equal("choice", p.Value.GetProperty("type").GetString());
                Assert.False(string.IsNullOrWhiteSpace(p.Value.GetProperty("instructions").GetString()));
            }

            Assert.Equal(new[] { "seat_0", "seat_1", "seat_2", "seat_3", "seat_4", "none" }.OrderBy(x => x, StringComparer.Ordinal), Keys(questions.GetProperty("seat").GetProperty("criteria")));
            Assert.Equal(new[] { "depot", "garden", "market", "none" }, Keys(questions.GetProperty("zone").GetProperty("criteria")));
            Assert.Equal(new[] { "accuse", "alibi", "question", "quiet", "react", "vote_talk" }, Keys(questions.GetProperty("kind").GetProperty("criteria")));
            Assert.Equal(new[] { "speak", "wait" }, Keys(questions.GetProperty("speak").GetProperty("criteria")));

            // the state holds this bot's own facts only: a citizen carries no partners, the evidence is just what it holds
            JsonElement state = root.GetProperty("state");
            Assert.Equal(new[] { "chat", "clues", "evidence", "heat", "living", "me", "seconds_left", "situation", "stage", "suspicion" }, Keys(state));
            Assert.False(state.GetProperty("me").TryGetProperty("partners", out _));
            Assert.DoesNotContain("partners", handler.Bodies[0]);
            Assert.Equal(1, state.GetProperty("evidence").GetArrayLength());
            Assert.Equal(1, state.GetProperty("chat").GetArrayLength());

            // voting: only the vote question, living seats except self, plus skip
            await rig.Decider.DecideAsync(State(stage: MeetingStage.Voting), CancellationToken.None);
            JsonElement voting = Parse(handler.Bodies[1]).GetProperty("questions");
            Assert.Equal(new[] { "vote" }, Keys(voting));
            Assert.Equal(new[] { "seat_1", "seat_2", "seat_3", "seat_4", "skip" }, Keys(voting.GetProperty("vote").GetProperty("criteria")));

            // a polluter's own state does carry its partners
            await rig.Decider.DecideAsync(State(Role.Polluter, partners: new byte[] { 4 }), CancellationToken.None);
            Assert.Equal(4, Parse(handler.Bodies[2]).GetProperty("state").GetProperty("me").GetProperty("partners")[0].GetInt32());
        }

        [Fact]
        public async Task Clef_ParsesChoiceIntoDecision()
        {
            string reply = "";
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json(reply)));
            var rig = new Rig(handler);
            var evidence = new[] { new EvidenceItem(EvidenceItem.SawDump, 3, "depot", 50f) };

            reply = Answers(("speak", "speak"), ("kind", "accuse"), ("seat", "seat_3"), ("zone", "depot"));
            MeetingDecision say = await rig.Decider.DecideAsync(State(evidence: evidence), CancellationToken.None);
            Assert.True(say.Speak);
            Assert.Equal("saw_dump", say.PhraseId);
            Assert.Equal((byte?)3, say.Seat);
            Assert.Equal("depot", say.Zone);
            Assert.Equal("clef", say.Source);

            reply = Answers(("speak", "wait"), ("kind", "accuse"), ("seat", "seat_3"), ("zone", "depot"));
            Assert.False((await rig.Decider.DecideAsync(State(evidence: evidence), CancellationToken.None)).Speak);

            reply = Answers(("speak", "speak"), ("kind", "question"), ("seat", "seat_2"), ("zone", "none"));
            MeetingDecision ask = await rig.Decider.DecideAsync(State(), CancellationToken.None);
            Assert.Equal("where", ask.PhraseId);
            Assert.Equal((byte?)2, ask.Seat);

            // an accusation it cannot back is grounded down, whatever the model said
            reply = Answers(("speak", "speak"), ("kind", "accuse"), ("seat", "seat_4"), ("zone", "garden"));
            MeetingDecision unseen = await rig.Decider.DecideAsync(State(), CancellationToken.None);
            Assert.Equal("hmm", unseen.PhraseId);
            Assert.Equal("clef", unseen.Source);

            reply = Answers(("vote", "seat_2"));
            MeetingDecision vote = await rig.Decider.DecideAsync(State(stage: MeetingStage.Voting), CancellationToken.None);
            Assert.Equal((byte?)2, vote.Vote);
            Assert.Equal("clef", vote.Source);

            reply = Answers(("vote", "skip"));
            MeetingDecision skip = await rig.Decider.DecideAsync(State(stage: MeetingStage.Voting), CancellationToken.None);
            Assert.True(skip.SkipVote);

            // the decider carries its latency (the talker logs the decision when it is sent) and writes no line itself
            Assert.True(skip.LatencyMs >= 0);
            Assert.Null(skip.Reason);
            Assert.Empty(rig.Lines);
        }

        [Theory]
        [InlineData("speak_now", "accuse", "seat_3", "depot")] // not one of the two options
        [InlineData("speak", "accuze", "seat_3", "depot")] // unknown kind
        [InlineData("speak", "accuse", "seat 3", "depot")]
        [InlineData("speak", "accuse", "seat_7", "depot")] // not a living seat
        [InlineData("speak", "accuse", "seat_3", "moon")] // not a zone of the map
        [InlineData("wait", "accuse", "seat_3", "moon")] // a bad answer is bad even when the bot waits
        public async Task Clef_UnknownDiscussionChoiceUsesTheRulePath(string speak, string kind, string seat, string zone)
        {
            string reply = Answers(("speak", speak), ("kind", kind), ("seat", seat), ("zone", zone));
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json(reply)));
            var rig = new Rig(handler);

            MeetingDecision d = await rig.Decider.DecideAsync(State(), CancellationToken.None);

            Assert.Equal("rule", d.Source);
            Assert.Equal("bad_answer", d.Reason);
        }

        [Theory]
        [InlineData("seat_three")]
        [InlineData("Skip")]
        [InlineData("seat_0")] // itself is not among the options of the vote question
        [InlineData("seat_7")] // out
        [InlineData("none")]
        public async Task Clef_UnknownVoteChoiceUsesTheRulePath(string choice)
        {
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json(Answers(("vote", choice)))));
            var rig = new Rig(handler);
            MeetingState voting = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, 90f } });

            MeetingDecision d = await rig.Decider.DecideAsync(voting, CancellationToken.None);

            Assert.Equal("rule", d.Source);
            Assert.Equal((byte?)3, d.Vote); // the rule path's own vote, not a silent skip
            Assert.Equal("bad_answer", d.Reason);
        }

        [Fact]
        public async Task Clef_TheSituationTextFollowsTheConfig()
        {
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json(Answers(("vote", "skip")))));
            var rig = new Rig(handler);
            MeetingState s = State(stage: MeetingStage.Voting);
            s.Seats = 6;
            s.Polluters = 1;

            await rig.Decider.DecideAsync(s, CancellationToken.None);

            string situation = Parse(handler.Bodies[0]).GetProperty("state").GetProperty("situation").GetString()!;
            Assert.Contains("6 seats", situation);
            Assert.Contains("1 hidden polluter", situation);
            Assert.DoesNotContain("8 seats", situation);
        }

        [Fact]
        public void Clef_PhraseTableMapsKindSeatAndZone()
        {
            MeetingState s = State();
            MeetingState accusedMe = State();
            accusedMe.Chat = new List<ChatAct> { new ChatAct(2, "sus", "accuse", 0, null, 5f) };

            void Is(MeetingDecision d, string id, byte? seat = null, string? zone = null)
            {
                Assert.Equal(id, d.PhraseId);
                Assert.Equal(seat, d.Seat);
                Assert.Equal(zone, d.Zone);
            }

            Is(ClefDecider.Phrase("alibi", 3, "depot", s), "with", 3, "depot");
            Is(ClefDecider.Phrase("alibi", null, "market", s), "i_was", null, "market");
            Is(ClefDecider.Phrase("alibi", 0, "market", s), "i_was", null, "market"); // never "with" itself
            Is(ClefDecider.Phrase("accuse", 3, null, s), "sus", 3);
            Assert.False(ClefDecider.Phrase("accuse", null, "depot", s).Speak);
            Is(ClefDecider.Phrase("question", 4, null, s), "where", 4);
            Is(ClefDecider.Phrase("react", 2, null, accusedMe), "not_me");
            Is(ClefDecider.Phrase("react", 2, null, s), "agree", 2); // seat 2 accused someone in the chat
            Is(ClefDecider.Phrase("react", 4, null, s), "doubt", 4);
            Is(ClefDecider.Phrase("vote_talk", 4, null, s), "vote", 4);
            Is(ClefDecider.Phrase("vote_talk", null, null, s), "skip_no_proof");
            Assert.False(ClefDecider.Phrase("quiet", 3, "depot", s).Speak);

            // a polluter with no evidence lies with a dump claim; a citizen with none stays vague
            Is(ClefDecider.Phrase("accuse", 3, "depot", State(Role.Polluter)), "saw_dump", 3, "depot");
            Is(ClefDecider.Phrase("accuse", 3, "depot", s), "sus", 3, "depot");
        }

        [Fact]
        public async Task Clef_TimeoutFallsBack()
        {
            Assert.Equal(TimeSpan.FromSeconds(3), ClefDecider.DefaultTimeout);
            var handler = new FakeHandler(async (req, body, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return Json(Answers(("vote", "seat_1")));
            });
            var rig = new Rig(handler); // the default 3 s timeout
            MeetingState voting = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, 80f } });

            Stopwatch sw = Stopwatch.StartNew();
            MeetingDecision d = await rig.Decider.DecideAsync(voting, CancellationToken.None);

            Assert.InRange(sw.Elapsed.TotalSeconds, 2.9, 4.5); // cancelled at 3 s, not at the handler's 5 s
            Assert.True(handler.SawCancel, "the request was cancelled at the timeout");
            Assert.Equal("rule", d.Source);
            Assert.Equal((byte?)3, d.Vote); // the rule path's own vote
            Assert.Equal("timeout", d.Reason);
            Assert.True(d.LatencyMs >= 2900);
        }

        [Fact]
        public async Task Clef_AnAbandonedCallIsCancelledAndLoggedLate()
        {
            var handler = new FakeHandler(async (req, body, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return Json("{}");
            });
            var rig = new Rig(handler);
            using var cts = new CancellationTokenSource();

            Task<MeetingDecision> pending = rig.Decider.DecideAsync(State(stage: MeetingStage.Voting), cts.Token);
            await Task.Delay(50);
            cts.Cancel();
            MeetingDecision d = await pending;

            Assert.Equal("rule", d.Source);
            Assert.Equal("late", d.Reason);
        }

        [Fact]
        public async Task Clef_401LogsOnceAndStopsCalling()
        {
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json("{\"success\":false}", HttpStatusCode.Unauthorized)));
            var rig = new Rig(handler);
            MeetingState voting = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, 80f } });

            for (int i = 0; i < 4; i++)
            {
                MeetingDecision d = await rig.Decider.DecideAsync(voting, CancellationToken.None);
                Assert.Equal("rule", d.Source);
                Assert.Equal((byte?)3, d.Vote);
            }

            Assert.Single(handler.Bodies);

            Assert.Single(rig.Records(), r => r.TryGetProperty("note", out JsonElement n) && n.GetString()!.Contains("401"));
            Assert.DoesNotContain(rig.Lines, l => l.Contains(Token));
        }

        [Fact]
        public async Task Clef_ConcurrentCallsThatAllGet401LogOnce()
        {
            // 7 bots decide at the same moment: all pass the "model is on" check before the first 401 comes back
            var allIn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int arrived = 0;
            var handler = new FakeHandler(async (req, body, ct) =>
            {
                if (Interlocked.Increment(ref arrived) == 7)
                {
                    allIn.SetResult(true);
                }

                await allIn.Task.WaitAsync(TimeSpan.FromSeconds(10)); // nobody gets its 401 before all 7 are in flight
                return Json("{\"success\":false}", HttpStatusCode.Unauthorized);
            });
            var rig = new Rig(handler);
            MeetingState voting = State(stage: MeetingStage.Voting);

            MeetingDecision[] all = await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => rig.Decider.DecideAsync(voting, CancellationToken.None)));

            Assert.All(all, d => Assert.Equal("rule", d.Source));
            Assert.Equal(7, handler.Bodies.Count);
            Assert.Single(rig.Records(), r => r.TryGetProperty("note", out _));
        }

        [Fact]
        public async Task Clef_429Or5xxFallsBackForThatCall()
        {
            int call = 0;
            var handler = new FakeHandler((req, body, ct) =>
            {
                call++;
                if (call == 1)
                {
                    return Task.FromResult(Json("{}", (HttpStatusCode)429));
                }

                if (call == 2)
                {
                    return Task.FromResult(Json("{}", HttpStatusCode.InternalServerError));
                }

                return Task.FromResult(Json(Answers(("vote", "seat_2"))));
            });
            var rig = new Rig(handler);
            MeetingState voting = State(stage: MeetingStage.Voting, suspicion: new Dictionary<byte, float> { { 3, 80f } });

            MeetingDecision first = await rig.Decider.DecideAsync(voting, CancellationToken.None);
            MeetingDecision second = await rig.Decider.DecideAsync(voting, CancellationToken.None);
            MeetingDecision third = await rig.Decider.DecideAsync(voting, CancellationToken.None);

            Assert.Equal("rule", first.Source);
            Assert.Equal("rule", second.Source);
            Assert.Equal("clef", third.Source);
            Assert.Equal((byte?)2, third.Vote);
            Assert.Equal(3, handler.Bodies.Count);

            Assert.Equal("http_429", first.Reason);
            Assert.Equal("http_500", second.Reason);
            Assert.Empty(rig.Lines);
        }

        [Fact]
        public async Task Budget_StopsAtCapAndResetsAtUtcMidnight()
        {
            DateTime now = new DateTime(2026, 11, 20, 23, 59, 0, DateTimeKind.Utc);
            var budget = new DailyBudget(2, () => now);
            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());
            now = now.AddSeconds(30);
            Assert.False(budget.TryConsume());
            now = now.AddSeconds(31); // 00:00:01 UTC the next day
            Assert.True(budget.TryConsume());
            Assert.True(budget.TryConsume());
            Assert.False(budget.TryConsume());

            // a decider with an empty budget never touches the network
            var handler = new FakeHandler((req, body, ct) => Task.FromResult(Json(Answers(("vote", "seat_1")))));
            var rig = new Rig(handler, cap: 1);
            MeetingState voting = State(stage: MeetingStage.Voting);
            Assert.Equal("clef", (await rig.Decider.DecideAsync(voting, CancellationToken.None)).Source);
            Assert.Equal("rule", (await rig.Decider.DecideAsync(voting, CancellationToken.None)).Source);
            Assert.Single(handler.Bodies);
            Assert.Equal("budget", (await rig.Decider.DecideAsync(voting, CancellationToken.None)).Reason);
        }

        [Fact]
        public void FromEnvironment_NullWithoutKeys()
        {
            var http = new HttpClient();
            var budget = new DailyBudget(10, () => DateTime.UtcNow);
            var log = new MeetingDecisionLog(_ => { });
            Func<string, string?> only(Dictionary<string, string> env) => k => env.TryGetValue(k, out string? v) ? v : null;

            Assert.Null(ClefDecider.FromEnvironment(http, budget, log, null, only(new Dictionary<string, string>())));
            Assert.Null(ClefDecider.FromEnvironment(http, budget, log, null, only(new Dictionary<string, string> { { "CLOUDFLARE_ACCOUNT_ID", "a" } })));
            Assert.Null(ClefDecider.FromEnvironment(http, budget, log, null, only(new Dictionary<string, string> { { "CLOUDFLARE_ACCOUNT_ID", "a" }, { "CLOUDFLARE_API_TOKEN", "" } })));
            Assert.NotNull(ClefDecider.FromEnvironment(http, budget, log, null, only(new Dictionary<string, string> { { "CLOUDFLARE_ACCOUNT_ID", "a" }, { "CLOUDFLARE_API_TOKEN", "t" } })));
        }

        [Fact]
        public void Log_SentDecisionLineCarriesSourceReasonLatencyAndPhrase()
        {
            var lines = new List<string>();
            var log = new MeetingDecisionLog(lines.Add);
            MeetingDecision d = MeetingDecision.Say("rule", "sus", 3);
            d.Reason = "timeout";
            d.LatencyMs = 3001;

            log.Sent(State(), d);
            log.Sent(State(stage: MeetingStage.Voting), new MeetingDecision { Vote = 2, Source = "clef", LatencyMs = 800 });

            JsonElement a = Parse(lines[0]);
            Assert.Equal("ROOM1", a.GetProperty("match").GetString());
            Assert.Equal(0, a.GetProperty("seat").GetInt32());
            Assert.Equal("discussion", a.GetProperty("stage").GetString());
            Assert.Equal("rule", a.GetProperty("source").GetString());
            Assert.Equal("timeout", a.GetProperty("fallback").GetString());
            Assert.Equal(3001, a.GetProperty("latency_ms").GetInt64());
            Assert.Equal("sus", a.GetProperty("phrase").GetString());
            Assert.Equal(3, a.GetProperty("target").GetInt32());
            JsonElement b = Parse(lines[1]);
            Assert.Equal("clef", b.GetProperty("source").GetString());
            Assert.False(b.TryGetProperty("fallback", out _));
            Assert.Equal(2, b.GetProperty("vote").GetInt32());
        }

        [Fact]
        public void Log_ToFileWritesOffTheCallingThread()
        {
            string path = Path.Combine(Path.GetTempPath(), "mlog-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                // the file is locked by someone else: a synchronous writer would block or lose the lines
                using (var hold = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    MeetingDecisionLog log = MeetingDecisionLog.ToFile(path);
                    Stopwatch sw = Stopwatch.StartNew();
                    log.Note("one");
                    log.Note("two");
                    Assert.True(sw.ElapsedMilliseconds < 200, "Note blocked for " + sw.ElapsedMilliseconds + " ms");
                    hold.Dispose();
                    log.Dispose();
                }

                string[] written = File.ReadAllLines(path);
                Assert.Equal(2, written.Length);
                Assert.Contains("one", written[0]);
                Assert.Contains("two", written[1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Skipped unless CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN are set (a real network call).</summary>
        public sealed class RealApiFactAttribute : FactAttribute
        {
            public RealApiFactAttribute()
            {
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClefDecider.AccountEnv))
                    || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ClefDecider.TokenEnv)))
                {
                    Skip = "CLOUDFLARE_ACCOUNT_ID / CLOUDFLARE_API_TOKEN not set";
                }
            }
        }

        [RealApiFact]
        public async Task Clef_RealApiAnswersAVoteAndATalkDecision()
        {
            var lines = new List<string>();
            var log = new MeetingDecisionLog(lines.Add);
            ClefDecider? clef = ClefDecider.FromEnvironment(new HttpClient(), new DailyBudget(10, () => DateTime.UtcNow), log);
            Assert.NotNull(clef);

            // a citizen who saw seat 3 dump in the depot, accused by seat 2
            var evidence = new[] { new EvidenceItem(EvidenceItem.SawDump, 3, "depot", 50f), new EvidenceItem(EvidenceItem.MyZone, null, "market", 60f) };
            var suspicion = new Dictionary<byte, float> { { 3, 100f }, { 2, 15f } };

            MeetingDecision talk = await clef!.DecideAsync(State(evidence: evidence, suspicion: suspicion), CancellationToken.None);
            MeetingDecision vote = await clef.DecideAsync(State(stage: MeetingStage.Voting, evidence: evidence, suspicion: suspicion), CancellationToken.None);

            Console.WriteLine("REAL CLEF talk: speak=" + talk.Speak + " phrase=" + talk.PhraseId + " seat=" + talk.Seat + " zone=" + talk.Zone + " source=" + talk.Source);
            Console.WriteLine("REAL CLEF vote: vote=" + vote.Vote + " skip=" + vote.SkipVote + " source=" + vote.Source);
            foreach (string l in lines)
            {
                Console.WriteLine("REAL CLEF log: " + l);
            }

            Assert.Equal("clef", talk.Source);
            Assert.Equal("clef", vote.Source);
        }

        [Fact]
        public void Settings_ReadTheMeetingCapAndTimeout()
        {
            Assert.Equal(5000, new BotsSettings().MeetingDailyCallCap);
            Assert.Equal(3f, new BotsSettings().MeetingTimeoutS);
            string path = Path.Combine(Path.GetTempPath(), "bots-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ \"brain\": \"scripted\", \"meeting\": { \"daily_call_cap\": 123, \"timeout_s\": 1.5 } }");
            try
            {
                BotsSettings s = BotsSettings.Load(path);
                Assert.Equal(123, s.MeetingDailyCallCap);
                Assert.Equal(1.5f, s.MeetingTimeoutS);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
