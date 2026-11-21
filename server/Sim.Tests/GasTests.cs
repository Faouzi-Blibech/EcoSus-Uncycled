using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>v2 Toxic Gas (spec section 6): warning, resolve on active time, mask, immunity, cooldown.</summary>
    public class GasTests
    {
        private static MatchConfig Cfg()
        {
            return new MatchConfig
            {
                TrasherStartCooldownS = 0f,
                SabotageStartCooldownS = 0f,
                UnlockAfter = 1,
                EmergencyMinSinceMeetingS = 0f,
                DiscussionS = 30f,
            };
        }

        /// <summary>Playing; one Trasher that re-polluted once and picked the gas. Everyone else is parked in the corner.</summary>
        private static byte GasTrasher(MatchEngine e, bool pick = true)
        {
            byte pol = Polluter(e);
            ParkOthers(e, pol);
            StandAt(e, pol, "bins");
            e.Tick(0f);
            e.Spots.Find("bins")!.State = SpotState.Clean;
            Assert.True(e.Apply(pol, new RepolluteCommand("bins")).IsOk);
            if (pick)
            {
                Assert.True(e.Apply(pol, new PickAbilityCommand(TrasherAbility.Gas)).IsOk);
            }

            e.TestTeleport(pol, Corner);
            e.Tick(0f);
            return pol;
        }

        private static readonly Vec2 InWest = new Vec2(2.5f, 2.5f);
        private static readonly Vec2 InWest2 = new Vec2(4.5f, 3.5f);
        private static readonly Vec2 InEast = new Vec2(15.5f, 2.5f);

        private static int CountBodies(MatchEngine e)
        {
            int n = 0;
            foreach (Pile p in e.Piles.All)
            {
                if (p.Kind == PileKind.Body)
                {
                    n++;
                }
            }

            return n;
        }

        [Fact]
        public void Gas_AfterWarning_UnmaskedCitizensInRoomOut()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            byte a = Citizen(e, 0);
            byte b = Citizen(e, 1);
            byte far = Citizen(e, 2);
            e.TestTeleport(a, InWest);
            e.TestTeleport(b, InWest2);
            e.TestTeleport(far, InEast);

            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);

            List<GameEvent> events = Advance(e, 0.05f);
            GasWarningEvent warn = Assert.Single(Of<GasWarningEvent>(events));
            Assert.Null(warn.Recipients);
            Assert.Equal("gas_w", warn.Room);
            Assert.Equal(3f, warn.InS, 2);
            SnapshotExtras mid = e.ExtrasFor(far);
            Assert.Equal("gas_w", mid.GasRoom);
            Assert.True(mid.GasInS > 2.8f && mid.GasInS < 3f);

            events = Advance(e, 2.7f);
            Assert.Empty(Of<YouAreOutEvent>(events));
            Assert.Empty(Of<GasDoneEvent>(events));
            Assert.True(P(e, a).IsActive);

            events = Advance(e, 0.5f);
            Assert.False(P(e, a).IsActive);
            Assert.False(P(e, b).IsActive);
            Assert.Equal(OutCause.Gas, P(e, a).OutCause);
            Assert.True(P(e, far).IsActive);
            Assert.True(P(e, pol).IsActive);
            List<YouAreOutEvent> outs = Of<YouAreOutEvent>(events);
            Assert.Equal(2, outs.Count);
            Assert.All(outs, o => Assert.Equal(OutCause.Gas, o.Cause));
            GasDoneEvent done = Assert.Single(Of<GasDoneEvent>(events));
            Assert.Null(done.Recipients);
            Assert.Equal("gas_w", done.Room);
            Assert.Equal(2, CountBodies(e));
            Assert.Null(e.ExtrasFor(far).GasRoom);
            Assert.True(e.ViewFor(pol).Me.Cooldowns!.Gas > 85f);
        }

        [Fact]
        public void Gas_MaskedCitizen_Survives_MaskUsed()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            byte masked = Citizen(e, 0);
            byte bare = Citizen(e, 1);
            e.TestTeleport(masked, InWest);
            e.TestTeleport(bare, InWest2);
            P(e, masked).Masks = 1;
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);

            List<GameEvent> events = Advance(e, 3.2f);

            Assert.True(P(e, masked).IsActive);
            Assert.Equal(0, P(e, masked).Masks);
            Assert.False(P(e, bare).IsActive);
            MaskUsedEvent used = Assert.Single(Of<MaskUsedEvent>(events));
            Assert.Equal(new[] { masked }, Arr(used.Recipients));
            Assert.Equal(masked, used.Seat);
            Assert.Single(Of<YouAreOutEvent>(events));
        }

        [Fact]
        public void Gas_PollutersImmune()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            byte partner = Polluter(e, 1);
            e.TestTeleport(partner, InWest);
            e.TestTeleport(pol, InWest2);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);

            List<GameEvent> events = Advance(e, 3.2f);

            Assert.True(P(e, partner).IsActive);
            Assert.True(P(e, pol).IsActive);
            Assert.Empty(Of<YouAreOutEvent>(events));
            Assert.Single(Of<GasDoneEvent>(events));
        }

        [Fact]
        public void Gas_WithoutAbility_NotUnlocked()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e, pick: false);
            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new GasCommand("gas_w")).Reason);

            Assert.True(e.Apply(pol, new PickAbilityCommand(TrasherAbility.Virus)).IsOk);
            Assert.Equal(RejectReason.NotUnlocked, e.Apply(pol, new GasCommand("gas_w")).Reason);
            Assert.Empty(Of<GasWarningEvent>(e.Tick(0f)));
        }

        [Fact]
        public void Gas_Citizen_NotAllowed()
        {
            MatchEngine e = Playing(Cfg());
            Assert.Equal(RejectReason.NotAllowed, e.Apply(Citizen(e), new GasCommand("gas_w")).Reason);
        }

        [Fact]
        public void Gas_UnknownRoom_TargetInvalid()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(pol, new GasCommand("west")).Reason); // a zone, not a gas room
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(pol, new GasCommand("nowhere")).Reason);
            Assert.Empty(Of<GasWarningEvent>(e.Tick(0f)));
            Assert.True(e.Apply(pol, new GasCommand("gas_e")).IsOk); // the refusals used no cooldown
        }

        [Fact]
        public void Gas_OnCooldown()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);
            Advance(e, 3.2f);

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new GasCommand("gas_e")).Reason);

            Advance(e, e.Config.GasCooldownS);
            Assert.True(e.Apply(pol, new GasCommand("gas_e")).IsOk);
        }

        [Fact]
        public void Gas_WhileAWarningRuns_OnCooldown()
        {
            MatchConfig cfg = Cfg();
            cfg.GasCooldownS = 1f; // shorter than the warning
            MatchEngine e = Playing(cfg);
            byte pol = GasTrasher(e);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);
            Advance(e, 1.5f);

            Assert.Equal(RejectReason.OnCooldown, e.Apply(pol, new GasCommand("gas_e")).Reason);
        }

        [Fact]
        public void Gas_WarningFrozenDuringMeeting()
        {
            MatchEngine e = Playing(Cfg());
            byte pol = GasTrasher(e);
            byte victim = Citizen(e, 0);
            byte caller = Citizen(e, 1);
            e.TestTeleport(victim, InWest);
            Assert.True(e.Apply(pol, new GasCommand("gas_w")).IsOk);
            Advance(e, 1f);
            e.TestTeleport(caller, ButtonPos);
            Assert.True(e.Apply(caller, new CallMeetingCommand()).IsOk);
            Assert.Equal(MatchPhase.Meeting, e.Phase);
            float before = e.ExtrasFor(victim).GasInS;

            List<GameEvent> events = Advance(e, 6f);

            Assert.Equal(MatchPhase.Meeting, e.Phase);
            Assert.Empty(Of<GasDoneEvent>(events));
            Assert.True(P(e, victim).IsActive);
            Assert.Equal(before, e.ExtrasFor(victim).GasInS, 3);
        }
    }
}
