using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    public class ScriptedBrainTests
    {
        private readonly ITestOutputHelper _out;

        public ScriptedBrainTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void Brain_StartsMidMatch()
        {
            // A human drops at 120 s and a fresh brain takes the seat: empty memory, current view,
            // and a move sequence the engine accepts. Search a few seeds for a match still running.
            int checkedSeeds = 0;
            for (ulong seed = 1; seed <= 8; seed++)
            {
                TestMatch m = TestMatch.Create(seed);
                var rig = new BrainRig(m, seed);
                rig.RunUntil(() => m.Engine.Phase == MatchPhase.Ended || (m.Engine.MatchTime >= 120f && m.Engine.Phase == MatchPhase.Playing), 400f);
                if (m.Engine.Phase == MatchPhase.Ended)
                {
                    continue;
                }

                PlayerState seatState = m.Engine.Players.First(p => p.Role == Role.Citizen && p.IsActive);
                byte seat = seatState.Seat;
                Assert.True(seatState.LastMoveSeq > 1, "the old brain had been moving");
                rig.Brains[seat] = rig.MakeBrain(seat, seed + 77);
                int before = rig.Sent.Count;

                // At human pace a task step takes up to 2.5x min_s and a meeting can follow, so the seat may stand
                // still for a long while: run until it has moved (bounded), not for a fixed 30 s.
                rig.RunUntil(
                    () => m.Engine.Phase == MatchPhase.Ended || rig.Sent.Skip(before).Any(c => c.Seat == seat && c.Command is MoveCommand),
                    200f);
                rig.RunUntil(() => m.Engine.Phase == MatchPhase.Ended, 10f); // and a little more, to see the moves accepted

                List<SentCommand> mine = rig.Sent.Skip(before).Where(c => c.Seat == seat).ToList();
                _out.WriteLine("seed " + seed + ": new brain sent " + mine.Count + " commands, rejected: "
                    + string.Join(",", mine.Where(c => !c.Result.IsOk).Select(c => c.Command.GetType().Name + ":" + c.Result)));
                Assert.NotEmpty(mine);
                Assert.All(mine, c => Assert.True(c.Result.IsOk, c.Command.GetType().Name + " " + c.Result));
                Assert.Contains(mine, c => c.Command is MoveCommand);
                checkedSeeds++;
                if (checkedSeeds >= 3)
                {
                    break;
                }
            }

            Assert.True(checkedSeeds >= 1, "no seed lasted past 120 s");
        }

        [Fact]
        public void Brain_SendsOnlyVoteInMeeting()
        {
            TestMatch m = TestMatch.Create(4);
            var rig = new BrainRig(m, 4);
            const byte caller = 3;
            rig.Skip.Add(caller);
            var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
            var exec = new ActionExecutor(nav, new MoveSender());
            exec.Begin(BotAction.GoEmergency, m.Engine.ViewFor(caller), m.Map, m.Engine.MatchTime);
            bool called = false;

            // seat 3 walks to the button and calls a meeting while the other brains play on
            for (int i = 0; i < 4000 && m.Engine.Phase == MatchPhase.Playing; i++)
            {
                SeatView view = m.Engine.ViewFor(caller);
                if (!called && !exec.Busy && m.Engine.MatchTime >= m.Config.EmergencyMinSinceMeetingS + 0.5f)
                {
                    exec.Begin(BotAction.CallMeeting, view, m.Map, m.Engine.MatchTime);
                    called = true;
                }

                exec.Step(m.Engine, caller, view, TestMatch.Dt, m.Engine.MatchTime, c => rig.Record(caller, c));
                rig.Step();
            }

            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);
            int activeVoters = m.Engine.Players.Count(p => p.IsActive && p.Seat != caller);
            int sentBefore = rig.Sent.Count;
            int maxVoted = 0;
            while (m.Engine.Phase == MatchPhase.Meeting || m.Engine.Phase == MatchPhase.Ejection)
            {
                rig.Step();
                MeetingView? mv = m.Engine.ViewFor(0).Meeting;
                if (mv != null)
                {
                    maxVoted = System.Math.Max(maxVoted, mv.Voted.Count);
                }
            }

            List<SentCommand> during = rig.Sent.Skip(sentBefore).Where(c => c.Seat != caller).ToList();
            Assert.All(during, c => Assert.IsType<VoteCommand>(c.Command));
            Assert.Equal(activeVoters, maxVoted);

            // after the round resumes the bots walk and act again, with no rejected command
            int resumed = rig.Sent.Count;
            Assert.Equal(MatchPhase.Playing, m.Engine.Phase);
            rig.RunUntil(() => m.Engine.Phase != MatchPhase.Playing, 10f);
            List<SentCommand> after = rig.Sent.Skip(resumed).Where(c => c.Seat != caller).ToList();
            Assert.Contains(after, c => c.Command is MoveCommand);
            Assert.All(after, c => Assert.True(c.Result.IsOk, c.Seat + " " + c.Command.GetType().Name + " " + c.Result));
        }

        [Fact]
        public void Brain_OutSeatDoesNothing()
        {
            TestMatch m = TestMatch.Create(2, startPlaying: true);
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView pv = m.Engine.ViewFor(polluter);
            PlayerView victim = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= m.Config.HitRange
                && !pv.Me.Partners.Contains(p.Seat));

            for (int i = 0; i < 3000 && m.Engine.Find(victim.Seat)!.IsActive; i++)
            {
                m.Engine.Apply(polluter, new HitCommand(victim.Seat));
                m.Tick();
            }

            Assert.False(m.Engine.Find(victim.Seat)!.IsActive);
            IBotBrain brain = BrainFactory.Scripted(Role.Citizen, new BotsSettings(), new XorShiftRandom(5));
            int sent = 0;
            for (int i = 0; i < 200; i++)
            {
                brain.Think(m.Engine, victim.Seat, TestMatch.Dt, _ => sent++);
                m.Tick();
            }

            Assert.Equal(0, sent);
        }
    }
}
