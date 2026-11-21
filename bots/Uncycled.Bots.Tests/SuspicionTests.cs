using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class SuspicionTests
    {
        private static WitnessRecord Record(ActKind act, byte actor, float at, string zone = "depot")
        {
            return new WitnessRecord(act, actor, 0, at, zone, new Vec2(20.5f, 12.5f), act == ActKind.Dump ? 1 : (int?)null, act == ActKind.Hit ? (byte)6 : (byte?)null);
        }

        private static SuspicionModel Run(TestMatch m, params WitnessRecord[] records)
        {
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(0);
            mem.Observe(view, m.Engine.MatchTime, new List<WitnessRecord>(records));
            var model = new SuspicionModel();
            model.Update(mem, view);
            return model;
        }

        [Fact]
        public void Suspicion_VotesWitnessedDumper()
        {
            TestMatch m = TestMatch.Create();
            SuspicionModel model = Run(m, Record(ActKind.Dump, 3, 12f));

            Assert.Equal(100f, model.Score(3));
            Assert.Equal((byte?)3, model.Vote(m.Engine.ViewFor(0), Role.Citizen, new List<byte>()));
        }

        [Fact]
        public void Suspicion_SkipsWhenNothingReachesThreshold()
        {
            TestMatch m = TestMatch.Create();
            SuspicionModel model = Run(m);

            Assert.Null(model.Vote(m.Engine.ViewFor(0), Role.Citizen, new List<byte>()));
        }

        [Fact]
        public void Suspicion_PolluterNeverVotesPartner()
        {
            TestMatch m = TestMatch.Create();
            var partners = new List<byte> { 3 };

            SuspicionModel onlyPartner = Run(m, Record(ActKind.Dump, 3, 12f));
            Assert.Null(onlyPartner.Vote(m.Engine.ViewFor(0), Role.Polluter, partners));

            SuspicionModel both = Run(m, Record(ActKind.Dump, 3, 12f), Record(ActKind.Hit, 5, 14f));
            Assert.Equal((byte?)5, both.Vote(m.Engine.ViewFor(0), Role.Polluter, partners));
        }

        [Fact]
        public void Suspicion_SightingInPileZoneNearItsTimeScoresThirty()
        {
            TestMatch m = TestMatch.Create();
            SeatView live = m.Engine.ViewFor(0);
            PlayerView seen = live.Players[0];
            string zone = m.Map.ZoneAt(seen.Position);

            var mem = new BotMemory(m.Map, m.Config);
            mem.Observe(live, 100f, new List<WitnessRecord>());

            var clue = new ClueInfo(1, PileKind.Dump, zone, 110f, WasteType.Glass, null);
            var meeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo> { clue }, new List<byte>(), false, null, 0f);
            var view = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, meeting, null);
            var model = new SuspicionModel();
            model.Update(mem, view);

            Assert.Equal(30f, model.Score(seen.Seat));
            Assert.Null(model.Vote(view, Role.Citizen, new List<byte>()));

            var farClue = new ClueInfo(2, PileKind.Dump, zone, 60f, WasteType.Glass, null);
            var oldMeeting = new MeetingView(MeetingStage.Discussion, 30f, 0, MeetingCause.Report, new List<ClueInfo> { farClue }, new List<byte>(), false, null, 0f);
            var oldView = new SeatView(0, MatchPhase.Meeting, 30f, 130f, live.DeadlineS, live.DeadlineClock, 130f, 10f, 2, live.Me, new List<PlayerView>(), new List<PileView>(), null, oldMeeting, null);
            model.Update(mem, oldView);
            Assert.Equal(0f, model.Score(seen.Seat));
        }
    }
}
