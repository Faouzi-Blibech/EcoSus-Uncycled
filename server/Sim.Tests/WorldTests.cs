using System.Collections.Generic;
using Sim.Core;
using Sim.World;
using Xunit;

namespace Sim.Tests
{
    public class WorldTests
    {
        [Theory]
        [InlineData(4.5f, 3.5f, 8.5f, 3.5f, false)] // straight through the inner wall
        [InlineData(4.5f, 5.5f, 8.5f, 5.5f, true)]  // below the inner wall
        [InlineData(4.5f, 1.5f, 8.5f, 1.5f, true)]  // above the inner wall
        [InlineData(5.5f, 4.5f, 7.5f, 6.5f, false)] // diagonal clipping the wall corner cell (6,4)/(6,5)
        [InlineData(3.0f, 3.0f, 3.9f, 3.9f, true)]  // same open area
        [InlineData(2.5f, 2.5f, 2.5f, 2.5f, true)]  // same point
        public void LineOfSight_respects_walls(float ax, float ay, float bx, float by, bool expected)
        {
            MapData map = TestWorld.Map();
            Assert.Equal(expected, map.Sight.HasLineOfSight(new Vec2(ax, ay), new Vec2(bx, by)));
        }

        [Fact]
        public void LineOfSight_is_symmetric_for_a_wall()
        {
            MapData map = TestWorld.Map();
            var a = new Vec2(5.5f, 3.0f);
            var b = new Vec2(7.5f, 3.0f);
            Assert.False(map.Sight.HasLineOfSight(a, b));
            Assert.False(map.Sight.HasLineOfSight(b, a));
        }

        [Fact]
        public void Diagonal_squeeze_between_two_corner_walls_is_blocked()
        {
            string[] rows =
            {
                "....",
                ".#..",
                "..#.",
                "....",
            };
            MapData map = TestWorld.FromAscii(rows, new List<ConsoleDef>(), new List<ZoneDef>(), new[] { new Vec2(0.5f, 0.5f) });
            // From cell (1,2) to cell (2,1) passing exactly through the corner (2,2) between the walls.
            Assert.False(map.Walk.SegmentClear(new Vec2(1.5f, 2.5f), new Vec2(2.5f, 1.5f)));
            Assert.False(map.Sight.HasLineOfSight(new Vec2(1.5f, 2.5f), new Vec2(2.5f, 1.5f)));
        }

        [Theory]
        [InlineData(0.5f, 0.5f, false)]  // border wall
        [InlineData(1.5f, 1.5f, true)]
        [InlineData(6.5f, 3.5f, false)]  // inner wall
        [InlineData(-1f, 3f, false)]     // out of bounds
        [InlineData(25f, 3f, false)]     // out of bounds
        public void Walkable_cells(float x, float y, bool expected)
        {
            Assert.Equal(expected, TestWorld.Map().Walk.IsWalkable(new Vec2(x, y)));
        }

        [Theory]
        [InlineData(5.5f, 3.5f, 7.5f, 3.5f, false)] // crosses the inner wall
        [InlineData(5.5f, 5.5f, 7.5f, 5.5f, true)]
        [InlineData(1.5f, 1.5f, 0.5f, 1.5f, false)] // into the border
        public void Segment_sweep_rejects_walls(float ax, float ay, float bx, float by, bool expected)
        {
            Assert.Equal(expected, TestWorld.Map().Walk.SegmentClear(new Vec2(ax, ay), new Vec2(bx, by)));
        }

        [Fact]
        public void AStar_finds_a_path_around_the_wall()
        {
            MapData map = TestWorld.Map();
            List<Vec2>? path = AStar.FindPath(map.Walk, new Vec2(4.5f, 3.5f), new Vec2(8.5f, 3.5f));
            Assert.NotNull(path);
            Assert.Equal(new Vec2(4.5f, 3.5f), path![0]);
            Assert.Equal(new Vec2(8.5f, 3.5f), path[path.Count - 1]);

            // Wall spans y = 2..4, so the shortest 4-neighbour path detours: 4 across + 2 x 2 vertical.
            Assert.Equal(9, path.Count);
            for (int i = 1; i < path.Count; i++)
            {
                Assert.True(map.Walk.IsWalkable(path[i]));
                Assert.Equal(1f, Vec2.Distance(path[i - 1], path[i]), 3);
            }
        }

        [Fact]
        public void AStar_fails_cleanly_when_no_path_exists()
        {
            string[] rows =
            {
                "#####",
                "#.#.#",
                "#####",
            };
            MapData map = TestWorld.FromAscii(rows, new List<ConsoleDef>(), new List<ZoneDef>(), new[] { new Vec2(1.5f, 1.5f) });
            Assert.Null(AStar.FindPath(map.Walk, new Vec2(1.5f, 1.5f), new Vec2(3.5f, 1.5f)));
            Assert.Null(AStar.FindPath(map.Walk, new Vec2(1.5f, 1.5f), new Vec2(2.5f, 1.5f))); // goal is a wall
            Assert.Null(AStar.FindPath(map.Walk, new Vec2(-3f, 1.5f), new Vec2(1.5f, 1.5f)));  // start outside
        }

        [Fact]
        public void Zones_are_found_by_position()
        {
            MapData map = TestWorld.Map();
            Assert.Equal("west", map.ZoneAt(new Vec2(3f, 3f)));
            Assert.Equal("east", map.ZoneAt(new Vec2(15f, 3f)));
            Assert.Equal("", map.ZoneAt(new Vec2(50f, 3f)));
        }
    }
}
