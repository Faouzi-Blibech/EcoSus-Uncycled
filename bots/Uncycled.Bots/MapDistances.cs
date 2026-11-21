using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sim.Core;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Walking distances in tiles on the 4-neighbour walk grid, cached per map (a breadth-first
    /// field per target cell, built on first use). Safe to share between threads.
    /// </summary>
    public sealed class MapDistances
    {
        public const int Unreachable = -1;

        private static readonly ConditionalWeakTable<MapData, MapDistances> Cache = new ConditionalWeakTable<MapData, MapDistances>();

        private readonly MapData _map;
        private readonly ConcurrentDictionary<int, ushort[]> _fields = new ConcurrentDictionary<int, ushort[]>();

        private MapDistances(MapData map)
        {
            _map = map;
        }

        public static MapDistances For(MapData map) => Cache.GetValue(map, m => new MapDistances(m));

        /// <summary>Steps from a to b (cell to cell), or Unreachable.</summary>
        public int Path(Vec2 a, Vec2 b)
        {
            int ai = CellIndex(a);
            int bi = CellIndex(b);
            if (ai < 0 || bi < 0)
            {
                return Unreachable;
            }

            ushort d = _fields.GetOrAdd(bi, Build)[ai];
            return d == ushort.MaxValue ? Unreachable : d;
        }

        /// <summary>The walkable cell centre nearest to p (p itself when its cell is walkable).</summary>
        public Vec2 NearestWalkable(Vec2 p)
        {
            int cx = (int)Math.Floor(p.X);
            int cy = (int)Math.Floor(p.Y);
            if (_map.Walk.IsWalkableCell(cx, cy))
            {
                return p;
            }

            int maxRing = Math.Max(_map.Width, _map.Height);
            Vec2 best = p;
            float bestDist = float.MaxValue;
            for (int ring = 1; ring <= maxRing && bestDist == float.MaxValue; ring++)
            {
                for (int y = cy - ring; y <= cy + ring; y++)
                {
                    for (int x = cx - ring; x <= cx + ring; x++)
                    {
                        if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) != ring || !_map.Walk.IsWalkableCell(x, y))
                        {
                            continue;
                        }

                        var c = new Vec2(x + 0.5f, y + 0.5f);
                        float d = Vec2.Distance(c, p);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = c;
                        }
                    }
                }
            }

            return best;
        }

        private int CellIndex(Vec2 p)
        {
            if (!p.IsFinite)
            {
                return -1;
            }

            int x = (int)Math.Floor(p.X);
            int y = (int)Math.Floor(p.Y);
            return _map.Walk.IsWalkableCell(x, y) ? y * _map.Width + x : -1;
        }

        private ushort[] Build(int source)
        {
            int w = _map.Width;
            int h = _map.Height;
            var dist = new ushort[w * h];
            for (int i = 0; i < dist.Length; i++)
            {
                dist[i] = ushort.MaxValue;
            }

            var queue = new Queue<int>();
            dist[source] = 0;
            queue.Enqueue(source);
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % w;
                int cy = cur / w;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];
                    if (!_map.Walk.IsWalkableCell(nx, ny))
                    {
                        continue;
                    }

                    int ni = ny * w + nx;
                    if (dist[ni] != ushort.MaxValue)
                    {
                        continue;
                    }

                    dist[ni] = (ushort)(dist[cur] + 1);
                    queue.Enqueue(ni);
                }
            }

            return dist;
        }
    }
}
