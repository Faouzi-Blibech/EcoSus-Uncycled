using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class BotMemoryTests
    {
        [Fact]
        public void BotMemory_RemembersLastSeen()
        {
            TestMatch m = TestMatch.Create();
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(0);
            PlayerView other = view.Players.Single(p => p.Seat == 1);

            mem.Observe(view, 10f, new List<WitnessRecord>());

            Assert.Equal(5f, mem.LastSeenAge(1, 15f));
            Assert.Equal(m.Map.ZoneAt(other.Position), mem.LastSeenZone(1));
            Assert.Null(mem.LastSeenAge(200, 15f));
            Assert.Null(mem.LastSeenZone(200));
        }

        [Fact]
        public void BotMemory_KeepsWitnessRecordsAndClears()
        {
            TestMatch m = TestMatch.Create();
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(0);
            var record = new WitnessRecord(ActKind.Dump, 3, 0, 12f, "depot", new Vec2(20.5f, 12.5f), 1, null);

            mem.Observe(view, 12f, new List<WitnessRecord> { record });
            mem.Observe(view, 13f, new List<WitnessRecord> { record });

            Assert.Single(mem.Witnessed);
            mem.Clear();
            Assert.Empty(mem.Witnessed);
            Assert.Null(mem.LastSeenAge(1, 20f));
        }
    }
}
