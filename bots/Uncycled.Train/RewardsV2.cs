using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Train
{
    /// <summary>
    /// v2 rewards of the learner role, computed from the engine events of one tick. Pure: the host passes
    /// the lookups (who is a learner, was a cue seen, where is a seat) and receives the payments through a callback.
    /// Win/lose, the report bonus and the v1 weights are not handled here (MatchRunner pays them).
    /// One instance per match slot; Reset() when a match ends.
    /// </summary>
    public sealed class RewardsV2
    {
        /// <summary>Seats looked at when a reward goes to "every learner" (the largest v2 room has 10).</summary>
        private const int MaxSeats = 16;

        /// <summary>
        /// A victim is blamed on the planted virus whose planter stood within this distance when planting it.
        /// A virus is planted within one spot range of the spot and a Cleaner finishes it within one spot range
        /// (1.2 tiles in the maps), so two ranges plus a tick of walking.
        /// </summary>
        private const float AttributionRange = 3f;

        private readonly RewardWeights _w;
        private readonly bool _cleaner;
        private readonly Dictionary<byte, Vec2> _planted = new Dictionary<byte, Vec2>();
        private readonly Dictionary<byte, byte> _virusBy = new Dictionary<byte, byte>();
        private byte? _gasCaster;
        private byte? _jamBy;

        public RewardsV2(RewardWeights w, Role learner)
        {
            _w = w ?? throw new ArgumentNullException(nameof(w));
            _cleaner = learner == Role.Citizen;
        }

        /// <summary>
        /// Tells whether a seat is a Trasher when an EjectionEvent does not say (the confirm-eject setting hides it).
        /// </summary>
        public Func<byte, bool>? IsTrasher { get; set; }

        /// <summary>A command the engine accepted for a seat; gas and machine sabotage name no seat in their events.</summary>
        public void NoteAccepted(byte seat, ICommand cmd)
        {
            switch (cmd)
            {
                case GasCommand:
                    _gasCaster = seat;
                    break;
                case SabotageCommand s when s.Kind == SabotageKind.Machine:
                    _jamBy = seat;
                    break;
            }
        }

        public void Reset()
        {
            _planted.Clear();
            _virusBy.Clear();
            _gasCaster = null;
            _jamBy = null;
        }

        public void Tick(IReadOnlyList<GameEvent> events, Func<byte, bool> isLearner, Func<CueEvent, bool> seen, Func<byte, Vec2> position, ushort fixHolders, Action<byte, float> pay)
        {
            if (events.Count == 0)
            {
                return;
            }

            var cuedActors = new HashSet<byte>();
            foreach (GameEvent e in events)
            {
                if (e is CueEvent cue && cue.Kind == CueKind.Repollute && seen(cue))
                {
                    cuedActors.Add(cue.Actor);
                }
            }

            foreach (GameEvent e in events)
            {
                switch (e)
                {
                    case DeliveredEvent d when _cleaner && isLearner(d.Seat):
                        pay(d.Seat, _w.Deliver * d.Bags);
                        break;
                    case SpotCleanedEvent sc when _cleaner && isLearner(sc.Seat):
                        pay(sc.Seat, _w.Spot);
                        break;
                    case SabotageEndedEvent end when _cleaner && end.Kind == SabotageKind.Machine && end.Result == SabotageEndResult.Fixed:
                        for (int s = 0; s < 16; s++)
                        {
                            if ((fixHolders & (1 << s)) != 0 && isLearner((byte)s))
                            {
                                pay((byte)s, _w.Fix);
                            }
                        }

                        break;
                    case HealedEvent h when _cleaner && isLearner(h.By):
                        pay(h.By, _w.Heal);
                        break;
                    case YouAreOutEvent o:
                        Out(o, isLearner, pay);
                        break;
                    case RepolluteOkEvent r when !_cleaner && isLearner(r.Seat) && !cuedActors.Contains(r.Seat):
                        pay(r.Seat, _w.Repollute);
                        break;
                    case VirusPlantedEvent vp:
                        // one virus per Trasher at a time, so a new plant replaces a stale record
                        _planted[vp.Seat] = position(vp.Seat);
                        break;
                    case InfectedEvent inf:
                        Infected(inf.Seat, position(inf.Seat));
                        break;
                    case GasDoneEvent:
                        _gasCaster = null;
                        break;
                    case SabotageStartedEvent st when st.Kind == SabotageKind.Machine:
                        if (_jamBy != null && !_cleaner && isLearner(_jamBy.Value))
                        {
                            pay(_jamBy.Value, _w.Jam);
                        }

                        _jamBy = null;
                        break;
                    case EjectionEvent ej:
                        Ejection(ej, isLearner, pay);
                        break;
                }
            }
        }

        private void Out(YouAreOutEvent o, Func<byte, bool> isLearner, Action<byte, float> pay)
        {
            if (o.Cause != OutCause.Gas && o.Cause != OutCause.Virus)
            {
                return;
            }

            if (_cleaner)
            {
                if (isLearner(o.Seat))
                {
                    pay(o.Seat, _w.Out);
                }

                return;
            }

            byte? culprit = null;
            if (o.Cause == OutCause.Gas)
            {
                culprit = _gasCaster;
            }
            else if (_virusBy.TryGetValue(o.Seat, out byte planter))
            {
                culprit = planter;
                _virusBy.Remove(o.Seat);
            }

            if (culprit != null && isLearner(culprit.Value))
            {
                pay(culprit.Value, _w.Victim);
            }
        }

        private void Infected(byte victim, Vec2 at)
        {
            _virusBy.Remove(victim);
            byte? best = null;
            float bestDistance = AttributionRange;
            foreach (KeyValuePair<byte, Vec2> p in _planted)
            {
                float d = Vec2.Distance(p.Value, at);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = p.Key;
                }
            }

            if (best != null)
            {
                _virusBy[victim] = best.Value;
                _planted.Remove(best.Value);
            }
        }

        private void Ejection(EjectionEvent ej, Func<byte, bool> isLearner, Action<byte, float> pay)
        {
            bool? trasher = ej.WasPolluter ?? (IsTrasher == null ? (bool?)null : IsTrasher(ej.Seat));
            if (trasher == null)
            {
                return;
            }

            for (int s = 0; s < MaxSeats; s++)
            {
                byte seat = (byte)s;
                if (!isLearner(seat))
                {
                    continue;
                }

                if (_cleaner)
                {
                    pay(seat, trasher.Value ? _w.Ejectright : _w.Ejectwrong);
                }
                else if (trasher.Value)
                {
                    if (seat == ej.Seat)
                    {
                        pay(seat, _w.Ejected);
                    }
                }
                else
                {
                    pay(seat, _w.Cleanerout);
                }
            }
        }
    }
}
