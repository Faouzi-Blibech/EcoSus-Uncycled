using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// A bot seat driven by a trained network. It reuses the scripted brain's plumbing (memory,
    /// navigation, executor, decision interval, reaction delay, meeting vote) and only swaps the
    /// choice of the next action for the network's. Any exception in a decision hands this seat to the
    /// fallback brain for the rest of the match (logged once), so a bad model never crashes the room.
    /// </summary>
    public sealed class NeuralBrain : IBotBrain, IMeetingObserver, IAccuser, ITalkingSeat
    {
        private readonly NeuralCore _core;
        private readonly IBotBrain _fallback;
        private readonly Action<string> _log;

        public NeuralBrain(IPolicy policy, IBotBrain fallback, BotsSettings settings, IRandom rng,
            IReadOnlyList<TaskTypeDef>? taskTypes = null, Action<string>? log = null)
        {
            _core = new NeuralCore(policy ?? throw new ArgumentNullException(nameof(policy)), settings, rng, taskTypes);
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _log = log ?? (message => Console.Error.WriteLine(message));
        }

        /// <summary>True once a decision failed; the fallback plays from then on.</summary>
        public bool UsingFallback { get; private set; }

        /// <summary>The memory of whichever brain plays now (the network's, or the fallback's after a failure).</summary>
        public BotMemory? Memory => UsingFallback ? (_fallback as ITalkingSeat)?.Memory : _core.Memory;

        /// <summary>True when a MeetingTalker casts the vote: neither the network's brain nor the fallback votes in a meeting.</summary>
        public bool ExternalVote
        {
            get => _core.ExternalVote;
            set
            {
                _core.ExternalVote = value;
                if (_fallback is ITalkingSeat fallback)
                {
                    fallback.ExternalVote = value;
                }
            }
        }

        /// <summary>The action the executor last began (what the network's choice became), for tests.</summary>
        internal BotAction CurrentAction => _core.CurrentAction;

        public byte? Accusation =>UsingFallback ? (_fallback as IAccuser)?.Accusation : _core.Accusation;

        public void OnMeetingEnded(MeetingOutcome outcome)
        {
            _core.OnMeetingEnded(outcome);
            (_fallback as IMeetingObserver)?.OnMeetingEnded(outcome);
        }

        public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
        {
            if (!UsingFallback)
            {
                try
                {
                    _core.Engine = engine;
                    _core.Think(engine, seat, dt, send);
                    return;
                }
                catch (Exception e)
                {
                    UsingFallback = true;
                    _log("neural brain failed on seat " + seat + ", scripted fallback from now on: " + e.Message);
                }
            }

            _fallback.Think(engine, seat, dt, send);
        }

        private sealed class NeuralCore : ScriptedBrain
        {
            private readonly IPolicy _policy;
            private readonly float _temperature;
            private readonly float[] _obs = new float[ObservationBuilderV2.Size];
            private readonly PolicyDriver _driver = new PolicyDriver();
            private readonly ReportRule _report;
            private readonly EmergencyRule _emergency;
            private int _index;
            private bool _forced;

            public NeuralCore(IPolicy policy, BotsSettings settings, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes)
                : base(settings, rng, taskTypes)
            {
                _policy = policy;
                _report = new ReportRule(settings, rng);
                _emergency = new EmergencyRule(settings, rng);
                // skill 1 = always the network's best action, lower skill = more sampling
                _temperature = Math.Min(1f, Math.Max(0f, 1f - settings.Skill));
            }

            public MatchEngine? Engine { get; set; }

            public BotAction CurrentAction => Exec.Current;

            internal override Choice Choose(SeatView view, bool[] mask)
            {
                // a Cleaner that sees a fresh body reports it, whatever the network would pick: the rule runs from the
                // sighting to the report (the base class adds the reaction delay), then the network chooses again
                if (_report.Wants(view, Mem, Mem.Map))
                {
                    _forced = true;
                    if (Exec.Busy && Exec.Current == BotAction.ReportNearest)
                    {
                        return Choice.Keep;
                    }

                    _report.NoteStart(view);
                    return Choice.Now(BotAction.ReportNearest);
                }

                // a Cleaner that saw a Trasher act and passed its roll walks to the emergency table and calls the meeting
                if (_emergency.Active(view, Mem, Mem.Map, Exec))
                {
                    _forced = true;
                    return Exec.Busy && Exec.Current == BotAction.CallMeeting ? Choice.Keep : Choice.Now(BotAction.CallMeeting);
                }

                _forced = false;
                // the scripted FullMask is not the network's: it picks among the v2 policy list, valid ones only
                ObservationBuilderV2.Build(view, Mem, Mem.Map, Engine!.Config, view.MatchTime, _obs);
                bool[] policyMask = PolicyActionsV2.Mask(view, Mem, Mem.Map);
                int index = _policy.Act(_obs, policyMask, _temperature, Rng);
                if (index < 0 || index >= policyMask.Length || !policyMask[index])
                {
                    throw new InvalidOperationException("the policy chose invalid action " + index);
                }

                // the action to start; the hints (spot, upgrade, ability, room, zone) are resolved by the driver in Prepare,
                // when the action begins. Urgent: the network may change its mind while an action runs.
                _index = index;
                return Choice.Now(PolicyActionsV2.Resolve(index, view, Mem, Mem.Map).Action);
            }

            internal override void Watch(SeatView view) => _emergency.Watch(view, Mem, Mem.Map);

            internal override bool StillValid(BotAction action, SeatView view, bool[] fullMask) =>
                fullMask[(int)action] || (action == BotAction.CallMeeting && _emergency.Wanted && _emergency.Allowed(view, Mem, Mem.Map));

            internal override void Started(BotAction action, SeatView view)
            {
                if (action == BotAction.CallMeeting && _emergency.Wanted)
                {
                    _emergency.NoteStart(Exec);
                }
            }

            internal override void Prepare(BotAction action, SeatView view)
            {
                // the shared driver (as the training host's MatchRunner uses): the same index while its action still runs
                // keeps the running target; any other index resolves afresh. Prepare only follows a Choose.
                if (_forced)
                {
                    new PolicyChoice(action).ApplyTo(Exec);
                    return;
                }

                _driver.Prepare(_index, view, Mem, Mem.Map, Exec);
            }
        }
    }
}
