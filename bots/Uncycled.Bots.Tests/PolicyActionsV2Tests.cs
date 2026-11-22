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

        private static SeatView WithPiles(SeatView v, IReadOnlyList<PileView> piles) =>
            new SeatView(v.Seat, v.Phase, v.PhaseTimeLeft, v.MatchTime, v.DeadlineS, v.DeadlineClock, v.DeadlineClockValue, v.Meter, v.EmergenciesLeft,
                v.Me, v.Players, piles, v.Sabotage, v.Meeting, v.GameOver, v.GhostChat, v.Spots, v.MachineJammed);

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
                var rig = new BrainRig(m, seed, s => s.EmergencyOnWitnessChance = 0f); // the coverage scenario of the mask, not the emergency rule: its early meetings would end the infections HealSelf needs
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

                        SeatView real = m.Engine.ViewFor(seat);
                        // Cleaners flee gas, so scripted matches rarely leave a reportable body near a bot: also check each sample with a fresh body staged beside the seat.
                        var staged = new List<PileView>(real.Piles) { new PileView(9999, real.Me.Position, PileKind.Body, PileState.Fresh) };
                        foreach (SeatView view in new[] { real, WithPiles(real, staged) })
                        {
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
            }

            _out.WriteLine("policy indices that were valid at least once: " + string.Join(",", seen.OrderBy(x => x)));
            string missing = string.Join(",", Enumerable.Range(0, PolicyActionsV2.Count).Where(i => !seen.Contains(i)).Select(i => PolicyActionsV2.Names[i]));
            Assert.True(missing.Length == 0, "never valid in 20 scripted matches: " + missing);
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
        public void GoZoneNear_ResolvesToTheNearestZonesId()
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
                PolicyChoice choice = PolicyActionsV2.Resolve(1 + k, view, mem, m.Map);
                Assert.Equal(BotAction.GoZoneAt, choice.Action);
                Assert.Equal(m.Map.Zones[ranked[k]].Id, choice.Zone);
            }
        }

        // ---------- the resolved choice makes the executor send the matching command ----------

        /// <summary>ApplyTo + Begin + Step on a real executor: the commands it sends for the policy index.</summary>
        private static List<ICommand> Play(TestMatch m, int index, SeatView view, BotMemory mem)
        {
            var exec = new ActionExecutor(new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS), new MoveSender()) { Memory = mem };
            PolicyChoice choice = PolicyActionsV2.Resolve(index, view, mem, m.Map);
            Assert.NotEqual(BotAction.Stay, choice.Action);
            choice.ApplyTo(exec);
            exec.Begin(choice.Action, view, m.Map, view.MatchTime);
            var sent = new List<ICommand>();
            exec.Step(view, TestMatch.Dt, view.MatchTime, sent.Add);
            return sent;
        }

        [Theory]
        [InlineData(PickBackpack, UpgradeKind.Backpack)]
        [InlineData(PickTool, UpgradeKind.Tool)]
        [InlineData(PickHeal, UpgradeKind.Heal)]
        [InlineData(PickMask, UpgradeKind.Mask)]
        public void PickIndex_SendsPickUpgradeWithThatKind(int index, UpgradeKind kind)
        {
            TestMatch m = TestMatch.Create(2);
            SeatView view = m.Engine.ViewFor(m.FirstSeatOf(Role.Citizen));
            var mem = new BotMemory(m.Map, m.Config)
            {
                // the first kind in the offer is a different one, so a send that ignores the hint is caught
                UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Heal, UpgradeKind.Mask },
            };
            Assert.True(PolicyActionsV2.Mask(view, mem, m.Map)[index]);

            List<ICommand> sent = Play(m, index, view, mem);

            PickUpgradeCommand cmd = Assert.IsType<PickUpgradeCommand>(Assert.Single(sent));
            Assert.Equal(kind, cmd.Item);
        }

        [Theory]
        [InlineData(PickVirus, TrasherAbility.Virus)]
        [InlineData(PickGas, TrasherAbility.Gas)]
        public void PickAbilityIndex_SendsPickAbilityWithThatAbility(int index, TrasherAbility ability)
        {
            TestMatch m = TestMatch.Create(2);
            SeatView start = m.Engine.ViewFor(m.FirstSeatOf(Role.Polluter));
            var unlocked = new MyView(start.Me.Seat, start.Me.Role, start.Me.Partners, start.Me.Signature, start.Me.Status, start.Me.Position, start.Me.Vision,
                start.Me.Poison, start.Me.Cooldowns, start.Me.Tasks, 0, 0, false, m.Config.UnlockAfter);
            SeatView view = WithPlayers(start, start.Players, unlocked);
            var mem = new BotMemory(m.Map, m.Config);
            Assert.True(PolicyActionsV2.Mask(view, mem, m.Map)[index]);

            List<ICommand> sent = Play(m, index, view, mem);

            PickAbilityCommand cmd = Assert.IsType<PickAbilityCommand>(Assert.Single(sent));
            Assert.Equal(ability, cmd.Ability);
        }

        [Fact]
        public void GasBusiestRoom_SendsGasToTheRoomWithPlayers()
        {
            TestMatch m = TestMatch.Create(2);
            SeatView live = m.Engine.ViewFor(m.FirstSeatOf(Role.Polluter));
            ZoneDef room = m.Map.GasRooms[m.Map.GasRooms.Count - 1]; // not the first in map order: a default of "first room" would be caught
            var inside = new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f);
            SeatView view = WithPlayers(live, new List<PlayerView> { new PlayerView(1, inside, 1, false) }, AsGasTrasher(live.Me));
            var mem = new BotMemory(m.Map, m.Config);
            Assert.True(PolicyActionsV2.Mask(view, mem, m.Map)[GasBusiestRoom]);

            List<ICommand> sent = Play(m, GasBusiestRoom, view, mem);

            GasCommand cmd = Assert.IsType<GasCommand>(Assert.Single(sent));
            Assert.Equal(room.Id, cmd.RoomId);
        }

        [Theory]
        [InlineData(RepolluteNearest, 0)]
        [InlineData(RepolluteSecond, 1)]
        public void RepolluteIndex_SendsRepolluteAtTheHintedSpot(int index, int rank)
        {
            TestMatch m = TestMatch.Create(2);
            var bot = new SyntheticBot(m, Role.Polluter) { Seat = 0 };
            foreach (SpotView s in m.Engine.ViewFor(0).Spots)
            {
                bot.Spots.Add(new SpotView(s.Id, SpotState.Clean));
            }

            List<string> order = ByDistance(m, bot.View());
            Assert.True(order.Count >= 2);
            bot.Observe();
            Assert.True(PolicyActionsV2.Mask(bot.View(), bot.Memory, m.Map)[index]);
            PolicyChoice choice = PolicyActionsV2.Resolve(index, bot.View(), bot.Memory, m.Map);
            Assert.Equal(order[rank], choice.Spot);

            choice.ApplyTo(bot.Executor);
            bot.Begin(choice.Action);
            bot.RunUntilIdle(120f);

            RepolluteCommand cmd = Assert.Single(bot.Sent.OfType<RepolluteCommand>());
            Assert.Equal(order[rank], cmd.SpotId);
        }

        // ---------- the schema hash ----------

        [Fact]
        public void SchemaV2_Hash_IsPrintedAndStable()
        {
            string first = SchemaV2.Hash;
            _out.WriteLine("SchemaV2.Hash = " + first);

            Assert.Equal(first, SchemaV2.Hash);
            Assert.Equal(64, first.Length);
            Assert.Matches("^[0-9a-f]{64}$", first);
            Assert.Contains(ObservationBuilderV2.SchemaText, SchemaV2.Text);
            Assert.Contains("policy:SabotageMachine", SchemaV2.Text);
            Assert.Contains("view:GasView.", SchemaV2.Text);
            Assert.Contains("view:SeatView.Spots:", SchemaV2.Text); // no exclusions: the v2 fields the v1 hash leaves out are in
        }

        /// <summary>
        /// The v2 contract is frozen once a model is trained on it. A deliberate change to the observation, the policy
        /// list or a view (for example a suspicion weight) changes the hash: update this value in the same commit and
        /// train again; the message prints the new one.
        /// </summary>
        private const string FrozenHash = "e4aa0cb05099abb1a055b200c3bc163028f07841648bc82f8798176b914730fa";

        [Fact]
        public void SchemaV2_Hash_IsFrozen()
        {
            Assert.True(FrozenHash == SchemaV2.Hash, "SchemaV2.Hash changed: now " + SchemaV2.Hash + ", frozen " + FrozenHash);
        }
    }
}
