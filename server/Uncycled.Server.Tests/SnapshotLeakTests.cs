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
        internal static MatchEngine BuildEngine(ulong seed, MatchConfig? config = null)
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

            return new MatchEngine(config ?? new MatchConfig(), map, seats, tasks, new XorShiftRandom(seed));
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

        [Fact]
        public void PlantedVirus_IsInNoSnapshotAndNoEventButThePlanters()
        {
            VirusRig rig = VirusRig.Planted(5UL);

            foreach (PlayerState p in rig.Engine.Players)
            {
                SeatView view = rig.Engine.ViewFor(p.Seat);
                string snapshot = Wire.Snapshot(view, 1, 1.0, rig.Engine.ExtrasFor(p.Seat));
                string welcome = Wire.Welcome("BCDF", p.Seat, "tok", 0, MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
                using System.Text.Json.JsonDocument s = System.Text.Json.JsonDocument.Parse(snapshot);
                foreach (System.Text.Json.JsonElement spot in s.RootElement.GetProperty("spots").EnumerateArray())
                {
                    int keys = 0;
                    foreach (System.Text.Json.JsonProperty _ in spot.EnumerateObject())
                    {
                        keys++;
                    }

                    Assert.Equal(2, keys); // id and state, nothing about a virus
                }

                if (p.IsCitizen)
                {
                    foreach (string json in new[] { snapshot, welcome })
                    {
                        Assert.DoesNotContain("virus", json, StringComparison.OrdinalIgnoreCase);
                        Assert.DoesNotContain("infected", json, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            foreach (GameEvent ev in rig.Events)
            {
                if (ev is VirusPlantedEvent planted)
                {
                    Assert.Equal(new[] { rig.Trasher }, planted.Recipients);
                }
                else
                {
                    string json = Wire.Event(ev) ?? "";
                    Assert.DoesNotContain("virus_planted", json);
                }
            }

            Assert.Contains(rig.Events, e => e is VirusPlantedEvent);
        }

        [Fact]
        public void InfectedS_ReachesOnlyTheInfectedSeat()
        {
            VirusRig rig = VirusRig.Planted(5UL);
            rig.Infect();

            foreach (PlayerState p in rig.Engine.Players)
            {
                SeatView view = rig.Engine.ViewFor(p.Seat);
                string snapshot = Wire.Snapshot(view, 1, 1.0, rig.Engine.ExtrasFor(p.Seat));
                string welcome = Wire.Welcome("BCDF", p.Seat, "tok", 0, MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
                if (p.Seat == rig.Victim)
                {
                    using System.Text.Json.JsonDocument s = System.Text.Json.JsonDocument.Parse(snapshot);
                    Assert.InRange(s.RootElement.GetProperty("me").GetProperty("infected_s").GetDouble(), 40.0, 60.0);
                }
                else
                {
                    Assert.DoesNotContain("infected_s", snapshot);
                    Assert.DoesNotContain("infected_s", welcome);
                }
            }

            foreach (GameEvent ev in rig.Events)
            {
                if (ev is InfectedEvent infected)
                {
                    Assert.Equal(new[] { rig.Victim }, infected.Recipients);
                }
            }

            Assert.Contains(rig.Events, e => e is InfectedEvent);
        }

        /// <summary>A Trasher plants the virus on market_bins through real commands; a second Cleaner can then trigger it.</summary>
        internal sealed class VirusRig
        {
            private long _seq;

            private VirusRig(MatchEngine engine, byte first, byte victim, byte trasher)
            {
                Engine = engine;
                First = first;
                Victim = victim;
                Trasher = trasher;
            }

            public MatchEngine Engine { get; }

            /// <summary>The Cleaner who cleans the bins before the virus is planted.</summary>
            public byte First { get; }

            /// <summary>The Cleaner who triggers the virus.</summary>
            public byte Victim { get; }

            public byte Trasher { get; }

            public List<GameEvent> Events { get; } = new List<GameEvent>();

            public static VirusRig Planted(ulong seed)
            {
                var cfg = new MatchConfig { UnlockAfter = 1, TrasherStartCooldownS = 0f };
                MatchEngine engine = BuildEngine(seed, cfg);
                engine.Tick(engine.Config.IntroS);
                byte trasher = 0;
                var cleaners = new List<byte>();
                foreach (PlayerState p in engine.Players)
                {
                    if (p.IsPolluter)
                    {
                        trasher = p.Seat;
                    }
                    else
                    {
                        cleaners.Add(p.Seat);
                    }
                }

                var rig = new VirusRig(engine, cleaners[0], cleaners[1], trasher);
                rig.Clean(rig.First);
                rig.WalkTo(trasher, "market_bins");
                rig.Do(trasher, new RepolluteCommand("market_bins"));
                rig.Do(trasher, new PickAbilityCommand(TrasherAbility.Virus));
                rig.Do(trasher, new PlantVirusCommand("market_bins"));
                rig.Step(0f);
                return rig;
            }

            public void Infect()
            {
                Clean(Victim);
                Step(0f);
            }

            private void Clean(byte seat)
            {
                WalkTo(seat, "market_bins");
                Do(seat, new UseCommand("market_bins"));
                for (int i = 0; i < 90; i++)
                {
                    Step(0.05f); // market_bins needs 4 s
                }

                Do(seat, new SpotDoneCommand("market_bins"));
            }

            private void Do(byte seat, ICommand command)
            {
                Assert.True(Engine.Apply(seat, command).IsOk, command.GetType().Name);
            }

            private void Step(float dt)
            {
                Events.AddRange(Engine.Tick(dt));
            }

            private void WalkTo(byte seat, string consoleId)
            {
                ConsoleDef console = Engine.Map.FindConsole(consoleId)!;
                for (int i = 0; i < 20; i++)
                {
                    Vec2 at = Engine.Find(seat)!.Position;
                    float d = Vec2.Distance(at, console.Position);
                    if (d <= 0.5f)
                    {
                        return;
                    }

                    float f = d <= 1f ? 1f : 1f / d;
                    var next = new Vec2(at.X + (console.Position.X - at.X) * f, at.Y + (console.Position.Y - at.Y) * f);
                    Do(seat, new MoveCommand(next, ++_seq));
                    Step(0.5f);
                }
            }
        }

        internal static Uncycled.Server.Content.ContentBundle MinimalBundle()
        {
            MatchEngine engine = BuildEngine(1UL);
            return new Uncycled.Server.Content.ContentBundle(
                new MatchConfig(), "{}", new List<TaskTypeDef>(), "[]", engine.Map);
        }
    }
}
