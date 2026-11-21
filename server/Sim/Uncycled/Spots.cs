using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.World;

namespace Sim.Uncycled
{
    /// <summary>One cleaning spot: a task console that starts dirty (spec section 5).</summary>
    public sealed class Spot
    {
        public Spot(string id, Vec2 position, float range, string taskType, float minSeconds, int step = 0)
        {
            Step = step;
            Id = id;
            Position = position;
            Range = range;
            TaskType = taskType;
            MinSeconds = minSeconds;
        }

        public string Id { get; }

        public Vec2 Position { get; }

        public float Range { get; }

        /// <summary>The mini-game the client plays here (the task type that lists the console).</summary>
        public string TaskType { get; }

        /// <summary>Index of this console in its task type's console list: with the type it picks the mini-game (use_ok step).</summary>
        public int Step { get; }

        /// <summary>Least seconds between use and spot_done, before the tool multiplier.</summary>
        public float MinSeconds { get; }

        public SpotState State { get; internal set; }

        /// <summary>Seat of the Trasher whose virus is planted here (never sent to clients).</summary>
        public byte? VirusBy { get; internal set; }

        /// <summary>ActiveTime the virus was planted.</summary>
        public float VirusPlantedAt { get; internal set; }
    }

    /// <summary>Every spot of a match, built once from the map.</summary>
    public sealed class SpotBoard
    {
        private readonly List<Spot> _spots;

        private SpotBoard(List<Spot> spots)
        {
            _spots = spots;
        }

        public IReadOnlyList<Spot> All => _spots;

        /// <summary>One spot per task console; type and minimum time come from the first task type that lists it.</summary>
        public static SpotBoard FromMap(MapData map, IReadOnlyList<TaskTypeDef> types)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            var spots = new List<Spot>();
            foreach (ConsoleDef console in map.ConsolesOfKind(ConsoleKind.Task))
            {
                string type = "";
                float minSeconds = 0f;
                int step = 0;
                foreach (TaskTypeDef t in types)
                {
                    int index = IndexIn(t, console.Id);
                    if (index >= 0)
                    {
                        type = t.Id;
                        minSeconds = t.MinSeconds;
                        step = index;
                        break;
                    }
                }

                spots.Add(new Spot(console.Id, console.Position, console.Range, type, minSeconds, step));
            }

            return new SpotBoard(spots);
        }

        private static int IndexIn(TaskTypeDef type, string consoleId)
        {
            for (int i = 0; i < type.ConsoleIds.Count; i++)
            {
                if (type.ConsoleIds[i] == consoleId)
                {
                    return i;
                }
            }

            return -1;
        }

        public Spot? Find(string id)
        {
            foreach (Spot s in _spots)
            {
                if (s.Id == id)
                {
                    return s;
                }
            }

            return null;
        }
    }
}
