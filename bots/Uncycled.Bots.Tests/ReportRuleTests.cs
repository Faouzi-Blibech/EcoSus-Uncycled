using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// A network-driven Cleaner that sees an unreported body walks to it and reports it (the scripted Cleaner's logic,
    /// reaction delay included); a network Trasher is not forced. The training host treats the forced decisions like
    /// a committed task step: a Stay-only placeholder frame, no reward, the sent action ignored.
    /// </summary>
    public sealed class ReportRuleTests
    {
        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));

        private static int Index(string name)
        {
            int i = Array.IndexOf(PolicyActionsV2.Names.ToArray(), name);
            Assert.True(i >= 0, "no policy action named " + name);
            return i;
        }

        private sealed class StayPolicy : IPolicy
        {
            public int Calls;

            public int Act(float[] obs, bool[] mask, float temperature, IRandom rng)
            {
                Calls++;
                return Index("Stay");
            }
        }

        private sealed class NoBrain : IBotBrain
        {
            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
            }
        }

        /// <summary>A fresh body a few tiles from the seat, in its sight (the victim is another seat, still on its feet: the engine does not care).</summary>
        private static Pile PutBodyInSight(MatchEngine engine, byte seat)
        {
            SeatView view = engine.ViewFor(seat);
            PlayerView other = view.Players.First(p => Vec2.Distance(p.Position, view.Me.Position) > 1.5f);
            Pile pile = engine.Piles.AddBody(other.Position, engine.Map.ZoneAt(other.Position), engine.MatchTime, other.Seat);
            Assert.Contains(engine.ViewFor(seat).Piles, p => p.Id == pile.Id);
            return pile;
        }

        private static (TestMatch Match, byte Seat) MatchWithSeatOf(Role role)
        {
            TestMatch m = TestMatch.Create(2);
            m.TickFor(11f);
            return (m, m.FirstSeatOf(role));
        }

        private static List<ReportCommand> RunBrain(TestMatch m, byte seat, StayPolicy policy, float seconds)
        {
            var brain = new NeuralBrain(policy, new NoBrain(), new BotsSettings { DecisionIntervalS = 0.2f, Skill = 1f }, new XorShiftRandom(3), m.Content.Tasks);
            var reports = new List<ReportCommand>();
            for (int i = 0; i < (int)(seconds / TestMatch.Dt); i++)
            {
                brain.Think(m.Engine, seat, TestMatch.Dt, cmd =>
                {
                    if (cmd is ReportCommand r)
                    {
                        reports.Add(r);
                    }

                    m.Engine.Apply(seat, cmd);
                });
                m.Tick();
            }

            return reports;
        }

        [Fact]
        public void NeuralCleaner_WithBodyInSight_ReportsItOnceWhateverTheNetworkWants()
        {
            (TestMatch m, byte seat) = MatchWithSeatOf(Role.Citizen);
            Pile body = PutBodyInSight(m.Engine, seat);
            var policy = new StayPolicy();

            List<ReportCommand> reports = RunBrain(m, seat, policy, 20f);

            Assert.Single(reports);
            Assert.Equal(body.Id, reports[0].PileId);
        }

        [Fact]
        public void NeuralCleaner_WithoutBody_IsStillAskedEveryDecision()
        {
            (TestMatch m, byte seat) = MatchWithSeatOf(Role.Citizen);
            var policy = new StayPolicy();

            List<ReportCommand> reports = RunBrain(m, seat, policy, 6f);

            Assert.Empty(reports);
            Assert.True(policy.Calls >= 5, "the network keeps choosing when there is no body, calls=" + policy.Calls);
        }

        [Fact]
        public void NeuralTrasher_IsNotForcedToReport()
        {
            (TestMatch m, byte seat) = MatchWithSeatOf(Role.Polluter);
            PutBodyInSight(m.Engine, seat);
            var policy = new StayPolicy();

            List<ReportCommand> reports = RunBrain(m, seat, policy, 20f);

            Assert.Empty(reports);
            Assert.True(policy.Calls >= 5, "calls=" + policy.Calls);
        }

        // ---------- the training host ----------

        private static MatchRunner Runner(string learner)
        {
            HostOptions o = HostOptions.Parse(new[] { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1",
                "--learner", learner, "--rw-win", "0" });
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            return new MatchRunner(o, 0, content, HostBrains.Scripted(BotsSettings.Load("none"), content));
        }

        private static bool StayOnly(byte[] frames, MatchRunner r, int agent)
        {
            int at = agent * r.FrameSize + r.MaskOffset;
            for (int i = 0; i < PolicyActionsV2.Count; i++)
            {
                if ((frames[at + i] != 0) != (i == Index("Stay")))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AdvanceToPlaying(MatchRunner r, byte[] buf)
        {
            for (int d = 0; d < 400 && !(r.Phase == MatchPhase.Playing && r.MatchTime > 12f); d++)
            {
                r.WriteFrames(buf);
                r.Advance(new int[r.AgentCount]);
            }

            Assert.Equal(MatchPhase.Playing, r.Phase);
        }

        [Fact]
        public void HostCleaner_WithBodyInSight_GetsPlaceholderFramesAndReportsAnyway()
        {
            MatchRunner r = Runner("citizen");
            var buf = new byte[r.AgentCount * r.FrameSize];
            AdvanceToPlaying(r, buf);
            byte seat = r.AgentSeat(0);
            int meetingsBefore = r.MeetingsHeld;
            Pile body = PutBodyInSight(r.Engine, seat);
            int go = Index("GoZoneNear0");

            int held = 0;
            for (int d = 0; d < 80 && body.State == PileState.Fresh; d++)
            {
                r.WriteFrames(buf);
                float reward = BitConverter.ToSingle(buf, r.RewardOffset);
                var actions = new int[r.AgentCount];
                if (!r.AgentCommitted(0) && StayOnly(buf, r, 0))
                {
                    held++;
                    Assert.Equal(0f, reward);
                    // the action sent for a placeholder frame is ignored: walking off would break the report
                    actions[0] = go;
                }

                r.Advance(actions);
            }

            Assert.True(held > 0, "the forced decisions must reach the trainer as Stay-only placeholder frames");
            Assert.NotEqual(PileState.Fresh, body.State);
            Assert.True(r.MeetingsHeld > meetingsBefore, "the report must call a meeting");
        }

        [Fact]
        public void HostTrasher_IsNotForcedToReport()
        {
            MatchRunner r = Runner("polluter");
            var buf = new byte[r.AgentCount * r.FrameSize];
            AdvanceToPlaying(r, buf);
            PutBodyInSight(r.Engine, r.AgentSeat(0));

            int placeholders = 0;
            for (int d = 0; d < 40; d++)
            {
                r.WriteFrames(buf);
                if (!r.AgentCommitted(0) && StayOnly(buf, r, 0))
                {
                    placeholders++;
                }

                r.Advance(new int[r.AgentCount]);
            }

            Assert.Equal(0, placeholders);
        }
    }
}
