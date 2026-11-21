using Sim.Core;

namespace Sim.World
{
    /// <summary>
    /// Line of sight on the sight-blocking grid. The cell that contains the target point is not
    /// checked, so a console mounted on a wall tile can still be seen and used.
    /// </summary>
    public sealed class GridLineOfSight
    {
        private readonly bool[] _blocks;

        internal GridLineOfSight(int width, int height, bool[] blocksSight)
        {
            Width = width;
            Height = height;
            _blocks = blocksSight;
        }

        public int Width { get; }

        public int Height { get; }

        public bool BlocksSightCell(int x, int y) => !GridRaycast.Clear(_blocks, Width, Height, false, x, y);

        public bool HasLineOfSight(Vec2 from, Vec2 to)
        {
            if (!from.IsFinite || !to.IsFinite)
            {
                return false;
            }

            return GridRaycast.IsClear(_blocks, Width, Height, false, from, to, true);
        }
    }
}
