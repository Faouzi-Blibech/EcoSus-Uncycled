using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>v2 rules: Recycling Machine delivery, upgrade offers and picks (spec section 5).</summary>
    public class MachineTests
    {
        private static MatchEngine Deliverer(out byte c, int bags, int cap = 3)
        {
            MatchEngine e = Playing();
            c = Citizen(e);
            e.Tick(0f);
            P(e, c).BagCap = cap;
            P(e, c).Bags = bags;
            StandAt(e, c, "machine");
            return e;
        }

        [Fact]
        public void Deliver_AddsBagMeterAddPerBag()
        {
            MatchEngine e = Deliverer(out byte c, 2);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);

            Assert.Equal(2 * e.Config.BagMeterAdd, e.Meter.Value, 3);
            Assert.Equal(20f, e.Meter.Value, 3);
            Assert.Equal(0, P(e, c).Bags);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            DeliveredEvent d = Assert.Single(Of<DeliveredEvent>(events));
            Assert.Equal(new[] { c }, Arr(d.Recipients));
            Assert.Equal(2, d.Bags);
            Assert.Equal(20f, d.Meter, 3);
            UpgradeOfferEvent offer = Assert.Single(Of<UpgradeOfferEvent>(events));
            Assert.Equal(new[] { c }, Arr(offer.Recipients));
            Assert.Equal(
                new[] { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Heal, UpgradeKind.Mask },
                new List<UpgradeKind>(offer.Items));
            Assert.True(P(e, c).UpgradeOfferOpen);
            UseOkEvent ok = Assert.Single(Of<UseOkEvent>(events));
            Assert.Equal(ConsoleKind.Machine, ok.Kind);
        }

        [Fact]
        public void Deliver_NoBags_NoBags()
        {
            MatchEngine e = Deliverer(out byte c, 0);
            CommandResult r = e.Apply(c, new UseCommand("machine"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NoBags, r.Reason);
            Assert.Equal(0f, e.Meter.Value);
            Assert.False(P(e, c).UpgradeOfferOpen);
            Assert.Empty(Of<UpgradeOfferEvent>(e.Tick(0f)));
        }

        [Fact]
        public void Deliver_OutOfRange_Rejected()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            e.TestTeleport(c, Corner);
            CommandResult r = e.Apply(c, new UseCommand("machine"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.OutOfRange, r.Reason);
            Assert.Equal(1, P(e, c).Bags);
        }

        [Fact]
        public void Deliver_ByATrasher_NotAllowed()
        {
            MatchEngine e = Playing();
            byte t = Polluter(e);
            StandAt(e, t, "machine");
            CommandResult r = e.Apply(t, new UseCommand("machine"));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NotAllowed, r.Reason);
        }

        [Fact]
        public void Offer_OmitsOwnedBackpackAndTool()
        {
            MatchEngine e = Deliverer(out byte c, 1, cap: 3);
            P(e, c).HasBackpack = true;
            P(e, c).HasTool = true;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            UpgradeOfferEvent offer = Assert.Single(Of<UpgradeOfferEvent>(e.Tick(0f)));
            Assert.Equal(new[] { UpgradeKind.Heal, UpgradeKind.Mask }, new List<UpgradeKind>(offer.Items));
        }

        [Fact]
        public void PickUpgrade_Backpack_CapThree()
        {
            MatchEngine e = Deliverer(out byte c, 1, cap: 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            e.Tick(0f);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Backpack)).IsOk);
            Assert.Equal(e.Config.BackpackCap, P(e, c).BagCap);
            Assert.Equal(3, P(e, c).BagCap);
            Assert.True(P(e, c).HasBackpack);
            Assert.False(P(e, c).UpgradeOfferOpen);
            UpgradeOkEvent ok = Assert.Single(Of<UpgradeOkEvent>(e.Tick(0f)));
            Assert.Equal(UpgradeKind.Backpack, ok.Item);
            Assert.Equal(new[] { c }, Arr(ok.Recipients));
        }

        [Fact]
        public void PickUpgrade_Tool_SetsHasTool()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Tool)).IsOk);
            Assert.True(P(e, c).HasTool);
        }

        [Fact]
        public void PickUpgrade_Mask_AddsOne()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask)).IsOk);
            Assert.Equal(1, P(e, c).Masks);
        }

        [Fact]
        public void Offer_OmitsAHeldHealAndAHeldMask()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            P(e, c).Heals = 1;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            UpgradeOfferEvent offer = Assert.Single(Of<UpgradeOfferEvent>(e.Tick(0f)));
            Assert.Equal(new[] { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Mask }, new List<UpgradeKind>(offer.Items));
        }

        [Fact]
        public void Deliver_WithEverythingOwned_AddsMeterAndOpensNoOffer()
        {
            MatchEngine e = Deliverer(out byte c, 2);
            P(e, c).HasBackpack = true;
            P(e, c).HasTool = true;
            P(e, c).Heals = 1;
            P(e, c).Masks = 1;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);

            Assert.Equal(20f, e.Meter.Value, 3);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            Assert.Single(Of<DeliveredEvent>(events));
            Assert.Empty(Of<UpgradeOfferEvent>(events));
            Assert.False(P(e, c).UpgradeOfferOpen);
            Assert.Null(e.OpenUpgradeOffer(c));
        }

        [Fact]
        public void PickUpgrade_ItemNotInTheOffer_NoItem_AndTheOfferStaysOpen()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            P(e, c).HasTool = true;
            P(e, c).Heals = 1;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);

            foreach (UpgradeKind notOffered in new[] { UpgradeKind.Tool, UpgradeKind.Heal })
            {
                CommandResult r = e.Apply(c, new PickUpgradeCommand(notOffered));
                Assert.False(r.IsOk);
                Assert.Equal(RejectReason.NoItem, r.Reason);
            }

            Assert.Equal(1, P(e, c).Heals);
            Assert.True(P(e, c).UpgradeOfferOpen);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask)).IsOk);
        }

        [Fact]
        public void PickUpgrade_HealThenSecondDelivery_HealIsNotOfferedAgain()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Heal)).IsOk);
            Assert.Equal(1, P(e, c).Heals);
            e.Tick(0f);

            P(e, c).Bags = 1;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            UpgradeOfferEvent offer = Assert.Single(Of<UpgradeOfferEvent>(e.Tick(0f)));
            Assert.DoesNotContain(UpgradeKind.Heal, offer.Items);
            Assert.Equal(RejectReason.NoItem, e.Apply(c, new PickUpgradeCommand(UpgradeKind.Heal)).Reason);
        }

        [Fact]
        public void PickUpgrade_WithoutOffer_NoItem()
        {
            MatchEngine e = Deliverer(out byte c, 0);
            CommandResult r = e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask));
            Assert.False(r.IsOk);
            Assert.Equal(RejectReason.NoItem, r.Reason);
            Assert.Equal(0, P(e, c).Masks);
        }

        [Fact]
        public void PickUpgrade_OnlyOncePerOffer()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask)).IsOk);
            Assert.False(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask)).IsOk);
            Assert.Equal(1, P(e, c).Masks);
        }

        [Fact]
        public void SecondDeliveryWhileOfferOpen_AddsMeter_ResendsTheSameOffer()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            UpgradeOfferEvent first = Assert.Single(Of<UpgradeOfferEvent>(e.Tick(0f)));
            P(e, c).Bags = 2;
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);

            Assert.Equal(30f, e.Meter.Value, 3);
            IReadOnlyList<GameEvent> events = e.Tick(0f);
            Assert.Single(Of<DeliveredEvent>(events));
            UpgradeOfferEvent again = Assert.Single(Of<UpgradeOfferEvent>(events));
            Assert.Equal(new List<UpgradeKind>(first.Items), new List<UpgradeKind>(again.Items));
            Assert.Equal(new[] { c }, Arr(again.Recipients));

            // still one offer: a single pick closes it
            Assert.True(e.Apply(c, new PickUpgradeCommand(UpgradeKind.Mask)).IsOk);
            Assert.False(P(e, c).UpgradeOfferOpen);
            Assert.Null(e.OpenUpgradeOffer(c));
        }

        [Fact]
        public void OpenUpgradeOffer_ReturnsTheStoredItemsUntilPicked()
        {
            MatchEngine e = Deliverer(out byte c, 1);
            Assert.Null(e.OpenUpgradeOffer(c));
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            Assert.Equal(
                new[] { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Heal, UpgradeKind.Mask },
                new List<UpgradeKind>(e.OpenUpgradeOffer(c)!));
        }

        [Fact]
        public void MatchConfig_DefaultMeterPassiveIsZero()
        {
            Assert.Equal(0f, new MatchConfig().MeterPassivePerS);
        }

        [Fact]
        public void Deliver_FillingTheMeter_CitizensWinMeterFull()
        {
            MatchEngine e = Deliverer(out byte c, 3);
            e.Meter.Add(75f);
            Assert.True(e.Apply(c, new UseCommand("machine")).IsOk);
            GameOverEvent over = Assert.Single(Of<GameOverEvent>(e.Tick(0f)));
            Assert.Equal(Winner.Citizens, over.Winner);
            Assert.Equal(GameOverReason.MeterFull, over.Reason);
        }
    }
}
