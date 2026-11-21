using System;
using System.Collections.Generic;
using Sim.Core;

namespace Sim.World
{
    /// <summary>A named rectangle of tiles: [X, X + Width) x [Y, Y + Height).</summary>
    public sealed class ZoneDef
    {
        public ZoneDef(string id, float x, float y, float width, float height)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public string Id { get; }

        public float X { get; }

        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public bool Contains(Vec2 p) => p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
    }

    /// <summary>A console: task console or special one (emergency, fire fix, smog fix).</summary>
    public sealed class ConsoleDef
    {
        public ConsoleDef(string id, ConsoleKind kind, Vec2 position, float range)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind;
            Position = position;
            Range = range;
        }

        public string Id { get; }

        public ConsoleKind Kind { get; }

        public Vec2 Position { get; }

        /// <summary>Interaction range in tiles. The server fills it in for specials that lack one.</summary>
        public float Range { get; }
    }

    /// <summary>
    /// Plain map data. Grids are row-major (index = y * Width + x); positions are in tiles.
    /// Sim never parses JSON: the server builds this from content/maps/*.json.
    /// </summary>
    public sealed class MapData
    {
        private readonly List<ConsoleDef> _consoles;
        private readonly List<ZoneDef> _zones;
        private readonly List<ZoneDef> _dark;
        private readonly List<ZoneDef> _gasRooms;
        private readonly List<Vec2> _spawns;

        public MapData(
            int width,
            int height,
            bool[] walkable,
            bool[] blocksSight,
            IReadOnlyList<ZoneDef> zones,
            IReadOnlyList<ConsoleDef> consoles,
            IReadOnlyList<Vec2> spawns,
            string id = "",
            string version = "",
            IReadOnlyList<ZoneDef>? dark = null,
            IReadOnlyList<ZoneDef>? gasRooms = null)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("Map size must be positive.");
            }

            if (walkable == null || walkable.Length != width * height)
            {
                throw new ArgumentException("walkable must hold width * height cells.", nameof(walkable));
            }

            if (blocksSight == null || blocksSight.Length != width * height)
            {
                throw new ArgumentException("blocksSight must hold width * height cells.", nameof(blocksSight));
            }

            if (spawns == null || spawns.Count == 0)
            {
                throw new ArgumentException("A map needs at least one spawn.", nameof(spawns));
            }

            Width = width;
            Height = height;
            Id = id ?? "";
            Version = version ?? "";
            _zones = new List<ZoneDef>(zones ?? Array.Empty<ZoneDef>());
            _dark = new List<ZoneDef>(dark ?? Array.Empty<ZoneDef>());
            _gasRooms = new List<ZoneDef>(gasRooms ?? Array.Empty<ZoneDef>());
            _consoles = new List<ConsoleDef>(consoles ?? Array.Empty<ConsoleDef>());
            _spawns = new List<Vec2>(spawns);

            var seen = new HashSet<string>();
            foreach (ConsoleDef c in _consoles)
            {
                if (!seen.Add(c.Id))
                {
                    throw new ArgumentException("Duplicate console id: " + c.Id, nameof(consoles));
                }
            }

            Walk = new WalkGrid(width, height, (bool[])walkable.Clone());
            Sight = new GridLineOfSight(width, height, (bool[])blocksSight.Clone());
        }

        public string Id { get; }

        public string Version { get; }

        public int Width { get; }

        public int Height { get; }

        public WalkGrid Walk { get; }

        public GridLineOfSight Sight { get; }

        public IReadOnlyList<ZoneDef> Zones => _zones;

        /// <summary>Dark rooms: rects where a citizen sees less (vision.dark_mult). Empty when the map has none.</summary>
        public IReadOnlyList<ZoneDef> Dark => _dark;

        /// <summary>Rooms the Toxic Gas can fill (map gas_rooms). Empty when the map has none.</summary>
        public IReadOnlyList<ZoneDef> GasRooms => _gasRooms;

        public IReadOnlyList<ConsoleDef> Consoles => _consoles;

        public IReadOnlyList<Vec2> Spawns => _spawns;

        public ConsoleDef? FindConsole(string id)
        {
            foreach (ConsoleDef c in _consoles)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }

            return null;
        }

        public List<ConsoleDef> ConsolesOfKind(ConsoleKind kind)
        {
            var list = new List<ConsoleDef>();
            foreach (ConsoleDef c in _consoles)
            {
                if (c.Kind == kind)
                {
                    list.Add(c);
                }
            }

            return list;
        }

        /// <summary>True when p lies inside a dark room.</summary>
        public bool IsDark(Vec2 p)
        {
            foreach (ZoneDef z in _dark)
            {
                if (z.Contains(p))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasZone(string id)
        {
            foreach (ZoneDef z in _zones)
            {
                if (z.Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Id of the first zone containing p, or "" when none does.</summary>
        public string ZoneAt(Vec2 p)
        {
            foreach (ZoneDef z in _zones)
            {
                if (z.Contains(p))
                {
                    return z.Id;
                }
            }

            return "";
        }

        public Vec2 SpawnFor(int index) => _spawns[index % _spawns.Count];
    }
}
