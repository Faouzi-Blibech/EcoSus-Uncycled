using System;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>What the polluter wants next. Action null = keep doing what it does.</summary>
    public readonly struct PolluterDecision
    {
        public PolluterDecision(BotAction? action, bool urgent)
        {
            Action = action;
            Urgent = urgent;
        }

        public BotAction? Action { get; }

        /// <summary>An urgent decision may interrupt an action that is still running.</summary>
        public bool Urgent { get; }

        public static PolluterDecision Keep => new PolluterDecision(null, false);

        public static PolluterDecision Do(BotAction action) => new PolluterDecision(action, false);

        public static PolluterDecision Now(BotAction action) => new PolluterDecision(action, true);
    }

    /// <summary>
    /// The scripted Trasher's rules (v2). Reads only the SeatView, the action mask and the bot's own
    /// memory (own heat included): pick the ability when it unlocks, gas a room with a Cleaner in it,
    /// jam the machine or fog the room now and then, re-pollute a clean spot (unseen when it can,
    /// careful when the table is suspicious), plant a virus on a dirty spot, otherwise fake clean and
    /// wander near the Cleaners. It never hits, dumps or lights a fire (those rules are out of play).
    /// </summary>
    public sealed class PolluterTactics
    {
        /// <summary>Own heat from which the polluter plays careful.</summary>
        public const float CarefulHeat = 0.4f;

        /// <summary>Seconds a Trasher waits at a spot for the watchers to leave before it acts anyway (and is seen).</summary>
        public const float ImpatientActS = 3f;

        /// <summary>Seconds a careful Trasher waits before it gives the act up.</summary>
        public const float CarefulActS = 8f;

        private const float SabotageChance = 0.3f;
        private const float MachineJamShare = 0.6f;
        private const float RepolluteChance = 0.85f;
        private const float VirusChance = 0.6f;
        private const float GasOneVictimChance = 0.5f;
        private const float FakeNearChance = 0.7f;
        private const float FakeNearChanceCareful = 0.95f;
        private const float FakeWanderChance = 0.5f;
        private const float FakeWanderChanceCareful = 0.8f;
        private const float SpotWalkChance = 0.5f;
        private const float SecondBestChance = 0.25f;
        private const int SlotLimit = 4;

        private readonly IRandom _rng;
        private bool _leaveAfter;

        public PolluterTactics(IRandom rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public void Started(BotAction action, SeatView view)
        {
            _leaveAfter = action == BotAction.Repollute || action == BotAction.PlantVirus || action == BotAction.GasRoom;
        }

        /// <summary>The ability this Trasher picks at its unlock: a coin flip between the two.</summary>
        public TrasherAbility ChooseAbility() => _rng.NextFloat() < 0.5f ? TrasherAbility.Virus : TrasherAbility.Gas;

        /// <summary>True when the Trasher plays careful (the table is suspicious of it): it waits longer and gives an act up when watched.</summary>
        public static bool IsCareful(BotMemory mem, SeatView view) => mem.HeatOf(view.Me.Seat) >= CarefulHeat;

        /// <summary>The spot id for a re-pollute (clean spots) or a plant (dirty spots): the least watched, then the nearest; now and then the second best.</summary>
        public string? ChooseSpot(SeatView view, BotMemory mem, SpotState state)
        {
            float range = WitnessRange(view, mem.Config);
            var scored = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, ConsoleDef>>();
            int order = 0;
            foreach (ConsoleDef c in ActionQuery.SpotsNearestFirst(view, mem.Map, state))
            {
                int watchers = 0;
                foreach (PlayerView p in ActionQuery.Slots(view))
                {
                    if (!ActionQuery.ContainsSeat(view.Me.Partners, p.Seat) && Vec2.Distance(p.Position, c.Position) <= range)
                    {
                        watchers++;
                    }
                }

                scored.Add(new System.Collections.Generic.KeyValuePair<int, ConsoleDef>(watchers * 1000 + order++, c));
            }

            if (scored.Count == 0)
            {
                return null;
            }

            scored.Sort((a, b) => a.Key.CompareTo(b.Key));
            int pick = scored.Count > 1 && _rng.NextFloat() < SecondBestChance ? 1 : 0;
            return scored[pick].Value.Id;
        }

        /// <summary>The gas room with the most Cleaners in sight, or null.</summary>
        public static string? ChooseRoom(SeatView view, MapData map) => ActionQuery.GasRoomWithPlayers(view, map, out _)?.Id;

        public PolluterDecision Decide(SeatView view, bool[] mask, BotMemory mem, bool busy, BotAction current)
        {
            MapData map = mem.Map;
            Vec2 me = view.Me.Position;

            // leave the area after an act
            if (_leaveAfter && !(busy && (current == BotAction.Repollute || current == BotAction.PlantVirus)))
            {
                _leaveAfter = false;
                return PolluterDecision.Now(LeaveAction(me, map));
            }

            System.Collections.Generic.List<PlayerView> slots = ActionQuery.Slots(view);
            int others = 0;
            int nearestOtherSlot = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (ActionQuery.ContainsSeat(view.Me.Partners, slots[i].Seat))
                {
                    continue;
                }

                others++;
                if (nearestOtherSlot < 0)
                {
                    nearestOtherSlot = i;
                }
            }

            bool careful = IsCareful(mem, view);

            // the ability unlocks after the third re-pollute: pick one at once
            if (mask[(int)BotAction.PickAbility])
            {
                return PolluterDecision.Now(BotAction.PickAbility);
            }

            // gas a room with a Cleaner in it (two or more: always; one: now and then)
            if (mask[(int)BotAction.GasRoom])
            {
                ActionQuery.GasRoomWithPlayers(view, map, out int victims);
                if (victims >= 2 || (victims == 1 && _rng.NextFloat() < GasOneVictimChance))
                {
                    return PolluterDecision.Now(BotAction.GasRoom);
                }
            }

            // sabotage now and then: jam the machine (deliveries stop) or fog the room
            bool jam = mask[(int)BotAction.SabotageMachine];
            bool smog = mask[(int)BotAction.SabotageSmog];
            if ((jam || smog) && _rng.NextFloat() < SabotageChance)
            {
                bool useJam = jam && (!smog || _rng.NextFloat() < MachineJamShare);
                return PolluterDecision.Now(useJam ? BotAction.SabotageMachine : BotAction.SabotageSmog);
            }

            // re-pollute a clean spot
            bool repolluting = busy && current == BotAction.Repollute;
            if (mask[(int)BotAction.Repollute] && (repolluting || _rng.NextFloat() < RepolluteChance))
            {
                return repolluting ? PolluterDecision.Keep : PolluterDecision.Do(BotAction.Repollute);
            }

            // plant the virus on a dirty spot
            bool planting = busy && current == BotAction.PlantVirus;
            if (mask[(int)BotAction.PlantVirus] && (planting || _rng.NextFloat() < VirusChance))
            {
                return planting ? PolluterDecision.Keep : PolluterDecision.Do(BotAction.PlantVirus);
            }

            // careful: stay with a group instead of walking alone
            if (careful && others >= 2 && nearestOtherSlot >= 0 && nearestOtherSlot < SlotLimit)
            {
                BotAction follow = BotAction.FollowSlot0 + nearestOtherSlot;
                return busy && current == follow ? PolluterDecision.Keep : PolluterDecision.Do(follow);
            }

            // Cleaners are near: look busy at a spot
            if (others >= 1 && mask[(int)BotAction.FakeTaskHere] && _rng.NextFloat() < (careful ? FakeNearChanceCareful : FakeNearChance))
            {
                return busy && current == BotAction.FakeTaskHere ? PolluterDecision.Keep : PolluterDecision.Do(BotAction.FakeTaskHere);
            }

            if (busy)
            {
                return PolluterDecision.Keep;
            }

            // fake a clean at the spot in reach, else walk spot to spot, else wander
            if (mask[(int)BotAction.FakeTaskHere] && _rng.NextFloat() < (careful ? FakeWanderChanceCareful : FakeWanderChance))
            {
                return PolluterDecision.Do(BotAction.FakeTaskHere);
            }

            if (mask[(int)BotAction.GoDirtySpot] && _rng.NextFloat() < SpotWalkChance)
            {
                return PolluterDecision.Do(BotAction.GoDirtySpot);
            }

            int pick = _rng.NextInt(5);
            if (pick == 0 && mask[(int)BotAction.GoTask2])
            {
                return PolluterDecision.Do(BotAction.GoTask2);
            }

            if (pick <= 1 && mask[(int)BotAction.GoNextTask])
            {
                return PolluterDecision.Do(BotAction.GoNextTask);
            }

            int zones = Math.Min(3, map.Zones.Count);
            return zones == 0 ? PolluterDecision.Do(BotAction.Stay) : PolluterDecision.Do(BotAction.GoZone0 + _rng.NextInt(zones));
        }

        /// <summary>
        /// How far a citizen sees (its vision, shrunk by smog): the farthest a witness can stand. From the
        /// public rules (config) and the smog level in the own view; no hidden state.
        /// </summary>
        private static float WitnessRange(SeatView view, MatchConfig cfg)
        {
            float level = view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Smog ? view.Sabotage.SmogLevel : 0f;
            level = level < 0f ? 0f : (level > 1f ? 1f : level);
            return cfg.VisionCitizen - level * (cfg.VisionCitizen - cfg.SmogMin);
        }

        /// <summary>Walk to a zone other than the one we stand in.</summary>
        private BotAction LeaveAction(Vec2 me, MapData map)
        {
            int zones = Math.Min(3, map.Zones.Count);
            if (zones == 0)
            {
                return BotAction.Stay;
            }

            string here = map.ZoneAt(me);
            for (int tries = 0; tries < 6; tries++)
            {
                int z = _rng.NextInt(zones);
                if (map.Zones[z].Id != here)
                {
                    return BotAction.GoZone0 + z;
                }
            }

            return BotAction.GoZone0;
        }
    }
}
