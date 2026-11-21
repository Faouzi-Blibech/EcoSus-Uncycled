using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;

namespace Uncycled.Bots
{
    /// <summary>What a scripted brain wants next. Null Action = keep doing what it does.</summary>
    internal readonly struct Choice
    {
        public Choice(BotAction? action, bool urgent)
        {
            Action = action;
            Urgent = urgent;
        }

        public BotAction? Action { get; }

        /// <summary>An urgent choice may interrupt an action that is still running.</summary>
        public bool Urgent { get; }

        public static Choice Keep => new Choice(null, false);

        public static Choice Do(BotAction action) => new Choice(action, false);

        public static Choice Now(BotAction action) => new Choice(action, true);
    }

    /// <summary>
    /// Shared plumbing of the scripted brains: memory, navigation, the executor, decisions every
    /// DecisionIntervalS with a reaction delay, and the meeting vote. A brain made mid-match starts
    /// from the current view with empty memory and a move sequence above the engine's last one.
    /// Subclasses only choose actions.
    /// </summary>
    public abstract class ScriptedBrain : IBotBrain, IMeetingObserver, IAccuser
    {
        private const float SkillScaleBase = 1.4f;
        private const float MinReactionScale = 0.2f;
        private const float VoteDelayFactor = 2f;

        private readonly BotsSettings _settings;
        private readonly SuspicionModel _suspicion = new SuspicionModel();

        private readonly IReadOnlyList<TaskTypeDef>? _taskTypes;
        private MatchEngine? _engine;
        private BotMemory? _mem;
        private ActionExecutor? _exec;
        private float _decisionIn;
        private BotAction? _pending;
        private float _reactionLeft;
        private bool _voteArmed;
        private float _voteDelay;
        private int _voteAttempts;
        private byte? _lastVote;

        protected ScriptedBrain(BotsSettings settings, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _taskTypes = taskTypes;
        }

        protected IRandom Rng { get; }

        /// <summary>The seat this brain votes for in the current meeting (once it has voted); it is also its accusation.</summary>
        public byte? Accusation => _voteAttempts > 0 ? _lastVote : null;

        /// <summary>Heat from a finished meeting goes into this brain's own memory.</summary>
        public void OnMeetingEnded(MeetingOutcome outcome) => _mem?.RecordMeeting(outcome);

        protected BotMemory Mem => _mem!;

        protected ActionExecutor Exec => _exec!;

        public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
        {
            SeatView view = engine.ViewFor(seat);
            EnsureInit(engine, seat, view);
            float now = view.MatchTime;
            Mem.Observe(view, now, engine.Witnesses.SeenBy(seat));

            if (view.Me.Status != PlayerStatus.Active || view.Phase == MatchPhase.Ended)
            {
                Exec.Step(view, dt, now, send);
                return;
            }

            if (view.Phase == MatchPhase.Meeting)
            {
                Exec.Step(view, dt, now, send);
                _pending = null;
                ThinkMeeting(view, dt, send);
                return;
            }

            _voteArmed = false;
            _voteAttempts = 0;
            if (view.Phase == MatchPhase.Playing)
            {
                Decide(view, dt, now);
            }
            else
            {
                _pending = null;
            }

            Exec.Step(view, dt, now, send);
        }

        /// <summary>Pick the next action. The mask holds what is valid right now.</summary>
        internal abstract Choice Choose(SeatView view, bool[] mask);

        /// <summary>Called when a chosen action actually starts.</summary>
        internal virtual void Started(BotAction action, SeatView view)
        {
        }

        private void EnsureInit(MatchEngine engine, byte seat, SeatView view)
        {
            if (ReferenceEquals(_engine, engine) && _mem != null)
            {
                return;
            }

            _engine = engine;
            _mem = new BotMemory(engine.Map, engine.Config);
            var nav = new Navigator(engine.Map.Walk, engine.Config.SpeedTilesPerS);

            // The engine drops a move whose seq is not above the seat's last one, and the room does not
            // reset it when a bot takes over a seat: continue the seat's own counter.
            PlayerState? self = engine.Find(seat);
            long first = self != null && self.LastMoveSeq >= 0 ? self.LastMoveSeq + 1 : 1;
            _exec = new ActionExecutor(nav, new MoveSender(first))
            {
                Memory = _mem,
                TaskTypes = _taskTypes ?? TaskDefinitionsOf(engine),
                FollowCapS = _settings.FollowCapS,
                Pace = new PacePolicy(_settings.Pace, Rng),
            };
            // a random first offset spreads the seats' decisions over the interval instead of one tick
            _decisionIn = Rng.NextFloat() * _settings.DecisionIntervalS;
            _pending = null;
            _voteArmed = false;
            _voteAttempts = 0;
        }

        /// <summary>
        /// The task definitions (id, min_s) when none were passed in. Only the static definitions are
        /// collected, never who owns or has done which task; a host that has the content files passes
        /// them to the constructor instead.
        /// </summary>
        private static IReadOnlyList<TaskTypeDef> TaskDefinitionsOf(MatchEngine engine)
        {
            var types = new List<TaskTypeDef>();
            var seen = new HashSet<string>();
            foreach (TaskState t in engine.Tasks.All)
            {
                if (seen.Add(t.Type.Id))
                {
                    types.Add(t.Type);
                }
            }

            return types;
        }

        private void Decide(SeatView view, float dt, float now)
        {
            if (_pending == null)
            {
                _decisionIn -= dt;
                if (_decisionIn > 0f)
                {
                    return;
                }

                _decisionIn = _settings.DecisionIntervalS;
                Choice choice = Choose(view, ActionCatalogue.Mask(view, Mem, Mem.Map));
                if (choice.Action != null && (!Exec.Busy || choice.Urgent))
                {
                    _pending = choice.Action;
                    _reactionLeft = ReactionDelay();
                }

                return;
            }

            _reactionLeft -= dt;
            if (_reactionLeft > 0f)
            {
                return;
            }

            BotAction action = _pending.Value;
            _pending = null;
            if (ActionCatalogue.Mask(view, Mem, Mem.Map)[(int)action])
            {
                Exec.Begin(action, view, Mem.Map, now);
                Started(action, view);
            }
        }

        private float ReactionDelay()
        {
            float skill = Math.Min(1f, Math.Max(0f, _settings.Skill));
            float scale = Math.Max(MinReactionScale, SkillScaleBase - skill);
            float span = Math.Max(0f, _settings.ReactionMaxS - _settings.ReactionMinS);
            return (_settings.ReactionMinS + Rng.NextFloat() * span) * scale;
        }

        private void ThinkMeeting(SeatView view, float dt, Action<ICommand> send)
        {
            MeetingView? meeting = view.Meeting;
            if (meeting == null || meeting.Stage != MeetingStage.Voting || meeting.MyVoteCast)
            {
                return;
            }

            if (!_voteArmed)
            {
                _voteArmed = true;
                _voteAttempts = 0;
                _voteDelay = ReactionDelay() * VoteDelayFactor;
            }

            _voteDelay -= dt;
            if (_voteDelay > 0f)
            {
                return;
            }

            if (_voteAttempts == 0)
            {
                _suspicion.Update(Mem, view);
                _lastVote = _suspicion.Vote(view, view.Me.Role, view.Me.Partners);
                send(new VoteCommand(_lastVote));
                _voteAttempts = 1;
            }
            else if (_voteAttempts == 1)
            {
                // The vote did not register (for example the seat was already ejected): skip instead.
                if (_lastVote != null)
                {
                    Mem.MarkOut(_lastVote.Value);
                }

                send(new VoteCommand(null));
                _voteAttempts = 2;
            }
        }
    }
}
