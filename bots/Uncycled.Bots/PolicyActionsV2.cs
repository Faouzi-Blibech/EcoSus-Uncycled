using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// What a policy index means once resolved at decision time: the BotAction to begin and the executor hints
    /// that go with it (a spot, an upgrade, an ability, a gas room).
    /// </summary>
    public readonly struct PolicyChoice
    {
        public PolicyChoice(BotAction action, string? spot = null, UpgradeKind? upgrade = null, TrasherAbility? ability = null, string? room = null)
        {
            Action = action;
            Spot = spot;
            Upgrade = upgrade;
            Ability = ability;
            Room = room;
        }

        public BotAction Action { get; }

        public string? Spot { get; }

        public UpgradeKind? Upgrade { get; }

        public TrasherAbility? Ability { get; }

        public string? Room { get; }

        /// <summary>Sets the executor's one-shot hints (all four, so none from an earlier choice is left over). Call before ActionExecutor.Begin.</summary>
        public void ApplyTo(ActionExecutor exec)
        {
            if (exec == null)
            {
                throw new ArgumentNullException(nameof(exec));
            }

            exec.SpotHint = Spot;
            exec.UpgradeChoice = Upgrade;
            exec.AbilityChoice = Ability;
            exec.RoomHint = Room;
        }
    }

    /// <summary>
    /// The v2 action list the bot networks choose from: 35 entries, each a BotAction plus a hint resolved at
    /// decision time (nearest spot, nearest zone, the offered upgrade, the busiest gas room). A mask entry is
    /// true only when the resolved action is valid in ActionCatalogue.FullMask, so the live re-check never drops
    /// a choice. Outside Playing only Stay is valid.
    /// </summary>
    public static class PolicyActionsV2
    {
        private const int ZoneActions = 3; // the executor has GoZone0..2 only: the zones it can walk to are the first three of the map

        private static readonly string[] NameList =
        {
            "Stay",
            "GoZoneNear0", "GoZoneNear1", "GoZoneNear2",
            "GoEmergency", "GoSmogPanel",
            "Follow0", "Follow1", "Follow2", "Follow3",
            "Avoid0", "Avoid1", "Avoid2", "Avoid3",
            "ReportNearest", "CallMeeting",
            "RepairSmog", "CleanNearest", "CleanSecond", "Deliver", "FixMachine",
            "PickBackpack", "PickTool", "PickHeal", "PickMask",
            "HealSelf",
            "FakeCleanNearest", "RepolluteNearest", "RepolluteSecond", "PickVirus", "PickGas",
            "PlantVirusNearest", "GasBusiestRoom", "SabotageSmog", "SabotageMachine",
        };

        private static readonly UpgradeKind[] UpgradeOrder = { UpgradeKind.Backpack, UpgradeKind.Tool, UpgradeKind.Heal, UpgradeKind.Mask };

        public static int Count => NameList.Length;

        public static IReadOnlyList<string> Names => NameList;

        /// <summary>Which policy indices the network may pick now (length Count).</summary>
        public static bool[] Mask(SeatView view, BotMemory mem, MapData map)
        {
            var mask = new bool[Count];
            mask[0] = true;
            if (!ActionQuery.Playing(view))
            {
                return mask;
            }

            bool[] full = ActionCatalogue.FullMask(view, mem, map);
            var world = new Decision(view, mem, map);
            for (int i = 1; i < mask.Length; i++)
            {
                PolicyChoice choice;
                mask[i] = TryResolve(i, world, out choice) && full[(int)choice.Action];
            }

            return mask;
        }

        /// <summary>
        /// The action and hints behind a policy index, for the current view. An index that does not resolve (wrong
        /// role, no such spot) gives Stay; the host only passes indices the mask allows.
        /// </summary>
        public static PolicyChoice Resolve(int index, SeatView view, BotMemory mem, MapData map)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            PolicyChoice choice;
            return TryResolve(index, new Decision(view, mem, map), out choice) ? choice : new PolicyChoice(BotAction.Stay);
        }

        /// <summary>The per-decision lists the resolver needs, each computed once and only when asked.</summary>
        private sealed class Decision
        {
            private List<ConsoleDef>? _dirty;
            private List<ConsoleDef>? _clean;
            private List<int>? _zones;

            public Decision(SeatView view, BotMemory mem, MapData map)
            {
                View = view;
                Mem = mem;
                Map = map;
            }

            public SeatView View { get; }

            public BotMemory Mem { get; }

            public MapData Map { get; }

            public bool Trasher => View.Me.Role == Role.Polluter;

            public List<ConsoleDef> Dirty => _dirty ??= ActionQuery.SpotsNearestFirst(View, Map, SpotState.Dirty);

            public List<ConsoleDef> Clean => _clean ??= ActionQuery.SpotsNearestFirst(View, Map, SpotState.Clean);

            /// <summary>Map indices of the reachable zones among the first three, nearest first by walking distance (ties by index).</summary>
            public List<int> NearZones
            {
                get
                {
                    if (_zones == null)
                    {
                        MapDistances dist = MapDistances.For(Map);
                        var ranked = new List<KeyValuePair<int, int>>();
                        for (int z = 0; z < ZoneActions && z < Map.Zones.Count; z++)
                        {
                            int d = dist.Path(View.Me.Position, ActionQuery.ZoneCentre(Map, z));
                            if (d != MapDistances.Unreachable)
                            {
                                ranked.Add(new KeyValuePair<int, int>(d, z));
                            }
                        }

                        ranked.Sort((a, b) =>
                        {
                            int c = a.Key.CompareTo(b.Key);
                            return c != 0 ? c : a.Value.CompareTo(b.Value);
                        });
                        _zones = new List<int>(ranked.Count);
                        foreach (KeyValuePair<int, int> kv in ranked)
                        {
                            _zones.Add(kv.Value);
                        }
                    }

                    return _zones;
                }
            }
        }

        private static bool Plain(BotAction action, out PolicyChoice choice)
        {
            choice = new PolicyChoice(action);
            return true;
        }

        private static bool OnSpot(BotAction action, List<ConsoleDef> spots, int nth, out PolicyChoice choice)
        {
            if (nth < spots.Count)
            {
                choice = new PolicyChoice(action, spot: spots[nth].Id);
                return true;
            }

            choice = default(PolicyChoice);
            return false;
        }

        /// <summary>True when the index means something for this seat right now (right role, target exists); the FullMask check is the caller's.</summary>
        private static bool TryResolve(int index, Decision w, out PolicyChoice choice)
        {
            choice = default(PolicyChoice);
            bool trasher = w.Trasher;
            switch (index)
            {
                case 0:
                    return Plain(BotAction.Stay, out choice);
                case 1:
                case 2:
                case 3:
                {
                    List<int> zones = w.NearZones;
                    int k = index - 1;
                    if (k >= zones.Count)
                    {
                        return false;
                    }

                    choice = new PolicyChoice(BotAction.GoZone0 + zones[k]);
                    return true;
                }

                case 4:
                    return Plain(BotAction.GoEmergency, out choice);
                case 5:
                    return Plain(BotAction.GoSmogPanel, out choice);
                case 14:
                    return Plain(BotAction.ReportNearest, out choice);
                case 15:
                    return Plain(BotAction.CallMeeting, out choice);
                case 16:
                    return !trasher && Plain(BotAction.RepairSmog, out choice);
                case 17:
                case 18:
                    return !trasher && OnSpot(BotAction.GoDirtySpot, w.Dirty, index - 17, out choice);
                case 19:
                    return !trasher && Plain(BotAction.GoMachine, out choice);
                case 20:
                    return !trasher && Plain(BotAction.FixMachine, out choice);
                case 21:
                case 22:
                case 23:
                case 24:
                {
                    UpgradeKind kind = UpgradeOrder[index - 21];
                    if (trasher || w.Mem.UpgradeOffer == null || !Offers(w.Mem.UpgradeOffer, kind))
                    {
                        return false;
                    }

                    choice = new PolicyChoice(BotAction.PickUpgrade, upgrade: kind);
                    return true;
                }

                case 25:
                    return !trasher && Plain(BotAction.HealSelf, out choice);
                case 26:
                    return trasher && OnSpot(BotAction.GoDirtySpot, w.Dirty, 0, out choice);
                case 27:
                case 28:
                    return trasher && OnSpot(BotAction.Repollute, w.Clean, index - 27, out choice);
                case 29:
                case 30:
                    if (!trasher)
                    {
                        return false;
                    }

                    choice = new PolicyChoice(BotAction.PickAbility, ability: index == 29 ? TrasherAbility.Virus : TrasherAbility.Gas);
                    return true;
                case 31:
                    return trasher && OnSpot(BotAction.PlantVirus, w.Dirty, 0, out choice);
                case 32:
                {
                    int count;
                    ZoneDef? room = trasher ? ActionQuery.GasRoomWithPlayers(w.View, w.Map, out count) : null;
                    if (room == null)
                    {
                        return false;
                    }

                    choice = new PolicyChoice(BotAction.GasRoom, room: room.Id);
                    return true;
                }

                case 33:
                    return trasher && Plain(BotAction.SabotageSmog, out choice);
                case 34:
                    return trasher && Plain(BotAction.SabotageMachine, out choice);
                default:
                    if (index >= 6 && index <= 9)
                    {
                        return Plain(BotAction.FollowSlot0 + (index - 6), out choice);
                    }

                    if (index >= 10 && index <= 13)
                    {
                        return Plain(BotAction.AvoidSlot0 + (index - 10), out choice);
                    }

                    return false;
            }
        }

        private static bool Offers(IReadOnlyList<UpgradeKind> offer, UpgradeKind kind)
        {
            for (int i = 0; i < offer.Count; i++)
            {
                if (offer[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
