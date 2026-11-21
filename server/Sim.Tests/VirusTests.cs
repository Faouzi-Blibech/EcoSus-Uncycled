using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>v2 virus: plant, infect on spot_done, countdown, heal, infected meeting (spec section 6).</summary>
    public class VirusTests
    {
        private const float BinsMinS = 1f;
        private const float CompostMinS = 2f;

        [Theory]
        [InlineData(0f, 60f)]
        [InlineData(45f, 35f)]
        [InlineData(90f, 10f)]
        [InlineData(300f, 10f)]
        public void Countdown_GetsDeadlierWithAge(float age, float expected) =>
            Assert.Equal(expected, VirusRule.Countdown(age, 60f, 10f, 90f), 3);

        private static MatchConfig Open()
        {
            return new MatchConfig { TrasherStartCooldownS = 0f };
        }

        /// <summary>Playing; the first Trasher has picked the virus and stands on the bins; everyone else is parked.</summary>
        private static MatchEngine Rig(out byte pol, out byte cleaner)
        {
            MatchEngine e = Playing(Open());
            pol = Polluter(e);
            cleaner = Citizen(e);
            P(e, pol).Ability = TrasherAbility.Virus;
            ParkOthers(e, pol);
            StandAt(e, pol, "bins");
            e.Tick(0f);
            return e;
        }

        private static void Plant(MatchEngine e, byte pol, string spot)
        {
            StandAt(e, pol, spot);
            Assert.True(e.Apply(pol, new PlantVirusCommand(spot)).IsOk);
            e.Tick(0f);
        }

        /// <summary>The cleaner opens the spot session, waits, and finishes it: the spot turns clean.</summary>
        private static void Clean(MatchEngine e, byte cleaner, string spot, float wait)
        {
            StandAt(e, cleaner, spot);
            Assert.True(e.Apply(cleaner, new UseCommand(spot)).IsOk);
            Advance(e, wait);
            Assert.True(e.Apply(cleaner, new SpotDoneCommand(spot)).IsOk);
        }

        /// <summary>Plants on the bins and has the cleaner clean them at once: the cleaner is infected with about 60 s.</summary>
        private static void Infect(MatchEngine e, byte pol, byte cleaner)
        {
            Plant(e, pol, "bins");
            Clean(e, cleaner, "bins", BinsMinS + 0.1f);
            e.Tick(0f);
        }

        private static void RunMeetingToEnd(MatchEngine e)
        {
            for (int i = 0; i < 4000 && e.Phase != MatchPhase.Playing; i++)
            {
                Advance(e, 0.05f);
            }

            Assert.Equal(MatchPhase.Playing, e.Phase);
        }

        // ---------- plant ----------

        [Fact]
        public void PlantVirus_DirtySpot_PlantedAndToldToThePlanterOnly()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);

            Assert.True(e.Apply(pol, new PlantVirusCommand("bins")).IsOk);

            Assert.Equal(pol, e.Spots.Find("bins")!.VirusBy);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            VirusPlantedEvent planted = Assert.Single(Of<VirusPlantedEvent>(events));
            Assert.Equal(new[] { pol }, Arr(planted.Recipients));
            Assert.Equal("bins", planted.Spot);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cleaner, new PlantVirusCommand("compost")).Reason);
        }

        [Fact]
        public void PlantVirus_WithoutVirusAbility_NotUnlocked()
        {
            MatchEngine e = Rig(out byte pol, out _);
            P(e, pol).Ability = TrasherAbility.Gas;

            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new PlantVirusCommand("bins")).Reason);

            P(e, pol).Ability = TrasherAbility.None;
            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new PlantVirusCommand("bins")).Reason);
            Assert.Null(e.Spots.Find("bins")!.VirusBy);
        }

        [Fact]
        public void PlantVirus_CleanSpot_SpotClean()
        {
            MatchEngine e = Rig(out byte pol, out _);
            e.Spots.Find("bins")!.State = SpotState.Clean;

            Assert.Equal(RejectReason.SpotClean, e.Apply(pol, new PlantVirusCommand("bins")).Reason);
            Assert.Null(e.Spots.Find("bins")!.VirusBy);
        }

        [Fact]
        public void PlantVirus_OutOfRange()
        {
            MatchEngine e = Rig(out byte pol, out _);
            e.TestTeleport(pol, Corner);

            Assert.Equal(RejectReason.OutOfRange, e.Apply(pol, new PlantVirusCommand("bins")).Reason);
        }

        [Fact]
        public void PlantVirus_SecondWhilePlanted_OnCooldown()
        {
            MatchEngine e = Rig(out byte pol, out _);
            Plant(e, pol, "bins");

            StandAt(e, pol, "compost");
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new PlantVirusCommand("compost")).Reason);
            Assert.Null(e.Spots.Find("compost")!.VirusBy);
        }

        // ---------- trigger and countdown ----------

        [Fact]
        public void CleaningPlantedSpot_Infects_WithAgedCountdown()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Plant(e, pol, "bins");
            StandAt(e, cleaner, "bins");
            Assert.True(e.Apply(cleaner, new UseCommand("bins")).IsOk);
            Advance(e, 45f);
            e.Tick(0f);

            Assert.True(e.Apply(cleaner, new SpotDoneCommand("bins")).IsOk);

            IReadOnlyList<GameEvent> events = e.Tick(0f);
            InfectedEvent infected = Assert.Single(Of<InfectedEvent>(events));
            Assert.Equal(new[] { cleaner }, Arr(infected.Recipients));
            Assert.Equal(cleaner, infected.Seat);
            Assert.Equal(35f, infected.Countdown, 1);
            Assert.Equal(35f, e.ViewFor(cleaner).Me.InfectedS!.Value, 1);
            Assert.Null(e.Spots.Find("bins")!.VirusBy);
            Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
            Assert.Equal(1, P(e, cleaner).Bags);
        }

        [Fact]
        public void Trigger_StartsTheVirusCooldown_ThenAnotherCanBePlanted()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);

            Assert.InRange(e.ViewFor(pol).Me.Cooldowns!.Virus, 29f, 30f);
            StandAt(e, pol, "compost");
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new PlantVirusCommand("compost")).Reason);

            Advance(e, 30.5f);
            Assert.True(e.Apply(pol, new PlantVirusCommand("compost")).IsOk);
        }

        [Fact]
        public void CleaningAnotherSpot_LeavesThePlantedVirusAlone()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Plant(e, pol, "bins");

            Clean(e, cleaner, "tap", BinsMinS + 0.1f);

            Assert.Empty(Of<InfectedEvent>(e.Tick(0f)));
            Assert.Null(e.ViewFor(cleaner).Me.InfectedS);
            Assert.Equal(pol, e.Spots.Find("bins")!.VirusBy);
        }

        [Fact]
        public void Infected_ReachesZero_OutWithBodyPile()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            Vec2 where = P(e, cleaner).Position;

            List<GameEvent> events = Advance(e, 61f);

            Assert.False(P(e, cleaner).IsActive);
            Assert.Equal(OutCause.Virus, P(e, cleaner).OutCause);
            YouAreOutEvent outEv = Assert.Single(Of<YouAreOutEvent>(events));
            Assert.Equal(new[] { cleaner }, Arr(outEv.Recipients));
            Assert.Equal(OutCause.Virus, outEv.Cause);
            bool body = false;
            foreach (Pile pile in e.Piles.All)
            {
                if (pile.Kind == PileKind.Body && pile.IsPresent && pile.Position == where)
                {
                    body = true;
                }
            }

            Assert.True(body, "a body pile at the position");
            Assert.Contains(e.Timeline, t => t.Kind == TimelineKind.Infected && t.Seat == cleaner);
        }

        [Fact]
        public void Countdown_FrozenDuringMeeting()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            Advance(e, 10f);
            float before = e.ViewFor(cleaner).Me.InfectedS!.Value;
            Assert.True(e.Apply(cleaner, new CallMeetingCommand()).IsOk);
            Assert.Equal(MatchPhase.Meeting, e.Phase);

            Advance(e, 70f); // longer than the whole countdown
            Assert.True(P(e, cleaner).IsActive);
            Assert.Equal(before, e.ViewFor(cleaner).Me.InfectedS!.Value, 2);

            RunMeetingToEnd(e);
            Assert.True(P(e, cleaner).IsActive);
            Assert.InRange(e.ViewFor(cleaner).Me.InfectedS!.Value, before - 0.1f, before);
        }

        [Fact]
        public void VirusAge_FrozenDuringMeeting()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            byte caller = Citizen(e, 1);
            Plant(e, pol, "bins");
            Advance(e, 16f);
            StandAt(e, caller, "button");
            Assert.True(e.Apply(caller, new CallMeetingCommand()).IsOk);
            RunMeetingToEnd(e);

            Clean(e, cleaner, "bins", BinsMinS + 0.1f);

            // about 17 s old on active time (60 - 17.1 / 90 * 50 = 50.5), not 110 s old
            float countdown = e.ViewFor(cleaner).Me.InfectedS!.Value;
            Assert.InRange(countdown, 49f, 52f);
        }

        // ---------- heal ----------

        [Fact]
        public void Heal_Self_Cures_UsesHeal()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            P(e, cleaner).Heals = 1;

            Assert.True(e.Apply(cleaner, new HealCommand(cleaner)).IsOk);

            Assert.Equal(0, P(e, cleaner).Heals);
            Assert.Null(e.ViewFor(cleaner).Me.InfectedS);
            HealedEvent healed = Assert.Single(Of<HealedEvent>(e.Tick(0f)));
            Assert.Equal(new[] { cleaner }, Arr(healed.Recipients));
            Assert.Equal(cleaner, healed.By);
            Advance(e, 70f);
            Assert.True(P(e, cleaner).IsActive);
        }

        [Fact]
        public void Heal_AnotherInRange_CuresThem_ByTheHealer()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            byte healer = Citizen(e, 1);
            Infect(e, pol, cleaner);
            P(e, healer).Heals = 1;
            e.TestTeleport(healer, new Vec2(BinsPos.X + 1.2f, BinsPos.Y));

            Assert.True(e.Apply(healer, new HealCommand(cleaner)).IsOk);

            Assert.Null(e.ViewFor(cleaner).Me.InfectedS);
            Assert.Equal(0, P(e, healer).Heals);
            HealedEvent healed = Assert.Single(Of<HealedEvent>(e.Tick(0f)));
            Assert.Equal(new[] { cleaner }, Arr(healed.Recipients));
            Assert.Equal(healer, healed.By);
        }

        [Fact]
        public void Heal_OutOfRange_OutOfRange()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            byte healer = Citizen(e, 1);
            Infect(e, pol, cleaner);
            P(e, healer).Heals = 1;
            e.TestTeleport(healer, new Vec2(BinsPos.X + 2.5f, BinsPos.Y));

            Assert.Equal(RejectReason.OutOfRange, e.Apply(healer, new HealCommand(cleaner)).Reason);

            Assert.Equal(1, P(e, healer).Heals);
            Assert.NotNull(e.ViewFor(cleaner).Me.InfectedS);
        }

        [Fact]
        public void Heal_NotInfected_TargetInvalid()
        {
            MatchEngine e = Rig(out _, out byte cleaner);
            P(e, cleaner).Heals = 1;

            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cleaner, new HealCommand(cleaner)).Reason);

            Assert.Equal(1, P(e, cleaner).Heals);
        }

        [Fact]
        public void Heal_WithoutAHeal_NoItem()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);

            Assert.Equal(RejectReason.NoItem, e.Apply(cleaner, new HealCommand(cleaner)).Reason);

            Assert.NotNull(e.ViewFor(cleaner).Me.InfectedS);
        }

        /// <summary>The test map with a wall at x = 5, y = 6..8: a seat at (6.5, 7.5) is 3 tiles from the bins and cannot see them.</summary>
        private static MatchEngine WalledRig(out byte pol, out byte cleaner)
        {
            string[] rows = (string[])Rows.Clone();
            for (int y = 6; y <= 8; y++)
            {
                char[] row = rows[y].ToCharArray();
                row[5] = '#';
                rows[y] = new string(row);
            }

            MapData basis = Map();
            MapData map = FromAscii(rows, new List<ConsoleDef>(basis.Consoles), new List<ZoneDef>(basis.Zones), new List<Vec2>(basis.Spawns));
            MatchConfig cfg = Open();
            var e = new MatchEngine(cfg, map, Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42));
            e.Tick(e.Config.IntroS);
            pol = Polluter(e);
            cleaner = Citizen(e);
            P(e, pol).Ability = TrasherAbility.Virus;
            ParkOthers(e, pol);
            StandAt(e, pol, "bins");
            e.Tick(0f);
            return e;
        }

        [Fact]
        public void Heal_ReplyDoesNotTellWhetherAFarSeatIsInfected()
        {
            MatchEngine e = Rig(out byte pol, out byte infected);
            byte healer = Citizen(e, 1);
            byte healthy = Citizen(e, 2);
            Infect(e, pol, infected);
            P(e, healer).Heals = 1;
            e.TestTeleport(healer, new Vec2(15.5f, 8.5f));
            e.TestTeleport(healthy, new Vec2(3.5f, 2.5f));

            RejectReason far = e.Apply(healer, new HealCommand(infected)).Reason;
            RejectReason farHealthy = e.Apply(healer, new HealCommand(healthy)).Reason;
            RejectReason farTrasher = e.Apply(healer, new HealCommand(pol)).Reason;

            Assert.Equal(RejectReason.OutOfRange, far);
            Assert.Equal(far, farHealthy);
            Assert.Equal(far, farTrasher);
            Assert.Equal(1, P(e, healer).Heals);
            Assert.NotNull(P(e, infected).InfectedDeathAt);
        }

        [Fact]
        public void Heal_BehindAWall_SameReplyInfectedOrNot()
        {
            MatchEngine e = WalledRig(out byte pol, out byte infected);
            byte healer = Citizen(e, 1);
            byte healthy = Citizen(e, 2);
            Infect(e, pol, infected);
            P(e, healer).Heals = 1;
            // the wall fills x 5..6: both targets are within heal range of the healer but cannot be seen
            Vec2 healerAt = new Vec2(6.1f, 7.5f);
            e.TestTeleport(infected, new Vec2(4.9f, 7.5f));
            e.TestTeleport(healthy, new Vec2(4.9f, 8.0f));
            e.TestTeleport(healer, healerAt);
            Assert.True(Vec2.Distance(healerAt, P(e, infected).Position) <= e.Config.HealRange);
            Assert.True(Vec2.Distance(healerAt, P(e, healthy).Position) <= e.Config.HealRange);

            RejectReason sick = e.Apply(healer, new HealCommand(infected)).Reason;
            RejectReason well = e.Apply(healer, new HealCommand(healthy)).Reason;

            Assert.Equal(RejectReason.WallBetween, sick);
            Assert.Equal(sick, well);
            Assert.Equal(1, P(e, healer).Heals);
            Assert.NotNull(P(e, infected).InfectedDeathAt);
        }

        [Fact]
        public void Heal_OnATrasherInReach_TargetInvalid()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            P(e, cleaner).Heals = 1;
            e.TestTeleport(cleaner, new Vec2(BinsPos.X + 1f, BinsPos.Y));

            Assert.Equal(RejectReason.TargetInvalid, e.Apply(cleaner, new HealCommand(pol)).Reason);

            Assert.Equal(1, P(e, cleaner).Heals);
        }

        [Fact]
        public void SecondVirusOnAnInfectedCleaner_KeepsTheRunningCountdown_NoNewInfectedEvent()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            Advance(e, 5f);
            float running = e.ViewFor(cleaner).Me.InfectedS!.Value;
            byte pol2 = Polluter(e, 1);
            P(e, pol2).Ability = TrasherAbility.Virus;
            Plant(e, pol2, "compost");
            Advance(e, 40f); // a replacing virus (47 s old) would restart the countdown at about 34 s

            P(e, cleaner).Bags = 0;
            Clean(e, cleaner, "compost", CompostMinS + 0.1f);

            Assert.Empty(Of<InfectedEvent>(e.Tick(0f)));
            Assert.Null(e.Spots.Find("compost")!.VirusBy);
            Assert.True(P(e, cleaner).IsActive);
            // the first countdown kept running (42.1 s passed since the reading), it was not replaced by the second virus
            Assert.InRange(e.ViewFor(cleaner).Me.InfectedS!.Value, 0f, running - 42f);
        }

        [Fact]
        public void InfectedThenGassed_IsNotPutOutASecondTime()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            byte pol2 = Polluter(e, 1);
            P(e, pol2).Ability = TrasherAbility.Gas;
            e.TestTeleport(cleaner, new Vec2(2.5f, 2.5f));
            Assert.True(e.Apply(pol2, new GasCommand("gas_w")).IsOk);

            List<GameEvent> events = Advance(e, e.Config.GasWarningS + 1f);
            Assert.False(P(e, cleaner).IsActive);
            Assert.Equal(OutCause.Gas, P(e, cleaner).OutCause);
            Assert.Null(P(e, cleaner).InfectedDeathAt);
            Assert.Single(Of<YouAreOutEvent>(events));

            List<GameEvent> later = Advance(e, 70f); // past the old countdown

            Assert.Empty(Of<YouAreOutEvent>(later));
            Assert.Equal(OutCause.Gas, P(e, cleaner).OutCause);
            int bodies = 0;
            foreach (Pile pile in e.Piles.All)
            {
                if (pile.Kind == PileKind.Body)
                {
                    bodies++;
                }
            }

            Assert.Equal(1, bodies);
        }

        // ---------- infected meeting ----------

        [Fact]
        public void Infected_CallMeetingAnywhere_OnceOnly()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            e.TestTeleport(cleaner, Corner);
            Assert.False(e.Apply(cleaner, new CallMeetingCommand()).IsOk); // healthy: still only at the console
            Infect(e, pol, cleaner);
            e.TestTeleport(cleaner, Corner);
            int emergencies = e.EmergenciesLeft;

            Assert.True(e.Apply(cleaner, new CallMeetingCommand()).IsOk);

            Assert.Equal(MatchPhase.Meeting, e.Phase);
            Assert.Equal(MeetingCause.Emergency, e.Meeting!.Cause);
            Assert.Equal(cleaner, e.Meeting.CalledBy);
            Assert.Equal(emergencies, e.EmergenciesLeft);

            RunMeetingToEnd(e);
            Advance(e, e.Config.EmergencyMinSinceMeetingS + 1f);
            e.TestTeleport(cleaner, Corner);
            Assert.True(P(e, cleaner).IsActive);
            Assert.False(e.Apply(cleaner, new CallMeetingCommand()).IsOk);
            Assert.Equal(MatchPhase.Playing, e.Phase);
        }

        // ---------- voted out ----------

        private static void VoteOut(MatchEngine e, byte target)
        {
            Advance(e, e.Config.DiscussionS + 1f);
            foreach (PlayerState p in e.Players)
            {
                if (p.IsActive)
                {
                    Assert.True(e.Apply(p.Seat, new VoteCommand(target)).IsOk);
                }
            }

            RunMeetingToEnd(e);
        }

        [Fact]
        public void InfectedVotedOut_CountdownCleared()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            Infect(e, pol, cleaner);
            Assert.True(e.Apply(cleaner, new CallMeetingCommand()).IsOk);
            VoteOut(e, cleaner);
            Assert.False(P(e, cleaner).IsActive);
            Assert.Equal(OutCause.Vote, P(e, cleaner).OutCause);
            Assert.Null(P(e, cleaner).InfectedDeathAt);
            Assert.Null(e.ViewFor(cleaner).Me.InfectedS);

            List<GameEvent> later = Advance(e, 80f);

            Assert.Empty(Of<YouAreOutEvent>(later)); // no second out when the old countdown would have run out
            Assert.Equal(OutCause.Vote, P(e, cleaner).OutCause);
        }

        [Fact]
        public void PlanterVotedOut_VirusStaysDeadly()
        {
            MatchEngine e = Rig(out byte pol, out byte cleaner);
            byte caller = Citizen(e, 1);
            Plant(e, pol, "bins");
            Advance(e, 16f);
            StandAt(e, caller, "button");
            Assert.True(e.Apply(caller, new CallMeetingCommand()).IsOk);
            VoteOut(e, pol);
            Assert.False(P(e, pol).IsActive);
            Assert.Equal(pol, e.Spots.Find("bins")!.VirusBy);

            Clean(e, cleaner, "bins", BinsMinS + 0.1f);

            InfectedEvent infected = Assert.Single(Of<InfectedEvent>(e.Tick(0f)));
            Assert.Equal(cleaner, infected.Seat);
            Assert.NotNull(e.ViewFor(cleaner).Me.InfectedS);
        }
    }
}
