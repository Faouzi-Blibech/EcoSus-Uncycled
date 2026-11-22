using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// A network-driven Cleaner that sees an unreported body always reports it, like a human (the scripted Cleaner's own
    /// step 5: the ReportNearest slot of the full mask, then the executor's walk-and-report). One instance per seat; the
    /// training host (Hold/Step) and the live NeuralBrain (Wants/NoteStart) share it. The rule overrides the network's
    /// choice only while it runs, reaction delay included; Trashers are not forced.
    /// </summary>
    public sealed class ReportRule
    {
        /// <summary>Starts of the report walk per pile before the rule gives it up (an unreachable body must not hold a seat for good).</summary>
        public const int MaxStartsPerPile = 3;

        private readonly BotsSettings _settings;
        private readonly IRandom _rng;
        private readonly Dictionary<int, int> _starts = new Dictionary<int, int>();
        private bool _armed;
        private float _delayLeft;
        private bool _running;

        public ReportRule(BotsSettings settings, IRandom rng)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _rng = rng ?? throw new System.ArgumentNullException(nameof(rng));
        }

        /// <summary>True when this Cleaner should report now: a fresh body in sight that the scripted Cleaner would report.</summary>
        public bool Wants(SeatView view, BotMemory mem, MapData map)
        {
            if (view.Me.Role != Role.Citizen || !ActionQuery.Playing(view))
            {
                return false;
            }

            // the cheap test first: most frames have no fresh pile in sight
            PileView? pile = ActionQuery.NearestPile(view, ActionQuery.IsFresh);
            if (pile == null || (_starts.TryGetValue(pile.Id, out int n) && n >= MaxStartsPerPile))
            {
                return false;
            }

            return ActionCatalogue.FullMask(view, mem, map)[(int)BotAction.ReportNearest];
        }

        /// <summary>Call when a report walk starts, so a body that cannot be reached is given up after MaxStartsPerPile tries.</summary>
        public void NoteStart(SeatView view)
        {
            PileView? pile = ActionQuery.NearestPile(view, ActionQuery.IsFresh);
            if (pile != null)
            {
                _starts[pile.Id] = (_starts.TryGetValue(pile.Id, out int n) ? n : 0) + 1;
            }
        }

        /// <summary>
        /// Training host: true while the rule has the seat (waiting out its reaction delay, walking to the body, or a body is
        /// in sight and the delay has yet to start). The frame of such a decision is a placeholder, like a committed task step.
        /// </summary>
        public bool Holds(SeatView view, BotMemory mem, MapData map, ActionExecutor exec)
        {
            if (!ActionQuery.Playing(view))
            {
                _armed = false;
                _running = false;
                return false;
            }

            if (_running && !(exec.Busy && exec.Current == BotAction.ReportNearest))
            {
                _running = false;
            }

            return _armed || _running || Wants(view, mem, map);
        }

        /// <summary>Training host: advance a held seat by dt seconds (call only while Holds is true): arm the reaction delay, then start the report.</summary>
        public void Step(float dt, SeatView view, BotMemory mem, MapData map, ActionExecutor exec, float now)
        {
            if (!_armed && !_running)
            {
                _armed = true;
                _delayLeft = ScriptedBrain.ReactionDelayOf(_settings, _rng);
            }

            if (!_armed)
            {
                return;
            }

            _delayLeft -= dt;
            if (_delayLeft > 0f)
            {
                return;
            }

            _armed = false;
            if (Wants(view, mem, map))
            {
                NoteStart(view);
                new PolicyChoice(BotAction.ReportNearest).ApplyTo(exec);
                exec.Begin(BotAction.ReportNearest, view, map, now);
                _running = exec.Busy && exec.Current == BotAction.ReportNearest;
            }
        }
    }
}
