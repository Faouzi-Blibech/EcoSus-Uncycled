using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Server.Content;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>The v2 observation: map-independent (relative positions, path distances), up to 10 seats.</summary>
    public class ObservationV2Tests
    {
        private static readonly MatchConfig Cfg = new MatchConfig();

        private static float Get(float[] obs, string name)
        {
            int i = ObservationBuilderV2.Names.ToList().IndexOf(name);
            Assert.True(i >= 0, "no field " + name);
            return obs[i];
        }

        private static void AssertInRange(float[] obs, string context)
        {
            for (int i = 0; i < obs.Length; i++)
            {
                Assert.True(float.IsFinite(obs[i]) && obs[i] >= -1f && obs[i] <= 1f, context + ": " + ObservationBuilderV2.Names[i] + " = " + obs[i]);
            }
        }

        // ---------- a hand-made room, shiftable ----------

        private const int RoomW = 24;
        private const int RoomH = 14;
        private const int Pad = 5;

        /// <summary>
        /// A walled room of RoomW x RoomH cells at offset (shift, shift) inside a (RoomW + Pad) x (RoomH + Pad) grid, with a wall
        /// column at x = 12 that has a gap at y = 7. Everything outside the room is wall, so a shift only moves the room.
        /// </summary>
        private static MapData RoomMap(float shift, IReadOnlyList<ConsoleDef> consoles, IReadOnlyList<ZoneDef> zones, IReadOnlyList<ZoneDef>? gasRooms = null, int wallColumn = 12, int gapRow = 7)
        {
            int s = (int)shift;
            int w = RoomW + Pad;
            int h = RoomH + Pad;
            var walk = new bool[w * h];
            var sight = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int rx = x - s;
                    int ry = y - s;
                    bool inside = rx > 0 && rx < RoomW - 1 && ry > 0 && ry < RoomH - 1;
                    bool wall = !inside || (rx == wallColumn && ry != gapRow);
                    walk[y * w + x] = !wall;
                    sight[y * w + x] = wall;
                }
            }

            return new MapData(w, h, walk, sight, zones, consoles, new List<Vec2> { new Vec2(3.5f + s, 3.5f + s) }, "room", "v2", null, gasRooms);
        }

        private static Vec2 P(float shift, float x, float y) => new Vec2(x + shift, y + shift);

        private static ConsoleDef Console(float shift, string id, ConsoleKind kind, float x, float y) => new ConsoleDef(id, kind, P(shift, x, y), 1.5f);

        private static ZoneDef Zone(float shift, string id, float x, float y, float w, float h) => new ZoneDef(id, x + shift, y + shift, w, h);

        private static List<ConsoleDef> RoomConsoles(float s) => new List<ConsoleDef>
        {
            Console(s, "emg", ConsoleKind.Emergency, 2.5f, 2.5f),
            Console(s, "machine", ConsoleKind.Machine, 20.5f, 3.5f),
            Console(s, "panel", ConsoleKind.SmogFix, 20.5f, 11.5f),
            Console(s, "s1", ConsoleKind.Task, 6.5f, 3.5f),
            Console(s, "s2", ConsoleKind.Task, 14.5f, 3.5f),
            Console(s, "s3", ConsoleKind.Task, 3.5f, 10.5f),
            Console(s, "s4", ConsoleKind.Task, 9.5f, 10.5f),
            Console(s, "s5", ConsoleKind.Task, 18.5f, 7.5f),
            Console(s, "s6", ConsoleKind.Task, 5.5f, 6.5f),
            Console(s, "s7", ConsoleKind.Task, 20.5f, 8.5f),
        };

        private static List<ZoneDef> RoomZones(float s) => new List<ZoneDef>
        {
            Zone(s, "west", 1, 1, 11, 12),
            Zone(s, "east", 13, 1, 10, 6),
            Zone(s, "south", 13, 7, 10, 6),
            Zone(s, "far", 1, 1, 2, 2),
        };

        private static List<ZoneDef> RoomGas(float s) => new List<ZoneDef> { Zone(s, "gasA", 1, 8, 8, 5), Zone(s, "gasB", 14, 1, 8, 5) };

        private static List<SpotView> RoomSpots() => new List<SpotView>
        {
            new SpotView("s1", SpotState.Dirty), new SpotView("s2", SpotState.Dirty), new SpotView("s3", SpotState.Dirty),
            new SpotView("s4", SpotState.Clean), new SpotView("s5", SpotState.Clean), new SpotView("s6", SpotState.Clean),
            new SpotView("s7", SpotState.Dirty),
        };

        private sealed class MeSpec
        {
            public byte Seat;
            public Role Role = Role.Citizen;
            public Vec2 Pos;
            public IReadOnlyList<byte> Partners = Array.Empty<byte>();
            public int Bags;
            public int BagCap = 1;
            public bool HasTool;
            public int Heals;
            public int Masks;
            public float? InfectedS;
            public int Repollutes;
            public TrasherAbility Ability = TrasherAbility.None;
            public CooldownView? Cooldowns;
        }

        private static SeatView MakeView(MeSpec me, IReadOnlyList<PlayerView> players, IReadOnlyList<PileView> piles, IReadOnlyList<SpotView> spots,
            SabotageView? sabotage = null, float meter = 40f, bool jammed = false, MatchPhase phase = MatchPhase.Playing, GasView? gas = null)
        {
            var my = new MyView(me.Seat, me.Role, me.Partners, null, PlayerStatus.Active, me.Pos, 4f, null, me.Cooldowns, new List<TaskInfo>(),
                me.Bags, me.BagCap, me.HasTool, me.Repollutes, me.Ability, me.Heals, me.Masks, me.InfectedS);
            return new SeatView(me.Seat, phase, 0f, 30f, 600f, DeadlineClock.MatchTime, 30f, meter, 2, my, players, piles, sabotage, null, null, null, spots, jammed, gas);
        }

        private static PlayerView Pl(float shift, byte seat, float x, float y, bool isOut = false) => new PlayerView(seat, P(shift, x, y), seat, isOut);

        private static float[] BuildFor(MapData map, SeatView view, BotMemory mem, float now)
        {
            var into = new float[ObservationBuilderV2.Size];
            ObservationBuilderV2.Build(view, mem, map, Cfg, now, into);
            return into;
        }

        // ---------- schema ----------

        [Fact]
        public void Names_AreUnique_AndSizeIs199()
        {
            Assert.Equal(199, ObservationBuilderV2.Size);
            Assert.Equal(ObservationBuilderV2.Size, ObservationBuilderV2.Names.Count);
            Assert.Equal(ObservationBuilderV2.Size, ObservationBuilderV2.Names.Distinct().Count());
            Assert.Contains("size=199", ObservationBuilderV2.SchemaText);
            Assert.Contains("const:RelScale=8", ObservationBuilderV2.SchemaText);
            Assert.DoesNotContain("action:", ObservationBuilderV2.SchemaText);
        }

        // ---------- ranges over real matches ----------

        [Fact]
        public void Build_ValuesStayInRange_OverAScriptedMatch()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                TestMatch m = TestMatch.Create(seed, startPlaying: false);
                var rig = new BrainRig(m, seed);
                var mems = new BotMemory[m.Config.Seats];
                for (int s = 0; s < mems.Length; s++)
                {
                    mems[s] = new BotMemory(m.Map, m.Config);
                }

                var obs = new float[ObservationBuilderV2.Size];
                int stepsPerSample = (int)Math.Round(0.5f / TestMatch.Dt);
                int max = (int)(120f / TestMatch.Dt);
                for (int i = 0; i < max && m.Engine.Phase != MatchPhase.Ended; i++)
                {
                    rig.Step();
                    if (i % stepsPerSample != 0)
                    {
                        continue;
                    }

                    for (byte seat = 0; seat < mems.Length; seat++)
                    {
                        SeatView view = m.Engine.ViewFor(seat);
                        mems[seat].Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(seat));
                        ObservationBuilderV2.Build(view, mems[seat], m.Map, m.Config, m.Engine.MatchTime, obs);
                        AssertInRange(obs, "seed " + seed + " seat " + seat + " t " + m.Engine.MatchTime);
                    }
                }
            }
        }

        [Fact]
        public void Build_TenSeats_DoesNotThrow()
        {
            string dir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            ContentBundle content = ContentLoader.Load(dir);
            MatchConfig cfg = content.Config.ForSize(10);
            var seats = new List<SeatSetup>();
            for (int i = 0; i < 10; i++)
            {
                seats.Add(new SeatSetup((byte)i, "bot" + i, (byte)i));
            }

            var engine = new MatchEngine(cfg, content.Map, seats, content.Tasks, new XorShiftRandom(7), content.Phrases);
            var mems = new BotMemory[10];
            for (int s = 0; s < 10; s++)
            {
                mems[s] = new BotMemory(content.Map, cfg);
            }

            var obs = new float[ObservationBuilderV2.Size];
            for (int i = 0; i < (int)(30f / TestMatch.Dt); i++)
            {
                engine.Tick(TestMatch.Dt);
                if (i % 10 != 0)
                {
                    continue;
                }

                for (byte seat = 0; seat < 10; seat++)
                {
                    SeatView view = engine.ViewFor(seat);
                    mems[seat].Observe(view, engine.MatchTime, engine.Witnesses.SeenBy(seat));
                    ObservationBuilderV2.Build(view, mems[seat], content.Map, cfg, engine.MatchTime, obs);
                    AssertInRange(obs, "10 seats, seat " + seat);
                }
            }
        }

        // ---------- map independence ----------

        [Fact]
        public void Build_IsTranslationInvariant()
        {
            float[][] results = new float[2][];
            float[][] later = new float[2][];
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? 0f : 5f;
                MapData map = RoomMap(s, RoomConsoles(s), RoomZones(s), RoomGas(s));
                var mem = new BotMemory(map, Cfg);
                var me = new MeSpec { Seat = 0, Role = Role.Polluter, Pos = P(s, 3.5f, 3.5f), Partners = new byte[] { 4 }, Repollutes = 2, Cooldowns = new CooldownView(0f, 0f, 10f, 5f, 0f, 0f) };
                var seen = new List<PlayerView> { Pl(s, 1, 5.5f, 4.5f), Pl(s, 4, 3.5f, 6.5f), Pl(s, 2, 15.5f, 3.5f) };
                var piles = new List<PileView> { new PileView(1, P(s, 4.5f, 9.5f), PileKind.Body, PileState.Fresh) };
                SeatView v1 = MakeView(me, seen, piles, RoomSpots());
                mem.Observe(v1, 10f, new List<WitnessRecord>());
                results[k] = BuildFor(map, v1, mem, 10f);

                SeatView v2 = MakeView(me, new List<PlayerView> { Pl(s, 4, 3.5f, 6.5f) }, piles, RoomSpots());
                mem.Observe(v2, 14f, new List<WitnessRecord>());
                later[k] = BuildFor(map, v2, mem, 14f);
            }

            Assert.Equal(results[0], results[1]);
            Assert.Equal(later[0], later[1]);
            Assert.Contains(results[0], v => v != 0f);
            Assert.True(Get(later[0], "seat1.age") > 0f && Get(later[0], "seat1.dx") != 0f, "a remembered seat must show age and a last-seen offset");
        }

        [Fact]
        public void Build_SyntheticMapWithoutGasRooms_StaysInRange()
        {
            // four zones, no gas rooms, no smog panel, no machine, no emergency button
            MapData map = RoomMap(0f, new List<ConsoleDef>
            {
                Console(0f, "s1", ConsoleKind.Task, 6.5f, 3.5f), Console(0f, "s2", ConsoleKind.Task, 14.5f, 3.5f),
                Console(0f, "s3", ConsoleKind.Task, 3.5f, 10.5f), Console(0f, "s4", ConsoleKind.Task, 9.5f, 10.5f),
                Console(0f, "s5", ConsoleKind.Task, 18.5f, 7.5f), Console(0f, "s6", ConsoleKind.Task, 5.5f, 6.5f),
                Console(0f, "s7", ConsoleKind.Task, 20.5f, 8.5f),
            }, RoomZones(0f));
            var me = new MeSpec { Seat = 9, Pos = P(0f, 3.5f, 3.5f) };
            var players = new List<PlayerView>();
            for (byte i = 0; i < 9; i++)
            {
                players.Add(Pl(0f, i, 2.5f + i, 4.5f));
            }

            foreach (SabotageView? sab in new SabotageView?[] { null, new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f) })
            {
                SeatView view = MakeView(me, players, new List<PileView>(), RoomSpots(), sab);
                var mem = new BotMemory(map, Cfg);
                mem.Observe(view, 12f, new List<WitnessRecord>());
                float[] obs = BuildFor(map, view, mem, 12f);
                AssertInRange(obs, "synthetic map");
                foreach (string name in ObservationBuilderV2.Names.Where(n => n.StartsWith("gas.", StringComparison.Ordinal)))
                {
                    Assert.Equal(0f, Get(obs, name));
                }

                Assert.Equal(0f, Get(obs, "machine.reach"));
                Assert.Equal(1f, Get(obs, "world.smog_panel.dist"));
            }
        }

        // ---------- groups ----------

        [Fact]
        public void CleanerKit_IsZeroForTrashers()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            // contradicting data on purpose: the guard is the role, not the engine's habit of sending zeros
            var trasher = new MeSpec { Seat = 0, Role = Role.Polluter, Pos = P(0f, 3.5f, 3.5f), Bags = 2, BagCap = 3, HasTool = true, Heals = 1, Masks = 1, InfectedS = 20f, Cooldowns = new CooldownView(0f, 0f, 0f) };
            var mem = new BotMemory(map, Cfg) { UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Backpack } };
            float[] obs = BuildFor(map, MakeView(trasher, new List<PlayerView>(), new List<PileView>(), RoomSpots()), mem, 10f);
            AssertInRange(obs, "trasher");
            foreach (string name in ObservationBuilderV2.Names.Where(n => n.StartsWith("kit.", StringComparison.Ordinal) || n.StartsWith("offer.", StringComparison.Ordinal)))
            {
                Assert.Equal(0f, Get(obs, name));
            }

            Assert.Equal(1f, Get(obs, "self.is_trasher"));
        }

        [Fact]
        public void TrasherKit_IsZeroForCleaners()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            var cleaner = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 3.5f), Repollutes = 3, Ability = TrasherAbility.Gas, Cooldowns = new CooldownView(5f, 5f, 5f, 5f, 5f, 5f) };
            var mem = new BotMemory(map, Cfg);
            float[] obs = BuildFor(map, MakeView(cleaner, new List<PlayerView> { Pl(0f, 1, 5.5f, 4.5f) }, new List<PileView>(), RoomSpots()), mem, 10f);
            foreach (string name in ObservationBuilderV2.Names.Where(n => n.StartsWith("trasher.", StringComparison.Ordinal)))
            {
                Assert.Equal(0f, Get(obs, name));
            }

            // control: the same view for a Trasher fills the kit
            var trasher = new MeSpec { Seat = 0, Role = Role.Polluter, Pos = P(0f, 3.5f, 3.5f), Repollutes = 3, Ability = TrasherAbility.Gas, Cooldowns = new CooldownView(5f, 5f, 5f, 5f, 5f, 5f) };
            float[] t = BuildFor(map, MakeView(trasher, new List<PlayerView> { Pl(0f, 1, 5.5f, 4.5f) }, new List<PileView>(), RoomSpots()), mem, 10f);
            Assert.Equal(1f, Get(t, "trasher.unlock_progress"));
            Assert.Equal(1f, Get(t, "trasher.ability_gas"));
            Assert.Equal(0f, Get(t, "trasher.ability_none"));
            Assert.True(Get(t, "trasher.cd_gas") > 0f && Get(t, "trasher.cd_repollute") > 0f);
            Assert.Equal(0.25f, Get(t, "trasher.watchers_now"));
        }

        [Fact]
        public void CleanerKit_ReflectsBagsOfferAndInfection()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            var cleaner = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 3.5f), Bags = 3, BagCap = Cfg.BackpackCap, HasTool = true, Heals = 1, InfectedS = Cfg.VirusStartS / 2f };
            var mem = new BotMemory(map, Cfg) { UpgradeOffer = new List<UpgradeKind> { UpgradeKind.Tool, UpgradeKind.Mask } };
            float[] obs = BuildFor(map, MakeView(cleaner, new List<PlayerView>(), new List<PileView>(), RoomSpots(), meter: 70f, jammed: true), mem, 10f);
            Assert.Equal(1f, Get(obs, "kit.bags_frac"));
            Assert.Equal(1f, Get(obs, "kit.bag_full"));
            Assert.Equal(1f, Get(obs, "kit.backpack"));
            Assert.Equal(1f, Get(obs, "kit.tool"));
            Assert.Equal(1f, Get(obs, "kit.heals"));
            Assert.Equal(0f, Get(obs, "kit.masks"));
            Assert.Equal(1f, Get(obs, "kit.infected"));
            Assert.Equal(0.5f, Get(obs, "kit.infected_left"));
            Assert.Equal(1f, Get(obs, "kit.infected_call_unused"));
            Assert.Equal(1f, Get(obs, "offer.open"));
            Assert.Equal(0f, Get(obs, "offer.has_backpack"));
            Assert.Equal(1f, Get(obs, "offer.has_tool"));
            Assert.Equal(1f, Get(obs, "offer.has_mask"));
            Assert.Equal(1f, Get(obs, "machine.jammed"));
            Assert.Equal(0.7f, Get(obs, "meter.cleanliness"), 4);
            Assert.Equal(0.3f, Get(obs, "meter.bags_to_win"), 4);
            Assert.Equal(4f / 7f, Get(obs, "spots.dirty_frac"), 4);
            mem.InfectedCallUsed = true;
            Assert.Equal(0f, Get(BuildFor(map, MakeView(cleaner, new List<PlayerView>(), new List<PileView>(), RoomSpots()), mem, 10f), "kit.infected_call_unused"));
        }

        [Fact]
        public void DirtySlots_AreNearestFirstByPath()
        {
            // a wall column at x = 6 with its only gap at y = 8: the dirty spot just behind the wall is nearer in a straight line, far by path
            var consoles = new List<ConsoleDef>
            {
                Console(0f, "behind", ConsoleKind.Task, 8.5f, 2.5f), Console(0f, "below", ConsoleKind.Task, 3.5f, 8.5f), Console(0f, "far", ConsoleKind.Task, 16.5f, 2.5f),
            };
            MapData map = RoomMap(0f, consoles, new List<ZoneDef>(), null, wallColumn: 6, gapRow: 8);
            var spots = new List<SpotView> { new SpotView("behind", SpotState.Dirty), new SpotView("below", SpotState.Dirty), new SpotView("far", SpotState.Dirty) };
            var me = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 2.5f) };
            SeatView view = MakeView(me, new List<PlayerView>(), new List<PileView>(), spots);
            float[] obs = BuildFor(map, view, new BotMemory(map, Cfg), 10f);

            Assert.Equal(1f, Get(obs, "dirty0.present"));
            Assert.Equal(0f, Get(obs, "dirty0.dx"));
            Assert.True(Get(obs, "dirty0.dy") > 0f, "the spot below is the nearest by path");
            Assert.Equal(5f / 8f, Get(obs, "dirty1.dx"), 4);
            for (int i = 0; i < 2; i++)
            {
                Assert.True(Get(obs, "dirty" + i + ".dist") <= Get(obs, "dirty" + (i + 1) + ".dist"));
            }

            Assert.Equal(1f, Get(obs, "dirty2.present"));
            Assert.Equal(0f, Get(obs, "clean0.present"));
            Assert.Equal(0f, Get(obs, "clean0.dist"));
        }

        [Fact]
        public void DirtySlot_OccupiedAndCleanSlot_Watchers()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            var me = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 3.5f) };
            // s1 (6.5, 3.5) is the nearest dirty spot and a player stands on it; s6 (5.5, 6.5) is a clean spot with a second player in sight
            var players = new List<PlayerView> { Pl(0f, 1, 7.5f, 2.5f), Pl(0f, 2, 5.5f, 5.5f) };
            float[] obs = BuildFor(map, MakeView(me, players, new List<PileView>(), RoomSpots()), new BotMemory(map, Cfg), 10f);
            Assert.Equal(1f, Get(obs, "dirty0.occupied"));
            Assert.Equal(0f, Get(obs, "dirty1.occupied"));
            Assert.Equal(1f, Get(obs, "clean0.present"));
            Assert.Equal(0.25f, Get(obs, "clean0.watchers"));
        }

        [Fact]
        public void Gas_IAmInWarnedRoom_IsOneInsideTheRoom()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            var gas = new GasView("gasB", Cfg.GasWarningS / 2f);

            // inside gasB (x 14..22, y 1..6)
            var inside = new MeSpec { Seat = 0, Pos = P(0f, 16.5f, 3.5f) };
            float[] a = BuildFor(map, MakeView(inside, new List<PlayerView>(), new List<PileView>(), RoomSpots(), gas: gas), new BotMemory(map, Cfg), 10f);
            Assert.Equal(1f, Get(a, "gas.warning"));
            Assert.Equal(0.5f, Get(a, "gas.in_s"));
            Assert.Equal(1f, Get(a, "gas.i_am_in_warned_room"));
            Assert.Equal(1f, Get(a, "gas.in_any_gas_room"));

            // inside the other gas room, gasA: in a gas room but not the warned one; the warned room is to the right and up
            var other = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 10.5f) };
            float[] b = BuildFor(map, MakeView(other, new List<PlayerView>(), new List<PileView>(), RoomSpots(), gas: gas), new BotMemory(map, Cfg), 10f);
            Assert.Equal(1f, Get(b, "gas.warning"));
            Assert.Equal(0f, Get(b, "gas.i_am_in_warned_room"));
            Assert.Equal(1f, Get(b, "gas.in_any_gas_room"));
            Assert.True(Get(b, "gas.warned.dx") > 0f && Get(b, "gas.warned.dy") < 0f && Get(b, "gas.warned.dist") > 0f);

            // no warning: the whole warned block is 0
            float[] c = BuildFor(map, MakeView(inside, new List<PlayerView>(), new List<PileView>(), RoomSpots()), new BotMemory(map, Cfg), 10f);
            foreach (string name in new[] { "gas.warning", "gas.in_s", "gas.i_am_in_warned_room", "gas.warned.dx", "gas.warned.dy", "gas.warned.dist" })
            {
                Assert.Equal(0f, Get(c, name));
            }

            // a warned room the map does not know (another map's id) must not throw
            AssertInRange(BuildFor(map, MakeView(inside, new List<PlayerView>(), new List<PileView>(), RoomSpots(), gas: new GasView("nowhere", 99f)), new BotMemory(map, Cfg), 10f), "unknown room");
        }

        [Fact]
        public void Gas_RoomFieldsFollowTheMapRoomsAndThePlayersInThem()
        {
            MapData map = RoomMap(0f, RoomConsoles(0f), RoomZones(0f), RoomGas(0f));
            // gasA covers x 1..9, y 8..13; gasB covers x 14..22, y 1..6
            var inside = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 10.5f) };
            var players = new List<PlayerView> { Pl(0f, 1, 16.5f, 3.5f), Pl(0f, 2, 17.5f, 4.5f) };
            float[] obs = BuildFor(map, MakeView(inside, players, new List<PileView>(), RoomSpots()), new BotMemory(map, Cfg), 10f);
            Assert.Equal(1f, Get(obs, "gas.in_any_gas_room"));
            Assert.Equal(0.5f, Get(obs, "gas.best_room.players"));
            Assert.True(Get(obs, "gas.best_room.dist") > 0f);

            var outside = new MeSpec { Seat = 0, Pos = P(0f, 3.5f, 3.5f) };
            float[] o2 = BuildFor(map, MakeView(outside, new List<PlayerView>(), new List<PileView>(), RoomSpots()), new BotMemory(map, Cfg), 10f);
            Assert.Equal(0f, Get(o2, "gas.in_any_gas_room"));
            Assert.Equal(0f, Get(o2, "gas.best_room.players"));
            Assert.Equal(0f, Get(o2, "gas.warning"));
        }
    }
}
