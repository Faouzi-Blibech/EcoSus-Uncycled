using Sim.Core;
using Sim.Match;
using Xunit;
using static Sim.Tests.TestWorld;

namespace Sim.Tests
{
    /// <summary>Dash: every seat, one charge, ready dash.cooldown_s after the last one (or after dash.cleans_refill cleans).</summary>
    public class DashTests
    {
        private static MatchConfig Cfg() => new MatchConfig { DashTiles = 6f, DashCooldownS = 20f, DashCleansRefill = 2, BagCap = 5 };

        /// <summary>A Cleaner standing at `from` with the dash ready.</summary>
        private static (MatchEngine e, byte c) Ready(Vec2 from)
        {
            var e = Playing(Cfg());
            byte c = Citizen(e);
            Advance(e, 20.1f);
            e.TestTeleport(c, from);
            return (e, c);
        }

        [Theory]
        [InlineData(2.5f, 7.5f, 1f, 0f, 8.5f, 7.5f)]   // open floor: the full 6 tiles
        [InlineData(10.5f, 7.5f, -1f, 0f, 4.5f, 7.5f)] // the other way
        [InlineData(2.5f, 7.5f, 3f, 0f, 8.5f, 7.5f)]   // the direction is normalised
        public void Dash_MovesDashTiles_OnOpenFloor(float x, float y, float dx, float dy, float ex, float ey)
        {
            var (e, c) = Ready(new Vec2(x, y));
            Assert.True(e.Apply(c, new DashCommand(new Vec2(dx, dy))).IsOk);
            Vec2 at = P(e, c).Position;
            Assert.Equal(ex, at.X, 2);
            Assert.Equal(ey, at.Y, 2);
        }

        [Fact]
        public void Dash_StopsBeforeAWall()
        {
            var (e, c) = Ready(new Vec2(2.5f, 3.5f)); // the wall column is x = 6 on rows 2-4
            Assert.True(e.Apply(c, new DashCommand(new Vec2(1f, 0f))).IsOk);
            float x = P(e, c).Position.X;
            Assert.True(x < 6f && x > 5.5f, "stopped at x = " + x);
        }

        /// <summary>Regression: the dash stopped with its centre 0.05 from the wall, the client's body inside it.</summary>
        [Theory]
        [InlineData(2.5f, 3.5f, 1f, 0f)]   // straight into the wall column (x = 6)
        [InlineData(2.5f, 2.5f, 1f, 0.3f)] // at an angle
        [InlineData(10.5f, 3.5f, -1f, 0f)] // from the other side
        public void Dash_EndsWhereTheBodyFits(float x, float y, float dx, float dy)
        {
            var (e, c) = Ready(new Vec2(x, y));
            Assert.True(e.Apply(c, new DashCommand(new Vec2(dx, dy))).IsOk);
            Vec2 at = P(e, c).Position;
            Assert.True(e.Map.Walk.CircleFits(at, 0.28f), "body overlaps a wall at " + at.X + "," + at.Y);
            Assert.True(Vec2.Distance(at, new Vec2(x, y)) > 2f, "still dashed");
        }

        [Fact]
        public void Dash_EmitsDashOkToTheDasherOnly()
        {
            var (e, c) = Ready(new Vec2(2.5f, 7.5f));
            e.Tick(0f);
            e.Apply(c, new DashCommand(new Vec2(1f, 0f)));
            DashOkEvent ok = Assert.Single(Of<DashOkEvent>(e.Tick(0f)));
            Assert.Equal(new[] { c }, Arr(ok.Recipients));
            Assert.Equal(8.5f, ok.Position.X, 2);
            Assert.Equal(20f, ok.Cooldown);
        }

        [Theory]
        [InlineData(0f)]    // not ready at the start
        [InlineData(19.5f)] // still not ready
        public void Dash_BeforeTheFirstCooldown_IsRejected(float wait)
        {
            var e = Playing(Cfg());
            byte c = Citizen(e);
            Advance(e, wait);
            CommandResult r = e.Apply(c, new DashCommand(new Vec2(1f, 0f)));
            Assert.Equal(RejectReason.OnCooldown, r.Reason);
        }

        [Fact]
        public void Dash_Twice_SecondIsOnCooldown()
        {
            var (e, c) = Ready(new Vec2(2.5f, 7.5f));
            Assert.True(e.Apply(c, new DashCommand(new Vec2(1f, 0f))).IsOk);
            Assert.Equal(RejectReason.OnCooldown, e.Apply(c, new DashCommand(new Vec2(-1f, 0f))).Reason);
        }

        [Fact]
        public void Dash_ZeroDirection_IsRejected()
        {
            var (e, c) = Ready(new Vec2(2.5f, 7.5f));
            Assert.Equal(RejectReason.TargetInvalid, e.Apply(c, new DashCommand(new Vec2(0f, 0f))).Reason);
        }

        [Fact]
        public void Trasher_CanDashToo()
        {
            var e = Playing(Cfg());
            byte t = Polluter(e);
            Advance(e, 20.1f);
            e.TestTeleport(t, new Vec2(2.5f, 7.5f));
            Assert.True(e.Apply(t, new DashCommand(new Vec2(1f, 0f))).IsOk);
        }

        [Fact]
        public void Cleans_RefillTheDash()
        {
            var (e, c) = Ready(new Vec2(2.5f, 7.5f));
            Assert.True(e.Apply(c, new DashCommand(new Vec2(1f, 0f))).IsOk);
            foreach (string spot in new[] { "bins", "compost" })
            {
                StandAt(e, c, spot);
                Assert.True(e.Apply(c, new UseCommand(spot)).IsOk);
                Advance(e, 2.1f);
                Assert.True(e.Apply(c, new SpotDoneCommand(spot)).IsOk);
            }

            Assert.True(e.Apply(c, new DashCommand(new Vec2(-1f, 0f))).IsOk, "2 cleans refill the dash");
        }
    }
}
