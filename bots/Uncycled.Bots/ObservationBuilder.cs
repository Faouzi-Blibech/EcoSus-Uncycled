using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Builds the fixed-size float vector a policy sees. Input is only the seat's SeatView, its own
    /// BotMemory and the map: nothing a human in that seat could not know. Every value is in [-1, 1]
    /// (most in [0, 1]). Layout changes change SchemaHash, so a model trained on another layout is refused.
    /// </summary>
    public static class ObservationBuilder
    {
        public const int PlayerSlots = 4;
        public const int PileSlots = 4;
        public const int TaskSlots = 4;
        public const int SeatSlots = 8;
        public const int ZoneSlots = 3;

        private const float RelScale = 8f;
        private const float DistScale = 60f;
        private const float AgeScale = 60f;
        private const float SinceMeetingScale = 60f;
        private const float PoisonMax = 100f;
        private const float MeterMax = 100f;
        private const float SuspicionMax = 100f;
        private const float MinDivisor = 1e-6f;

        private static readonly IReadOnlyList<string> Names = MakeNames();
        private static readonly Lazy<string> Text = new Lazy<string>(BuildSchemaText);
        private static readonly Lazy<string> Hash = new Lazy<string>(ComputeHash);

        /// <summary>Field names in vector order.</summary>
        public static IReadOnlyList<string> FieldNames => Names;

        public static int Size => Names.Count;

        /// <summary>SHA-256 hex of the field names, Size, the BotAction names and the SeatView shape.</summary>
        public static string SchemaHash => Hash.Value;

        /// <summary>The text the hash is made of (field names, size, constants, actions, view shape).</summary>
        public static string SchemaText => Text.Value;

        /// <param name="now">Match time in seconds (same clock as the memory).</param>
        public static void Build(SeatView view, BotMemory mem, MapData map, MatchConfig cfg, float now, float[] into)
        {
            if (into == null || into.Length < Size)
            {
                throw new ArgumentException("into must hold at least Size floats.", nameof(into));
            }

            if (map.Zones.Count > ZoneSlots)
            {
                throw new NotSupportedException("The map has " + map.Zones.Count + " zones; the observation schema holds " + ZoneSlots + ".");
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
            bool polluter = view.Me.Role == Role.Polluter;

            var sus = new SuspicionModel();
            sus.Update(mem, view);

            // self
            w.Put(me.X / map.Width);
            w.Put(me.Y / map.Height);
            w.Put(polluter ? 1f : 0f);
            w.Put((view.Me.Poison ?? 0) / PoisonMax);
            CooldownView? cd = view.Me.Cooldowns;
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Hit, cfg.HitCooldownS)));
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Dump, cfg.DumpCooldownS)));
            w.Put(cd == null ? 0f : Unit(Ratio(cd.Sabotage, cfg.SabotageSharedCooldownS)));
            w.Put(Unit(Ratio(view.Me.Vision, Math.Max(cfg.VisionCitizen, cfg.VisionPolluter))));
            string zone = map.ZoneAt(me);
            for (int z = 0; z < ZoneSlots; z++)
            {
                w.Put(z < map.Zones.Count && map.Zones[z].Id == zone ? 1f : 0f);
            }

            w.Put(Unit(Ratio(view.EmergenciesLeft, cfg.EmergenciesPerMatch)));
            ConsoleDef? button = ActionQuery.NthOfKind(map, ConsoleKind.Emergency, 0);
            w.Put(button == null ? 1f : PathNorm(dist, me, button.Position));
            w.Put(Unit(mem.SinceMeeting(now) / SinceMeetingScale));
            w.Put(Unit(mem.HeatVotes(view.Me.Seat)));
            w.Put(Unit(mem.HeatAccusations(view.Me.Seat)));
            w.Put(Unit(mem.MeetingsSinceAccused(view.Me.Seat) / (float)BotMemory.MeetingsSinceAccusedCap));

            // own tasks, nearest console first
            List<TaskInfo> open = playing && !polluter ? ActionQuery.OpenTasksNearestFirst(view, map) : new List<TaskInfo>();
            for (int i = 0; i < TaskSlots; i++)
            {
                ConsoleDef? c = i < open.Count ? map.FindConsole(open[i].ConsoleId) : null;
                if (c == null)
                {
                    w.Skip(4);
                    continue;
                }

                w.Put(1f);
                w.Put(Rel(c.Position.X - me.X));
                w.Put(Rel(c.Position.Y - me.Y));
                w.Put(PathNorm(dist, me, c.Position));
            }

            // visible players, nearest first
            List<PlayerView> slots = playing ? ActionQuery.Slots(view) : new List<PlayerView>();
            for (int i = 0; i < PlayerSlots; i++)
            {
                if (i >= slots.Count)
                {
                    w.Skip(7);
                    continue;
                }

                PlayerView p = slots[i];
                w.Put(1f);
                w.Put(Rel(p.Position.X - me.X));
                w.Put(Rel(p.Position.Y - me.Y));
                w.Put(PathNorm(dist, me, p.Position));
                w.Put(ActionQuery.ContainsSeat(view.Me.Partners, p.Seat) ? 1f : 0f);
                w.Put(Unit(sus.Score(p.Seat) / SuspicionMax));
                w.Put(Unit(mem.HeatOf(p.Seat)));
            }

            // remembered players, by seat
            for (int s = 0; s < SeatSlots; s++)
            {
                byte seat = (byte)s;
                if (seat == view.Me.Seat)
                {
                    w.Skip(6);
                    continue;
                }

                float? age = mem.LastSeenAge(seat, now);
                w.Put(age == null ? 1f : Unit(age.Value / AgeScale));
                string? lastZone = mem.LastSeenZone(seat);
                for (int z = 0; z < ZoneSlots; z++)
                {
                    w.Put(lastZone != null && z < map.Zones.Count && map.Zones[z].Id == lastZone ? 1f : 0f);
                }

                w.Put(Unit(sus.Score(seat) / SuspicionMax));
                w.Put(mem.IsKnownOut(seat) ? 1f : 0f);
            }

            // visible piles, nearest first
            var piles = new List<PileView>(playing ? view.Piles : (IReadOnlyList<PileView>)Array.Empty<PileView>());
            piles.Sort((a, b) =>
            {
                int c = Vec2.Distance(a.Position, me).CompareTo(Vec2.Distance(b.Position, me));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            for (int i = 0; i < PileSlots; i++)
            {
                if (i >= piles.Count)
                {
                    w.Skip(6);
                    continue;
                }

                PileView p = piles[i];
                w.Put(1f);
                w.Put(Rel(p.Position.X - me.X));
                w.Put(Rel(p.Position.Y - me.Y));
                w.Put(PathNorm(dist, me, p.Position));
                w.Put(p.Kind == PileKind.Body ? 1f : 0f);
                w.Put(p.State == PileState.Fresh ? 1f : 0f);
            }

            // world
            SabotageView? sab = view.Sabotage;
            bool fire = ActionQuery.FireActive(view);
            bool smog = ActionQuery.SmogActive(view);
            ConsoleDef? fireA = ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 0);
            ConsoleDef? fireB = ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 1);
            ConsoleDef? panel = ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0);
            w.Put(view.Phase == MatchPhase.Meeting ? 1f : 0f);
            w.Put(Unit((view.DeadlineS - view.DeadlineClockValue) / Math.Max(1f, view.DeadlineS)));
            w.Put(Unit(view.Meter / MeterMax));
            w.Put(fire ? 1f : 0f);
            w.Put(fire && sab!.Countdown != null ? Unit(Ratio(sab.Countdown.Value, cfg.FireCountdownS)) : 0f);
            w.Put(smog ? 1f : 0f);
            w.Put(smog ? Unit(sab!.SmogLevel) : 0f);
            w.Put(fire && fireA != null && sab!.Held.Contains(fireA.Id) ? 1f : 0f);
            w.Put(fire && fireB != null && sab!.Held.Contains(fireB.Id) ? 1f : 0f);
            w.Put(fireA == null ? 1f : PathNorm(dist, me, fireA.Position));
            w.Put(fireB == null ? 1f : PathNorm(dist, me, fireB.Position));
            w.Put(panel == null ? 1f : PathNorm(dist, me, panel.Position));
            int totalSteps = 0;
            int doneSteps = 0;
            foreach (TaskInfo t in view.Me.Tasks)
            {
                totalSteps += t.Steps;
                doneSteps += t.StepsDone;
            }

            w.Put(totalSteps == 0 ? 0f : Unit(doneSteps / (float)totalSteps));

            // reach flags
            ConsoleDef? usable;
            w.Put(playing && !polluter && ActionQuery.OpenTaskInRange(view, map, out usable) != null ? 1f : 0f);
            w.Put(playing && button != null && ActionQuery.InRange(map, me, button) ? 1f : 0f);
            w.Put(playing && fireA != null && ActionQuery.InRange(map, me, fireA) ? 1f : 0f);
            w.Put(playing && fireB != null && ActionQuery.InRange(map, me, fireB) ? 1f : 0f);
            w.Put(playing && panel != null && ActionQuery.InRange(map, me, panel) ? 1f : 0f);
            PileView? reportable = playing ? ActionQuery.NearestPile(view, ActionQuery.IsFresh) : null;
            w.Put(reportable != null && Vec2.Distance(reportable.Position, me) <= cfg.ReportRange ? 1f : 0f);
            PileView? cleanable = playing ? ActionQuery.NearestPile(view, ActionQuery.IsCleanable) : null;
            w.Put(cleanable != null && Vec2.Distance(cleanable.Position, me) <= cfg.CleanRange ? 1f : 0f);
            w.Put(playing && polluter && ActionQuery.HitTarget(view, mem, cfg) != null ? 1f : 0f);

            if (w.Count != Size)
            {
                throw new InvalidOperationException("Observation layout wrote " + w.Count + " values, expected " + Size);
            }
        }

        /// <summary>Clamp to [0, 1]; NaN becomes 0.</summary>
        private static float Unit(float v) => float.IsNaN(v) ? 0f : (v < 0f ? 0f : (v > 1f ? 1f : v));

        /// <summary>a / b, or 0 when b is not positive (a config value of 0 must not give NaN).</summary>
        private static float Ratio(float a, float b) => b > MinDivisor ? a / b : 0f;

        private static float Rel(float delta)
        {
            float v = delta / RelScale;
            return v < -1f ? -1f : (v > 1f ? 1f : v);
        }

        private static float PathNorm(MapDistances dist, Vec2 from, Vec2 to)
        {
            int d = dist.Path(from, to);
            return d == MapDistances.Unreachable ? 1f : Unit(d / DistScale);
        }

        private static IReadOnlyList<string> MakeNames()
        {
            var n = new List<string>
            {
                "self.x", "self.y", "self.is_polluter", "self.poison", "self.cd_hit", "self.cd_dump", "self.cd_sabotage",
                "self.vision",
            };
            for (int z = 0; z < ZoneSlots; z++)
            {
                n.Add("self.in_zone" + z);
            }

            n.Add("self.emergencies_left");
            n.Add("self.emergency_dist");
            n.Add("self.since_meeting");
            n.Add("self.heat_votes");
            n.Add("self.heat_accusations");
            n.Add("self.meetings_since_accused");
            for (int i = 0; i < TaskSlots; i++)
            {
                n.Add("task" + i + ".open");
                n.Add("task" + i + ".dx");
                n.Add("task" + i + ".dy");
                n.Add("task" + i + ".dist");
            }

            for (int i = 0; i < PlayerSlots; i++)
            {
                n.Add("player" + i + ".present");
                n.Add("player" + i + ".dx");
                n.Add("player" + i + ".dy");
                n.Add("player" + i + ".dist");
                n.Add("player" + i + ".partner");
                n.Add("player" + i + ".suspicion");
                n.Add("player" + i + ".heat");
            }

            for (int s = 0; s < SeatSlots; s++)
            {
                n.Add("seat" + s + ".age");
                for (int z = 0; z < ZoneSlots; z++)
                {
                    n.Add("seat" + s + ".zone" + z);
                }

                n.Add("seat" + s + ".suspicion");
                n.Add("seat" + s + ".out");
            }

            for (int i = 0; i < PileSlots; i++)
            {
                n.Add("pile" + i + ".present");
                n.Add("pile" + i + ".dx");
                n.Add("pile" + i + ".dy");
                n.Add("pile" + i + ".dist");
                n.Add("pile" + i + ".is_body");
                n.Add("pile" + i + ".is_fresh");
            }

            n.AddRange(new[]
            {
                "world.in_meeting", "world.time_left", "world.meter", "world.fire", "world.fire_countdown", "world.smog",
                "world.smog_level", "world.fire_a_held", "world.fire_b_held", "world.fire_a_dist", "world.fire_b_dist",
                "world.smog_panel_dist", "world.my_tasks_done",
                "reach.task_here", "reach.emergency", "reach.fire_a", "reach.fire_b", "reach.smog_panel",
                "reach.report_pile", "reach.clean_pile", "reach.hit_target",
            });
            return n;
        }

        private static string BuildSchemaText()
        {
            var sb = new StringBuilder();
            foreach (string name in Names)
            {
                sb.Append(name).Append('\n');
            }

            sb.Append("size=").Append(Names.Count).Append('\n');

            // normalisation and suspicion constants: a rescale changes what a value means, so it changes the hash
            sb.Append("const:RelScale=").Append(RelScale.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:DistScale=").Append(DistScale.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:AgeScale=").Append(AgeScale.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:SinceMeetingScale=").Append(SinceMeetingScale.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:PoisonMax=").Append(PoisonMax.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:MeterMax=").Append(MeterMax.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:SuspicionMax=").Append(SuspicionMax.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:DirectWeight=").Append(SuspicionModel.DirectWeight.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:NearPileWeight=").Append(SuspicionModel.NearPileWeight.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("const:NearPileWindowS=").Append(SuspicionModel.NearPileWindowS.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            foreach (string action in Enum.GetNames(typeof(BotAction)))
            {
                sb.Append("action:").Append(action).Append('\n');
            }

            foreach (Type t in new[]
            {
                typeof(SeatView), typeof(MyView), typeof(PlayerView), typeof(PileView), typeof(SabotageView),
                typeof(MeetingView), typeof(CooldownView), typeof(TaskInfo),
            })
            {
                // Chat data is for the welcome message and the chat panels; the observation never reads it (networks never see chat):
                // MeetingView.MyChatCount, .MyText and .Living, and SeatView.GhostChat.
                // The v2 spots and bags (SeatView.Spots, MyView.Bags/BagCap/HasTool) and the Trasher kit (MyView.Repollutes/Ability, CooldownView.Repollute/Virus/Gas) are not read yet either: the trained models keep their hash.
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(x => !(t == typeof(MeetingView) && (x.Name == "MyChatCount" || x.Name == "MyText" || x.Name == "Living")) && !(t == typeof(SeatView) && (x.Name == "GhostChat" || x.Name == "Spots")) && !(t == typeof(MyView) && (x.Name == "Bags" || x.Name == "BagCap" || x.Name == "HasTool" || x.Name == "Repollutes" || x.Name == "Ability")) && !(t == typeof(CooldownView) && (x.Name == "Repollute" || x.Name == "Virus" || x.Name == "Gas")))
                    .OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    sb.Append("view:").Append(t.Name).Append('.').Append(p.Name).Append(':').Append(p.PropertyType.ToString()).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string ComputeHash()
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(SchemaText));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    hex.Append(b.ToString("x2"));
                }

                return hex.ToString();
            }
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
