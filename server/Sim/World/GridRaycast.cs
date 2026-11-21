using System;
using Sim.Core;

namespace Sim.World
{
    /// <summary>
    /// Walks every grid cell a segment touches (Amanatides-Woo). When the segment passes exactly
    /// through a cell corner, both side cells must be clear (no squeezing between diagonal walls).
    /// </summary>
    internal static class GridRaycast
    {
        public static int CellOf(float v) => (int)Math.Floor(v);

        /// <summary>
        /// True when every touched cell has cells[i] == clearValue (out of bounds = blocked).
        /// When skipEndCell is true the cell containing b is not checked.
        /// </summary>
        public static bool IsClear(bool[] cells, int width, int height, bool clearValue, Vec2 a, Vec2 b, bool skipEndCell)
        {
            int x = CellOf(a.X);
            int y = CellOf(a.Y);
            int endX = CellOf(b.X);
            int endY = CellOf(b.Y);

            bool atEnd = x == endX && y == endY;
            if (!(atEnd && skipEndCell) && !Clear(cells, width, height, clearValue, x, y))
            {
                return false;
            }

            if (atEnd)
            {
                return true;
            }

            double dx = (double)b.X - a.X;
            double dy = (double)b.Y - a.Y;
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepY = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            double tDeltaX = stepX != 0 ? 1.0 / Math.Abs(dx) : double.PositiveInfinity;
            double tDeltaY = stepY != 0 ? 1.0 / Math.Abs(dy) : double.PositiveInfinity;
            double tMaxX = stepX > 0 ? (x + 1 - (double)a.X) / dx
                : stepX < 0 ? ((double)a.X - x) / -dx
                : double.PositiveInfinity;
            double tMaxY = stepY > 0 ? (y + 1 - (double)a.Y) / dy
                : stepY < 0 ? ((double)a.Y - y) / -dy
                : double.PositiveInfinity;

            int maxSteps = Math.Abs(endX - x) + Math.Abs(endY - y);
            for (int i = 0; i < maxSteps; i++)
            {
                if (tMaxX < tMaxY)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY < tMaxX)
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    // Exactly through a corner: both side cells must be clear.
                    if (!Clear(cells, width, height, clearValue, x + stepX, y)
                        || !Clear(cells, width, height, clearValue, x, y + stepY))
                    {
                        return false;
                    }

                    x += stepX;
                    y += stepY;
                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                }

                atEnd = x == endX && y == endY;
                if (atEnd && skipEndCell)
                {
                    return true;
                }

                if (!Clear(cells, width, height, clearValue, x, y))
                {
                    return false;
                }

                if (atEnd)
                {
                    return true;
                }
            }

            // Float drift kept us off the exact end cell: judge by the end cell itself.
            return skipEndCell || Clear(cells, width, height, clearValue, endX, endY);
        }

        public static bool Clear(bool[] cells, int width, int height, bool clearValue, int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return false;
            }

            return cells[y * width + x] == clearValue;
        }
    }
}
