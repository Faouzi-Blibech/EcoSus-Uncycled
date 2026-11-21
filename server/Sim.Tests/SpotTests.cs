using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>v2 rules: cleaning spots, bags and the clean session (spec section 5).</summary>
    public class SpotTests
    {
        // The test map lists bins under check_meter (min 1 s) and sort_waste (min 1 s); the first one wins.
        private const float BinsMinS = 1f;

        // turn_compost lists compost first: min 2 s.
        private const float CompostMinS = 2f;

        private static void OpenAndFinish(MatchEngine e, byte seat, string spot, float seconds)
        {
            StandAt(e, seat, spot);
            Assert.True(e.Apply(seat, new UseCommand(spot)).IsOk);
            Advance(e, seconds);
            Assert.True(e.Apply(seat, new SpotDoneCommand(spot)).IsOk);
        }

        [Fact]
        public void SpotDone_GivesBag_AndCleansSpot()
        {
            var e = Playing();
            byte c = Citizen(e);
            StandAt(e, c, "bins");
            Assert.True(e.Apply(c, new UseCommand("bins")).IsOk);
            Advance(e, BinsMinS + 0.1f);
            Assert.True(e.Apply(c, new SpotDoneCommand("bins")).IsOk);
            Assert.Equal(1, P(e, c).Bags);
            Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void SpotDone_EmitsSpotCleanedToTheCleanerOnly()
        {
            var e = Playing();
            byte c = Citizen(e);
            e.Tick(0f);
            OpenAndFinish(e, c, "bins", BinsMinS + 0.1f);
            SpotCleanedEvent ev = Assert.Single(Of<SpotCleanedEvent>(e.Tick(0f)));
            Assert.Equal(new[] { c }, Arr(ev.Recipients));
            Assert.Equal("bins", ev.Spot);
            Assert.Equal(1, ev.Bags);
        }

        [Theory]
        [InlineData(0f, 300f, SpotState.Clean)]   // 0 turns litter off
        [InlineData(90f, 89f, SpotState.Clean)]   // not yet
        [InlineData(90f, 90.5f, SpotState.Dirty)] // litter is back
        public void CleanSpot_GetsLitteredAgain_AfterRelitterSeconds(float relitterS, float wait, SpotState expected)
        {
            var e = Playing(new MatchConfig { SpotRelitterS = relitterS });
            byte c = Citizen(e);
            OpenAndFinish(e, c, "bins", BinsMinS + 0.1f);
            Advance(e, wait);
            Assert.Equal(expected, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void Relitter_CountsFromTheLatestClean()
        {
            var e = Playing(new MatchConfig { SpotRelitterS = 90f });
            byte c = Citizen(e);
            Advance(e, 80f);
            OpenAndFinish(e, c, "bins", BinsMinS + 0.1f);
            Advance(e, 60f);
            Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void AllSpotsStartDirty_MeterStartsZero()
        {
            var e = Playing();
            Assert.Equal(3, e.Spots.All.Count);
            foreach (Spot s in e.Spots.All)
            {
                Assert.Equal(SpotState.Dirty, s.State);
                Assert.Null(s.VirusBy);
            }

            Assert.Equal(0f, e.Meter.Value);
        }

        [Fact]
        public void Board_TakesPositionRangeTypeAndMinSecondsFromTheMapAndTaskTypes()
        {
            var e = Playing();
            Spot bins = e.Spots.Find("bins")!;
            Assert.Equal(BinsPos, bins.Position);
            Assert.Equal(1.2f, bins.Range);
            Assert.Equal("check_meter", bins.TaskType);
            Assert.Equal(BinsMinS, bins.MinSeconds);
            Spot compost = e.Spots.Find("compost")!;
            Assert.Equal("turn_compost", compost.TaskType);
            Assert.Equal(CompostMinS, compost.MinSeconds);
            Assert.Null(e.Spots.Find("button"));
        }

        [Fact]
        public void TwoConsolesOfOneType_GetTheirIndexAsStep_AndUseOkCarriesIt()
        {
            // one two-step type: compost is step 0, tap is step 1 (the client picks the mini-game by type and step)
            var types = new List<TaskTypeDef> { new TaskTypeDef("turn_compost", TaskCategory.Long, 2, 2f, new[] { "compost", "tap" }) };
            var e = Playing(tasks: types);
            Assert.Equal(0, e.Spots.Find("compost")!.Step);
            Assert.Equal(1, e.Spots.Find("tap")!.Step);

            byte c = Citizen(e);
            StandAt(e, c, "tap");
            Assert.True(e.Apply(c, new UseCommand("tap")).IsOk);
            UseOkEvent ev = Assert.Single(Of<UseOkEvent>(e.Tick(0f)));
            Assert.Equal("turn_compost", ev.TaskType);
            Assert.Equal(1, ev.Step);
            Assert.Null(ev.TaskId);
        }

        [Fact]
        public void SpotDone_BeforeMinSeconds_OnCooldown()
        {
            var e = Playing();
            byte c = Citizen(e);
            StandAt(e, c, "compost");
            Assert.True(e.Apply(c, new UseCommand("compost")).IsOk);
            Advance(e, CompostMinS - 1f);
            CommandResult early = e.Apply(c, new SpotDoneCommand("compost"));
            Assert.False(early.IsOk);
            Assert.Equal(RejectReason.OnCooldown, early.Reason);
            Assert.Equal(0, P(e, c).Bags);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("compost")!.State);

            // the session survives the early try
            Advance(e, 1.1f);
            Assert.True(e.Apply(c, new SpotDoneCommand("compost")).IsOk);
        }

        [Fact]
        public void SpotDone_WithTool_HalfTheMinSecondsIsEnough()
        {
            var e = Playing();
            byte c = Citizen(e);
            P(e, c).HasTool = true;
            StandAt(e, c, "compost");
            Assert.True(e.Apply(c, new UseCommand("compost")).IsOk);
            Advance(e, CompostMinS * e.Config.ToolTimeMult + 0.05f);
            Assert.True(e.Apply(c, new SpotDoneCommand("compost")).IsOk);
            Assert.Equal(1, P(e, c).Bags);
        }

        [Fact]
        public void SpotDone_WalkedAway_OutOfRange()
        {
            var e = Playing();
            byte c = Citizen(e);
            StandAt(e, c, "bins");
            Assert.True(e.Apply(c, new UseCommand("bins")).IsOk);
            Advance(e, BinsMinS + 0.1f);
            e.TestTeleport(c, Corner);
            CommandResult r = e.Apply(c, new SpotDoneCommand("bins"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.OutOfRange, r.Reason);
            Assert.Equal(0, P(e, c).Bags);
        }

        [Fact]
        public void Use_WithBagsFull_BagsFull()
        {
            var e = Playing();
            byte c = Citizen(e);
            OpenAndFinish(e, c, "bins", BinsMinS + 0.1f);
            Assert.Equal(e.Config.BagCap, P(e, c).BagCap);
            StandAt(e, c, "tap");
            CommandResult r = e.Apply(c, new UseCommand("tap"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.BagsFull, r.Reason);
        }

        [Fact]
        public void Use_CleanSpot_SpotClean()
        {
            var e = Playing();
            byte c = Citizen(e);
            byte d = Citizen(e, 1);
            OpenAndFinish(e, c, "bins", BinsMinS + 0.1f);
            StandAt(e, d, "bins");
            CommandResult r = e.Apply(d, new UseCommand("bins"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.SpotClean, r.Reason);
        }

        [Fact]
        public void SpotDone_WithoutUse_NotAllowed()
        {
            var e = Playing();
            byte c = Citizen(e);
            StandAt(e, c, "bins");
            Advance(e, 5f);
            CommandResult r = e.Apply(c, new SpotDoneCommand("bins"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NotAllowed, r.Reason);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void SpotDone_ForAnotherSpotThanTheOpenOne_NotAllowed()
        {
            var e = Playing();
            byte c = Citizen(e);
            StandAt(e, c, "bins");
            Assert.True(e.Apply(c, new UseCommand("bins")).IsOk);
            Advance(e, 3f);
            CommandResult r = e.Apply(c, new SpotDoneCommand("tap"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NotAllowed, r.Reason);
        }

        [Fact]
        public void Polluter_Use_Spot_NotAllowed()
        {
            var e = Playing();
            byte p = Polluter(e);
            StandAt(e, p, "bins");
            CommandResult r = e.Apply(p, new UseCommand("bins"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NotAllowed, r.Reason);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void TwoCleaners_SameSpotSameTick_SecondGetsSpotClean()
        {
            var e = Playing();
            byte a = Citizen(e);
            byte b = Citizen(e, 1);
            StandAt(e, a, "bins");
            StandAt(e, b, "bins");
            Assert.True(e.Apply(a, new UseCommand("bins")).IsOk);
            Assert.True(e.Apply(b, new UseCommand("bins")).IsOk);
            List<GameEvent> events = Advance(e, BinsMinS + 0.1f);

            Assert.True(e.Apply(a, new SpotDoneCommand("bins")).IsOk);
            CommandResult second = e.Apply(b, new SpotDoneCommand("bins"));
            Assert.False(second.IsOk);
            Assert.Equal(RejectReason.SpotClean, second.Reason);

            Assert.Equal(1, P(e, a).Bags);
            Assert.Equal(0, P(e, b).Bags);
            events.AddRange(e.Tick(0f));
            SpotCleanedEvent only = Assert.Single(Of<SpotCleanedEvent>(events));
            Assert.Equal(a, only.Seat);
        }

        [Fact]
        public void Meeting_DropsCleanSession()
        {
            var e = Playing(new MatchConfig { DiscussionS = 1f, VotingS = 1f, ResultsS = 1f, EmergencyMinSinceMeetingS = 0f });
            byte c = Citizen(e);
            StandAt(e, c, "bins");
            Assert.True(e.Apply(c, new UseCommand("bins")).IsOk);
            Advance(e, BinsMinS + 0.1f);

            StandAt(e, c, "button");
            Assert.True(e.Apply(c, new CallMeetingCommand()).IsOk);
            Assert.Equal(MatchPhase.Meeting, e.Phase);
            for (int i = 0; i < 400 && e.Phase != MatchPhase.Playing; i++)
            {
                e.Tick(0.05f);
            }

            Assert.Equal(MatchPhase.Playing, e.Phase);

            StandAt(e, c, "bins");
            CommandResult r = e.Apply(c, new SpotDoneCommand("bins"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NotAllowed, r.Reason);
            Assert.Equal(0, P(e, c).Bags);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void Hit_And_Dump_NotAllowedInV2()
        {
            var e = Playing();
            byte p = Polluter(e);
            byte c = Citizen(e);
            ParkOthers(e, p, c);
            e.TestTeleport(p, new Vec2(3.5f, 6.5f));
            e.TestTeleport(c, new Vec2(4.0f, 6.5f));
            Advance(e, e.Config.StartCooldownS + 1f);

            Assert.Equal(RejectReason.NotAllowed, e.Apply(p, new HitCommand(c)).Reason);
            Assert.Equal(0, P(e, c).Poison);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(p, new DumpCommand()).Reason);
            Assert.Empty(e.Piles.All);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(p, new SabotageCommand(SabotageKind.Fire)).Reason);
            Assert.False(e.Fire.Active);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(c, new CleanStartCommand(1)).Reason);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(c, new CleanCancelCommand(1)).Reason);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(c, new TaskStepCommand(1)).Reason);
        }
    }
}
