using System.Collections.Generic;
using System.Text.Json;
using Sim.Core;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Server.Content;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>
    /// Loads the real content/ folder, the one the server reads at startup. Some content mistakes
    /// only show when the host starts a match (a task on an unknown console) or never crash at all
    /// (fewer task types than tuning.json asks for), so they are checked here.
    /// </summary>
    public class ContentTests
    {
        /// <summary>ResolveDir(null) walks up from the test's bin folder to the repo's content/.</summary>
        private static ContentBundle LoadReal() => ContentLoader.Load(ContentLoader.ResolveDir(null));

        [Fact]
        public void Real_content_loads()
        {
            ContentBundle content = LoadReal();

            Assert.Equal(ContentLoader.DefaultMapId, content.MapId);
            Assert.NotEmpty(content.Tasks);
        }

        [Fact]
        public void Every_task_console_is_a_task_console_on_the_map()
        {
            ContentBundle content = LoadReal();
            var problems = new List<string>();

            foreach (TaskTypeDef task in content.Tasks)
            {
                foreach (string id in task.ConsoleIds)
                {
                    ConsoleDef? console = content.Map.FindConsole(id);
                    if (console == null)
                    {
                        problems.Add("task " + task.Id + " uses console " + id + ", which is not on the map");
                    }
                    else if (console.Kind != ConsoleKind.Task)
                    {
                        problems.Add("task " + task.Id + " uses console " + id + ", which is a " + console.Kind + " console");
                    }
                }
            }

            Assert.Empty(problems);
        }

        [Fact]
        public void Every_seat_has_a_spawn_on_a_floor_tile()
        {
            ContentBundle content = LoadReal();
            IReadOnlyList<Vec2> spawns = content.Map.Spawns;

            Assert.True(
                spawns.Count >= content.Config.Seats,
                "the map has " + spawns.Count + " spawns for " + content.Config.Seats + " seats");

            var problems = new List<string>();
            for (int i = 0; i < spawns.Count; i++)
            {
                if (!content.Map.Walk.IsWalkable(spawns[i]))
                {
                    problems.Add("spawn " + i + " at " + spawns[i] + " is not on a floor tile");
                }
            }

            Assert.Empty(problems);
        }

        [Theory]
        [InlineData(TaskCategory.Common)]
        [InlineData(TaskCategory.Short)]
        [InlineData(TaskCategory.Long)]
        public void Tuning_asks_for_no_more_tasks_than_exist(TaskCategory category)
        {
            ContentBundle content = LoadReal();
            int wanted = category switch
            {
                TaskCategory.Common => content.Config.TasksCommon,
                TaskCategory.Short => content.Config.TasksShort,
                _ => content.Config.TasksLong,
            };

            int available = 0;
            foreach (TaskTypeDef task in content.Tasks)
            {
                if (task.Category == category)
                {
                    available++;
                }
            }

            Assert.True(
                available >= wanted,
                "tuning.json asks for " + wanted + " " + category + " task(s) per citizen, but tasks.json has "
                + available + " " + category + " task type(s)");
        }

        private const string TinyMap =
            "{\"id\":\"tiny\",\"width\":4,\"height\":3,\"walk\":[\"####\",\"#..#\",\"####\"],"
            + "\"spawns\":[[1.5,1.5]]";

        [Fact]
        public void Map_dark_rects_are_read_when_present()
        {
            using JsonDocument doc = JsonDocument.Parse(TinyMap + ",\"dark\":[{\"id\":\"d1\",\"rect\":[1,1,2,1]}]}");

            MapData map = ContentLoader.ReadMap(doc.RootElement);

            Assert.Single(map.Dark);
            Assert.True(map.IsDark(new Vec2(1.5f, 1.5f)));
            Assert.False(map.IsDark(new Vec2(3.5f, 1.5f)));
        }

        [Fact]
        public void Map_without_a_dark_key_has_no_dark_rooms()
        {
            using JsonDocument doc = JsonDocument.Parse(TinyMap + "}");

            MapData map = ContentLoader.ReadMap(doc.RootElement);

            Assert.Empty(map.Dark);
            Assert.False(map.IsDark(new Vec2(1.5f, 1.5f)));
        }

        [Fact]
        public void The_real_map_has_two_dark_rooms_away_from_every_spawn()
        {
            ContentBundle content = LoadReal();

            Assert.Equal(2, content.Map.Dark.Count);
            foreach (Vec2 spawn in content.Map.Spawns)
            {
                Assert.False(content.Map.IsDark(spawn), "spawn " + spawn + " is in a dark room");
            }
        }
    }
}
