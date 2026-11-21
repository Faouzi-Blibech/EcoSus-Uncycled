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
