using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// The v2 observation for the bot networks. Map-independent: no absolute position, no map-order zone or spot index.
    /// Targets are relative offsets (dx, dy over RelScale) and walking distances over DistScale, picked nearest first by
    /// path; a slot with nothing in it is all 0 (present 0). Every value is in [-1, 1]. Input is only the seat's SeatView,
    /// its BotMemory and the map: nothing a human in that seat could not know. The layout is in <see cref="Names"/>;
    /// a change to it or to a scale changes <see cref="SchemaText"/>.
    /// </summary>
    public static class ObservationBuilderV2
    {
        public const int PlayerSlots = 4;
        public const int SeatSlots = 10;
        public const int BodySlots = 4;
        public const int ZoneSlots = 3;
        public const int DirtySlots = 3;
        public const int CleanSlots = 3;

        private const float RelScale = 8f;
        private const float DistScale = 60f;
        private const float AgeScale = 60f;
        private const float SinceMeetingScale = 60f;
        private const float MeterMax = 100f;
        private const float SuspicionMax = 100f;
        private const float CountScale = 4f;
        private const float BagMeterAdd = 10f;
        private const float BagsToWinScale = 10f;
        private const float MinDivisor = 1e-6f;

        private static readonly IReadOnlyList<string> NameList = MakeNames();
        private static readonly Lazy<string> Text = new Lazy<string>(BuildSchemaText);

        /// <summary>Field names in vector order.</summary>
        public static IReadOnlyList<string> Names => NameList;

        public static int Size => NameList.Count;

        /// <summary>The field names, the size and every scale constant (the action names are added by the action schema).</summary>
        public static string SchemaText => Text.Value;

        /// <param name="now">Match time in seconds (same clock as the memory).</param>
        public static void Build(SeatView view, BotMemory mem, MapData map, MatchConfig cfg, float now, float[] into)
        {
            if (into == null || into.Length < Size)
            {
                throw new ArgumentException("into must hold at least Size floats.", nameof(into));
            }

            if (cfg.Seats > SeatSlots)
            {
                throw new NotSupportedException("The match has " + cfg.Seats + " seats; the observation schema holds " + SeatSlots + ".");
            }

            Array.Clear(into, 0, Size);
            var w = new Writer(into);
            MapDistances dist = MapDistances.For(map);
            Vec2 me = view.Me.Position;
            bool playing = ActionQuery.Playing(view);
            bool trasher = view.Me.Role == Role.Polluter;
            MyView my = view.Me;

            var sus = new SuspicionModel();
            sus.Update(mem, view);
            List<PlayerView> visible = playing ? ActionQuery.Slots(view) : new List<PlayerView>();

            // self
            w.Put(trasher ? 1f : 0f);
            w.Put(Unit(Ratio(my.Vision, Math.Max(cfg.VisionCitizen, cfg.VisionPolluter))));
            w.Put(Unit(Ratio(view.EmergenciesLeft, cfg.EmergenciesPerMatch)));
            PutTarget(w, dist, me, NearestOfKind(map, dist, me, ConsoleKind.Emergency)?.Position);
            w.Put(Unit(mem.SinceMeeting(now) / SinceMeetingScale));
            w.Put(Unit(mem.HeatVotes(my.Seat)));
            w.Put(Unit(mem.HeatAccusations(my.Seat)));
            w.Put(Unit(mem.MeetingsSinceAccused(my.Seat) / (float)BotMemory.MeetingsSinceAccusedCap));

            // zones, nearest centre first
            PutZones(w, map, dist, me);

            // cleaner kit
            bool cleaner = !trasher;
            w.Put(cleaner ? Unit(Ratio(my.Bags, my.BagCap)) : 0f);
            w.Put(cleaner && my.BagCap > 0 && my.Bags >= my.BagCap ? 1f : 0f);
            w.Put(cleaner && my.BagCap > cfg.BagCap ? 1f : 0f);
            w.Put(cleaner && my.HasTool ? 1f : 0f);
            w.Put(cleaner ? Unit(my.Heals) : 0f);
            w.Put(cleaner ? Unit(my.Masks) : 0f);
            bool infected = cleaner && my.InfectedS != null;
            w.Put(infected ? 1f : 0f);
            w.Put(infected ? Unit(Ratio(my.InfectedS!.Value, cfg.VirusStartS)) : 0f);
            w.Put(infected && !mem.InfectedCallUsed ? 1f : 0f);

            // offer
            IReadOnlyList<UpgradeKind>? offer = cleaner ? mem.UpgradeOffer : null;
            w.Put(offer != null && offer.Count > 0 ? 1f : 0f);
            w.Put(Offers(offer, UpgradeKind.Backpack));
            w.Put(Offers(offer, UpgradeKind.Tool));
            w.Put(Offers(offer, UpgradeKind.Heal));
            w.Put(Offers(offer, UpgradeKind.Mask));

            // machine
            ConsoleDef? machine = ActionQuery.MachineConsole(map);
            w.Put(view.MachineJammed ? 1f : 0f);
            PutTarget(w, dist, me, machine?.Position);
            w.Put(playing && machine != null && ActionQuery.InRange(map, me, machine) ? 1f : 0f);

            // meter
            w.Put(Unit(view.Meter / MeterMax));
            w.Put(Unit((MeterMax - view.Meter) / BagMeterAdd / BagsToWinScale));

            // spots
            int dirty = 0;
            int clean = 0;
            foreach (SpotView s in view.Spots)
            {
                if (s.State == Sim.Core.SpotState.Dirty)
                {
                    dirty++;
                }
                else
                {
                    clean++;
                }
            }

            w.Put(view.Spots.Count == 0 ? 0f : dirty / (float)view.Spots.Count);
            w.Put(view.Spots.Count == 0 ? 0f : clean / (float)view.Spots.Count);
            List<ConsoleDef> dirtySpots = playing ? ActionQuery.SpotsNearestFirst(view, map, Sim.Core.SpotState.Dirty) : new List<ConsoleDef>();
            for (int i = 0; i < DirtySlots; i++)
            {
                if (i >= dirtySpots.Count)
                {
                    w.Skip(5);
                    continue;
                }

                ConsoleDef c = dirtySpots[i];
                w.Put(1f);
                PutTarget(w, dist, me, c.Position);
                bool occupied = false;
                foreach (PlayerView p in visible)
                {
                    occupied |= Vec2.Distance(p.Position, c.Position) <= c.Range;
                }

                w.Put(occupied ? 1f : 0f);
            }

            List<ConsoleDef> cleanSpots = playing ? ActionQuery.SpotsNearestFirst(view, map, Sim.Core.SpotState.Clean) : new List<ConsoleDef>();
            float watchRange = WatchRange(view, cfg);
            for (int i = 0; i < CleanSlots; i++)
            {
                if (i >= cleanSpots.Count)
                {
                    w.Skip(5);
                    continue;
                }

                ConsoleDef c = cleanSpots[i];
                int watchers = 0;
                foreach (PlayerView p in visible)
                {
                    if (!ActionQuery.ContainsSeat(my.Partners, p.Seat) && Vec2.Distance(p.Position, c.Position) <= watchRange
                        && map.Sight.HasLineOfSight(c.Position, p.Position))
                    {
                        watchers++;
                    }
                }

                w.Put(1f);
                PutTarget(w, dist, me, c.Position);
                w.Put(Unit(watchers / CountScale));
            }

            // trasher kit
            CooldownView? cd = trasher ? my.Cooldowns : null;
            w.Put(trasher ? Unit(Ratio(my.Repollutes, cfg.UnlockAfter)) : 0f);
            w.Put(trasher && my.Ability == TrasherAbility.None ? 1f : 0f);
            w.Put(trasher && my.Ability == TrasherAbility.Virus ? 1f : 0f);
            w.Put(trasher && my.Ability == TrasherAbility.Gas ? 1f : 0f);
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Repollute, cfg.RepolluteCooldownS)));
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Virus, cfg.VirusCooldownS)));
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Gas, cfg.GasCooldownS)));
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Sabotage, cfg.SabotageSharedCooldownS)));
            w.Put(trasher && playing ? Unit(ActionQuery.Watchers(view, cfg, map) / CountScale) : 0f);

            // gas
            PutGas(w, view, map, cfg, dist, me);

            // visible players, nearest first
            for (int i = 0; i < PlayerSlots; i++)
            {
                if (i >= visible.Count)
                {
                    w.Skip(7);
                    continue;
                }

                PlayerView p = visible[i];
                w.Put(1f);
                PutTarget(w, dist, me, p.Position);
                w.Put(ActionQuery.ContainsSeat(my.Partners, p.Seat) ? 1f : 0f);
                w.Put(Unit(sus.Score(p.Seat) / SuspicionMax));
                w.Put(Unit(mem.HeatOf(p.Seat)));
            }

            // remembered players, by seat
            for (int s = 0; s < SeatSlots; s++)
            {
                byte seat = (byte)s;
                if (seat == my.Seat)
                {
                    w.Skip(5);
                    continue;
                }

                float? age = mem.LastSeenAge(seat, now);
                Vec2? last = mem.LastSeenPosition(seat);
                w.Put(age == null ? 1f : Unit(age.Value / AgeScale));
                w.Put(last == null ? 0f : Rel(last.Value.X - me.X));
                w.Put(last == null ? 0f : Rel(last.Value.Y - me.Y));
                w.Put(Unit(sus.Score(seat) / SuspicionMax));
                w.Put(mem.IsKnownOut(seat) ? 1f : 0f);
            }

            // bodies, nearest first by path
            List<PileView> bodies = new List<PileView>();
            if (playing)
            {
                foreach (PileView p in view.Piles)
                {
                    if (p.Kind == PileKind.Body)
                    {
                        bodies.Add(p);
                    }
                }
            }

            var order = new Dictionary<int, int>();
            foreach (PileView p in bodies)
            {
                order[p.Id] = PathKey(dist, me, p.Position);
            }

            bodies.Sort((a, b) =>
            {
                int c = order[a.Id].CompareTo(order[b.Id]);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < BodySlots; i++)
            {
                if (i >= bodies.Count)
                {
                    w.Skip(5);
                    continue;
                }

                PileView p = bodies[i];
                w.Put(1f);
                PutTarget(w, dist, me, p.Position);
                w.Put(p.State == PileState.Fresh ? 1f : 0f);
            }

            // world
            bool smog = ActionQuery.SmogActive(view);
            ConsoleDef? panel = NearestOfKind(map, dist, me, ConsoleKind.SmogFix);
            w.Put(view.Phase == MatchPhase.Meeting ? 1f : 0f);
            w.Put(Unit((view.DeadlineS - view.DeadlineClockValue) / Math.Max(1f, view.DeadlineS)));
            w.Put(smog ? 1f : 0f);
            w.Put(smog ? Unit(view.Sabotage!.SmogLevel) : 0f);
            w.Put(panel == null ? 1f : PathNorm(dist, me, panel.Position));
            bool atButton = false;
            foreach (ConsoleDef c in map.ConsolesOfKind(ConsoleKind.Emergency))
            {
                atButton |= ActionQuery.InRange(map, me, c);
            }

            w.Put(playing && atButton ? 1f : 0f);
            w.Put(playing && panel != null && ActionQuery.InRange(map, me, panel) ? 1f : 0f);
            bool reportable = false;
            foreach (PileView p in bodies)
            {
                reportable |= p.State == PileState.Fresh && Vec2.Distance(p.Position, me) <= cfg.ReportRange;
            }

            w.Put(reportable ? 1f : 0f);

            if (w.Count != Size)
            {
                throw new InvalidOperationException("Observation layout wrote " + w.Count + " values, expected " + Size);
            }
        }

        private static void PutZones(Writer w, MapData map, MapDistances dist, Vec2 me)
        {
            var keys = new List<KeyValuePair<int, int>>();
            for (int z = 0; z < map.Zones.Count; z++)
            {
                keys.Add(new KeyValuePair<int, int>(PathKey(dist, me, ActionQuery.ZoneCentre(map, z)), z));
            }

            keys.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                return c != 0 ? c : string.CompareOrdinal(map.Zones[a.Value].Id, map.Zones[b.Value].Id);
            });
            for (int i = 0; i < ZoneSlots; i++)
            {
                if (i >= keys.Count)
                {
                    w.Skip(4);
                    continue;
                }

                ZoneDef zone = map.Zones[keys[i].Value];
                PutTarget(w, dist, me, ActionQuery.ZoneCentre(map, keys[i].Value));
                w.Put(zone.Contains(me) ? 1f : 0f);
            }
        }

        private static void PutGas(Writer w, SeatView view, MapData map, MatchConfig cfg, MapDistances dist, Vec2 me)
        {
            GasView? gas = view.Gas;
            ZoneDef? warned = null;
            if (gas != null)
            {
                foreach (ZoneDef room in map.GasRooms)
                {
                    if (room.Id == gas.Room)
                    {
                        warned = room;
                        break;
                    }
                }
            }

            w.Put(gas != null ? 1f : 0f);
            w.Put(gas != null ? Unit(Ratio(gas.InS, cfg.GasWarningS)) : 0f);
            w.Put(warned != null && warned.Contains(me) ? 1f : 0f);
            if (warned == null)
            {
                w.Skip(3);
            }
            else
            {
                PutTarget(w, dist, me, RoomCentre(dist, warned));
            }

            bool inAny = false;
            foreach (ZoneDef room in map.GasRooms)
            {
                inAny |= room.Contains(me);
            }

            w.Put(inAny ? 1f : 0f);
            ZoneDef? best = ActionQuery.GasRoomWithPlayers(view, map, out int players);
            w.Put(best == null ? 0f : PathNorm(dist, me, RoomCentre(dist, best)));
            w.Put(best == null ? 0f : Unit(players / CountScale));
        }

        private static Vec2 RoomCentre(MapDistances dist, ZoneDef room) => dist.NearestWalkable(new Vec2(room.X + room.Width / 2f, room.Y + room.Height / 2f));

        /// <summary>The console of a kind nearest by walking distance (unreachable ones dropped; ties by id), or null.</summary>
        private static ConsoleDef? NearestOfKind(MapData map, MapDistances dist, Vec2 me, ConsoleKind kind)
        {
            ConsoleDef? best = null;
            int bestD = int.MaxValue;
            foreach (ConsoleDef c in map.ConsolesOfKind(kind))
            {
                int d = dist.Path(me, c.Position);
                if (d == MapDistances.Unreachable)
                {
                    continue;
                }

                if (d < bestD || (d == bestD && best != null && string.CompareOrdinal(c.Id, best.Id) < 0))
                {
                    best = c;
                    bestD = d;
                }
            }

            return best;
        }

        /// <summary>Citizen sight range, shrunk by smog (the range ActionQuery.Watchers uses).</summary>
        private static float WatchRange(SeatView view, MatchConfig cfg)
        {
            float level = ActionQuery.SmogActive(view) ? view.Sabotage!.SmogLevel : 0f;
            level = level < 0f ? 0f : (level > 1f ? 1f : level);
            return cfg.VisionCitizen - level * (cfg.VisionCitizen - cfg.SmogMin);
        }

        private static float Offers(IReadOnlyList<UpgradeKind>? offer, UpgradeKind kind)
        {
            if (offer != null)
            {
                foreach (UpgradeKind k in offer)
                {
                    if (k == kind)
                    {
                        return 1f;
                    }
                }
            }

            return 0f;
        }

        /// <summary>Path length for sorting: unreachable goes last.</summary>
        private static int PathKey(MapDistances dist, Vec2 from, Vec2 to)
        {
            int d = dist.Path(from, to);
            return d == MapDistances.Unreachable ? int.MaxValue : d;
        }

        /// <summary>dx, dy, dist of a target; no target (null) writes 0, 0 and dist 1 (far).</summary>
        private static void PutTarget(Writer w, MapDistances dist, Vec2 me, Vec2? target)
        {
            if (target == null)
            {
                w.Put(0f);
                w.Put(0f);
                w.Put(1f);
                return;
            }

            w.Put(Rel(target.Value.X - me.X));
            w.Put(Rel(target.Value.Y - me.Y));
            w.Put(PathNorm(dist, me, target.Value));
        }

        /// <summary>Clamp to [0, 1]; NaN becomes 0.</summary>
        private static float Unit(float v) => float.IsNaN(v) ? 0f : (v < 0f ? 0f : (v > 1f ? 1f : v));

        /// <summary>a / b, or 0 when b is not positive (a config value of 0 must not give NaN).</summary>
        private static float Ratio(float a, float b) => b > MinDivisor ? a / b : 0f;

        private static float Rel(float delta)
        {
            float v = delta / RelScale;
            return float.IsNaN(v) ? 0f : (v < -1f ? -1f : (v > 1f ? 1f : v));
        }

        private static float PathNorm(MapDistances dist, Vec2 from, Vec2 to)
        {
            int d = dist.Path(from, to);
            return d == MapDistances.Unreachable ? 1f : Unit(d / DistScale);
        }

        private static IReadOnlyList<string> MakeNames()
        {
            var n = new List<string>();
            n.AddRange(new[]
            {
                "self.is_trasher", "self.vision", "self.emergencies_left", "self.emergency.dx", "self.emergency.dy", "self.emergency.dist",
                "self.since_meeting", "self.heat_votes", "self.heat_accusations", "self.meetings_since_accused",
            });
            for (int i = 0; i < ZoneSlots; i++)
            {
                AddAll(n, "zone" + i + ".", "dx", "dy", "dist", "inside");
            }

            AddAll(n, "kit.", "bags_frac", "bag_full", "backpack", "tool", "heals", "masks", "infected", "infected_left", "infected_call_unused");
            AddAll(n, "offer.", "open", "has_backpack", "has_tool", "has_heal", "has_mask");
            AddAll(n, "machine.", "jammed", "dx", "dy", "dist", "reach");
            AddAll(n, "meter.", "cleanliness", "bags_to_win");
            AddAll(n, "spots.", "dirty_frac", "clean_frac");
            for (int i = 0; i < DirtySlots; i++)
            {
                AddAll(n, "dirty" + i + ".", "present", "dx", "dy", "dist", "occupied");
            }

            for (int i = 0; i < CleanSlots; i++)
            {
                AddAll(n, "clean" + i + ".", "present", "dx", "dy", "dist", "watchers");
            }

            AddAll(n, "trasher.", "unlock_progress", "ability_none", "ability_virus", "ability_gas", "cd_repollute", "cd_virus", "cd_gas", "cd_sabotage", "watchers_now");
            AddAll(n, "gas.", "warning", "in_s", "i_am_in_warned_room", "warned.dx", "warned.dy", "warned.dist", "in_any_gas_room", "best_room.dist", "best_room.players");
            for (int i = 0; i < PlayerSlots; i++)
            {
                AddAll(n, "player" + i + ".", "present", "dx", "dy", "dist", "partner", "suspicion", "heat");
            }

            for (int s = 0; s < SeatSlots; s++)
            {
                AddAll(n, "seat" + s + ".", "age", "dx", "dy", "suspicion", "out");
            }

            for (int i = 0; i < BodySlots; i++)
            {
                AddAll(n, "body" + i + ".", "present", "dx", "dy", "dist", "fresh");
            }

            n.AddRange(new[]
            {
                "world.in_meeting", "world.time_left", "world.smog", "world.smog_level", "world.smog_panel.dist",
                "reach.emergency", "reach.smog_panel", "reach.report_body",
            });
            return n;
        }

        private static void AddAll(List<string> names, string prefix, params string[] fields)
        {
            foreach (string f in fields)
            {
                names.Add(prefix + f);
            }
        }

        private static string BuildSchemaText()
        {
            var sb = new StringBuilder();
            foreach (string name in NameList)
            {
                sb.Append(name).Append('\n');
            }

            sb.Append("size=").Append(NameList.Count).Append('\n');

            // a rescale changes what a value means, so it changes the text
            Const(sb, "RelScale", RelScale);
            Const(sb, "DistScale", DistScale);
            Const(sb, "AgeScale", AgeScale);
            Const(sb, "SinceMeetingScale", SinceMeetingScale);
            Const(sb, "MeterMax", MeterMax);
            Const(sb, "SuspicionMax", SuspicionMax);
            Const(sb, "CountScale", CountScale);
            Const(sb, "BagMeterAdd", BagMeterAdd);
            Const(sb, "BagsToWinScale", BagsToWinScale);
            Const(sb, "DirectWeight", SuspicionModel.DirectWeight);
            Const(sb, "NearPileWeight", SuspicionModel.NearPileWeight);
            Const(sb, "NearPileWindowS", SuspicionModel.NearPileWindowS);
            sb.Append("slots:players=").Append(PlayerSlots).Append(",seats=").Append(SeatSlots).Append(",bodies=").Append(BodySlots)
                .Append(",zones=").Append(ZoneSlots).Append(",dirty=").Append(DirtySlots).Append(",clean=").Append(CleanSlots).Append('\n');
            return sb.ToString();
        }

        private static void Const(StringBuilder sb, string name, float value)
        {
            sb.Append("const:").Append(name).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        private sealed class Writer
        {
            private readonly float[] _into;

            public Writer(float[] into)
            {
                _into = into;
            }

            public int Count { get; private set; }

            public void Put(float v)
            {
                if (Count < _into.Length)
                {
                    _into[Count] = v;
                }

                Count++;
            }

            public void Skip(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    Put(0f);
                }
            }
        }
    }
}
