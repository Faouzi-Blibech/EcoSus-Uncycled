using System;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>Which BotActions are valid for a seat right now (the mask the network may choose from).</summary>
    public static class ActionCatalogue
    {
        /// <summary>The 30 actions the networks were trained on (the v1 set); Mask has this length.</summary>
        public const int Count = 30;

        /// <summary>Every action of the enum, v2 ones included; FullMask has this length.</summary>
        public const int TotalCount = 42;

        private const int SlotCount = 4;
        private const float CooldownEps = 1e-3f;

        /// <summary>Stay is always valid. Outside Playing (meeting, intro, out) it is the only one.</summary>
        public static bool[] Mask(SeatView view, BotMemory mem, MapData map)
        {
            var mask = new bool[Count];
            mask[(int)BotAction.Stay] = true;
            if (!ActionQuery.Playing(view))
            {
                return mask;
            }

            MatchConfig cfg = mem.Config;
            bool polluter = view.Me.Role == Role.Polluter;
            Vec2 pos = view.Me.Position;

            if (polluter)
            {
                int consoles = ActionQuery.TaskConsolesNearestFirst(view, map).Count;
                mask[(int)BotAction.GoNextTask] = consoles >= 1;
                mask[(int)BotAction.GoTask2] = consoles >= 2;
            }
            else
            {
                int open = ActionQuery.OpenTasksNearestFirst(view, map).Count;
                mask[(int)BotAction.GoNextTask] = open >= 1;
                mask[(int)BotAction.GoTask2] = open >= 2;
            }

            for (int z = 0; z < 3; z++)
            {
                mask[(int)BotAction.GoZone0 + z] = z < map.Zones.Count;
            }

            bool fire = ActionQuery.FireActive(view);
            bool smog = ActionQuery.SmogActive(view);
            mask[(int)BotAction.GoEmergency] = ActionQuery.NthOfKind(map, ConsoleKind.Emergency, 0) != null;
            mask[(int)BotAction.GoFireA] = fire && ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 0) != null;
            mask[(int)BotAction.GoFireB] = fire && ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 1) != null;
            mask[(int)BotAction.GoSmogPanel] = smog && ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0) != null;

            int visible = ActionQuery.Slots(view).Count;
            for (int i = 0; i < SlotCount; i++)
            {
                mask[(int)BotAction.FollowSlot0 + i] = i < visible;
                mask[(int)BotAction.AvoidSlot0 + i] = i < visible;
            }

            mask[(int)BotAction.ReportNearest] = ActionQuery.NearestPile(view, ActionQuery.IsFresh) != null;

            bool emergencyReady = !fire
                && view.EmergenciesLeft > 0
                && mem.SinceMeeting(view.MatchTime) + CooldownEps >= cfg.EmergencyMinSinceMeetingS
                && ActionQuery.ConsoleInRange(view, map, ConsoleKind.Emergency) != null;
            mask[(int)BotAction.CallMeeting] = emergencyReady;

            if (polluter)
            {
                CooldownView? cd = view.Me.Cooldowns;
                bool hitReady = cd != null && cd.Hit <= CooldownEps;
                mask[(int)BotAction.HitNearest] = hitReady && ActionQuery.HitTarget(view, mem, cfg) != null;
                mask[(int)BotAction.DumpHere] = cd != null && cd.Dump <= CooldownEps;
                bool sabotageReady = cd != null && cd.Sabotage <= CooldownEps && view.Sabotage == null;
                mask[(int)BotAction.SabotageFire] = sabotageReady && ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 0) != null;
                mask[(int)BotAction.SabotageSmog] = sabotageReady && ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0) != null;
                mask[(int)BotAction.FakeTaskHere] = ActionQuery.TaskConsoleInRange(view, map) != null;
            }
            else
            {
                ConsoleDef? taskConsole;
                mask[(int)BotAction.UseHere] = ActionQuery.OpenTaskInRange(view, map, out taskConsole) != null;
                mask[(int)BotAction.CleanNearest] = ActionQuery.NearestPile(view, ActionQuery.IsCleanable) != null;
                mask[(int)BotAction.HoldFire] = fire && ActionQuery.HoldableFireConsoleInRange(view, map, mem) != null;
                mask[(int)BotAction.ReleaseFire] = ActionQuery.HoldingFire(view, mem);
                mask[(int)BotAction.RepairSmog] = smog && ActionQuery.ConsoleInRange(view, map, ConsoleKind.SmogFix) != null;
            }

            return mask;
        }

        /// <summary>
        /// Mask over every action, v2 included (length TotalCount): the legacy mask first, then what the v2
        /// rules allow. The scripted brains choose from this one; the networks only see the legacy part.
        /// </summary>
        public static bool[] FullMask(SeatView view, BotMemory mem, MapData map)
        {
            var full = new bool[TotalCount];
            Array.Copy(Mask(view, mem, map), full, Count);
            if (!ActionQuery.Playing(view))
            {
                return full;
            }

            MatchConfig cfg = mem.Config;
            ConsoleDef? machine = ActionQuery.MachineConsole(map);
            if (view.Me.Role == Role.Polluter)
            {
                CooldownView? cd = view.Me.Cooldowns;
                bool anyDirty = ActionQuery.SpotConsoles(view, map, SpotState.Dirty).Count > 0;
                bool anyClean = ActionQuery.SpotConsoles(view, map, SpotState.Clean).Count > 0;
                full[(int)BotAction.GoDirtySpot] = anyDirty;
                full[(int)BotAction.Repollute] = anyClean && cd != null && cd.Repollute <= CooldownEps;
                full[(int)BotAction.PickAbility] = view.Me.Ability == TrasherAbility.None && view.Me.Repollutes >= cfg.UnlockAfter;
                full[(int)BotAction.PlantVirus] = anyDirty && view.Me.Ability == TrasherAbility.Virus && cd != null && cd.Virus <= CooldownEps;
                full[(int)BotAction.GasRoom] = map.GasRooms.Count > 0 && view.Me.Ability == TrasherAbility.Gas && cd != null && cd.Gas <= CooldownEps;
                bool sabotageReady = cd != null && cd.Sabotage <= CooldownEps && view.Sabotage == null && !view.MachineJammed;
                full[(int)BotAction.SabotageSmog] = sabotageReady && ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0) != null;
                full[(int)BotAction.SabotageMachine] = sabotageReady && machine != null;
                return full;
            }

            int bags = view.Me.Bags;
            bool hasRoom = bags < view.Me.BagCap;
            full[(int)BotAction.GoDirtySpot] = hasRoom && ActionQuery.SpotsNearestFirst(view, map, SpotState.Dirty).Count > 0;
            full[(int)BotAction.FinishSpot] = hasRoom && ActionQuery.SpotInRange(view, map, SpotState.Dirty) != null;
            full[(int)BotAction.GoMachine] = bags > 0 && machine != null;
            full[(int)BotAction.Deliver] = bags > 0 && !view.MachineJammed && machine != null && ActionQuery.InRange(map, view.Me.Position, machine);
            full[(int)BotAction.PickUpgrade] = mem.UpgradeOffer != null && mem.UpgradeOffer.Count > 0;
            full[(int)BotAction.HealSelf] = ActionQuery.Infected(view) && view.Me.Heals > 0;
            full[(int)BotAction.FixMachine] = view.MachineJammed && machine != null;
            if (ActionQuery.Infected(view) && !mem.InfectedCallUsed)
            {
                full[(int)BotAction.CallMeeting] = true; // the infected may call one meeting from anywhere
            }

            return full;
        }
    }
}
