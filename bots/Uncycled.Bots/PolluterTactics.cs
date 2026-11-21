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
    /// The scripted polluter's rules. Reads only the SeatView, the action mask and the bot's own
    /// memory (own heat included). Careful play when the table is suspicious of this seat.
    /// </summary>
    public sealed class PolluterTactics
    {
        /// <summary>Own heat from which the polluter plays careful.</summary>
        public const float CarefulHeat = 0.4f;

        /// <summary>Careful: seconds nobody (but the target) must have been in sight before a hit or dump.</summary>
        public const float CarefulClearS = 4f;

        private const float SabotageChance = 0.5f;
        private const float FireWhenAloneChance = 0.5f;
        private const float SmogPrepChance = 0.9f;
        private const float SmogPrepExtraRange = 3.5f;
        private const float FakeNearChance = 0.7f;
        private const float FakeNearChanceCareful = 0.95f;
        private const float FakeWanderChance = 0.5f;
        private const float FakeWanderChanceCareful = 0.8f;
        private const float DumpPileClearance = 5f;
        private const float DumpSpread = 6f;
        private const float ImpatientDumpS = 12f;
        private const float StalkMaxS = 10f;
        private const float StalkPauseS = 5f;
        private const int SlotLimit = 4;

        private readonly IRandom _rng;
        private float _stalkSince = -1f;
        private float _stalkBlockedUntil;
        private bool _justActed;
        private float _lastOthersAt = float.NegativeInfinity;
        private float _soloSince = -1f;
        private Vec2? _lastDumpAt;
        private float _dumpReadySince = -1f;

        public PolluterTactics(IRandom rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public void Started(BotAction action, SeatView view)
        {
            _justActed = action == BotAction.HitNearest || action == BotAction.DumpHere;
            if (action == BotAction.DumpHere)
            {
                _lastDumpAt = view.Me.Position;
            }
        }

        public PolluterDecision Decide(SeatView view, bool[] mask, BotMemory mem, bool busy, BotAction current)
        {
            MapData map = mem.Map;
            MatchConfig cfg = mem.Config;
            Vec2 me = view.Me.Position;
            float now = view.MatchTime;

            // leave the area after an act (the act itself is instant, so it is already over)
            if (_justActed && !(busy && current == BotAction.HitNearest))
            {
                _justActed = false;
                return PolluterDecision.Now(LeaveAction(me, map));
            }

            System.Collections.Generic.List<PlayerView> slots = ActionQuery.Slots(view);
            float witnessRange = WitnessRange(view, cfg);
            int others = 0;
            int watchers = 0;
            int nearestOtherSlot = -1;
            int stalkSlot = -1;
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

                if (Vec2.Distance(slots[i].Position, me) <= witnessRange)
                {
                    watchers++;
                }

                // the nearest target nobody else could see from where it stands
                if (stalkSlot < 0 && IsolatedFrom(slots, i, view.Me.Partners, witnessRange))
                {
                    stalkSlot = i;
                }
            }

            if (others > 0)
            {
                _lastOthersAt = now;
            }

            if (watchers <= 1)
            {
                if (_soloSince < 0f)
                {
                    _soloSince = now;
                }
            }
            else
            {
                _soloSince = -1f;
            }
            bool careful = mem.HeatOf(view.Me.Seat) >= CarefulHeat;
            bool aloneWithTarget = !careful || (_soloSince >= 0f && now - _soloSince >= CarefulClearS);
            bool quiet = !careful || now - _lastOthersAt >= CarefulClearS;

            // hit: the target in range and nobody else watching
            if (mask[(int)BotAction.HitNearest] && watchers <= 1 && aloneWithTarget)
            {
                return PolluterDecision.Now(BotAction.HitNearest);
            }

            // dump out of sight of everyone, away from piles in view and from the last dump
            if (mask[(int)BotAction.DumpHere])
            {
                if (_dumpReadySince < 0f)
                {
                    _dumpReadySince = now;
                }
            }
            else
            {
                _dumpReadySince = -1f;
            }

            bool impatient = !careful && _dumpReadySince >= 0f && now - _dumpReadySince >= ImpatientDumpS;
            if (mask[(int)BotAction.DumpHere] && ((watchers == 0 && quiet && NoPileNear(view, me) && FarFromLastDump(me)) || impatient))
            {
                return PolluterDecision.Now(BotAction.DumpHere);
            }

            // smog before a planned hit: the target is close, the hit is ready, citizens will see less
            CooldownView? cd = view.Me.Cooldowns;
            bool hitReady = cd != null && cd.Hit <= 1e-3f;
            if (hitReady && stalkSlot >= 0 && aloneWithTarget && mask[(int)BotAction.SabotageSmog]
                && Vec2.Distance(slots[stalkSlot].Position, me) <= cfg.HitRange + SmogPrepExtraRange
                && _rng.NextFloat() < SmogPrepChance)
            {
                return PolluterDecision.Now(BotAction.SabotageSmog);
            }

            // stalk a lone citizen while the hit is ready
            if (hitReady && stalkSlot >= 0 && stalkSlot < SlotLimit && now >= _stalkBlockedUntil && aloneWithTarget)
            {
                if (_stalkSince < 0f)
                {
                    _stalkSince = now;
                }

                if (now - _stalkSince <= StalkMaxS)
                {
                    BotAction follow = BotAction.FollowSlot0 + stalkSlot;
                    return busy && current == follow ? PolluterDecision.Keep : PolluterDecision.Now(follow);
                }

                _stalkBlockedUntil = now + StalkPauseS;
                _stalkSince = -1f;
            }
            else
            {
                _stalkSince = -1f;
            }

            // sabotage: a fire when the citizens are spread out (it pulls them away), else now and then
            bool fire = mask[(int)BotAction.SabotageFire];
            bool smog = mask[(int)BotAction.SabotageSmog];
            if (fire && others == 0 && _rng.NextFloat() < FireWhenAloneChance)
            {
                return PolluterDecision.Now(BotAction.SabotageFire);
            }

            if ((fire || smog) && others <= 1 && _rng.NextFloat() < SabotageChance)
            {
                bool useFire = fire && (!smog || _rng.NextFloat() < 0.5f);
                return PolluterDecision.Now(useFire ? BotAction.SabotageFire : BotAction.SabotageSmog);
            }

            // careful: stay with a group instead of walking alone
            if (careful && others >= 2 && nearestOtherSlot >= 0 && nearestOtherSlot < SlotLimit)
            {
                BotAction follow = BotAction.FollowSlot0 + nearestOtherSlot;
                return busy && current == follow ? PolluterDecision.Keep : PolluterDecision.Do(follow);
            }

            // citizens are near: look busy at a task console
            if (others >= 1 && mask[(int)BotAction.FakeTaskHere] && _rng.NextFloat() < (careful ? FakeNearChanceCareful : FakeNearChance))
            {
                return busy && current == BotAction.FakeTaskHere ? PolluterDecision.Keep : PolluterDecision.Do(BotAction.FakeTaskHere);
            }

            if (busy)
            {
                return PolluterDecision.Keep;
            }

            // fake a task at a console, else wander
            if (mask[(int)BotAction.FakeTaskHere] && _rng.NextFloat() < (careful ? FakeWanderChanceCareful : FakeWanderChance))
            {
                return PolluterDecision.Do(BotAction.FakeTaskHere);
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

        /// <summary>True when no other non-partner stands within witness range of the target slot.</summary>
        private static bool IsolatedFrom(System.Collections.Generic.List<PlayerView> slots, int target, System.Collections.Generic.IReadOnlyList<byte> partners, float range)
        {
            for (int j = 0; j < slots.Count; j++)
            {
                if (j == target || ActionQuery.ContainsSeat(partners, slots[j].Seat))
                {
                    continue;
                }

                if (Vec2.Distance(slots[j].Position, slots[target].Position) <= range)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NoPileNear(SeatView view, Vec2 me)
        {
            foreach (PileView p in view.Piles)
            {
                if (Vec2.Distance(p.Position, me) < DumpPileClearance)
                {
                    return false;
                }
            }

            return true;
        }

        private bool FarFromLastDump(Vec2 me) => _lastDumpAt == null || Vec2.Distance(_lastDumpAt.Value, me) >= DumpSpread;

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
