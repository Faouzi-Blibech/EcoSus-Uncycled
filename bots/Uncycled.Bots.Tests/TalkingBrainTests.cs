using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Meeting;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class TalkingBrainTests
    {
        [Fact]
        public void TalkingBrain_WhenTheTalkerThrowsTheBrainTakesItsVoteBack()
        {
            TestMatch m = TestMatch.Create(2);
            MatchEngine e = m.Engine;
            byte polluter = m.FirstSeatOf(Role.Polluter);
            SeatView pv = e.ViewFor(polluter);
            byte witness = pv.Players.First(p => Vec2.Distance(p.Position, pv.Me.Position) <= 1.0f && !pv.Me.Partners.Contains(p.Seat)).Seat;
            byte bot = e.Players.First(p => p.IsActive && p.Seat != polluter && p.Seat != witness).Seat;

            var inner = BrainFactory.Scripted(e.Find(bot)!.Role, new BotsSettings(), new XorShiftRandom(5), m.Content.Tasks);
            var talker = new MeetingTalker(new RuleDecider(), new RuleDecider(), new MeetingFloor(), new Random(1))
            {
                HeatOf = seat => throw new InvalidOperationException("talker bug"),
            };
            var warnings = new List<string>();
            var brain = new TalkingBrain(inner, talker, new MeetingLog(m.Content.Phrases!), warnings.Add);

            Action<ICommand> send = c => e.Apply(bot, c);
            for (int i = 0; i < 220; i++)
            {
                brain.Think(e, bot, TestMatch.Dt, send); // the brain plays (and builds its memory) before the meeting
                m.Tick();
            }

            Assert.True(e.Apply(polluter, new DumpCommand()).IsOk);
            Assert.True(e.Apply(witness, new ReportCommand(e.Piles.All[0].Id)).IsOk);
            Assert.Equal(MatchPhase.Meeting, e.Phase);

            bool voted = false;
            for (int i = 0; i < 4000 && e.Phase == MatchPhase.Meeting; i++)
            {
                brain.Think(e, bot, TestMatch.Dt, send);
                MeetingView? mv = e.ViewFor(bot).Meeting;
                voted |= mv != null && mv.Stage == MeetingStage.Voting && mv.MyVoteCast;
                m.Tick();
            }

            Assert.Single(warnings);
            Assert.Contains("talker", warnings[0]);
            Assert.True(voted, "the brain must vote by itself once its talker is off");
        }

        private sealed class Counting : IMeetingDecider
        {
            public Counting(string name) { Name = name; }

            public string Name { get; }

            public List<MeetingStage> Asked { get; } = new List<MeetingStage>();

            public System.Threading.Tasks.Task<MeetingDecision> DecideAsync(MeetingState state, System.Threading.CancellationToken ct)
            {
                Asked.Add(state.Stage);
                return System.Threading.Tasks.Task.FromResult(MeetingDecision.Quiet(Name));
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task StageDecider_AsksTheVoteDeciderOnlyInVoting()
        {
            var talk = new Counting("rule");
            var vote = new Counting("clef");
            var split = new StageDecider(talk, vote);
            var discussion = new MeetingState { Stage = MeetingStage.Discussion };
            var voting = new MeetingState { Stage = MeetingStage.Voting };

            MeetingDecision a = await split.DecideAsync(discussion, System.Threading.CancellationToken.None);
            MeetingDecision b = await split.DecideAsync(voting, System.Threading.CancellationToken.None);

            Assert.Equal(new[] { MeetingStage.Discussion }, talk.Asked);
            Assert.Equal(new[] { MeetingStage.Voting }, vote.Asked);
            Assert.Equal("rule", a.Source);
            Assert.Equal("clef", b.Source);
        }
    }
}
