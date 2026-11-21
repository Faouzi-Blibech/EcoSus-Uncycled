using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;

namespace Sim.Tests
{
    /// <summary>Full matches driven only through Apply and Tick.</summary>
    public class MatchScriptTests
    {
        private const float Dt = 0.1f;
        private const float StepLength = 0.25f;

        private sealed class Driver
        {
            private readonly Dictionary<byte, long> _seq = new Dictionary<byte, long>();

            public Driver(MatchEngine engine)
            {
                E = engine;
            }

            public MatchEngine E { get; }

            public List<GameEvent> Events { get; } = new List<GameEvent>();

            public void Tick()
            {
                Events.AddRange(E.Tick(Dt));
            }

            public void Wait(float seconds)
            {
                int n = (int)Math.Ceiling(seconds / Dt - 1e-3);
                for (int i = 0; i < n && E.Phase != MatchPhase.Ended; i++)
                {
                    Tick();
                }
            }

            public void WaitFor(MatchPhase phase, float maxSeconds)
            {
                int n = (int)Math.Ceiling(maxSeconds / Dt);
                for (int i = 0; i < n && E.Phase != phase; i++)
                {
                    Tick();
                }

                Assert.Equal(phase, E.Phase);
            }

            public CommandResult Do(byte seat, ICommand c) => E.Apply(seat, c);

            /// <summary>Walks along an A* path in small moves, one Tick per move.</summary>
            public void WalkTo(byte seat, Vec2 target)
            {
                PlayerState p = E.Find(seat) ?? throw new InvalidOperationException();
                List<Vec2>? path = AStar.FindPath(E.Map.Walk, p.Position, target);
                Assert.NotNull(path);
                var waypoints = new List<Vec2>(path!);
                waypoints.Add(target);
                foreach (Vec2 wp in waypoints)
                {
                    while (Vec2.Distance(p.Position, wp) > 1e-3f)
                    {
                        Vec2 delta = wp - p.Position;
                        float d = delta.Length;
                        Vec2 next = d <= StepLength ? wp : p.Position + delta * (StepLength / d);
                        long seq;
                        _seq.TryGetValue(seat, out seq);
                        seq++;
                        _seq[seat] = seq;
                        CommandResult r = E.Apply(seat, new MoveCommand(next, seq));
                        Assert.True(r.IsOk, "move rejected: " + r);
                        Tick();
                    }
                }
            }
        }

        private static List<TaskTypeDef> OneShortTask() => new List<TaskTypeDef>
        {
            new TaskTypeDef("sort_waste", TaskCategory.Short, 1, 1f, new[] { "bins" }),
        };

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Citizens_win_by_finishing_every_task()
        {
            var cfg = new MatchConfig { TasksCommon = 0, TasksShort = 1, TasksLong = 0 };
            var d = new Driver(new MatchEngine(cfg, TestWorld.Map(), TestWorld.Seats(8), OneShortTask(), new XorShiftRandom(11)));
            d.WaitFor(MatchPhase.Playing, 6f);

            var binsSpot = new Vec2(3.5f, 7.0f);
            foreach (PlayerState p in d.E.Players)
            {
                if (!p.IsCitizen)
                {
                    continue;
                }

                TaskState task = Assert.Single(d.E.Tasks.OwnedBy(p.Seat));
                d.WalkTo(p.Seat, binsSpot);
                Assert.True(d.Do(p.Seat, new UseCommand("bins")).IsOk);
                d.Wait(1.1f);
                Assert.True(d.Do(p.Seat, new TaskStepCommand(task.Id)).IsOk);
                d.Tick();
            }

            Assert.Equal(MatchPhase.Ended, d.E.Phase);
            GameOverEvent over = Assert.Single(TestWorld.Of<GameOverEvent>(d.Events));
            Assert.Null(over.Recipients);
            Assert.Equal(Winner.Citizens, over.Winner);
            Assert.Equal(GameOverReason.TasksDone, over.Reason);
            Assert.Equal(8, over.Roles.Count);
            Assert.Equal(6, TestWorld.Of<TaskProgressEvent>(d.Events).Count);
        }

