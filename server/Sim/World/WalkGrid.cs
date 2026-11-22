using System;
using Sim.Core;

namespace Sim.World
{
    /// <summary>Walkable cells. Out of bounds is never walkable.</summary>
    public sealed class WalkGrid
    {
        private readonly bool[] _walkable;

        internal WalkGrid(int width, int height, bool[] walkable)
        {
            Width = width;
            Height = height;
            _walkable = walkable;
        }

        public int Width { get; }

        public int Height { get; }

        public bool IsWalkableCell(int x, int y) => GridRaycast.Clear(_walkable, Width, Height, true, x, y);

        public bool IsWalkable(Vec2 p) =>
            p.IsFinite && IsWalkableCell(GridRaycast.CellOf(p.X), GridRaycast.CellOf(p.Y));

        /// <summary>
        /// True when a disc of the given radius centred on p overlaps no blocked cell (out of bounds
        /// counts as blocked). The client's player body is such a disc.
        /// </summary>
        public bool CircleFits(Vec2 p, float radius)
        {
            if (!p.IsFinite)
            {
                return false;
            }

            float r2 = radius * radius;
            int x0 = GridRaycast.CellOf(p.X - radius);
            int x1 = GridRaycast.CellOf(p.X + radius);
            int y0 = GridRaycast.CellOf(p.Y - radius);
            int y1 = GridRaycast.CellOf(p.Y + radius);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (IsWalkableCell(x, y))
                    {
                        continue;
                    }

                    float nx = Math.Min(Math.Max(p.X, x), x + 1);
                    float ny = Math.Min(Math.Max(p.Y, y), y + 1);
                    float dx = p.X - nx;
                    float dy = p.Y - ny;
                    if (dx * dx + dy * dy < r2)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>True when the straight segment a-b only crosses walkable cells.</summary>
        public bool SegmentClear(Vec2 a, Vec2 b)
        {
            if (!a.IsFinite || !b.IsFinite)
            {
                return false;
            }

            return GridRaycast.IsClear(_walkable, Width, Height, true, a, b, false);
        }
    }
}
