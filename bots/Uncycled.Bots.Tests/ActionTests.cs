using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class ActionTests
    {
        private static readonly BotAction[] PolluterOnly =
        {
            BotAction.HitNearest, BotAction.DumpHere, BotAction.SabotageFire, BotAction.SabotageSmog, BotAction.FakeTaskHere,
        };

        private static bool[] MaskOf(TestMatch m, byte seat, BotMemory? mem = null)
        {
            mem ??= new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(seat);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(seat));
            return ActionCatalogue.Mask(view, mem, m.Map);
        }

        [Fact]
        public void ActionCatalogue_CountMatchesEnum()
        {
            Assert.Equal(ActionCatalogue.Count, Enum.GetValues(typeof(BotAction)).Length);
            Assert.Equal(0, (int)BotAction.Stay);
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Mask_CitizenHasNoPolluterActions()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(12f);
            byte citizen = m.FirstSeatOf(Role.Citizen);

            bool[] mask = MaskOf(m, citizen);

            Assert.Equal(ActionCatalogue.Count, mask.Length);
            Assert.True(mask[(int)BotAction.Stay]);
            Assert.True(mask[(int)BotAction.GoNextTask]);
            foreach (BotAction a in PolluterOnly)
            {
                Assert.False(mask[(int)a], a.ToString());
            }
        }

        [Fact]
        public void Mask_PolluterHitOnlyWhenTargetInRange()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(11f);
            byte polluter = m.FirstSeatOf(Role.Polluter);
            Vec2 pPos = m.Engine.ViewFor(polluter).Me.Position;
            PlayerView? adjacent = m.Engine.ViewFor(polluter).Players
                .Where(p => Vec2.Distance(p.Position, pPos) <= m.Config.HitRange
                    && !m.Engine.ViewFor(polluter).Me.Partners.Contains(p.Seat))
                .Cast<PlayerView?>().FirstOrDefault();
            Assert.NotNull(adjacent);

            bool[] near = MaskOf(m, polluter);
            Assert.True(near[(int)BotAction.HitNearest]);
            Assert.True(near[(int)BotAction.DumpHere]);

            var driver = new BotDriver(m, polluter);
            driver.Begin(BotAction.GoZone2);
            driver.RunUntilIdle(30f);
            bool[] far = MaskOf(m, polluter);
            Assert.False(far[(int)BotAction.HitNearest]);
            Assert.True(far[(int)BotAction.DumpHere]);
        }

        [Fact]
        public void Mask_OnlyStayAndNothingElseInMeeting()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(16f);
            byte caller = 3;
            var driver = new BotDriver(m, caller);
            driver.Begin(BotAction.GoEmergency);
            driver.RunUntilIdle(30f);
            driver.Begin(BotAction.CallMeeting);
            driver.RunUntilIdle(3f);
            Assert.Equal(MatchPhase.Meeting, m.Engine.Phase);

            for (byte seat = 0; seat < 8; seat++)
            {
                bool[] mask = MaskOf(m, seat);
                for (int i = 0; i < mask.Length; i++)
                {
                    Assert.Equal(i == (int)BotAction.Stay, mask[i]);
                }
            }
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Executor_CompletesATaskStep()
        {
            TestMatch m = TestMatch.Create();
            byte seat = m.FirstSeatOf(Role.Citizen);
            var driver = new BotDriver(m, seat);

            driver.Begin(BotAction.GoNextTask);
            float walked = driver.RunUntilIdle(80f);
            Assert.False(driver.Executor.Busy);

            SeatView here = driver.View;
            TaskInfo task = here.Me.Tasks.First(t => t.StepsDone < t.Steps
                && Vec2.Distance(here.Me.Position, m.Map.FindConsole(t.ConsoleId)!.Position) <= m.Map.FindConsole(t.ConsoleId)!.Range);
            float minS = m.Engine.Tasks.Find(task.Id)!.Type.MinSeconds;

            driver.Begin(BotAction.UseHere);
            float used = driver.RunUntilIdle(minS + 5f);

            Assert.Contains(m.Events, e => e is TaskProgressEvent p && p.Seat == seat);
            Assert.True(used <= minS + 1f, "task step took " + used + " s, min " + minS);
            Assert.True(walked > 0f);
            Assert.All(driver.Sent, kv => Assert.True(kv.Value.IsOk, kv.Key.GetType().Name + " " + kv.Value));
        }

        [Fact(Skip = "v1 rule, out of play in v2")]
        public void Executor_FireHoldReleasedWhenLeaving()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(11f); // past the sabotage opening lock
            byte polluter = m.FirstSeatOf(Role.Polluter);
            Assert.True(m.Engine.Apply(polluter, new SabotageCommand(SabotageKind.Fire)).IsOk);
            byte seat = m.FirstSeatOf(Role.Citizen);
            var driver = new BotDriver(m, seat);

            driver.Begin(BotAction.GoFireA);
            driver.RunUntilIdle(30f);
            driver.Begin(BotAction.HoldFire);
            for (int i = 0; i < 10; i++)
            {
                driver.Step();
            }

            Assert.Contains("fire_a", driver.View.Sabotage!.Held);
            Assert.True(driver.Executor.Busy);

            driver.Begin(BotAction.GoZone0);
            driver.Step();

            SabotageView? after = driver.View.Sabotage;
            Assert.True(after == null || !after.Held.Contains("fire_a"));
            Assert.Contains(driver.Sent, kv => kv.Key is FixCommand f && !f.Holding && kv.Value.IsOk);
        }
    }
}
