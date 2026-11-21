using System.Collections.Generic;
using System.Linq;
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
        public void Map_gas_rooms_are_read_when_present_and_empty_otherwise()
        {
            using JsonDocument with = JsonDocument.Parse(TinyMap + ",\"gas_rooms\":[{\"id\":\"g1\",\"rect\":[1,1,2,1]}]}");
            using JsonDocument without = JsonDocument.Parse(TinyMap + "}");

            MapData map = ContentLoader.ReadMap(with.RootElement);

            ZoneDef room = Assert.Single(map.GasRooms);
            Assert.Equal("g1", room.Id);
            Assert.True(room.Contains(new Vec2(1.5f, 1.5f)));
            Assert.False(room.Contains(new Vec2(3.5f, 1.5f)));
            Assert.Empty(ContentLoader.ReadMap(without.RootElement).GasRooms);
        }

        [Fact]
        public void The_real_map_loads_its_four_gas_rooms()
        {
            ContentBundle content = LoadReal();

            Assert.Equal(
                new[] { "gas_dark", "gas_depot", "gas_garden", "gas_market" },
                content.Map.GasRooms.Select(z => z.Id).OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
            Assert.All(content.Map.GasRooms, z => Assert.True(z.Width > 0 && z.Height > 0));
        }

        [Fact]
        public void The_play_map_loads_with_its_sight_grid_and_gas_rooms()
        {
            ContentBundle content = ContentLoader.Load(ContentLoader.ResolveDir(null), ContentLoader.PlayMapId);

            Assert.Equal(ContentLoader.PlayMapId, content.MapId);
            Assert.Equal(
                new[] { "gas_amphi", "gas_club", "gas_lounge", "gas_office" },
                content.Map.GasRooms.Select(z => z.Id).OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
            Assert.Equal(10, content.Map.Spawns.Count);
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
        public void The_real_map_has_one_recycling_machine_and_room_for_ten_seats()
        {
            ContentBundle content = LoadReal();

            var machines = new List<ConsoleDef>(content.Map.ConsolesOfKind(ConsoleKind.Machine));
            Assert.Single(machines);
            Assert.Equal("machine", machines[0].Id);
            Assert.True(content.Map.Spawns.Count >= 10, "the map has " + content.Map.Spawns.Count + " spawns");
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
