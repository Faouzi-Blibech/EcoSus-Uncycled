using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Uncycled.Bots;
using Uncycled.Bots.Meeting;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>
    /// One match slot: runs a real MatchEngine, drives every seat of the learner role from the
    /// trainer's actions and every other seat with a scripted brain. When a match ends, the next one
    /// (next seed of this slot) starts at once, so the agent list never changes.
    ///
    /// Not thread-safe; use one runner per thread at a time. Randomness: one XorShiftRandom per match.
    /// </summary>
    public sealed class MatchRunner
    {
        public const float TickDt = 0.05f;

        private const int MaxMeetingTicks = 20000;

        /// <summary>Policy index 0 is Stay (PolicyActionsV2.Names[0]).</summary>
        private const int StayIndex = 0;

        private readonly HostOptions _options;
        private readonly ContentBundle _content;
        private readonly Func<Role, IRandom, IBotBrain> _brains;
        private readonly OpponentMix? _mix;
        private readonly int _slot;
        private readonly int _ticksPerDecision;
        private readonly BotsSettings _settings;
        private readonly float[] _obsBuf = new float[ObservationBuilderV2.Size];

        private MatchEngine _engine = null!;
        private IRandom _rng = null!;
        private Agent[] _agents = Array.Empty<Agent>();
        private IBotBrain?[] _brainBySeat = Array.Empty<IBotBrain?>();

        /// <summary>The scripted seats' brains with meeting talk added (the brain itself when it keeps no memory for a talker); what Think calls.</summary>
        private IBotBrain?[] _talkBySeat = Array.Empty<IBotBrain?>();
        private Action<ICommand>[] _brainSend = Array.Empty<Action<ICommand>>();
        private int[] _agentOfSeat = Array.Empty<int>();
        private float[][] _obs = Array.Empty<float[]>();
        private bool[][] _masks = Array.Empty<bool[]>();
        private float[] _reward = Array.Empty<float>();
        private float[] _owed = Array.Empty<float>();
        private bool[] _done = Array.Empty<bool>();
        private bool _prepared;
        private ulong _nextSeed;
        private byte? _reportBy;
        private bool _warnedSeedRange;
        private MeetingFeed _feed = new MeetingFeed();
        private readonly RewardsV2 _v2;

        public MatchRunner(HostOptions options, int slot, ContentBundle content, Func<Role, IRandom, IBotBrain> brains, OpponentMix? mix = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _brains = brains ?? throw new ArgumentNullException(nameof(brains));
            _mix = mix;
            _slot = slot;
            _v2 = new RewardsV2(options.Rewards, options.Learner);
            _v2.IsTrasher = seat => _engine.Find(seat)?.Role == Role.Polluter;
            _settings = options.Settings;
            BotsSettings settings = _settings;
            _ticksPerDecision = Math.Max(1, (int)Math.Round(settings.DecisionIntervalS / TickDt));
            _nextSeed = options.Seed + (ulong)slot;
            StartMatch();
            AgentCount = _agents.Length;
            FrameSize = PipeProtocol.FrameSize(ObservationBuilderV2.Size, PolicyActionsV2.Count);
            MaskOffset = ObservationBuilderV2.Size * 4;
            RewardOffset = MaskOffset + PolicyActionsV2.Count;
            DoneOffset = RewardOffset + 4;
        }

        /// <summary>The first bytes of the pipe: observation size, action count, agent count and schema hash the trainer checks.</summary>
        public static void WriteHandshake(System.IO.Stream stdout, int agents)
        {
            PipeProtocol.WriteHandshake(stdout, ObservationBuilderV2.Size, PolicyActionsV2.Count, agents, SchemaV2.Hash);
        }

        public int AgentCount { get; }

        public string MapId => _content.MapId;

        public int Seats => _content.Config.Seats;

        /// <summary>
        /// True while the agent works a task step (see ActionExecutor.Committed). Its frame is then a placeholder
        /// that is no decision: only Stay is valid, no reward, and the action the trainer sends for it is ignored.
        /// The trainer must not learn a policy sample from it.
        /// </summary>
        public bool AgentCommitted(int agent) => _agents[agent].Executor.Committed;

        public int FrameSize { get; }

        public int MaskOffset { get; }

        public int RewardOffset { get; }

        public int DoneOffset { get; }

        /// <summary>Seed of the match running now.</summary>
        public ulong Seed { get; private set; }

        /// <summary>Brain playing the seat (null for a learner seat); lets tests see which kind an opponent mix picked.</summary>
        public IBotBrain? BrainOfSeat(int seat) => _brainBySeat[seat];

        public int MatchesFinished { get; private set; }

        /// <summary>The game_over of the match that ended last, if any.</summary>
        public GameOverEvent? LastResult { get; private set; }

        /// <summary>Actions that were out of range or masked; each ran as Stay.</summary>
        public int InvalidActions { get; private set; }

        /// <summary>Decisions taken (agents times decision steps).</summary>
        public long Decisions { get; private set; }

        /// <summary>Decisions where Stay was the only valid action (out seats, mostly).</summary>
        public long ForcedDecisions { get; private set; }

        /// <summary>The engine of the match running now (it is replaced when a match ends): lets a test stage a situation.</summary>
        public MatchEngine Engine => _engine;

        public MatchPhase Phase => _engine.Phase;

        public float MatchTime => _engine.MatchTime;

        /// <summary>The chat of the meeting in progress or just over (the feed owns it; cleared when the next meeting starts).</summary>
        public MeetingLog MeetingChat => _feed.Log!;

        public int MeetingsHeld { get; private set; }

        /// <summary>Votes that ejected someone, and how many of them were polluters.</summary>
        public int Ejections { get; private set; }

        public int PollutersEjected { get; private set; }

        public byte? LastEjectedSeat { get; private set; }

        public byte AgentSeat(int agent) => _agents[agent].Seat;

        public BotMemory AgentMemory(int agent) => _agents[agent].Memory;

        public ActionExecutor AgentExecutor(int agent) => _agents[agent].Executor;

        /// <summary>Writes the current frame of every agent (AgentCount * FrameSize bytes).</summary>
        public void WriteFrames(Span<byte> dest)
        {
            if (dest.Length < AgentCount * FrameSize)
            {
                throw new ArgumentException("destination too small", nameof(dest));
            }

            Prepare();
            for (int a = 0; a < AgentCount; a++)
            {
                PipeProtocol.WriteFrame(dest.Slice(a * FrameSize, FrameSize), _obs[a], _masks[a], _reward[a], _done[a]);
            }
        }

        /// <summary>Applies one action per agent, then advances the match one decision interval.</summary>
        public void Advance(ReadOnlySpan<int> actions)
        {
            if (actions.Length != AgentCount)
            {
                throw new ArgumentException("expected " + AgentCount + " actions", nameof(actions));
            }

            Prepare();
            Array.Clear(_reward, 0, _reward.Length);
            Array.Clear(_done, 0, _done.Length);
            int asked = 0;
            for (int a = 0; a < AgentCount; a++)
            {
                if (_agents[a].Executor.Committed)
                {
                    // working a task step: not a decision, the action the trainer sent for the placeholder frame is ignored
                    continue;
                }

                asked++;
                int action = actions[a];
                if (OnlyStay(_masks[a]))
                {
                    ForcedDecisions++;
                }

                if (action < 0 || action >= PolicyActionsV2.Count || !_masks[a][action])
                {
                    InvalidActions++;
                    Console.Error.WriteLine("invalid action " + action + " for agent " + a + " (match slot " + _slot + ")");
                    action = StayIndex;
                }

                Agent agent = _agents[a];
                SeatView view = _engine.ViewFor(agent.Seat);
                // the policy index becomes a BotAction plus the executor hints (spot, upgrade, ability, room, zone) for this view;
                // the same index while its action still runs keeps its target (see PolicyDriver)
                agent.Policy.Begin(action, view, agent.Memory, _engine.Map, agent.Executor, _engine.MatchTime);
            }

            Decisions += asked;
            bool ended = false;
            for (int t = 0; t < _ticksPerDecision && !ended; t++)
            {
                ended = Tick();
            }

            // Meetings and ejections are not decisions for the learner (its only action there is Stay,
            // and the host votes for it): run them through, summing the rewards into this step.
            for (int guard = 0; !ended && (_engine.Phase == MatchPhase.Meeting || _engine.Phase == MatchPhase.Ejection) && guard < MaxMeetingTicks; guard++)
            {
                ended = Tick();
            }

            if (ended)
            {
                FinishMatch();
            }

            // Reward earned while an agent is committed waits and is paid with its next real frame
            // (a finished match pays at once; a new match has no committed agents).
            for (int a = 0; a < _agents.Length; a++)
            {
                _reward[a] += _owed[a];
                _owed[a] = 0f;
                if (!_done[a] && _agents[a].Executor.Committed)
                {
                    _owed[a] = _reward[a];
                    _reward[a] = 0f;
                }
            }

            _prepared = false;
        }

        private static bool OnlyStay(bool[] mask)
        {
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] && i != StayIndex)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when a dump cue means "someone not on the dumper's team saw it": a cue that only the
        /// partner polluter received does not count.
        /// </summary>
        public static bool CueCountsAsSeen(CueEvent cue, MatchEngine engine)
        {
            if (cue.Recipients == null)
            {
                return true;
            }

            foreach (byte seat in cue.Recipients)
            {
                if (engine.Find(seat)?.Role != Role.Polluter)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Bench helper: random valid actions from each agent's mask, then Advance.</summary>
        public void AdvanceRandom(IRandom rng)
        {
            Prepare();
            var actions = new int[AgentCount];
            var valid = new int[PolicyActionsV2.Count];
            for (int a = 0; a < AgentCount; a++)
            {
                int n = 0;
                for (int i = 0; i < PolicyActionsV2.Count; i++)
                {
                    if (_masks[a][i])
                    {
                        valid[n++] = i;
                    }
                }

                actions[a] = valid[rng.NextInt(n)];
            }

            Advance(actions);
        }

        // ---------- match lifecycle ----------

        private void StartMatch()
        {
            Seed = _nextSeed;
            if (Seed >= HostOptions.EvalSeedStart && !_warnedSeedRange)
            {
                _warnedSeedRange = true;
                _options.Warn("training seed " + Seed + " reached the evaluation seed range (" + HostOptions.EvalSeedStart + "+); use fewer matches or another --seed");
            }

            _nextSeed += (ulong)_options.Matches;
            int opponentChoice = -1;
            if (_mix != null)
            {
                opponentChoice = _mix.Pick(Seed);
                _mix.Record(opponentChoice);
            }

            _rng = new XorShiftRandom(Seed);
            MatchConfig cfg = _content.Config;
            var seats = new List<SeatSetup>();
            for (int i = 0; i < cfg.Seats; i++)
            {
                seats.Add(new SeatSetup((byte)i, "bot" + i, (byte)i));
            }

            _engine = new MatchEngine(cfg, _content.Map, seats, _content.Tasks, _rng, _content.Phrases);
            while (_engine.Phase == MatchPhase.Intro)
            {
                _engine.Tick(TickDt);
            }

            var agents = new List<Agent>();
            _brainBySeat = new IBotBrain?[cfg.Seats];
            _brainSend = new Action<ICommand>[cfg.Seats];
            _agentOfSeat = new int[cfg.Seats];
            for (int s = 0; s < cfg.Seats; s++)
            {
                _agentOfSeat[s] = -1;
                Role role = _engine.ViewFor((byte)s).Me.Role;
                if (role == _options.Learner)
                {
                    _agentOfSeat[s] = agents.Count;
                    agents.Add(new Agent((byte)s, role, _engine, _content, _settings, _rng, _v2.NoteAccepted));
                }
                else
                {
                    _brainBySeat[s] = _mix != null ? _mix.Create(opponentChoice, role, _rng) : _brains(role, _rng);
                    byte brainSeat = (byte)s;
                    _brainSend[s] = cmd => _engine.Apply(brainSeat, cmd);
                }
            }

            if (_agents.Length != 0 && agents.Count != _agents.Length)
            {
                throw new InvalidOperationException("learner seat count changed between matches");
            }

            _agents = agents.ToArray();
            _feed = new MeetingFeed(new MeetingLog(_content.Phrases ?? throw new InvalidOperationException("the content has no phrase catalogue")));
            foreach (Agent agent in _agents)
            {
                agent.Memory.Chat = _feed.Log;
                _feed.Add(agent.Seat, agent.Memory, agent.Voter);
            }

            var floor = new MeetingFloor();
            _talkBySeat = new IBotBrain?[_brainBySeat.Length];
            for (int s = 0; s < _brainBySeat.Length; s++)
            {
                IBotBrain? scripted = _brainBySeat[s];
                // the scripted seats talk in the meetings like in the live room (rule decider, no model call)
                IBotBrain? talking = scripted == null ? null : OfflineTalk.Wrap(scripted, _feed, floor, (byte)s, Seed);
                _talkBySeat[s] = talking;
                if (talking is IMeetingObserver observer)
                {
                    _feed.Add((byte)s, observer, talking as IAccuser);
                }
            }

            if (_obs.Length != _agents.Length)
            {
                _obs = new float[_agents.Length][];
                _masks = new bool[_agents.Length][];
                for (int a = 0; a < _agents.Length; a++)
                {
                    _obs[a] = new float[ObservationBuilderV2.Size];
                    _masks[a] = new bool[PolicyActionsV2.Count];
                }

                _reward = new float[_agents.Length];
                _owed = new float[_agents.Length];
                _done = new bool[_agents.Length];
            }

            _reportBy = null;
            _prepared = false;
        }

        private void FinishMatch()
        {
            GameOverEvent result = _engine.Result!;
            LastResult = result;
            bool learnerWon = result.Winner == (_options.Learner == Role.Citizen ? Winner.Citizens : Winner.Polluters);
            for (int a = 0; a < _agents.Length; a++)
            {
                _reward[a] += learnerWon ? _options.Rewards.Win : -_options.Rewards.Win;
                _done[a] = true;
            }

            MatchesFinished++;
            _v2.Reset();
            StartMatch();
        }

        // ---------- stepping ----------

        /// <summary>One engine tick; true when the match ended.</summary>
        private bool Tick()
        {
            float now = _engine.MatchTime;
            for (int s = 0; s < _brainBySeat.Length; s++)
            {
                IBotBrain? brain = _talkBySeat[s];
                if (brain != null)
                {
                    byte seat = (byte)s;
                    brain.Think(_engine, seat, TickDt, _brainSend[s]);
                }
            }

            foreach (Agent agent in _agents)
            {
                SeatView view = _engine.ViewFor(agent.Seat);
                agent.Memory.Observe(view, now, _engine.Witnesses.SeenBy(agent.Seat));
                agent.Voter.Step(_engine, agent.Seat, view, agent.Memory);
                agent.Executor.Step(_engine, agent.Seat, view, TickDt, now, agent.Send);
            }

            int fixHolders = SnapshotFixHolders();
            IReadOnlyList<GameEvent> events = _engine.Tick(TickDt);
            Reward(events, fixHolders);
            _feed.Handle(events, _engine.MatchTime);
            return _engine.Phase == MatchPhase.Ended && _engine.Result != null;
        }

        /// <summary>Bit mask of the seats repairing the jammed machine before this tick.</summary>
        private int SnapshotFixHolders()
        {
            byte? repairing = _engine.Machine.Jammed ? _engine.Machine.RepairSeat : null;
            return repairing == null ? 0 : 1 << repairing.Value;
        }

        private void Reward(IReadOnlyList<GameEvent> events, int fixHolders)
        {
            if (events.Count == 0)
            {
                return;
            }

            RewardWeights w = _options.Rewards;
            bool citizens = _options.Learner == Role.Citizen;
            _v2.Tick(events, s => s < _agentOfSeat.Length && _agentOfSeat[s] >= 0, cue => CueCountsAsSeen(cue, _engine), s => _engine.Find(s)?.Position ?? Vec2.Zero, (ushort)fixHolders, Give);
            foreach (GameEvent e in events)
            {
                switch (e)
                {
                    case MeetingStartedEvent m:
                        MeetingsHeld++;
                        _reportBy = m.Cause == MeetingCause.Report ? m.By : (byte?)null;
                        break;
                    case RoundResumedEvent:
                        _reportBy = null;
                        break;
                    case EjectionEvent ej:
                        PlayerState? ejected = _engine.Find(ej.Seat);
                        Ejections++;
                        LastEjectedSeat = ej.Seat;
                        foreach (Agent known in _agents)
                        {
                            if (known.Seat != ej.Seat)
                            {
                                known.Memory.MarkOut(ej.Seat);
                            }
                        }

                        if (ejected != null && ejected.Role == Role.Polluter)
                        {
                            PollutersEjected++;
                            if (citizens && _reportBy != null)
                            {
                                Give(_reportBy.Value, w.Report);
                            }
                        }

                        _reportBy = null;
                        break;
                }
            }
        }

        private void Give(byte seat, float amount)
        {
            if (seat < _agentOfSeat.Length && _agentOfSeat[seat] >= 0)
            {
                _reward[_agentOfSeat[seat]] += amount;
            }
        }

        // ---------- frames ----------

        private void Prepare()
        {
            if (_prepared)
            {
                return;
            }

            float now = _engine.MatchTime;
            for (int a = 0; a < _agents.Length; a++)
            {
                Agent agent = _agents[a];
                SeatView view = _engine.ViewFor(agent.Seat);
                ObservationBuilderV2.Build(view, agent.Memory, _engine.Map, _engine.Config, now, _obsBuf);
                Array.Copy(_obsBuf, _obs[a], _obsBuf.Length);
                bool[] mask = PolicyActionsV2.Mask(view, agent.Memory, _engine.Map);
                Array.Copy(mask, _masks[a], mask.Length);
                if (agent.Executor.Committed)
                {
                    Array.Clear(_masks[a], 0, _masks[a].Length);
                    _masks[a][StayIndex] = true;
                }
            }

            _prepared = true;
        }

        /// <summary>One learner seat: its own memory, navigator and executor.</summary>
        private sealed class Agent
        {
            public Agent(byte seat, Role role, MatchEngine engine, ContentBundle content, BotsSettings settings, IRandom rng, Action<byte, ICommand> accepted)
            {
                Seat = seat;
                Role = role;
                Memory = new BotMemory(content.Map, content.Config);
                Executor = new ActionExecutor(new Navigator(content.Map.Walk, content.Config.SpeedTilesPerS), new MoveSender())
                {
                    Memory = Memory,
                    TaskTypes = content.Tasks,
                    FollowCapS = settings.FollowCapS,
                    Pace = new PacePolicy(settings.Pace, rng),
                };
                Voter = new MeetingVoter();
                Send = cmd =>
                {
                    CommandResult result = engine.Apply(seat, cmd);
                    Executor.NotifyResult(cmd, result);
                    if (result.IsOk)
                    {
                        accepted(seat, cmd);
                    }
                };
            }

            public byte Seat { get; }

            public Role Role { get; }

            public BotMemory Memory { get; }

            public ActionExecutor Executor { get; }

            public PolicyDriver Policy { get; } = new PolicyDriver();

            public MeetingVoter Voter { get; }

            public Action<ICommand> Send { get; }
        }
    }
}
