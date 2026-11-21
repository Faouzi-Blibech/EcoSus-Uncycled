using System;
using System.Collections.Generic;
using Sim.Core;

namespace Sim.World
{
    /// <summary>4-neighbour A* on the walk grid. Deterministic (ties broken by insertion order).</summary>
    public static class AStar
    {
        /// <summary>
        /// Path of cell centres from the start cell to the goal cell (both included), or null when
        /// either end is not walkable or no path exists.
        /// </summary>
        public static List<Vec2>? FindPath(WalkGrid grid, Vec2 start, Vec2 goal)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!start.IsFinite || !goal.IsFinite)
            {
                return null;
            }

            int sx = GridRaycast.CellOf(start.X);
            int sy = GridRaycast.CellOf(start.Y);
            int gx = GridRaycast.CellOf(goal.X);
            int gy = GridRaycast.CellOf(goal.Y);
            if (!grid.IsWalkableCell(sx, sy) || !grid.IsWalkableCell(gx, gy))
            {
                return null;
            }

            int w = grid.Width;
            int h = grid.Height;
            int count = w * h;
            var gScore = new int[count];
            var cameFrom = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                gScore[i] = int.MaxValue;
                cameFrom[i] = -1;
            }

            int startIndex = sy * w + sx;
            int goalIndex = gy * w + gx;
            gScore[startIndex] = 0;
            var open = new MinHeap();
            open.Push(Heuristic(sx, sy, gx, gy), startIndex);

            int[] dxs = { 1, -1, 0, 0 };
            int[] dys = { 0, 0, 1, -1 };

            while (open.Count > 0)
            {
                int current = open.Pop();
                if (closed[current])
                {
                    continue;
                }

                if (current == goalIndex)
                {
                    return Build(cameFrom, goalIndex, w);
                }

                closed[current] = true;
                int cx = current % w;
                int cy = current / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dxs[d];
                    int ny = cy + dys[d];
                    if (!grid.IsWalkableCell(nx, ny))
                    {
                        continue;
                    }

                    int ni = ny * w + nx;
                    if (closed[ni])
                    {
                        continue;
                    }

                    int tentative = gScore[current] + 1;
                    if (tentative < gScore[ni])
                    {
                        gScore[ni] = tentative;
                        cameFrom[ni] = current;
                        open.Push(tentative + Heuristic(nx, ny, gx, gy), ni);
                    }
                }
            }

            return null;
        }

        private static int Heuristic(int x, int y, int gx, int gy) => Math.Abs(x - gx) + Math.Abs(y - gy);

        private static List<Vec2> Build(int[] cameFrom, int goalIndex, int width)
        {
            var path = new List<Vec2>();
            int i = goalIndex;
            while (i != -1)
            {
                path.Add(new Vec2(i % width + 0.5f, i / width + 0.5f));
                i = cameFrom[i];
            }

            path.Reverse();
            return path;
        }

        /// <summary>Binary min-heap on (priority, insertion order).</summary>
        private sealed class MinHeap
        {
            private readonly List<long> _keys = new List<long>();
            private readonly List<int> _values = new List<int>();
            private long _counter;

            public int Count => _values.Count;

            public void Push(int priority, int value)
            {
                long key = ((long)priority << 32) | (_counter++ & 0xFFFFFFFFL);
                _keys.Add(key);
                _values.Add(value);
                int i = _keys.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= _keys[i])
                    {
                        break;
                    }

                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop()
            {
                int result = _values[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last];
                _values[0] = _values[last];
                _keys.RemoveAt(last);
                _values.RemoveAt(last);
                int i = 0;
                int n = _keys.Count;
                while (true)
                {
                    int l = 2 * i + 1;
                    int r = l + 1;
                    int smallest = i;
                    if (l < n && _keys[l] < _keys[smallest])
                    {
                        smallest = l;
                    }

                    if (r < n && _keys[r] < _keys[smallest])
                    {
                        smallest = r;
                    }

                    if (smallest == i)
                    {
                        break;
                    }

                    Swap(i, smallest);
                    i = smallest;
                }

                return result;
            }

            private void Swap(int a, int b)
            {
                long k = _keys[a];
                _keys[a] = _keys[b];
                _keys[b] = k;
                int v = _values[a];
                _values[a] = _values[b];
                _values[b] = v;
            }
        }
    }
}
