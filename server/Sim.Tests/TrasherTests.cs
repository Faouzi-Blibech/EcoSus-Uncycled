using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>v2 Trasher kit: re-pollute, ability unlock and pick, machine jam and fix (spec sections 5 and 6).</summary>
    public class TrasherTests
    {
        private const float Cooldown = 20f;

        /// <summary>No opening locks: these tests act at once.</summary>
        private static MatchConfig Open()
        {
            return new MatchConfig { TrasherStartCooldownS = 0f };
        }

        /// <summary>The test map with an extra wall at x = 5, y = 6..8: a seat at (6.5, 7.5) is 3 tiles from the bins but cannot see them.</summary>
        private static MatchEngine Walled(MatchConfig cfg)
        {
            string[] rows = (string[])Rows.Clone();
            for (int y = 6; y <= 8; y++)
            {
                char[] row = rows[y].ToCharArray();
                row[5] = '#';
                rows[y] = new string(row);
            }

            MapData basis = Map();
            var zones = new List<ZoneDef>(basis.Zones);
            var spawns = new List<Vec2>(basis.Spawns);
            MapData map = FromAscii(rows, new List<ConsoleDef>(basis.Consoles), zones, spawns);
            var e = new MatchEngine(cfg, map, Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42));
            e.Tick(e.Config.IntroS);
            return e;
        }

        private static void MakeClean(MatchEngine e, string spot)
        {
            e.Spots.Find(spot)!.State = SpotState.Clean;
        }

        /// <summary>Playing, one Trasher parked on the bins, every other seat in the far corner.</summary>
        private static byte TrasherAtBins(MatchEngine e)
        {
            byte pol = Polluter(e);
            ParkOthers(e, pol);
            StandAt(e, pol, "bins");
            e.Tick(0f);
            return pol;
        }

        [Fact]
        public void Repollute_CleanSpot_DirtyAndMeterDown()
        {
            MatchEngine e = Walled(Open());
            byte pol = Polluter(e);
            byte seen = Citizen(e, 0);
            byte hidden = Citizen(e, 1);
            ParkOthers(e, pol, seen, hidden);
            StandAt(e, pol, "bins");
            e.TestTeleport(seen, new Vec2(3.5f, 5.5f));
            e.TestTeleport(hidden, new Vec2(6.5f, 7.5f));
            e.Meter.Add(30f);
            MakeClean(e, "bins");
            e.Tick(0f);

            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);

            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
            Assert.Equal(20f, e.Meter.Value, 3);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            CueEvent cue = Assert.Single(Of<CueEvent>(events));
            Assert.Equal(CueKind.Repollute, cue.Kind);
            Assert.Equal(pol, cue.Actor);
            Assert.Equal(new[] { seen }, Arr(cue.Recipients));
            RepolluteOkEvent ok = Assert.Single(Of<RepolluteOkEvent>(events));
            Assert.Equal(new[] { pol }, Arr(ok.Recipients));
            Assert.Equal(Cooldown, ok.Cooldown);
            Assert.Equal(1, ok.Count);
        }

        [Fact]
        public void Repollute_DirtySpot_SpotDirty()
        {
            MatchEngine e = Playing(Open());
            byte pol = TrasherAtBins(e);
            e.Meter.Add(30f);

            Assert.Equal(RejectReason.SpotDirty, e.Apply(pol, new RepolluteCommand("bins")).Reason);

            Assert.Equal(30f, e.Meter.Value, 3);
            Assert.Empty(Of<RepolluteOkEvent>(e.Tick(0f)));
            MakeClean(e, "bins");
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk); // the refusal used no cooldown
        }

        [Fact]
        public void Repollute_OnCooldown()
        {
            MatchEngine e = Playing(Open());
            byte pol = TrasherAtBins(e);
            MakeClean(e, "bins");
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);

            MakeClean(e, "bins");
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new RepolluteCommand("bins")).Reason);
            Assert.Equal(Cooldown, e.ViewFor(pol).Me.Cooldowns!.Repollute, 2);

            Advance(e, Cooldown + 0.1f);
            Assert.Equal(0f, e.ViewFor(pol).Me.Cooldowns!.Repollute, 2);
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
        }

        [Fact]
        public void Repollute_BeforeTheStartCooldown_OnCooldown()
        {
            MatchEngine e = Playing(new MatchConfig { TrasherStartCooldownS = 10f });
            byte pol = TrasherAtBins(e);
            MakeClean(e, "bins");
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new RepolluteCommand("bins")).Reason);
            Advance(e, 10.1f);
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
        }

        [Fact]
        public void Repollute_OutOfRangeOrUnknownSpot_Rejected()
        {
            MatchEngine e = Playing(Open());
            byte pol = TrasherAtBins(e);
            MakeClean(e, "compost");
            Assert.Equal(RejectReason.OutOfRange, e.Apply(pol, new RepolluteCommand("compost")).Reason);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(pol, new RepolluteCommand("button")).Reason);
            Assert.Equal(SpotState.Clean, e.Spots.Find("compost")!.State);
        }

        [Fact]
        public void Citizen_Repollute_NotAllowed()
        {
            MatchEngine e = Playing(Open());
            byte cit = Citizen(e);
            ParkOthers(e, cit);
            StandAt(e, cit, "bins");
            MakeClean(e, "bins");
            Assert.Equal(RejectReason.NotAllowed, e.Apply(cit, new RepolluteCommand("bins")).Reason);
            Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void ThirdRepollute_SendsAbilityUnlocked()
        {
            MatchEngine e = Playing(Open());
            byte pol = TrasherAtBins(e);
            for (int i = 1; i <= 3; i++)
            {
                MakeClean(e, "bins");
                Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
                IReadOnlyList<GameEvent> events = e.Tick(0f);
                List<AbilityUnlockedEvent> unlocked = Of<AbilityUnlockedEvent>(events);
                if (i < 3)
                {
                    Assert.Empty(unlocked);
                }
                else
                {
                    Assert.Equal(new[] { pol }, Arr(Assert.Single(unlocked).Recipients));
                    Assert.Equal(3, Assert.Single(Of<RepolluteOkEvent>(events)).Count);
                }

                Advance(e, Cooldown + 0.1f);
            }

            Assert.Equal(3, e.ViewFor(pol).Me.Repollutes);
            Assert.Equal(TrasherAbility.None, e.ViewFor(pol).Me.Ability);

            MakeClean(e, "bins");
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
            Assert.Empty(Of<AbilityUnlockedEvent>(e.Tick(0f))); // sent once, at the third
        }

        private static byte UnlockedTrasher(MatchEngine e)
        {
            byte pol = TrasherAtBins(e);
            for (int i = 0; i < 3; i++)
            {
                MakeClean(e, "bins");
                Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
                Advance(e, Cooldown + 0.1f);
            }

            e.Tick(0f);
            return pol;
        }

        [Fact]
        public void PickAbility_AfterUnlock_AbilityOk()
        {
            MatchEngine e = Playing(Open());
            byte pol = UnlockedTrasher(e);

            Assert.True(e.Apply(pol, new PickAbilityCommand(TrasherAbility.Gas)).IsOk);

            AbilityOkEvent ok = Assert.Single(Of<AbilityOkEvent>(e.Tick(0f)));
            Assert.Equal(new[] { pol }, Arr(ok.Recipients));
            Assert.Equal(TrasherAbility.Gas, ok.Ability);
            Assert.Equal(TrasherAbility.Gas, e.ViewFor(pol).Me.Ability);
        }

        [Fact]
        public void PickAbility_Twice_RejectedNotUnlocked()
        {
            MatchEngine e = Playing(Open());
            byte pol = UnlockedTrasher(e);
            Assert.True(e.Apply(pol, new PickAbilityCommand(TrasherAbility.Virus)).IsOk);

            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new PickAbilityCommand(TrasherAbility.Gas)).Reason);
            Assert.Equal(TrasherAbility.Virus, e.ViewFor(pol).Me.Ability);
        }

        [Fact]
        public void PickAbility_BeforeUnlock_NotUnlocked()
        {
            MatchEngine e = Playing(Open());
            byte pol = TrasherAtBins(e);
            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new PickAbilityCommand(TrasherAbility.Virus)).Reason);
            Assert.Equal(TrasherAbility.None, e.ViewFor(pol).Me.Ability);
        }

        [Fact]
        public void PickAbility_NoneOrCitizen_Rejected()
        {
            MatchEngine e = Playing(Open());
            byte pol = UnlockedTrasher(e);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(pol, new PickAbilityCommand(TrasherAbility.None)).Reason);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(Citizen(e), new PickAbilityCommand(TrasherAbility.Virus)).Reason);
        }

        [Fact]
        public void Repollute_ClosesTheOtherCleanersOpenSessionsOnThatSpot()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte a = Citizen(e, 0);
            byte b = Citizen(e, 1);
            ParkOthers(e, pol, a, b);
            StandAt(e, pol, "bins");
            StandAt(e, a, "bins");
            StandAt(e, b, "bins");
            Assert.True(e.Apply(a, new UseCommand("bins")).IsOk);
            Assert.True(e.Apply(b, new UseCommand("bins")).IsOk);
            Advance(e, 1.1f);
            Assert.True(e.Apply(b, new SpotDoneCommand("bins")).IsOk); // b cleans it; a still holds an open session

            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);

            Advance(e, 0.1f);
            CommandResult stale = e.Apply(a, new SpotDoneCommand("bins"));
            Assert.False(stale.IsOk);
            Assert.Equal(RejectReason.NotAllowed, stale.Reason);
            Assert.Equal(SpotState.Dirty, e.Spots.Find("bins")!.State);
            Assert.Equal(0, P(e, a).Bags);

            // a has to open the spot again and play the whole minimum time
            Assert.True(e.Apply(a, new UseCommand("bins")).IsOk);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(a, new SpotDoneCommand("bins")).Reason);
            Advance(e, 1.1f);
            Assert.True(e.Apply(a, new SpotDoneCommand("bins")).IsOk);
            Assert.Equal(SpotState.Clean, e.Spots.Find("bins")!.State);
        }

        [Fact]
        public void Repollute_LeavesSessionsOnOtherSpotsAlone()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte a = Citizen(e, 0);
            ParkOthers(e, pol, a);
            StandAt(e, pol, "bins");
            StandAt(e, a, "tap");
            Assert.True(e.Apply(a, new UseCommand("tap")).IsOk);
            MakeClean(e, "bins");
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
            Advance(e, 1.1f);
            Assert.True(e.Apply(a, new SpotDoneCommand("tap")).IsOk);
        }

        [Fact]
        public void SabotageOpeningLock_ComesFromTheTrasherStartCooldown()
        {
            MatchEngine e = Playing(new MatchConfig { TrasherStartCooldownS = 12f });
            byte pol = Polluter(e);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).Reason);
            Assert.Equal(12f, e.ViewFor(pol).Me.Cooldowns!.Sabotage, 2);
            Advance(e, 12.1f);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).IsOk);
        }

        // ---------- machine jam and fix ----------

        [Fact]
        public void SabotageMachine_DeliveryRejected_MachineJammed()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, cit);
            P(e, cit).BagCap = 3;
            P(e, cit).Bags = 2;
            StandAt(e, cit, "machine");
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            e.Tick(0f);

            CommandResult r = e.Apply(cit, new UseCommand("machine"));

            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.MachineJammed, r.Reason);
            Assert.Equal(2, P(e, cit).Bags);
            Assert.Equal(0f, e.Meter.Value);
            Assert.False(P(e, cit).UpgradeOfferOpen);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            Assert.Empty(Of<DeliveredEvent>(events));
            Assert.Empty(Of<UpgradeOfferEvent>(events));
            Assert.Empty(Of<UseOkEvent>(events));
        }

        [Fact]
        public void Delivery_WorksAgainAfterTheFixHold()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, cit);
            P(e, cit).BagCap = 3;
            P(e, cit).Bags = 2;
            StandAt(e, cit, "machine");
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 3.5f);
            Assert.Equal(RejectReason.MachineJammed, e.Apply(cit, new UseCommand("machine")).Reason); // still jammed mid-hold
            Advance(e, 0.6f);
            Assert.False(e.Machine.Jammed);
            float before = e.Meter.Value;

            Assert.True(e.Apply(cit, new UseCommand("machine")).IsOk);

            Assert.Equal(0, P(e, cit).Bags);
            Assert.Equal(before + 2 * e.Config.BagMeterAdd, e.Meter.Value, 3);
        }

        [Fact]
        public void SabotageMachine_Jams_SharesTheCooldownAndTellsEveryone()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            e.Tick(0f);

            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);

            Assert.True(e.Machine.Jammed);
            SabotageStartedEvent started = Assert.Single(Of<SabotageStartedEvent>(e.Tick(0f)));
            Assert.Equal(SabotageKind.Machine, started.Kind);
            Assert.Null(started.Countdown);
            Assert.Null(started.Recipients); // everyone
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Smog)).Reason);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).Reason);
        }

        [Fact]
        public void SabotageMachine_ByCitizenOrOnCooldown_Rejected()
        {
            MatchEngine e = Playing(new MatchConfig());
            Assert.Equal(RejectReason.NotAllowed, e.Apply(Citizen(e), new SabotageCommand(SabotageKind.Machine)).Reason);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(Polluter(e), new SabotageCommand(SabotageKind.Machine)).Reason);
            Assert.False(e.Machine.Jammed);
        }

        private static (MatchEngine e, byte cit) Jammed()
        {
            MatchEngine e = Playing(Open());
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, cit);
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            StandAt(e, cit, "machine");
            e.Tick(0f);
            return (e, cit);
        }

        [Fact]
        public void FixMachine_HoldFourSeconds_Unjams()
        {
            (MatchEngine e, byte cit) = Jammed();
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);

            List<GameEvent> early = Advance(e, 3.8f);
            Assert.True(e.Machine.Jammed);
            Assert.Equal(cit, e.Machine.RepairSeat);
            Assert.Empty(Of<SabotageEndedEvent>(early));

            List<GameEvent> late = Advance(e, 0.4f);
            Assert.False(e.Machine.Jammed);
            Assert.Null(e.Machine.RepairSeat);
            SabotageEndedEvent ended = Assert.Single(Of<SabotageEndedEvent>(late));
            Assert.Equal(SabotageKind.Machine, ended.Kind);
            Assert.Equal(SabotageEndResult.Fixed, ended.Result);
            Assert.Null(ended.Recipients);
            // the shared cooldown restarts at the end of the jam
            Assert.InRange(e.ViewFor(Polluter(e)).Me.Cooldowns!.Sabotage, e.Config.SabotageSharedCooldownS - 0.5f, e.Config.SabotageSharedCooldownS);
        }

        [Fact]
        public void FixMachine_LeaveRange_RepairDropped()
        {
            (MatchEngine e, byte cit) = Jammed();
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 3f);
            Assert.Equal(3f, e.Machine.RepairElapsed, 1);

            e.TestTeleport(cit, Corner);
            Advance(e, 0.1f);
            Assert.Null(e.Machine.RepairSeat);
            Assert.Equal(0f, e.Machine.RepairElapsed);
            Advance(e, 2f);
            Assert.True(e.Machine.Jammed);

            // back in range the hold starts again from zero
            StandAt(e, cit, "machine");
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 3.5f);
            Assert.True(e.Machine.Jammed);
            Advance(e, 0.6f);
            Assert.False(e.Machine.Jammed);
        }

        [Fact]
        public void FixMachine_ReleasingTheHold_DropsTheRepair()
        {
            (MatchEngine e, byte cit) = Jammed();
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 2f);
            Assert.True(e.Apply(cit, new FixCommand("machine", false)).IsOk);
            Assert.Null(e.Machine.RepairSeat);
            Advance(e, 3f);
            Assert.True(e.Machine.Jammed);
        }

        [Fact]
        public void FixMachine_Rejections()
        {
            (MatchEngine e, byte cit) = Jammed();
            byte other = Citizen(e, 1);
            StandAt(e, other, "machine");
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Assert.Equal(RejectReason.NotAllowed, e.Apply(other, new FixCommand("machine", true)).Reason); // one repairer at a time
            Assert.Equal(RejectReason.NotAllowed, e.Apply(Polluter(e), new FixCommand("machine", true)).Reason);
            e.TestTeleport(cit, Corner);
            Assert.Equal(RejectReason.OutOfRange, e.Apply(cit, new FixCommand("machine", true)).Reason);

            MatchEngine clear = Playing(Open());
            byte c = Citizen(clear);
            StandAt(clear, c, "machine");
            Assert.Equal(RejectReason.NotAllowed, clear.Apply(c, new FixCommand("machine", true)).Reason); // nothing to fix
        }

        [Fact]
        public void Machine_RepairProgressGoesToTheRepairerOnly_JamToEveryone()
        {
            (MatchEngine e, byte cit) = Jammed();
            byte other = Citizen(e, 1);
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 1.5f);

            Assert.Equal(1.5f, e.ExtrasFor(cit).MachineRepairElapsed!.Value, 1);
            Assert.Null(e.ExtrasFor(other).MachineRepairElapsed);
            Assert.Null(e.ExtrasFor(Polluter(e)).MachineRepairElapsed);
            Assert.True(e.ExtrasFor(cit).MachineJammed);
            Assert.True(e.ExtrasFor(other).MachineJammed);
            Assert.True(e.ExtrasFor(Polluter(e)).MachineJammed);
        }

        [Fact]
        public void Machine_AMeetingDropsTheRepairButKeepsTheJam()
        {
            MatchEngine e = Playing(new MatchConfig { TrasherStartCooldownS = 0f, DiscussionS = 1f, VotingS = 1f, ResultsS = 1f });
            byte pol = Polluter(e);
            byte cit = Citizen(e);
            ParkOthers(e, cit);
            Advance(e, 15.2f); // the emergency button needs 15 s since the start
            Assert.True(e.Apply(pol, new SabotageCommand(SabotageKind.Machine)).IsOk);
            StandAt(e, cit, "machine");
            Assert.True(e.Apply(cit, new FixCommand("machine", true)).IsOk);
            Advance(e, 1f);
            Assert.Equal(cit, e.Machine.RepairSeat);

            StandAt(e, cit, "button");
            Assert.True(e.Apply(cit, new CallMeetingCommand()).IsOk);

            Assert.Null(e.Machine.RepairSeat);
            Assert.True(e.Machine.Jammed);
        }
    }
}
