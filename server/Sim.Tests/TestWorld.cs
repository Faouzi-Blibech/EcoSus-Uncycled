using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Sim.Tests
{
    /// <summary>Tiny hand-made maps and engine helpers shared by the tests.</summary>
    internal static class TestWorld
    {
        // 20 x 10. '#' = wall (not walkable, blocks sight). Inner wall at x = 6, y = 2..4.
        public static readonly string[] Rows =
        {
            "####################",
            "#..................#",
            "#.....#............#",
            "#.....#............#",
            "#.....#............#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#..................#",
            "####################",
        };

        public static readonly Vec2 BinsPos = new Vec2(3.5f, 7.5f);
        public static readonly Vec2 CompostPos = new Vec2(16.5f, 2.5f);
        public static readonly Vec2 TapPos = new Vec2(16.5f, 7.5f);
        public static readonly Vec2 ButtonPos = new Vec2(10.5f, 5.5f);
        public static readonly Vec2 FireAPos = new Vec2(2.5f, 1.5f);
        public static readonly Vec2 FireBPos = new Vec2(17.5f, 1.5f);
        public static readonly Vec2 SmogPos = new Vec2(12.5f, 8.5f);
        public static readonly Vec2 MachinePos = new Vec2(1.5f, 5.5f);

        /// <summary>A far corner used to park seats that should not matter.</summary>
        public static readonly Vec2 Corner = new Vec2(18.5f, 8.5f);

        public static MapData FromAscii(string[] rows, IReadOnlyList<ConsoleDef> consoles, IReadOnlyList<ZoneDef> zones, IReadOnlyList<Vec2> spawns, IReadOnlyList<ZoneDef>? dark = null)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var walk = new bool[w * h];
            var sight = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    char ch = rows[y][x];
                    walk[y * w + x] = ch != '#';
                    sight[y * w + x] = ch == '#';
                }
            }

            return new MapData(w, h, walk, sight, zones, consoles, spawns, "test_map", "v1", dark);
        }

        public static MapData Map(IReadOnlyList<ZoneDef>? dark = null)
        {
            var consoles = new List<ConsoleDef>
            {
                new ConsoleDef("bins", ConsoleKind.Task, BinsPos, 1.2f),
                new ConsoleDef("compost", ConsoleKind.Task, CompostPos, 1.2f),
                new ConsoleDef("tap", ConsoleKind.Task, TapPos, 1.2f),
                new ConsoleDef("button", ConsoleKind.Emergency, ButtonPos, 1.5f),
                new ConsoleDef("fire_a", ConsoleKind.FireFix, FireAPos, 1.2f),
                new ConsoleDef("fire_b", ConsoleKind.FireFix, FireBPos, 1.2f),
                new ConsoleDef("smog_panel", ConsoleKind.SmogFix, SmogPos, 1.2f),
                new ConsoleDef("machine", ConsoleKind.Machine, MachinePos, 1.2f),
            };
            var zones = new List<ZoneDef>
            {
                new ZoneDef("west", 0, 0, 10, 10),
                new ZoneDef("east", 10, 0, 10, 10),
            };
            var spawns = new List<Vec2>
            {
                new Vec2(9.5f, 4.5f), new Vec2(10.5f, 4.5f), new Vec2(11.5f, 4.5f), new Vec2(9.5f, 5.5f),
                new Vec2(11.5f, 5.5f), new Vec2(9.5f, 6.5f), new Vec2(10.5f, 6.5f), new Vec2(11.5f, 6.5f),
            };
            return FromAscii(Rows, consoles, zones, spawns, dark);
        }

        public static List<TaskTypeDef> TaskTypes()
        {
            return new List<TaskTypeDef>
            {
                new TaskTypeDef("check_meter", TaskCategory.Common, 1, 1f, new[] { "bins" }),
                new TaskTypeDef("sort_waste", TaskCategory.Short, 1, 1f, new[] { "bins" }),
                new TaskTypeDef("fill_crate", TaskCategory.Short, 1, 1f, new[] { "tap" }),
                new TaskTypeDef("turn_compost", TaskCategory.Long, 2, 2f, new[] { "compost", "tap" }),
            };
        }

        public static List<SeatSetup> Seats(int count)
        {
            var list = new List<SeatSetup>();
            for (int i = 0; i < count; i++)
            {
                list.Add(new SeatSetup((byte)i, "P" + i, (byte)i));
            }

            return list;
        }

        public static MatchEngine Engine(MatchConfig? config = null, ulong seed = 42, IReadOnlyList<TaskTypeDef>? tasks = null)
        {
            MatchConfig cfg = config ?? new MatchConfig();
            return new MatchEngine(cfg, Map(), Seats(cfg.Seats), tasks ?? TaskTypes(), new XorShiftRandom(seed));
        }

        /// <summary>A dark room in the east of the test map (x 12..18, y 1..4), clear of the spawns and consoles.</summary>
        public static readonly ZoneDef DarkRoom = new ZoneDef("dark_ne", 12f, 1f, 6f, 4f);

        /// <summary>Past Intro on a map with one dark room.</summary>
        public static MatchEngine PlayingWithDark(MatchConfig? config = null)
        {
            MatchConfig cfg = config ?? new MatchConfig();
            var e = new MatchEngine(cfg, Map(new[] { DarkRoom }), Seats(cfg.Seats), TaskTypes(), new XorShiftRandom(42));
            e.Tick(e.Config.IntroS);
            return e;
        }

        /// <summary>An engine already past Intro (ActiveTime = 0).</summary>
        public static MatchEngine Playing(MatchConfig? config = null, ulong seed = 42, IReadOnlyList<TaskTypeDef>? tasks = null)
        {
            MatchEngine e = Engine(config, seed, tasks);
            e.Tick(e.Config.IntroS);
            if (e.Phase != MatchPhase.Playing)
            {
                throw new InvalidOperationException("Intro did not end.");
            }

            return e;
        }

        /// <summary>Ticks in small steps and returns every event.</summary>
        public static List<GameEvent> Advance(MatchEngine e, float seconds, float step = 0.05f)
        {
            var all = new List<GameEvent>();
            int steps = (int)Math.Ceiling(seconds / step - 1e-3);
            for (int i = 0; i < steps; i++)
            {
                all.AddRange(e.Tick(step));
            }

            return all;
        }

        public static byte Polluter(MatchEngine e, int k = 0) => SeatsWithRole(e, Role.Polluter)[k];

        public static byte Citizen(MatchEngine e, int k = 0) => SeatsWithRole(e, Role.Citizen)[k];

        public static List<byte> SeatsWithRole(MatchEngine e, Role role)
        {
            var list = new List<byte>();
            foreach (PlayerState p in e.Players)
            {
                if (p.Role == role)
                {
                    list.Add(p.Seat);
                }
            }

            return list;
        }

        /// <summary>Moves every seat not in keep to the far corner.</summary>
        public static void ParkOthers(MatchEngine e, params byte[] keep)
        {
            foreach (PlayerState p in e.Players)
            {
                if (Array.IndexOf(keep, p.Seat) < 0)
                {
                    e.TestTeleport(p.Seat, Corner);
                }
            }
        }

        public static List<T> Of<T>(IEnumerable<GameEvent> events)
            where T : GameEvent
        {
            var list = new List<T>();
            foreach (GameEvent ev in events)
            {
                if (ev is T t)
                {
                    list.Add(t);
                }
            }

            return list;
        }

        /// <summary>Recipients as an array (null = everyone, returned as null).</summary>
        public static byte[]? Arr(IReadOnlyList<byte>? seats)
        {
            if (seats == null)
            {
                return null;
            }

            var a = new byte[seats.Count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = seats[i];
            }

            return a;
        }

        public static PlayerState P(MatchEngine e, byte seat) => e.Find(seat) ?? throw new InvalidOperationException("no seat");

        /// <summary>Moves a seat onto a console (its exact position).</summary>
        public static void StandAt(MatchEngine e, byte seat, string consoleId)
        {
            ConsoleDef console = e.Map.FindConsole(consoleId) ?? throw new InvalidOperationException("no console " + consoleId);
            e.TestTeleport(seat, console.Position);
        }
    }
}
