using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.World;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class NavigatorTests
    {
        private static float TimeoutFor(int pathTiles, float speed) => pathTiles / speed * 1.5f + 3f;

        [Fact]
        public void Navigator_ReachesEveryConsole()
        {
            TestMatch m = TestMatch.Create();
            Assert.Equal(15, m.Map.Consoles.Count);
            Vec2 spawn = m.Map.SpawnFor(0);
            foreach (ConsoleDef console in m.Map.Consoles)
            {
                var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
                Assert.True(nav.SetGoal(spawn, console.Position, 0f), console.Id);
                Vec2 cur = spawn;
                float now = 0f;
                float limit = TimeoutFor(200, m.Config.SpeedTilesPerS);
                while (!nav.Arrived && now < limit)
                {
                    Vec2? next = nav.Next(cur, TestMatch.Dt, now);
                    if (next != null)
                    {
                        cur = next.Value;
                    }

                    now += TestMatch.Dt;
                }

                Assert.True(nav.Arrived, "did not arrive at " + console.Id);
                Assert.False(nav.TimedOut, console.Id);
                Assert.True(Vec2.Distance(cur, console.Position) <= console.Range, console.Id);
            }
        }

        [Fact]
        public void Navigator_DropsGoalAfterTimeout()
        {
            TestMatch m = TestMatch.Create();
            Vec2 spawn = m.Map.SpawnFor(0);
            ConsoleDef far = m.Map.FindConsole("fire_a")!;
            var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
            Assert.True(nav.SetGoal(spawn, far.Position, 0f));
            int tiles = AStar.FindPath(m.Map.Walk, spawn, far.Position)!.Count - 1;
            float timeout = TimeoutFor(tiles, m.Config.SpeedTilesPerS);

            // The bot never gets anywhere (its position stays put): the goal must expire.
            float now = 0f;
            while (now < timeout - 0.2f)
            {
                Assert.NotNull(nav.Next(spawn, TestMatch.Dt, now));
                Assert.False(nav.TimedOut);
                now += TestMatch.Dt;
            }

            now = timeout + 0.2f;
            Assert.Null(nav.Next(spawn, TestMatch.Dt, now));
            Assert.True(nav.TimedOut);
            Assert.False(nav.Arrived);
            Assert.Null(nav.Next(spawn, TestMatch.Dt, now + 1f));
        }

        [Fact]
        public void Navigator_MovesAreAccepted()
        {
            TestMatch m = TestMatch.Create();
            byte seat = 0;
            var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
            var sender = new MoveSender();
            ConsoleDef goal = m.Map.FindConsole("garden_tap")!;
            Assert.True(nav.SetGoal(m.Engine.ViewFor(seat).Me.Position, goal.Position, m.Engine.MatchTime));

            int moves = 0;
            while (!nav.Arrived && !nav.TimedOut)
            {
                SeatView view = m.Engine.ViewFor(seat);
                Vec2? next = nav.Next(view.Me.Position, TestMatch.Dt, m.Engine.MatchTime);
                if (next != null)
                {
                    CommandResult result = m.Engine.Apply(seat, sender.Next(next.Value));
                    Assert.True(result.IsOk, "move rejected: " + result);
                    moves++;
                }

                m.Tick();
            }

            Assert.True(nav.Arrived);
            Assert.True(moves > 20);
            Vec2 end = m.Engine.ViewFor(seat).Me.Position;
            Assert.True(Vec2.Distance(end, goal.Position) <= goal.Range);
        }

        [Fact]
        public void MoveSender_SeqStrictlyIncreasingFromOne()
        {
            var sender = new MoveSender();
            Assert.Equal(1, sender.Next(new Vec2(1f, 1f)).Seq);
            Assert.Equal(2, sender.Next(new Vec2(1f, 1f)).Seq);
            Assert.Equal(3, sender.Next(new Vec2(2f, 1f)).Seq);
        }

        [Fact]
        public void BotsSettings_MissingFileGivesDefaults()
        {
            BotsSettings s = BotsSettings.Load("/definitely/not/here/bots.json");
            Assert.Equal(0.5f, s.DecisionIntervalS);
            Assert.Equal(0.4f, s.ReactionMinS);
            Assert.Equal(1.2f, s.ReactionMaxS);
            Assert.Equal(0.6f, s.Skill);
            Assert.Equal("scripted", s.Brain);
        }
    }
}
