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
    }
}
