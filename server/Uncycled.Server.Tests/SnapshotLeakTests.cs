using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    public class SnapshotLeakTests
    {
        internal static MatchEngine BuildEngine(ulong seed)
        {
            const int w = 14;
            const int h = 10;
            var walkable = new bool[w * h];
            var blocks = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool wall = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    walkable[y * w + x] = !wall;
                    blocks[y * w + x] = wall;
                }
            }

            var consoles = new List<ConsoleDef>
            {
                new ConsoleDef("market_bins", ConsoleKind.Task, new Vec2(3.5f, 3.5f), 1.2f),
                new ConsoleDef("garden_compost", ConsoleKind.Task, new Vec2(10.5f, 3.5f), 1.2f),
                new ConsoleDef("garden_tap", ConsoleKind.Task, new Vec2(10.5f, 7.5f), 1.2f),
                new ConsoleDef("meeting_button", ConsoleKind.Emergency, new Vec2(7f, 5f), 1.2f),
                new ConsoleDef("fire_a", ConsoleKind.FireFix, new Vec2(2.5f, 8f), 1.2f),
                new ConsoleDef("fire_b", ConsoleKind.FireFix, new Vec2(11.5f, 8f), 1.2f),
                new ConsoleDef("smog_panel", ConsoleKind.SmogFix, new Vec2(7f, 2f), 1.2f),
            };
            var zones = new List<ZoneDef> { new ZoneDef("market", 1, 1, 12, 8) };
            var spawns = new List<Vec2>();
            for (int i = 0; i < 8; i++)
            {
                spawns.Add(new Vec2(5.5f + i % 4, 4.5f + i / 4));
            }

            var map = new MapData(w, h, walkable, blocks, zones, consoles, spawns, "test", "v1");
            var tasks = new List<TaskTypeDef>
            {
                new TaskTypeDef("sort_waste", TaskCategory.Short, 1, 4f, new[] { "market_bins" }),
                new TaskTypeDef("turn_compost", TaskCategory.Long, 2, 6f, new[] { "garden_compost", "garden_tap" }),
            };
            var seats = new List<SeatSetup>();
            for (int i = 0; i < 8; i++)
            {
                seats.Add(new SeatSetup((byte)i, "P" + i, (byte)i));
            }

            return new MatchEngine(new MatchConfig(), map, seats, tasks, new XorShiftRandom(seed));
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(7UL)]
        [InlineData(12345UL)]
        public void CitizenMessages_NeverRevealPolluters(ulong seed)
        {
            MatchEngine engine = BuildEngine(seed);

            // first tick carries the match_started events
            var all = new List<GameEvent>(engine.Tick(0.05f));
            for (int i = 0; i < 160; i++)
            {
                all.AddRange(engine.Tick(0.05f));
            }

            int citizens = 0;
            foreach (PlayerState p in engine.Players)
            {
                if (p.IsPolluter)
                {
                    continue;
                }

                citizens++;
                SeatView view = engine.ViewFor(p.Seat);
                string snapshot = Wire.Snapshot(view, 100, 5.0);
                AssertNoPolluterInfo(snapshot);

                string state = Wire.Welcome("BCDF", p.Seat, "tok", 0, MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
                AssertNoPolluterInfo(state);
            }

            Assert.True(citizens > 0);

            foreach (GameEvent ev in all)
            {
                if (ev is MatchStartedEvent started && started.Role == Role.Citizen)
                {
                    string json = Wire.Event(ev)!;
                    Assert.DoesNotContain("partner", json);
                    Assert.DoesNotContain("signature", json);
                }
            }
        }

        [Fact]
        public void CleanerSnapshotAndWelcome_HaveNoTrasherFields_ButTheTrasherHasThem()
        {
            MatchEngine engine = BuildEngine(5UL);
            engine.Tick(engine.Config.IntroS);
            foreach (PlayerState p in engine.Players)
            {
                SeatView view = engine.ViewFor(p.Seat);
                string snapshot = Wire.Snapshot(view, 1, 1.0, engine.ExtrasFor(p.Seat));
                string welcome = Wire.Welcome("BCDF", p.Seat, "tok", 0, MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
                if (p.IsCitizen)
                {
                    foreach (string json in new[] { snapshot, welcome })
                    {
                        Assert.DoesNotContain("\"repollutes\"", json);
                        Assert.DoesNotContain("\"ability\"", json);
                        Assert.DoesNotContain("\"repollute\"", json);
                    }
                }
                else
                {
                    using System.Text.Json.JsonDocument s = System.Text.Json.JsonDocument.Parse(snapshot);
                    System.Text.Json.JsonElement me = s.RootElement.GetProperty("me");
                    Assert.Equal(0, me.GetProperty("repollutes").GetInt32());
                    Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("ability").ValueKind);
                    Assert.True(me.GetProperty("cd").TryGetProperty("repollute", out _));
                    Assert.False(me.GetProperty("cd").TryGetProperty("hit", out _));
                    using System.Text.Json.JsonDocument w = System.Text.Json.JsonDocument.Parse(welcome);
                    System.Text.Json.JsonElement state = w.RootElement.GetProperty("state");
                    Assert.Equal(0, state.GetProperty("repollutes").GetInt32());
                    Assert.True(state.GetProperty("cooldowns").TryGetProperty("repollute", out _));
                }
            }
        }

        private static void AssertNoPolluterInfo(string json)
        {
            Assert.DoesNotContain("polluter", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"partner\":", json.Replace("\"partner\":null", ""));
            Assert.DoesNotContain("\"signature\":", json.Replace("\"signature\":null", ""));
            Assert.DoesNotContain("\"cd\"", json);
            Assert.DoesNotContain("\"cooldowns\":{", json);
        }

        internal static Uncycled.Server.Content.ContentBundle MinimalBundle()
        {
            MatchEngine engine = BuildEngine(1UL);
            return new Uncycled.Server.Content.ContentBundle(
                new MatchConfig(), "{}", new List<TaskTypeDef>(), "[]", engine.Map);
        }
    }
}