        /// <summary>Polluters dump, one is voted out at a meeting, the other fills the meter.</summary>
        private static Driver PollutersWinByMeter(ulong seed)
        {
            var cfg = new MatchConfig
            {
                DumpCooldownS = 5f,
                DiscussionS = 2f,
                VotingS = 5f,
                ResultsS = 1f,
            };
            var d = new Driver(new MatchEngine(cfg, TestWorld.Map(), TestWorld.Seats(8), TestWorld.TaskTypes(), new XorShiftRandom(seed)));
            d.WaitFor(MatchPhase.Playing, 6f);
            List<byte> polluters = TestWorld.SeatsWithRole(d.E, Role.Polluter);
            List<byte> citizens = TestWorld.SeatsWithRole(d.E, Role.Citizen);

            // polluters walk a little, then dump on cooldown for 15 s
            d.WalkTo(polluters[0], new Vec2(8.5f, 6.5f));
            d.WalkTo(polluters[1], new Vec2(12.5f, 6.5f));
            for (int i = 0; i < 150; i++)
            {
                foreach (byte pol in polluters)
                {
                    d.Do(pol, new DumpCommand());
                }

                d.Tick();
            }

            // a citizen next to a pile reports it; everyone votes the first polluter out
            Pile pile = d.E.Piles.All[0];
            d.WalkTo(citizens[0], pile.Position + new Vec2(0f, 1f));
            Assert.True(d.Do(citizens[0], new ReportCommand(pile.Id)).IsOk);
            d.WaitFor(MatchPhase.Meeting, 1f);
            d.Wait(2.1f);
            Assert.Equal(MeetingStage.Voting, d.E.Meeting!.Stage);
            foreach (PlayerState p in d.E.Players)
            {
                Assert.True(d.Do(p.Seat, new VoteCommand(polluters[0])).IsOk);
            }

            d.WaitFor(MatchPhase.Ejection, 3f);
            d.WaitFor(MatchPhase.Playing, 6f);
            Assert.False(d.E.Find(polluters[0])!.IsActive);

            // the remaining polluter keeps dumping
            for (int i = 0; i < 3000 && d.E.Phase != MatchPhase.Ended; i++)
            {
                d.Do(polluters[1], new DumpCommand());
                d.Tick();
            }

            return d;
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Polluters_win_by_filling_the_meter()
        {
            Driver d = PollutersWinByMeter(5);
            Assert.Equal(MatchPhase.Ended, d.E.Phase);
            GameOverEvent over = Assert.Single(TestWorld.Of<GameOverEvent>(d.Events));
            Assert.Equal(Winner.Polluters, over.Winner);
            Assert.Equal(GameOverReason.PollutionFull, over.Reason);
            Assert.Equal(100f, d.E.Meter.Value);

            EjectionEvent ej = Assert.Single(TestWorld.Of<EjectionEvent>(d.Events));
            Assert.True(ej.WasPolluter);

            var kinds = new HashSet<TimelineKind>();
            foreach (TimelineEntry t in over.Timeline)
            {
                kinds.Add(t.Kind);
            }

            Assert.Contains(TimelineKind.Dump, kinds);
            Assert.Contains(TimelineKind.Report, kinds);
            Assert.Contains(TimelineKind.Meeting, kinds);
            Assert.Contains(TimelineKind.Ejection, kinds);
        }

        /// <summary>A v2 match: three spots cleaned, a smog sabotage fixed, an emergency meeting, a vote-out, then the deadline.</summary>
        private static Driver V2Match(ulong seed)
        {
            var cfg = new MatchConfig
            {
                DiscussionS = 2f,
                VotingS = 5f,
                ResultsS = 1f,
                EmergencyMinSinceMeetingS = 0f,
                DeadlineS = 90f,
            };
            var d = new Driver(new MatchEngine(cfg, TestWorld.Map(), TestWorld.Seats(8), TestWorld.TaskTypes(), new XorShiftRandom(seed)));
            d.WaitFor(MatchPhase.Playing, 6f);
            List<byte> polluters = TestWorld.SeatsWithRole(d.E, Role.Polluter);
            List<byte> citizens = TestWorld.SeatsWithRole(d.E, Role.Citizen);

            string[] spots = { "bins", "compost", "tap" };
            for (int i = 0; i < spots.Length; i++)
            {
                d.WalkTo(citizens[i], d.E.Map.FindConsole(spots[i])!.Position);
                Assert.True(d.Do(citizens[i], new UseCommand(spots[i])).IsOk);
                d.Wait(2.1f);
                Assert.True(d.Do(citizens[i], new SpotDoneCommand(spots[i])).IsOk);
            }

            d.Wait(10f);
            Assert.True(d.Do(polluters[0], new SabotageCommand(SabotageKind.Smog)).IsOk);
            d.WalkTo(citizens[3], d.E.Map.FindConsole("smog_panel")!.Position);
            Assert.True(d.Do(citizens[3], new FixCommand("smog_panel", true)).IsOk);
            d.Wait(3.2f);

            d.WalkTo(citizens[4], d.E.Map.FindConsole("button")!.Position);
            Assert.True(d.Do(citizens[4], new CallMeetingCommand()).IsOk);
            d.WaitFor(MatchPhase.Meeting, 1f);
            d.Wait(2.1f);
            Assert.Equal(MeetingStage.Voting, d.E.Meeting!.Stage);
            foreach (PlayerState p in d.E.Players)
            {
                Assert.True(d.Do(p.Seat, new VoteCommand(polluters[0])).IsOk);
            }

            d.WaitFor(MatchPhase.Ejection, 3f);
            d.WaitFor(MatchPhase.Playing, 6f);
            d.Wait(100f);
            return d;
        }

        [Fact]
        public void Same_seed_and_commands_give_the_same_events()
        {
            Driver first = V2Match(9);
            Assert.Equal(MatchPhase.Ended, first.E.Phase);
            Assert.Equal(GameOverReason.Deadline, Assert.Single(TestWorld.Of<GameOverEvent>(first.Events)).Reason);
            Assert.Equal(3, TestWorld.Of<SpotCleanedEvent>(first.Events).Count);
            Assert.Single(TestWorld.Of<EjectionEvent>(first.Events));
            Assert.NotEmpty(TestWorld.Of<SabotageEndedEvent>(first.Events));

            List<string> a = Describe(first.Events);
            List<string> b = Describe(V2Match(9).Events);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i], b[i]);
            }
        }

        // ---------- event description for determinism ----------

        private static List<string> Describe(List<GameEvent> events)
        {
            var list = new List<string>(events.Count);
            foreach (GameEvent e in events)
            {
                list.Add(Format(e, 0));
            }

            return list;
        }

        private static string Format(object? v, int depth)
        {
            if (v == null)
            {
                return "null";
            }

            if (v is string s)
            {
                return s;
            }

            if (v is float f)
            {
                return f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (v is IEnumerable en)
            {
                var parts = new List<string>();
                foreach (object? item in en)
                {
                    parts.Add(Format(item, depth + 1));
                }

                return "[" + string.Join(",", parts) + "]";
            }

            Type t = v.GetType();
            bool ours = t.Namespace != null && t.Namespace.StartsWith("Sim", StringComparison.Ordinal);
            if (!ours || t.IsEnum || v is Vec2 || depth > 4)
            {
                return v.ToString() ?? "";
            }

            var sb = new StringBuilder(t.Name).Append('{');
            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(props, (x, y) => string.CompareOrdinal(x.Name, y.Name));
            foreach (PropertyInfo prop in props)
            {
                sb.Append(prop.Name).Append('=').Append(Format(prop.GetValue(v), depth + 1)).Append(';');
            }

            return sb.Append('}').ToString();
        }
    }
}
