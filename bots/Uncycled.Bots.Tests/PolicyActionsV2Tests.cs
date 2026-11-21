using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    /// <summary>The v2 policy action list the bot networks choose from: its mask, its hints and the schema hash.</summary>
    public class PolicyActionsV2Tests
    {
        private const int FirstCleanerOnly = 16;
        private const int LastCleanerOnly = 25;
        private const int FirstTrasherOnly = 26;
        private const int CleanNearest = 17;
        private const int CleanSecond = 18;
        private const int FakeCleanNearest = 26;
        private const int RepolluteNearest = 27;
        private const int RepolluteSecond = 28;
        private const int PickBackpack = 21;
        private const int PickTool = 22;
        private const int PickHeal = 23;
        private const int PickMask = 24;
        private const int PickVirus = 29;
        private const int PickGas = 30;
        private const int GasBusiestRoom = 32;

        private readonly ITestOutputHelper _out;

        public PolicyActionsV2Tests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static SeatView WithSpots(SeatView v, IReadOnlyList<SpotView> spots) =>
            new SeatView(v.Seat, v.Phase, v.PhaseTimeLeft, v.MatchTime, v.DeadlineS, v.DeadlineClock, v.DeadlineClockValue, v.Meter, v.EmergenciesLeft,
                v.Me, v.Players, v.Piles, v.Sabotage, v.Meeting, v.GameOver, v.GhostChat, spots, v.MachineJammed);

        private static SeatView WithPlayers(SeatView v, IReadOnlyList<PlayerView> players, MyView? me = null) =>
            new SeatView(v.Seat, v.Phase, v.PhaseTimeLeft, v.MatchTime, v.DeadlineS, v.DeadlineClock, v.DeadlineClockValue, v.Meter, v.EmergenciesLeft,
                me ?? v.Me, players, v.Piles, v.Sabotage, v.Meeting, v.GameOver, v.GhostChat, v.Spots, v.MachineJammed);

        private static MyView AsGasTrasher(MyView me, Vec2? position = null) =>
            new MyView(me.Seat, me.Role, me.Partners, me.Signature, me.Status, position ?? me.Position, me.Vision, me.Poison,
                new CooldownView(0f, 0f, 0f), me.Tasks, me.Bags, me.BagCap, me.HasTool, 5, TrasherAbility.Gas);

        private static List<SpotView> SpotsWith(SeatView v, params string[] dirty) =>
            v.Spots.Select(s => new SpotView(s.Id, dirty.Contains(s.Id) ? SpotState.Dirty : SpotState.Clean)).ToList();

        /// <summary>The spot ids ordered by walking distance from the seat (ties by id), as the tests expect the policy to rank them.</summary>
        private static List<string> ByDistance(TestMatch m, SeatView v) =>
            v.Spots.Select(s => s.Id)
                .Select(id => new { id, d = MapDistances.For(m.Map).Path(v.Me.Position, m.Map.FindConsole(id)!.Position) })
                .Where(x => x.d != MapDistances.Unreachable)
                .OrderBy(x => x.d).ThenBy(x => x.id, StringComparer.Ordinal)
                .Select(x => x.id).ToList();

        [Fact]
        public void Names_AreUnique_Count35()
        {
            Assert.Equal(35, PolicyActionsV2.Count);
            Assert.Equal(35, PolicyActionsV2.Names.Count);
            Assert.Equal(35, PolicyActionsV2.Names.Distinct().Count());
            Assert.Equal("Stay", PolicyActionsV2.Names[0]);
            Assert.Equal("SabotageMachine", PolicyActionsV2.Names[34]);
        }

        [Fact]
        public void OutsidePlaying_OnlyStay()
        {
            TestMatch m = TestMatch.Create(3, startPlaying: false);
            SeatView view = m.Engine.ViewFor(0);
            Assert.NotEqual(MatchPhase.Playing, view.Phase);

            bool[] mask = PolicyActionsV2.Mask(view, new BotMemory(m.Map, m.Config), m.Map);

            Assert.Equal(PolicyActionsV2.Count, mask.Length);
            Assert.True(mask[0]);
            Assert.Equal(1, mask.Count(x => x));
        }

        [Fact]
        public void Mask_ValidPolicyIndexIsValidInFullMask()
        {
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 20; seed++)
            {
                TestMatch m = TestMatch.Create(seed, startPlaying: false);
                var rig = new BrainRig(m, seed);
                int max = (int)(150f / TestMatch.Dt);
                for (int i = 0; i < max && m.Engine.Phase != MatchPhase.Ended; i++)
                {
                    rig.Step();
                    if (i % 10 != 0)
                    {
                        continue;
                    }

                    for (byte seat = 0; seat < m.Config.Seats; seat++)
                    {
                        BotMemory? mem = ((ScriptedBrain)rig.Brains[seat]).Memory;
                        if (mem == null)
                        {
                            continue;
                        }

                        SeatView view = m.Engine.ViewFor(seat);
                        bool[] mask = PolicyActionsV2.Mask(view, mem, m.Map);
                        bool[] full = ActionCatalogue.FullMask(view, mem, m.Map);
                        for (int p = 0; p < mask.Length; p++)
                        {
                            if (!mask[p])
                            {
                                continue;
                            }

                            seen.Add(p);
                            BotAction action = PolicyActionsV2.Resolve(p, view, mem, m.Map).Action;
                            Assert.True(full[(int)action], "seed " + seed + " seat " + seat + " t=" + m.Engine.MatchTime.ToString("0.0") + ": policy " + PolicyActionsV2.Names[p] + " -> " + action + " is not valid in FullMask");
                        }
                    }
                }
            }

            _out.WriteLine("policy indices that were valid at least once: " + string.Join(",", seen.OrderBy(x => x)));
            Assert.True(seen.Count >= 8, "the scripted matches exercised only " + seen.Count + " policy indices");
        }

        [Fact]
        public void Mask_RoleEntriesOff_ForOtherRole()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            byte pol = m.FirstSeatOf(Role.Polluter);

            // dirty spots exist at the start, so GoDirtySpot is valid for both roles: the policy must still split it by role
            bool[] cleaner = PolicyActionsV2.Mask(m.Engine.ViewFor(cit), new BotMemory(m.Map, m.Config), m.Map);
            bool[] trasher = PolicyActionsV2.Mask(m.Engine.ViewFor(pol), new BotMemory(m.Map, m.Config), m.Map);

            Assert.True(cleaner[CleanNearest], "the Cleaner should be able to clean at the start");
            Assert.True(trasher[FakeCleanNearest], "the Trasher should be able to fake-clean at the start");
            for (int i = FirstTrasherOnly; i < PolicyActionsV2.Count; i++)
            {
                Assert.False(cleaner[i], "Cleaner has " + PolicyActionsV2.Names[i]);
            }

            for (int i = FirstCleanerOnly; i <= LastCleanerOnly; i++)
            {
                Assert.False(trasher[i], "Trasher has " + PolicyActionsV2.Names[i]);
            }
        }

        [Fact]
        public void CleanSecond_NeedsTwoDirtySpots()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            SeatView live = m.Engine.ViewFor(cit);
            List<string> order = ByDistance(m, live);
            Assert.True(order.Count >= 2);
            var mem = new BotMemory(m.Map, m.Config);

            SeatView one = WithSpots(live, SpotsWith(live, order[1]));
            bool[] maskOne = PolicyActionsV2.Mask(one, mem, m.Map);
            Assert.True(maskOne[CleanNearest]);
            Assert.False(maskOne[CleanSecond]);

            SeatView two = WithSpots(live, SpotsWith(live, order[0], order[1]));
            bool[] maskTwo = PolicyActionsV2.Mask(two, mem, m.Map);
            Assert.True(maskTwo[CleanNearest]);
            Assert.True(maskTwo[CleanSecond]);
            Assert.Equal(order[0], PolicyActionsV2.Resolve(CleanNearest, two, mem, m.Map).Spot);
            Assert.Equal(order[1], PolicyActionsV2.Resolve(CleanSecond, two, mem, m.Map).Spot);
        }

        [Fact]
        public void RepolluteSecond_NeedsTwoCleanSpots()
        {
            TestMatch m = TestMatch.Create(2);
            byte pol = m.FirstSeatOf(Role.Polluter);
            SeatView start = m.Engine.ViewFor(pol);
            SeatView live = WithPlayers(start, start.Players, AsGasTrasher(start.Me)); // cooldowns ready (a Trasher starts on a cooldown)
            List<string> order = ByDistance(m, live);
            var mem = new BotMemory(m.Map, m.Config);
            IEnumerable<string> allExcept(params string[] clean) => order.Where(id => !clean.Contains(id));

            SeatView one = WithSpots(live, SpotsWith(live, allExcept(order[0]).ToArray()));
            bool[] maskOne = PolicyActionsV2.Mask(one, mem, m.Map);
            Assert.True(maskOne[RepolluteNearest]);
            Assert.False(maskOne[RepolluteSecond]);

            SeatView two = WithSpots(live, SpotsWith(live, allExcept(order[0], order[1]).ToArray()));
            bool[] maskTwo = PolicyActionsV2.Mask(two, mem, m.Map);
            Assert.True(maskTwo[RepolluteSecond]);
            Assert.Equal(order[1], PolicyActionsV2.Resolve(RepolluteSecond, two, mem, m.Map).Spot);
        }

        [Fact]
        public void PickUpgrade_OnlyKindsInTheOffer()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            SeatView view = m.Engine.ViewFor(cit);
            var mem = new BotMemory(m.Map, m.Config);

            bool[] none = PolicyActionsV2.Mask(view, mem, m.Map);
            Assert.False(none[PickBackpack] || none[PickTool] || none[PickHeal] || none[PickMask]);

            mem.UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Backpack, UpgradeKind.Mask };
            bool[] offer = PolicyActionsV2.Mask(view, mem, m.Map);
            Assert.True(offer[PickBackpack]);
            Assert.False(offer[PickTool]);
            Assert.False(offer[PickHeal]);
            Assert.True(offer[PickMask]);
            PolicyChoice pick = PolicyActionsV2.Resolve(PickMask, view, mem, m.Map);
            Assert.Equal(BotAction.PickUpgrade, pick.Action);
            Assert.Equal(UpgradeKind.Mask, pick.Upgrade);
        }

        [Fact]
        public void GasBusiestRoom_MaskedOff_WithoutPlayersInAGasRoom()
        {
            TestMatch m = TestMatch.Create(2);
            byte pol = m.FirstSeatOf(Role.Polluter);
            SeatView live = m.Engine.ViewFor(pol);
            MyView me = AsGasTrasher(live.Me);
            var mem = new BotMemory(m.Map, m.Config);
            ZoneDef room = m.Map.GasRooms[0];
            Vec2 inside = new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f);
            Vec2 outside = new Vec2(-5f, -5f);
            Assert.DoesNotContain(m.Map.GasRooms, r => r.Contains(outside));

            SeatView empty = WithPlayers(live, new List<PlayerView>(), me);
            Assert.False(PolicyActionsV2.Mask(empty, mem, m.Map)[GasBusiestRoom]);

            SeatView elsewhere = WithPlayers(live, new List<PlayerView> { new PlayerView(1, outside, 1, false) }, me);
            Assert.False(PolicyActionsV2.Mask(elsewhere, mem, m.Map)[GasBusiestRoom]);

            SeatView occupied = WithPlayers(live, new List<PlayerView> { new PlayerView(1, inside, 1, false) }, me);
            Assert.True(PolicyActionsV2.Mask(occupied, mem, m.Map)[GasBusiestRoom]);
            PolicyChoice gas = PolicyActionsV2.Resolve(GasBusiestRoom, occupied, mem, m.Map);
            Assert.Equal(BotAction.GasRoom, gas.Action);
            Assert.Equal(room.Id, gas.Room);
        }

        [Fact]
        public void Mask_MapWithoutGasRooms_GasMaskedOff()
        {
            TestMatch m = TestMatch.Create(2);
            MapData bare = SyntheticBot.TinyMap(20, 20, null, new List<ConsoleDef>());
            Assert.Empty(bare.GasRooms);
            var mem = new BotMemory(bare, m.Config);
            var me = new MyView(0, Role.Polluter, new List<byte>(), null, PlayerStatus.Active, new Vec2(10.5f, 10.5f), 4f, null,
                new CooldownView(0f, 0f, 0f), new List<TaskInfo>(), 0, 0, false, 5, TrasherAbility.Gas);
            var view = new SeatView(0, MatchPhase.Playing, 0f, 30f, 600f, DeadlineClock.MatchTime, 30f, 10f, 2, me,
                new List<PlayerView> { new PlayerView(1, new Vec2(11.5f, 10.5f), 1, false) }, new List<PileView>(), null, null, null);

            bool[] mask = PolicyActionsV2.Mask(view, mem, bare);

            Assert.Equal(PolicyActionsV2.Count, mask.Length);
            Assert.False(mask[GasBusiestRoom]);
            Assert.False(mask[1] || mask[2] || mask[3], "no zones on this map, so no GoZone entries");
        }

        [Fact]
        public void PickAbility_OnlyWhenUnlocked_AndOnePerAbility()
        {
            TestMatch m = TestMatch.Create(2);
            byte pol = m.FirstSeatOf(Role.Polluter);
            SeatView live = m.Engine.ViewFor(pol);
            var mem = new BotMemory(m.Map, m.Config);
            Assert.False(PolicyActionsV2.Mask(live, mem, m.Map)[PickVirus]);

            MyView unlocked = new MyView(live.Me.Seat, live.Me.Role, live.Me.Partners, live.Me.Signature, live.Me.Status, live.Me.Position, live.Me.Vision,
                live.Me.Poison, live.Me.Cooldowns, live.Me.Tasks, 0, 0, false, m.Config.UnlockAfter);
            SeatView view = WithPlayers(live, live.Players, unlocked);
            bool[] mask = PolicyActionsV2.Mask(view, mem, m.Map);
            Assert.True(mask[PickVirus]);
            Assert.True(mask[PickGas]);
            Assert.Equal(TrasherAbility.Virus, PolicyActionsV2.Resolve(PickVirus, view, mem, m.Map).Ability);
            Assert.Equal(TrasherAbility.Gas, PolicyActionsV2.Resolve(PickGas, view, mem, m.Map).Ability);
        }

        [Fact]
        public void Resolve_SetsHints_ApplyToSetsExecutor()
        {
            TestMatch m = TestMatch.Create(2);
            byte cit = m.FirstSeatOf(Role.Citizen);
            SeatView live = m.Engine.ViewFor(cit);
            List<string> order = ByDistance(m, live);
            SeatView view = WithSpots(live, SpotsWith(live, order[0], order[1]));
            var mem = new BotMemory(m.Map, m.Config);
            var exec = new ActionExecutor(new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS), new MoveSender()) { Memory = mem };

            PolicyChoice clean = PolicyActionsV2.Resolve(CleanNearest, view, mem, m.Map);
            clean.ApplyTo(exec);
            Assert.Equal(BotAction.GoDirtySpot, clean.Action);
            Assert.Equal(order[0], exec.SpotHint);

            mem.UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Heal };
            PolicyActionsV2.Resolve(PickHeal, view, mem, m.Map).ApplyTo(exec);
            Assert.Equal(UpgradeKind.Heal, exec.UpgradeChoice);
            Assert.Null(exec.SpotHint);

            PolicyActionsV2.Resolve(CleanSecond, view, mem, m.Map).ApplyTo(exec);
            Assert.Equal(order[1], exec.SpotHint);
            Assert.Null(exec.UpgradeChoice);
        }

        [Fact]
        public void GoZoneNear_ResolvesToTheNearestZonesMapIndex()
        {
            TestMatch m = TestMatch.Create(2);
            SeatView view = m.Engine.ViewFor(0);
            var mem = new BotMemory(m.Map, m.Config);
            Assert.True(m.Map.Zones.Count >= 3);
            MapDistances dist = MapDistances.For(m.Map);
            var ranked = Enumerable.Range(0, 3)
                .Select(z => new { z, d = dist.Path(view.Me.Position, dist.NearestWalkable(new Vec2(m.Map.Zones[z].X + m.Map.Zones[z].Width / 2f, m.Map.Zones[z].Y + m.Map.Zones[z].Height / 2f))) })
                .OrderBy(x => x.d).ThenBy(x => x.z).Select(x => x.z).ToList();

            bool[] mask = PolicyActionsV2.Mask(view, mem, m.Map);

            for (int k = 0; k < 3; k++)
            {
                Assert.True(mask[1 + k]);
                Assert.Equal(BotAction.GoZone0 + ranked[k], PolicyActionsV2.Resolve(1 + k, view, mem, m.Map).Action);
            }
        }
    }
}
