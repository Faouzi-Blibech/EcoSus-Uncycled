using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>Questions about a SeatView and the map shared by the action masks and the executor.</summary>
    internal static class ActionQuery
    {
        public static bool Playing(SeatView view) =>
            view.Phase == MatchPhase.Playing && view.Me.Status == PlayerStatus.Active;

        /// <summary>Within the console range and in line of sight (the engine checks both).</summary>
        public static bool InRange(MapData map, Vec2 pos, ConsoleDef console) =>
            Vec2.Distance(pos, console.Position) <= console.Range && map.Sight.HasLineOfSight(pos, console.Position);

        public static bool FireActive(SeatView view) => view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Fire;

        public static bool SmogActive(SeatView view) => view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Smog;

        /// <summary>The n-th console of a kind in map order, or null.</summary>
        public static ConsoleDef? NthOfKind(MapData map, ConsoleKind kind, int n)
        {
            int i = 0;
            foreach (ConsoleDef c in map.Consoles)
            {
                if (c.Kind != kind)
                {
                    continue;
                }

                if (i == n)
                {
                    return c;
                }

                i++;
            }

            return null;
        }

        public static ConsoleDef? ConsoleInRange(SeatView view, MapData map, ConsoleKind kind)
        {
            foreach (ConsoleDef c in map.Consoles)
            {
                if (c.Kind == kind && InRange(map, view.Me.Position, c))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>True when this bot's own fire hold (tracked in memory) is still listed as held.</summary>
        public static bool HoldingFire(SeatView view, BotMemory mem) =>
            mem.HeldFireConsole != null && FireActive(view) && ContainsId(view.Sabotage!.Held, mem.HeldFireConsole);

        /// <summary>A fire console in reach that this bot can hold: free, or the one it already holds.</summary>
        public static ConsoleDef? HoldableFireConsoleInRange(SeatView view, MapData map, BotMemory mem)
        {
            foreach (ConsoleDef c in map.Consoles)
            {
                if (c.Kind != ConsoleKind.FireFix || !InRange(map, view.Me.Position, c))
                {
                    continue;
                }

                bool heldByOther = view.Sabotage != null && ContainsId(view.Sabotage.Held, c.Id) && c.Id != mem.HeldFireConsole;
                if (!heldByOther)
                {
                    return c;
                }
            }

            return null;
        }

        public static bool ContainsId(IReadOnlyList<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Own unfinished tasks, nearest console first (by walking distance), then by task id.</summary>
        public static List<TaskInfo> OpenTasksNearestFirst(SeatView view, MapData map)
        {
            MapDistances dist = MapDistances.For(map);
            var open = new List<KeyValuePair<int, TaskInfo>>();
            foreach (TaskInfo t in view.Me.Tasks)
            {
                if (t.StepsDone >= t.Steps)
                {
                    continue;
                }

                ConsoleDef? c = map.FindConsole(t.ConsoleId);
                int d = c == null ? int.MaxValue : dist.Path(view.Me.Position, c.Position);
                open.Add(new KeyValuePair<int, TaskInfo>(d == MapDistances.Unreachable ? int.MaxValue : d, t));
            }

            open.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : a.Value.Id.CompareTo(b.Value.Id);
            });
            var result = new List<TaskInfo>(open.Count);
            foreach (KeyValuePair<int, TaskInfo> kv in open)
            {
                result.Add(kv.Value);
            }

            return result;
        }

        /// <summary>Task consoles on the map, nearest first (a polluter's "next task" to fake).</summary>
        public static List<ConsoleDef> TaskConsolesNearestFirst(SeatView view, MapData map)
        {
            MapDistances dist = MapDistances.For(map);
            var list = new List<KeyValuePair<int, ConsoleDef>>();
            foreach (ConsoleDef c in map.Consoles)
            {
                if (c.Kind != ConsoleKind.Task)
                {
                    continue;
                }

                int d = dist.Path(view.Me.Position, c.Position);
                list.Add(new KeyValuePair<int, ConsoleDef>(d == MapDistances.Unreachable ? int.MaxValue : d, c));
            }

            list.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Value.Id, b.Value.Id);
            });
            var result = new List<ConsoleDef>(list.Count);
            foreach (KeyValuePair<int, ConsoleDef> kv in list)
            {
                result.Add(kv.Value);
            }

            return result;
        }

        /// <summary>The console of an own unfinished task step that is within reach, or null.</summary>
        public static TaskInfo? OpenTaskInRange(SeatView view, MapData map, out ConsoleDef? console)
        {
            foreach (TaskInfo t in OpenTasksNearestFirst(view, map))
            {
                ConsoleDef? c = map.FindConsole(t.ConsoleId);
                if (c != null && InRange(map, view.Me.Position, c))
                {
                    console = c;
                    return t;
                }
            }

            console = null;
            return null;
        }

        /// <summary>The first task console (any) in reach, or null.</summary>
        public static ConsoleDef? TaskConsoleInRange(SeatView view, MapData map) => ConsoleInRange(view, map, ConsoleKind.Task);

        /// <summary>Visible players (not out), nearest first, ties by seat.</summary>
        public static List<PlayerView> Slots(SeatView view)
        {
            var list = new List<PlayerView>();
            foreach (PlayerView p in view.Players)
            {
                if (!p.IsOut)
                {
                    list.Add(p);
                }
            }

            Vec2 me = view.Me.Position;
            list.Sort((a, b) =>
            {
                int c = Vec2.Distance(a.Position, me).CompareTo(Vec2.Distance(b.Position, me));
                return c != 0 ? c : a.Seat.CompareTo(b.Seat);
            });
            return list;
        }

        /// <summary>Nearest visible pile satisfying the filter, or null.</summary>
        public static PileView? NearestPile(SeatView view, Func<PileView, bool> filter)
        {
            PileView? best = null;
            float bestDist = float.MaxValue;
            foreach (PileView p in view.Piles)
            {
                if (!filter(p))
                {
                    continue;
                }

                float d = Vec2.Distance(p.Position, view.Me.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        public static bool IsFresh(PileView p) => p.State == PileState.Fresh;

        public static bool IsCleanable(PileView p) =>
            p.Kind == PileKind.Dump && (p.State == PileState.Fresh || p.State == PileState.Reported);

        /// <summary>Nearest visible non-partner within hit range that is not inside an own-hit immunity window.</summary>
        public static PlayerView? HitTarget(SeatView view, BotMemory mem, MatchConfig cfg)
        {
            PlayerView? best = null;
            float bestDist = float.MaxValue;
            foreach (PlayerView p in Slots(view))
            {
                float d = Vec2.Distance(p.Position, view.Me.Position);
                if (d > cfg.HitRange || ContainsSeat(view.Me.Partners, p.Seat) || mem.RecentlyHit(p.Seat)
                    || !mem.Map.Sight.HasLineOfSight(view.Me.Position, p.Position))
                {
                    continue;
                }

                if (d < bestDist)
                {
                    bestDist = d;
                    best = p;
                }
            }

            return best;
        }

        public static bool ContainsSeat(IReadOnlyList<byte> seats, byte seat)
        {
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i] == seat)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- v2: spots, the machine, rooms ----------

        public static ConsoleDef? MachineConsole(MapData map) => NthOfKind(map, ConsoleKind.Machine, 0);

        public static bool Infected(SeatView view) => view.Me.InfectedS != null;

        /// <summary>Consoles of the spots in the given state (every seat sees every spot's state), in view order.</summary>
        public static List<ConsoleDef> SpotConsoles(SeatView view, MapData map, SpotState state)
        {
            var list = new List<ConsoleDef>();
            foreach (SpotView s in view.Spots)
            {
                if (s.State != state)
                {
                    continue;
                }

                ConsoleDef? c = map.FindConsole(s.Id);
                if (c != null)
                {
                    list.Add(c);
                }
            }

            return list;
        }

        /// <summary>The spots of a state nearest first by walking distance (unreachable ones dropped); ties by id.</summary>
        public static List<ConsoleDef> SpotsNearestFirst(SeatView view, MapData map, SpotState state)
        {
            MapDistances dist = MapDistances.For(map);
            var list = new List<KeyValuePair<int, ConsoleDef>>();
            foreach (ConsoleDef c in SpotConsoles(view, map, state))
            {
                int d = dist.Path(view.Me.Position, c.Position);
                if (d != MapDistances.Unreachable)
                {
                    list.Add(new KeyValuePair<int, ConsoleDef>(d, c));
                }
            }

            list.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Value.Id, b.Value.Id);
            });
            var result = new List<ConsoleDef>(list.Count);
            foreach (KeyValuePair<int, ConsoleDef> kv in list)
            {
                result.Add(kv.Value);
            }

            return result;
        }

        /// <summary>A spot of the given state within reach (range and line of sight), or null.</summary>
        public static ConsoleDef? SpotInRange(SeatView view, MapData map, SpotState state)
        {
            foreach (ConsoleDef c in SpotConsoles(view, map, state))
            {
                if (InRange(map, view.Me.Position, c))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>True when a visible player other than a partner stands within the radius of the point.</summary>
        public static bool SomeoneNear(SeatView view, Vec2 point, float radius)
        {
            foreach (PlayerView p in Slots(view))
            {
                if (!ContainsSeat(view.Me.Partners, p.Seat) && Vec2.Distance(p.Position, point) <= radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Visible players (not partners) close enough to see this seat act: within a citizen's vision (shrunk by smog) with line of sight.</summary>
        public static int Watchers(SeatView view, MatchConfig cfg, MapData map)
        {
            float level = view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Smog ? view.Sabotage.SmogLevel : 0f;
            level = level < 0f ? 0f : (level > 1f ? 1f : level);
            float range = cfg.VisionCitizen - level * (cfg.VisionCitizen - cfg.SmogMin);
            int n = 0;
            foreach (PlayerView p in Slots(view))
            {
                if (!ContainsSeat(view.Me.Partners, p.Seat) && Vec2.Distance(p.Position, view.Me.Position) <= range
                    && map.Sight.HasLineOfSight(view.Me.Position, p.Position))
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>
        /// The gas room whose warning runs (the public gas warning) when this Cleaner stands in it without a
        /// mask; null otherwise. A mask holder may stay: the mask takes the gas.
        /// </summary>
        public static ZoneDef? WarnedGasRoomHere(SeatView view, BotMemory mem, MapData map)
        {
            if (mem.GasWarnedRoom == null || view.Me.Role != Role.Citizen || view.Me.Masks > 0)
            {
                return null;
            }

            foreach (ZoneDef room in map.GasRooms)
            {
                if (room.Id == mem.GasWarnedRoom && room.Contains(view.Me.Position))
                {
                    return room;
                }
            }

            return null;
        }

        /// <summary>The walkable cell just outside a room that is the shortest walk from a position; null when none is reachable.</summary>
        public static Vec2? NearestCellOutside(MapData map, ZoneDef room, Vec2 from)
        {
            MapDistances dist = MapDistances.For(map);
            Vec2? best = null;
            int bestPath = int.MaxValue;
            int x0 = (int)System.Math.Floor(room.X) - 1;
            int y0 = (int)System.Math.Floor(room.Y) - 1;
            int x1 = (int)System.Math.Ceiling(room.X + room.Width);
            int y1 = (int)System.Math.Ceiling(room.Y + room.Height);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var c = new Vec2(x + 0.5f, y + 0.5f);
                    if (room.Contains(c) || !map.Walk.IsWalkableCell(x, y))
                    {
                        continue;
                    }

                    int d = dist.Path(from, c);
                    if (d != MapDistances.Unreachable && d < bestPath)
                    {
                        bestPath = d;
                        best = c;
                    }
                }
            }

            return best;
        }

        /// <summary>The gas room (map gas_rooms) holding the most visible non-partner players, or null when none holds one.</summary>
        public static ZoneDef? GasRoomWithPlayers(SeatView view, MapData map, out int count)
        {
            ZoneDef? best = null;
            count = 0;
            foreach (ZoneDef room in map.GasRooms)
            {
                int n = 0;
                foreach (PlayerView p in Slots(view))
                {
                    if (!ContainsSeat(view.Me.Partners, p.Seat) && room.Contains(p.Position))
                    {
                        n++;
                    }
                }

                if (n > count)
                {
                    count = n;
                    best = room;
                }
            }

            return best;
        }

        public static Vec2 ZoneCentre(MapData map, int zoneIndex)
        {
            ZoneDef z = map.Zones[zoneIndex];
            return MapDistances.For(map).NearestWalkable(new Vec2(z.X + z.Width / 2f, z.Y + z.Height / 2f));
        }
    }
}
