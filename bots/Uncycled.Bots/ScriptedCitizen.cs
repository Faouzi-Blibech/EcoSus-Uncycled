using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Scripted citizen (team doc 06 section 2): fire first, then smog, report a fresh pile, clean a
    /// dump pile nearby, do own tasks nearest first, wander when done. Votes by suspicion.
    /// </summary>
    public sealed class ScriptedCitizen : ScriptedBrain
    {
        private const float FireSafetyS = 3f;
        private const int CleanMaxPathTiles = 8;
        private const int SmogMaxPathTiles = 25;
        private const float WanderStayChance = 0.3f;

        private readonly BotsSettings _settings;

        public ScriptedCitizen(BotsSettings settings, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null)
            : base(settings, rng, taskTypes)
        {
            _settings = settings;
        }

        internal override Choice Choose(SeatView view, bool[] mask)
        {
            MapData map = Mem.Map;
            MatchConfig cfg = Mem.Config;
            Vec2 me = view.Me.Position;
            MapDistances dist = MapDistances.For(map);

            // 1. fire: hold a free console we can still reach in time
            if (ActionQuery.FireActive(view))
            {
                if (Exec.Busy && Exec.Current == BotAction.HoldFire)
                {
                    return Choice.Keep;
                }

                int index = 0;
                int bestIndex = -1;
                int bestDist = int.MaxValue;
                foreach (ConsoleDef c in map.ConsolesOfKind(ConsoleKind.FireFix))
                {
                    bool held = view.Sabotage!.Held.Contains(c.Id);
                    int d = dist.Path(me, c.Position);
                    float reach = d / cfg.SpeedTilesPerS + FireSafetyS;
                    if (!held && d != MapDistances.Unreachable && reach <= (view.Sabotage.Countdown ?? 0f) && d < bestDist)
                    {
                        bestDist = d;
                        bestIndex = index;
                    }

                    index++;
                }

                if (bestIndex >= 0)
                {
                    ConsoleDef target = ActionQuery.NthOfKind(map, ConsoleKind.FireFix, bestIndex)!;
                    if (mask[(int)BotAction.HoldFire] && ActionQuery.InRange(map, me, target))
                    {
                        return Choice.Now(BotAction.HoldFire);
                    }

                    return Same(bestIndex == 0 ? BotAction.GoFireA : BotAction.GoFireB, true);
                }
            }

            // 2. smog: some citizens walk to the panel and repair it
            if (ActionQuery.SmogActive(view))
            {
                if (mask[(int)BotAction.RepairSmog])
                {
                    return Exec.Busy && Exec.Current == BotAction.RepairSmog ? Choice.Keep : Choice.Now(BotAction.RepairSmog);
                }

                ConsoleDef? panel = ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0);
                if (panel != null && mask[(int)BotAction.GoSmogPanel] && Rng.NextFloat() < _settings.Skill
                    && dist.Path(me, panel.Position) is int p && p != MapDistances.Unreachable && p <= SmogMaxPathTiles)
                {
                    return Same(BotAction.GoSmogPanel, true);
                }
            }

            // 3. a fresh pile in sight: report it
            if (mask[(int)BotAction.ReportNearest])
            {
                return Same(BotAction.ReportNearest, true);
            }

            // 4. a dump pile close by: clean it
            PileView? pile = ActionQuery.NearestPile(view, ActionQuery.IsCleanable);
            if (pile != null && mask[(int)BotAction.CleanNearest])
            {
                int d = dist.Path(me, pile.Position);
                bool noTasks = !mask[(int)BotAction.GoNextTask];
                if (noTasks || (d != MapDistances.Unreachable && d <= CleanMaxPathTiles))
                {
                    return Same(BotAction.CleanNearest, false);
                }
            }

            // 5. own tasks
            if (mask[(int)BotAction.UseHere])
            {
                return Same(BotAction.UseHere, false);
            }

            if (mask[(int)BotAction.GoNextTask])
            {
                return Same(BotAction.GoNextTask, false);
            }

            // 6. nothing to do: wander
            if (Exec.Busy)
            {
                return Choice.Keep;
            }

            if (Rng.NextFloat() < WanderStayChance)
            {
                return Choice.Do(BotAction.Stay);
            }

            int zones = Math.Min(3, map.Zones.Count);
            return zones == 0 ? Choice.Do(BotAction.Stay) : Choice.Do(BotAction.GoZone0 + Rng.NextInt(zones));
        }

        /// <summary>Keep the running action when it is already this one.</summary>
        private Choice Same(BotAction action, bool urgent)
        {
            if (Exec.Busy && Exec.Current == action)
            {
                return Choice.Keep;
            }

            return urgent ? Choice.Now(action) : Choice.Do(action);
        }
    }
}
