using System.Collections.Generic;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Snapshot fields added with the design requests: smog repair progress and the smog level.</summary>
    public class SnapshotFieldsTests
    {
        private static SeatView View(SabotageView? sabotage)
        {
            var me = new MyView(0, Role.Citizen, new List<byte>(), null, PlayerStatus.Active, new Vec2(1f, 1f), 4f, 0, null, new List<TaskInfo>());
            return new SeatView(0, MatchPhase.Playing, 0f, 10f, 600f, DeadlineClock.ActiveTime, 10f, 0f, 2, me, new List<PlayerView>(), new List<PileView>(), sabotage, null, null);
        }

        [Fact]
        public void Snapshot_sends_repair_t_only_when_the_viewer_is_repairing()
        {
            var repairing = new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f);
            var idle = new SabotageView(SabotageKind.Smog, null, new List<string>(), 1f);

            using JsonDocument yes = JsonDocument.Parse(Wire.Snapshot(View(repairing), 1, 1.0, new SnapshotExtras(1f, 1.25f)));
            using JsonDocument no = JsonDocument.Parse(Wire.Snapshot(View(idle), 1, 1.0, new SnapshotExtras(1f, null)));

            Assert.Equal(1.25, yes.RootElement.GetProperty("sabotage").GetProperty("repair_t").GetDouble(), 3);
            Assert.False(no.RootElement.GetProperty("sabotage").TryGetProperty("repair_t", out _));
        }

        [Fact]
        public void Snapshot_me_carries_the_smog_level_also_without_a_sabotage()
        {
            using JsonDocument doc = JsonDocument.Parse(Wire.Snapshot(View(null), 1, 1.0, new SnapshotExtras(0.4f, null)));

            Assert.Equal(0.4, doc.RootElement.GetProperty("me").GetProperty("smog_level").GetDouble(), 3);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("sabotage").ValueKind);
        }
    }
}
