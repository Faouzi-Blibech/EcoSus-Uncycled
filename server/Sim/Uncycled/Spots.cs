using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.World;

namespace Sim.Uncycled
{
    /// <summary>One cleaning spot: a task console that starts dirty (spec section 5).</summary>
    public sealed class Spot
    {
        public Spot(string id, Vec2 position, float range, string taskType, float minSeconds)
        {
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
                foreach (TaskTypeDef t in types)
                {
                    if (Lists(t, console.Id))
                    {
                        type = t.Id;
                        minSeconds = t.MinSeconds;
                        break;
                    }
                }

                spots.Add(new Spot(console.Id, console.Position, console.Range, type, minSeconds));
            }

            return new SpotBoard(spots);
        }

        private static bool Lists(TaskTypeDef type, string consoleId)
        {
            foreach (string id in type.ConsoleIds)
            {
                if (id == consoleId)
                {
                    return true;
                }
            }

            return false;
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
