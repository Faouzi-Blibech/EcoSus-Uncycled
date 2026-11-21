using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>Votes of one group of voters, split by what the target really is.</summary>
    public sealed class VoteCounts
    {
        public int Polluter { get; set; }

        public int Innocent { get; set; }

        public int Skip { get; set; }

        public int Total => Polluter + Innocent + Skip;

        public double Share(int n) => Total == 0 ? 0.0 : n / (double)Total;
    }

    /// <summary>What one decider did over all sampled vote stages.</summary>
    public sealed class DeciderStats
    {
        public DeciderStats(string name)
        {
            Name = name;
        }

        public string Name { get; }

        /// <summary>Decisions asked of this decider (one per living bot per meeting).</summary>
        public int Calls { get; set; }

        /// <summary>Calls that did not end in the decider's own answer (decision.Reason set, an exception, or the harness timeout).</summary>
        public int Failures { get; set; }

        public VoteCounts CitizenVotes { get; } = new VoteCounts();

        public VoteCounts PolluterVotes { get; } = new VoteCounts();

        /// <summary>Meetings where the decider's votes, counted by the engine's rule, ejected a polluter / a citizen / nobody.</summary>
        public int EjectionsCorrect { get; set; }

        public int EjectionsWrong { get; set; }

        public int NoEjection { get; set; }

        public List<double> LatenciesMs { get; } = new List<double>();

        public double Percentile(double p)
        {
            if (LatenciesMs.Count == 0)
            {
                return 0.0;
            }

            var sorted = new List<double>(LatenciesMs);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p * sorted.Count);
            return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
        }
    }

    public sealed class BakeOffOptions
    {
        public int Meetings { get; set; } = 300;

        public ulong SeedFrom { get; set; } = HostOptions.EvalSeedStart;

        public ulong SeedTo { get; set; } = HostOptions.EvalSeedStart + 499;

        /// <summary>Most decisions asked of each decider; the run stops before a meeting that would pass it.</summary>
        public int MaxCalls { get; set; } = 2500;

        /// <summary>Hard limit per call; a decider that is slower counts as a failure and its vote as a skip.</summary>
        public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(10);
    }

    public sealed class BakeOffReport
    {
        public BakeOffReport(BakeOffOptions options, IReadOnlyList<DeciderStats> deciders)
        {
            Options = options;
            Deciders = deciders;
        }

        public BakeOffOptions Options { get; }

        public IReadOnlyList<DeciderStats> Deciders { get; }

        public int Matches { get; set; }

        public int Meetings { get; set; }

        /// <summary>meetings (target reached), max_calls (budget) or seeds_exhausted.</summary>
        public string StoppedBy { get; set; } = "seeds_exhausted";

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"seeds\":\"").Append(N(Options.SeedFrom)).Append('-').Append(N(Options.SeedTo)).Append('"');
            sb.Append(",\"matches\":").Append(N(Matches));
            sb.Append(",\"meetings\":").Append(N(Meetings));
            sb.Append(",\"max_calls\":").Append(N(Options.MaxCalls));
            sb.Append(",\"stopped_by\":\"").Append(StoppedBy).Append('"');
            sb.Append(",\"deciders\":[");
            for (int i = 0; i < Deciders.Count; i++)
            {
                DeciderStats d = Deciders[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"name\":\"").Append(d.Name).Append('"');
                sb.Append(",\"calls\":").Append(N(d.Calls));
                sb.Append(",\"call_failures\":").Append(N(d.Failures));
                sb.Append(",\"citizen_votes\":").Append(Counts(d.CitizenVotes));
                sb.Append(",\"polluter_votes\":").Append(Counts(d.PolluterVotes));
                sb.Append(",\"ejections_correct\":").Append(N(d.EjectionsCorrect));
                sb.Append(",\"ejections_wrong\":").Append(N(d.EjectionsWrong));
                sb.Append(",\"no_ejection\":").Append(N(d.NoEjection));
                sb.Append(",\"latency_ms_p50\":").Append(F(d.Percentile(0.5)));
                sb.Append(",\"latency_ms_p95\":").Append(F(d.Percentile(0.95)));
                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string Counts(VoteCounts c)
        {
            return "{\"polluter\":" + N(c.Polluter) + ",\"innocent\":" + N(c.Innocent) + ",\"skip\":" + N(c.Skip)
                + ",\"share_polluter\":" + F(c.Share(c.Polluter)) + ",\"share_innocent\":" + F(c.Share(c.Innocent)) + ",\"share_skip\":" + F(c.Share(c.Skip)) + "}";
        }

        private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static string N(ulong v) => v.ToString(CultureInfo.InvariantCulture);

        private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>A baseline decider that never votes.</summary>
    public sealed class SkipDecider : IMeetingDecider
    {
        public string Name => "skip";

        public Task<MeetingDecision> DecideAsync(MeetingState state, CancellationToken ct)
        {
            return Task.FromResult(new MeetingDecision { SkipVote = true, Source = Name });
        }
    }

    /// <summary>
    /// --bakeoff: plays scripted matches (Round 5 bots, their votes as accusations through the MeetingLog)
    /// and, when the vote stage of a meeting starts, asks every living bot every decider for a vote on
    /// the same MeetingState. The match goes on with the scripted brains' own votes, so the deciders do
    /// not change the game they are measured on. Each decider's vote is grounded like the talker would
    /// ground it, then counted: vote on a real polluter / an innocent / skip, and which seat the votes
    /// would eject by the engine's rule. Vote only; no chat.
    /// </summary>
    public static class BakeOff
    {
        private const float TickDt = MatchRunner.TickDt;
        private const float ExtraSecondsAfterDeadline = 300f;

        public static BakeOffReport Run(ContentBundle content, BotsSettings settings, BakeOffOptions options, IReadOnlyList<IMeetingDecider> deciders)
        {
            if (deciders == null || deciders.Count == 0)
            {
                throw new ArgumentException("at least one decider is needed", nameof(deciders));
            }

            var stats = new List<DeciderStats>();
            foreach (IMeetingDecider d in deciders)
            {
                stats.Add(new DeciderStats(d.Name));
            }

            var report = new BakeOffReport(options, stats);
            var run = new Session(content, settings, options, deciders, stats, report);
            for (ulong seed = options.SeedFrom; seed <= options.SeedTo && report.StoppedBy == "seeds_exhausted"; seed++)
            {
                run.PlayMatch(seed);
                if (seed == ulong.MaxValue)
                {
                    break;
                }
            }

            return report;
        }

        private sealed class Session
        {
            private readonly ContentBundle _content;
            private readonly BotsSettings _settings;
            private readonly BakeOffOptions _options;
            private readonly IReadOnlyList<IMeetingDecider> _deciders;
            private readonly List<DeciderStats> _stats;
            private readonly BakeOffReport _report;
            private int _calls;

            public Session(ContentBundle content, BotsSettings settings, BakeOffOptions options, IReadOnlyList<IMeetingDecider> deciders, List<DeciderStats> stats, BakeOffReport report)
            {
                _content = content;
                _settings = settings;
                _options = options;
                _deciders = deciders;
                _stats = stats;
                _report = report;
            }

            public void PlayMatch(ulong seed)
            {
                MatchConfig cfg = _content.Config;
                var rng = new XorShiftRandom(seed);
                var seats = new List<SeatSetup>();
                for (int i = 0; i < cfg.Seats; i++)
                {
                    seats.Add(new SeatSetup((byte)i, "bot" + i, (byte)i));
                }

                var engine = new MatchEngine(cfg, _content.Map, seats, _content.Tasks, rng, _content.Phrases);
                var log = new MeetingLog(_content.Phrases ?? throw new InvalidOperationException("the content has no phrase catalogue"));
                var feed = new MeetingFeed(log);
                Func<Role, IRandom, IBotBrain> makeBrain = HostBrains.Scripted(_settings, _content);
                var brains = new IBotBrain[cfg.Seats];
                var send = new Action<ICommand>[cfg.Seats];
                var memories = new BotMemory[cfg.Seats];
                var suspicions = new SuspicionModel[cfg.Seats];
                for (int s = 0; s < cfg.Seats; s++)
                {
                    byte seat = (byte)s;
                    brains[s] = makeBrain(engine.ViewFor(seat).Me.Role, rng);
                    send[s] = cmd => engine.Apply(seat, cmd);
                    memories[s] = new BotMemory(_content.Map, cfg);
                    suspicions[s] = new SuspicionModel();
                    feed.Add(seat, memories[s]);
                    if (brains[s] is IMeetingObserver observer)
                    {
                        feed.Add(seat, observer, brains[s] as IAccuser);
                    }
                }

                _report.Matches++;
                int maxTicks = (int)((cfg.DeadlineS + ExtraSecondsAfterDeadline) / TickDt) * 4;
                for (int t = 0; t < maxTicks && engine.Phase != MatchPhase.Ended; t++)
                {
                    for (int s = 0; s < brains.Length; s++)
                    {
                        brains[s].Think(engine, (byte)s, TickDt, send[s]);
                    }

                    IReadOnlyList<GameEvent> events = engine.Tick(TickDt);
                    feed.Handle(events, engine.MatchTime);
                    for (int s = 0; s < memories.Length; s++)
                    {
                        memories[s].Observe(engine.ViewFor((byte)s), engine.MatchTime, engine.Witnesses.SeenBy((byte)s));
                    }

                    foreach (GameEvent e in events)
                    {
                        if (e is EjectionEvent ej)
                        {
                            foreach (BotMemory m in memories)
                            {
                                m.MarkOut(ej.Seat);
                            }
                        }
                        else if (e is VotingStartedEvent && !Sample(engine, seed, log, memories, suspicions))
                        {
                            return;
                        }
                    }
                }
            }

            /// <summary>Asks the deciders at the start of a vote stage; false when the run is over (target or budget reached).</summary>
            private bool Sample(MatchEngine engine, ulong seed, MeetingLog log, BotMemory[] memories, SuspicionModel[] suspicions)
            {
                var voters = new List<byte>();
                var states = new List<MeetingState>();
                for (int s = 0; s < memories.Length; s++)
                {
                    SeatView view = engine.ViewFor((byte)s);
                    if (view.Phase == MatchPhase.Meeting && view.Meeting != null && view.Meeting.Stage == MeetingStage.Voting && view.Me.Status == PlayerStatus.Active)
                    {
                        MeetingState state = MeetingStateBuilder.Build(view, memories[s], log, suspicions[s], null);
                        state.MatchId = "bakeoff-" + seed.ToString(CultureInfo.InvariantCulture);
                        voters.Add((byte)s);
                        states.Add(state);
                    }
                }

                if (voters.Count == 0)
                {
                    return true;
                }

                if (_calls + voters.Count > _options.MaxCalls)
                {
                    _report.StoppedBy = "max_calls";
                    return false;
                }

                _calls += voters.Count;
                _report.Meetings++;

                var pending = new List<Task<Answer>>[_deciders.Count];
                for (int d = 0; d < _deciders.Count; d++)
                {
                    pending[d] = new List<Task<Answer>>();
                    for (int v = 0; v < voters.Count; v++)
                    {
                        pending[d].Add(Ask(_deciders[d], states[v]));
                    }
                }

                var all = new List<Task>();
                foreach (List<Task<Answer>> list in pending)
                {
                    all.AddRange(list);
                }

                Task.WhenAll(all).GetAwaiter().GetResult();

                for (int d = 0; d < _deciders.Count; d++)
                {
                    Tally(engine, _stats[d], voters, pending[d]);
                }

                if (_report.Meetings >= _options.Meetings)
                {
                    _report.StoppedBy = "meetings";
                    return false;
                }

                return true;
            }

            private async Task<Answer> Ask(IMeetingDecider decider, MeetingState state)
            {
                var sw = Stopwatch.StartNew();
                MeetingDecision? decision = null;
                bool failed;
                try
                {
                    using (var cts = new CancellationTokenSource(_options.CallTimeout))
                    {
                        decision = await decider.DecideAsync(state, cts.Token).ConfigureAwait(false);
                    }

                    failed = decision == null || decision.Reason != null;
                }
                catch (Exception)
                {
                    failed = true;
                }

                sw.Stop();
                byte? vote = null;
                if (decision != null)
                {
                    MeetingDecision grounded = Grounding.Check(decision, state);
                    vote = grounded.SkipVote ? null : grounded.Vote;
                }

                return new Answer(vote, sw.Elapsed.TotalMilliseconds, failed);
            }

            private static void Tally(MatchEngine engine, DeciderStats stats, List<byte> voters, List<Task<Answer>> answers)
            {
                var records = new List<VoteRecord>();
                for (int v = 0; v < voters.Count; v++)
                {
                    Answer a = answers[v].Result;
                    stats.Calls++;
                    stats.LatenciesMs.Add(a.Ms);
                    if (a.Failed)
                    {
                        stats.Failures++;
                    }

                    records.Add(new VoteRecord(voters[v], a.Vote, false));
                    VoteCounts group = engine.Find(voters[v])?.Role == Role.Polluter ? stats.PolluterVotes : stats.CitizenVotes;
                    if (a.Vote == null)
                    {
                        group.Skip++;
                    }
                    else if (engine.Find(a.Vote.Value)?.Role == Role.Polluter)
                    {
                        group.Polluter++;
                    }
                    else
                    {
                        group.Innocent++;
                    }
                }

                byte? ejected = VoteTally.Tally(records, true).Ejected;
                if (ejected == null)
                {
                    stats.NoEjection++;
                }
                else if (engine.Find(ejected.Value)?.Role == Role.Polluter)
                {
                    stats.EjectionsCorrect++;
                }
                else
                {
                    stats.EjectionsWrong++;
                }
            }
        }

        private readonly struct Answer
        {
            public Answer(byte? vote, double ms, bool failed)
            {
                Vote = vote;
                Ms = ms;
                Failed = failed;
            }

            public byte? Vote { get; }

            public double Ms { get; }

            public bool Failed { get; }
        }
    }
}
