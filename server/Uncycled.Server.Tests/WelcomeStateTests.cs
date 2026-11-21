using System.Collections.Generic;
using System.Text.Json;
using Sim.Match;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    public class WelcomeStateTests
    {
        private static double ClockOf(string json, bool inState)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (inState)
            {
                root = root.GetProperty("state");
            }

            return root.GetProperty("clock_t").GetDouble();
        }

        [Fact]
        public void Welcome_state_carries_the_deadline_clock_like_the_snapshot()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            for (int i = 0; i < 160; i++)
            {
                engine.Tick(0.05f); // 5 s intro, then play
            }

            SeatView view = engine.ViewFor(0);
            Assert.True(view.DeadlineClockValue > 0f, "the clock has run");

            string welcome = Wire.Welcome("BCDF", 0, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
            string snapshot = Wire.Snapshot(view, 1, 1.0);

            Assert.Equal(ClockOf(snapshot, false), ClockOf(welcome, true), 3);
            Assert.Equal(view.DeadlineClockValue, ClockOf(welcome, true), 2);
        }

        /// <summary>Walks a seat next to a console with legal moves (the budget refills between hops).</summary>
        private static void WalkTo(MatchEngine engine, byte seat, string consoleId, ref long seq)
        {
            Sim.World.ConsoleDef console = engine.Map.FindConsole(consoleId)!;
            for (int i = 0; i < 20; i++)
            {
                Sim.Core.Vec2 at = engine.Find(seat)!.Position;
                if (Sim.Core.Vec2.Distance(at, console.Position) <= 0.5f)
                {
                    return;
                }

                float d = Sim.Core.Vec2.Distance(at, console.Position);
                float f = System.Math.Min(1f, 1f / d);
                var next = new Sim.Core.Vec2(at.X + (console.Position.X - at.X) * (d <= 1f ? 1f : f), at.Y + (console.Position.Y - at.Y) * (d <= 1f ? 1f : f));
                Assert.True(engine.Apply(seat, new MoveCommand(next, ++seq)).IsOk);
                engine.Tick(0.5f);
            }
        }

        [Fact]
        public void Rejoin_KeepsBags()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            engine.Tick(engine.Config.IntroS);
            byte cleaner = 0;
            foreach (Sim.Core.PlayerState p in engine.Players)
            {
                if (p.IsCitizen)
                {
                    cleaner = p.Seat;
                    break;
                }
            }

            long seq = 0;
            WalkTo(engine, cleaner, "market_bins", ref seq);
            Assert.True(engine.Apply(cleaner, new UseCommand("market_bins")).IsOk);
            for (int i = 0; i < 90; i++)
            {
                engine.Tick(0.05f); // market_bins needs 4 s
            }

            Assert.True(engine.Apply(cleaner, new SpotDoneCommand("market_bins")).IsOk);

            SeatView view = engine.ViewFor(cleaner);
            string welcome = Wire.Welcome("BCDF", cleaner, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
            string snapshot = Wire.Snapshot(view, 1, 1.0);

            using JsonDocument w = JsonDocument.Parse(welcome);
            JsonElement state = w.RootElement.GetProperty("state");
            Assert.Equal(1, state.GetProperty("bags").GetInt32());
            Assert.Equal(engine.Config.BagCap, state.GetProperty("cap").GetInt32());
            Assert.False(state.GetProperty("tool").GetBoolean());
            Assert.Equal(0, state.GetProperty("heals").GetInt32());
            Assert.Equal(0, state.GetProperty("masks").GetInt32());

            using JsonDocument s = JsonDocument.Parse(snapshot);
            JsonElement me = s.RootElement.GetProperty("me");
            Assert.Equal(1, me.GetProperty("bags").GetInt32());
            Assert.Equal(engine.Config.BagCap, me.GetProperty("cap").GetInt32());
            Assert.Equal(0, me.GetProperty("heals").GetInt32());
            Assert.Equal(0, me.GetProperty("masks").GetInt32());

            // the spot board reaches every seat, the clean one marked clean
            bool found = false;
            foreach (JsonElement spot in s.RootElement.GetProperty("spots").EnumerateArray())
            {
                if (spot.GetProperty("id").GetString() == "market_bins")
                {
                    Assert.Equal("clean", spot.GetProperty("state").GetString());
                    found = true;
                }
                else
                {
                    Assert.Equal("dirty", spot.GetProperty("state").GetString());
                }
            }

            Assert.True(found);
        }

        [Fact]
        public void Rejoin_KeepsInfectedCountdown()
        {
            SnapshotLeakTests.VirusRig rig = SnapshotLeakTests.VirusRig.Planted(5UL);
            rig.Infect();
            MatchEngine engine = rig.Engine;
            engine.Tick(2f); // two more seconds of the countdown pass before the rejoin

            SeatView view = engine.ViewFor(rig.Victim);
            Assert.NotNull(view.Me.InfectedS);
            string welcome = Wire.Welcome("BCDF", rig.Victim, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
            string snapshot = Wire.Snapshot(view, 1, 1.0);

            using JsonDocument w = JsonDocument.Parse(welcome);
            double inWelcome = w.RootElement.GetProperty("state").GetProperty("infected_s").GetDouble();
            using JsonDocument s = JsonDocument.Parse(snapshot);
            double inSnapshot = s.RootElement.GetProperty("me").GetProperty("infected_s").GetDouble();
            Assert.Equal(view.Me.InfectedS!.Value, inWelcome, 2);
            Assert.Equal(inSnapshot, inWelcome, 2);
            Assert.InRange(inWelcome, 40.0, 58.5);

            // a seat that is not infected gets none
            SeatView other = engine.ViewFor(rig.First);
            string otherWelcome = Wire.Welcome("BCDF", rig.First, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", other, new List<Seat>(), _ => false);
            Assert.DoesNotContain("infected_s", otherWelcome);
        }

        [Fact]
        public void Trasher_welcome_and_snapshot_carry_no_bag_fields()
        {
            MatchEngine engine = SnapshotLeakTests.BuildEngine(3UL);
            engine.Tick(engine.Config.IntroS);
            byte trasher = 0;
            foreach (Sim.Core.PlayerState p in engine.Players)
            {
                if (p.IsPolluter)
                {
                    trasher = p.Seat;
                    break;
                }
            }

            SeatView view = engine.ViewFor(trasher);
            string welcome = Wire.Welcome("BCDF", trasher, "tok", 0, SnapshotLeakTests.MinimalBundle(), "playing", view, new List<Seat>(), _ => false);
            string snapshot = Wire.Snapshot(view, 1, 1.0);
            foreach (string json in new[] { welcome, snapshot })
            {
                Assert.DoesNotContain("\"bags\"", json);
                Assert.DoesNotContain("\"cap\"", json);
                Assert.DoesNotContain("\"tool\"", json);
                Assert.DoesNotContain("\"heals\"", json);
                Assert.DoesNotContain("\"masks\"", json);
            }
        }
    }
}
