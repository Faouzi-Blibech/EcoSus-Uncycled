using Sim.Core;
using Sim.Match;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public sealed class HitMemoryTests
    {
        private static (ActionExecutor Exec, BotMemory Mem) Rig()
        {
            TestMatch m = TestMatch.Create();
            var mem = new BotMemory(m.Map, m.Config);
            var exec = new ActionExecutor(new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS), new MoveSender()) { Memory = mem };
            return (exec, mem);
        }

        [Theory]
        [InlineData(RejectReason.OutOfRange)]
        [InlineData(RejectReason.WallBetween)]
        [InlineData(RejectReason.OnCooldown)]
        public void RejectedHit_DoesNotBlockTheTarget(RejectReason reason)
        {
            (ActionExecutor exec, BotMemory mem) = Rig();
            mem.NoteOwnHit(3, 10f);
            Assert.True(mem.RecentlyHit(3));
            exec.NotifyResult(new HitCommand(3), CommandResult.Rejected(reason));
            Assert.False(mem.RecentlyHit(3));
        }

        [Fact]
        public void HitRejectedAsImmune_KeepsTheImmunityWindow()
        {
            (ActionExecutor exec, BotMemory mem) = Rig();
            mem.NoteOwnHit(3, 10f);
            exec.NotifyResult(new HitCommand(3), CommandResult.Rejected(RejectReason.TargetImmune));
            Assert.True(mem.RecentlyHit(3));
        }
    }
}
