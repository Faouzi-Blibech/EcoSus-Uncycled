using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;
using Sim.World;

namespace Sim.Match
{
    /// <summary>
    /// The whole match, engine-free. The lobby lives outside: the engine starts in Intro with
    /// every seat filled. Drive it with Apply (commands) and Tick (time); read ViewFor per seat.
    /// Not thread-safe: one room loop owns it.
    /// </summary>
    public sealed partial class MatchEngine
    {
        /// <summary>Tolerance for float time and distance comparisons.</summary>
        internal const float Epsilon = 1e-4f;

        private readonly List<PlayerState> _players;
        private readonly IRandom _rng;
        private readonly PhraseCatalogue? _phrases;
        private readonly ChatFilter _filter;
        private readonly List<ChatEvent> _ghostChat = new List<ChatEvent>();
        private readonly Dictionary<byte, float> _lastChatAt = new Dictionary<byte, float>();
        private readonly List<GameEvent> _pending = new List<GameEvent>();
        private readonly List<TimelineEntry> _timeline = new List<TimelineEntry>();
        private readonly Dictionary<byte, TaskSession> _sessions = new Dictionary<byte, TaskSession>();
        private readonly List<CleanJob> _cleanJobs = new List<CleanJob>();
        private readonly List<ConsoleDef> _fireConsoles;
        private readonly List<ConsoleDef> _emergencyConsoles;
        private readonly float[] _lastSeenAt;
        private readonly Vec2[] _lastSeenPos;
        private ConsoleDef? _smogRepairConsole;
        private float _phaseTimer;

        /// <param name="phrases">Chat phrase catalogue; null accepts any non-empty id.</param>
        /// <param name="filter">Word filter for typed chat; null or ChatFilter.Empty changes nothing.</param>
        public MatchEngine(
            MatchConfig config,
            MapData map,
            IReadOnlyList<SeatSetup> seats,
            IReadOnlyList<TaskTypeDef> taskTypes,
            IRandom rng,
            PhraseCatalogue? phrases = null,
            ChatFilter? filter = null,
            byte? forcedPolluter = null,
            byte? forcedCitizen = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            if (seats == null)
            {
                throw new ArgumentNullException(nameof(seats));
            }

            if (taskTypes == null)
            {
                throw new ArgumentNullException(nameof(taskTypes));
            }

            if (seats.Count != config.Seats)
            {
                throw new ArgumentException("Seat count must equal MatchConfig.Seats.", nameof(seats));
            }

            foreach (TaskTypeDef t in taskTypes)
            {
                foreach (string consoleId in t.ConsoleIds)
                {
                    if (map.FindConsole(consoleId) == null)
                    {
                        throw new ArgumentException("Task type " + t.Id + " uses unknown console " + consoleId, nameof(taskTypes));
                    }
                }
            }

            var sorted = new List<SeatSetup>(seats);
            sorted.Sort((a, b) => a.Seat.CompareTo(b.Seat));
            _players = new List<PlayerState>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && sorted[i].Seat == sorted[i - 1].Seat)
                {
                    throw new ArgumentException("Duplicate seat id " + sorted[i].Seat, nameof(seats));
                }

                _players.Add(new PlayerState(sorted[i].Seat, sorted[i].Name, sorted[i].Color));
            }

            RoleAssigner.Assign(_players, config.Polluters, _rng, forcedPolluter, forcedCitizen);
            Tasks = TaskLedger.Assign(ActiveCitizenSeats(), taskTypes, config.TasksCommon, config.TasksShort, config.TasksLong, _rng);
            Spots = SpotBoard.FromMap(map, taskTypes);

            _fireConsoles = map.ConsolesOfKind(ConsoleKind.FireFix);
            _emergencyConsoles = map.ConsolesOfKind(ConsoleKind.Emergency);
            var fireIds = new List<string>();
            foreach (ConsoleDef c in _fireConsoles)
            {
                fireIds.Add(c.Id);
            }

            Fire = new FireSystem(fireIds);
            List<ConsoleDef> smogPanels = map.ConsolesOfKind(ConsoleKind.SmogFix);
            Smog = new SmogSystem(smogPanels.Count > 0 ? smogPanels[0].Id : null);
            List<ConsoleDef> machines = map.ConsolesOfKind(ConsoleKind.Machine);
            Machine = new MachineSystem(machines.Count > 0 ? machines[0].Id : null);
            SabotageTimer = new SabotageCooldown(config.SabotageSharedCooldownS, config.SabotageStartCooldownS);
            Meter = new PollutionMeter();
            Piles = new PileRegistry();
            Witnesses = new WitnessLog();
            EmergenciesLeft = config.EmergenciesPerMatch;
            _phrases = phrases;
            _filter = filter ?? ChatFilter.Empty;

            int n = _players.Count;
            _lastSeenAt = new float[n * n];
            _lastSeenPos = new Vec2[n * n];
            ClearSightMemory();

            for (int i = 0; i < n; i++)
            {
                PlayerState p = _players[i];
                p.Status = PlayerStatus.Active;
                p.Position = map.SpawnFor(i);
                p.HitReadyAt = config.StartCooldownS;
                p.DumpReadyAt = config.StartCooldownS;
                p.RepolluteReadyAt = config.TrasherStartCooldownS;
                p.MoveBudget = config.MoveBudgetCap;
                p.BagCap = p.IsCitizen ? config.BagCap : 0;
            }

            Phase = MatchPhase.Intro;
            _phaseTimer = config.IntroS;

            foreach (PlayerState p in _players)
            {
                var tasks = new List<TaskInfo>();
                foreach (TaskState t in Tasks.OwnedBy(p.Seat))
                {
                    tasks.Add(TaskInfo.From(t));
                }

                Emit(new MatchStartedEvent(
                    p.Seat,
                    p.Role,
                    PartnersOf(p),
                    p.IsPolluter ? p.Signature : null,
                    tasks,
                    p.Position,
                    config.IntroS));
            }
        }

        public MatchConfig Config { get; }

        public MapData Map { get; }

        public MatchPhase Phase { get; private set; }

        /// <summary>Current meeting (Meeting phase only).</summary>
        public MeetingSession? Meeting { get; private set; }

        /// <summary>Runs in every phase after Intro, never resets.</summary>
        public float MatchTime { get; private set; }

        /// <summary>Runs only in Playing.</summary>
        public float ActiveTime { get; private set; }

        /// <summary>Runs only in Playing; resets when a meeting ends.</summary>
        public float SinceMeeting { get; private set; }

        public PollutionMeter Meter { get; }

        public PileRegistry Piles { get; }

        public TaskLedger Tasks { get; }

        /// <summary>The cleaning spots (v2): one per task console, all dirty at the start.</summary>
        public SpotBoard Spots { get; }

        /// <summary>Server-side witness records (for bots). Never sent to clients.</summary>
        public WitnessLog Witnesses { get; }

        public FireSystem Fire { get; }

        public SmogSystem Smog { get; }

        /// <summary>The Recycling Machine jam (v2). A delivery must be refused while Machine.Jammed.</summary>
        public MachineSystem Machine { get; }

        public SabotageCooldown SabotageTimer { get; }

        /// <summary>Match-wide emergency meetings left.</summary>
        public int EmergenciesLeft { get; private set; }

        /// <summary>
        /// All seats with their roles. Server and bot code only: never serialize this to a client
        /// (use ViewFor).
        /// </summary>
        public IReadOnlyList<PlayerState> Players => _players;

        public IReadOnlyList<TimelineEntry> Timeline => _timeline;

        /// <summary>Set once the match has ended.</summary>
        public GameOverEvent? Result { get; private set; }

        public PlayerState? Find(byte seat)
        {
            foreach (PlayerState p in _players)
            {
                if (p.Seat == seat)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>
        /// Lets a reconnected client restart its move sequence numbers. Server calls this when a
        /// human reclaims a seat.
        /// </summary>
        public void ResetMoveSeq(byte seat)
        {
            PlayerState? p = Find(seat);
            if (p != null)
            {
                p.LastMoveSeq = -1;
            }
        }

        /// <summary>Test hook only (InternalsVisibleTo Sim.Tests): places a seat without a move.</summary>
        internal void TestTeleport(byte seat, Vec2 position)
        {
            PlayerState? p = Find(seat);
            if (p != null)
            {
                p.Position = position;
            }
        }

        /// <summary>
        /// Current vision radius of a seat. Smog and dark rooms apply to citizens only; a citizen in
        /// a dark room under smog gets the lower of the two values.
        /// </summary>
        public float VisionOf(PlayerState p)
        {
            if (p.IsPolluter)
            {
                return Config.VisionPolluter;
            }

            float vision = Smog.CitizenVision(Config.VisionCitizen, Config.SmogMin);
            if (Map.IsDark(p.Position))
            {
                vision = Math.Min(vision, Config.VisionCitizen * Config.DarkMult);
            }

            return vision;
        }

        /// <summary>True when the seat is active and the point is within its vision and line of sight.</summary>
        public bool CanSee(byte viewer, Vec2 point)
        {
            PlayerState? p = Find(viewer);
            return p != null && CanSee(p, point);
        }

        /// <summary>Validates and applies one command from a seat.</summary>
        public CommandResult Apply(byte seat, ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            PlayerState? p = Find(seat);
            if (p == null)
            {
                return CommandResult.Rejected(RejectReason.NotAllowed);
            }

            if (Phase == MatchPhase.Ended)
            {
                return CommandResult.Rejected(RejectReason.WrongPhase);
            }

            switch (command)
            {
                case MoveCommand c: return ApplyMove(p, c);
                case UseCommand c: return ApplyUse(p, c);
                case SpotDoneCommand c: return ApplySpotDone(p, c);
                case PickUpgradeCommand c: return ApplyPickUpgrade(p, c);
                // v1 actions, out of play in v2 (their handlers stay in MatchEngine.Actions.cs)
                case TaskStepCommand _: return Reject(RejectReason.NotAllowed);
                case HitCommand _: return Reject(RejectReason.NotAllowed);
                case DumpCommand _: return Reject(RejectReason.NotAllowed);
                case ReportCommand c: return ApplyReport(p, c);
                case CallMeetingCommand _: return ApplyCallMeeting(p);
                case VoteCommand c: return ApplyVote(p, c);
                case RepolluteCommand c: return ApplyRepollute(p, c);
                case PickAbilityCommand c: return ApplyPickAbility(p, c);
                case GasCommand c: return ApplyGas(p, c);
                case SabotageCommand c: return ApplySabotage(p, c);
                case FixCommand c: return ApplyFix(p, c);
                case CleanStartCommand _: return Reject(RejectReason.NotAllowed);
                case CleanCancelCommand c: return ApplyCleanCancel(p, c);
                case ChatPhraseCommand c: return ApplyChat(p, c);
                case ChatTextCommand c: return ApplyChatText(p, c);
                default: return CommandResult.Rejected(RejectReason.NotAllowed);
            }
        }

        /// <summary>
        /// Advances clocks and timers by dt seconds and returns (then clears) every event produced
        /// since the previous Tick, including events caused by Apply.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f)
            {
                dt = 0f;
            }

            switch (Phase)
            {
                case MatchPhase.Intro:
                    TickIntro(dt);
                    break;
                case MatchPhase.Playing:
                    TickPlaying(dt);
                    break;
                case MatchPhase.Meeting:
                    MatchTime += dt;
                    TickMeeting(dt);
                    break;
                case MatchPhase.Ejection:
                    MatchTime += dt;
                    TickEjection(dt);
                    break;
                case MatchPhase.Ended:
                    break;
            }

            GameEvent[] events = _pending.ToArray();
            _pending.Clear();
            return events;
        }

        private void TickIntro(float dt)
        {
            _phaseTimer -= dt;
            if (_phaseTimer > 0f)
            {
                return;
            }

            _phaseTimer = 0f;
            Phase = MatchPhase.Playing;
            Emit(new PhaseChangedEvent(MatchPhase.Playing, null));
            UpdateSightMemory();
        }

        private void TickPlaying(float dt)
        {
            MatchTime += dt;
            ActiveTime += dt;
            SinceMeeting += dt;

            float refill = Config.SpeedTilesPerS * Config.MoveBudgetFactor * dt;
            foreach (PlayerState p in _players)
            {
                if (p.IsActive)
                {
                    p.MoveBudget = Math.Min(Config.MoveBudgetCap, p.MoveBudget + refill);
                }
            }

            Meter.Add(Config.MeterPassivePerS * dt);
            Smog.Ease(dt, Config.SmogEaseS);
            TickSmogRepair(dt);
            TickMachineRepair(dt);
            TickGas(dt);
            TickFire(dt);
            TickCleaning(dt);
            UpdateSightMemory();
            TryEndMatch();
        }

        private void TickEjection(float dt)
        {
            _phaseTimer -= dt;
            if (_phaseTimer <= 0f)
            {
                _phaseTimer = 0f;
                AfterMeeting();
            }
        }

        // ---------- win and end ----------

        /// <summary>Evaluates the win order (04 section 9). Ends the match and returns true on a win.</summary>
        private bool TryEndMatch()
        {
            if (Phase == MatchPhase.Ended)
            {
                return true;
            }

            int polluters = 0;
            int citizens = 0;
            foreach (PlayerState p in _players)
            {
                if (!p.IsActive)
                {
                    continue;
                }

                if (p.IsPolluter)
                {
                    polluters++;
                }
                else
                {
                    citizens++;
                }
            }

            float clock = Config.DeadlineClock == DeadlineClock.MatchTime ? MatchTime : ActiveTime;
            bool deadline = clock + Epsilon >= Config.DeadlineS;
            WinResult? result = WinEvaluator.Evaluate(polluters, citizens, Meter.Value, deadline);
            if (result == null)
            {
                return false;
            }

            EndMatch(result.Value);
            return true;
        }

        private void EndMatch(WinResult result)
        {
            _cleanJobs.Clear();
            _sessions.Clear();
            Smog.CancelRepair();
            _smogRepairConsole = null;
            Machine.CancelRepair();
            _gasRoom = null;
            if (Fire.Active)
            {
                Fire.Stop();
            }

            Meeting = null;
            Phase = MatchPhase.Ended;
            _phaseTimer = 0f;

            var roles = new List<RoleEntry>(_players.Count);
            foreach (PlayerState p in _players)
            {
                roles.Add(new RoleEntry(p.Seat, p.Role));
            }

            var gameOver = new GameOverEvent(result.Winner, result.Reason, roles, _timeline.ToArray(), MatchTime);
            Result = gameOver;
            Emit(new PhaseChangedEvent(MatchPhase.Ended, null));
            Emit(gameOver);
        }

        // ---------- shared helpers ----------

        private void Emit(GameEvent e)
        {
            _pending.Add(e);
        }

        private void AddTimeline(TimelineKind kind, byte? seat, byte? target, string? zone)
        {
            _timeline.Add(new TimelineEntry(MatchTime, kind, seat, target, zone));
        }

        private int IndexOf(byte seat)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Seat == seat)
                {
                    return i;
                }
            }

            return -1;
        }

        private List<byte> ActiveCitizenSeats()
        {
            var list = new List<byte>();
            foreach (PlayerState p in _players)
            {
                if (p.IsActive && p.IsCitizen)
                {
                    list.Add(p.Seat);
                }
            }

            return list;
        }

        private List<byte> ActiveSeats()
        {
            var list = new List<byte>();
            foreach (PlayerState p in _players)
            {
                if (p.IsActive)
                {
                    list.Add(p.Seat);
                }
            }

            return list;
        }

        private List<byte> PartnersOf(PlayerState p)
        {
            var list = new List<byte>();
            if (!p.IsPolluter)
            {
                return list;
            }

            foreach (PlayerState q in _players)
            {
                if (q != p && q.IsPolluter)
                {
                    list.Add(q.Seat);
                }
            }

            return list;
        }

        private bool CanSee(PlayerState viewer, Vec2 point)
        {
            if (!viewer.IsActive)
            {
                return false;
            }

            return Vec2.Distance(viewer.Position, point) <= VisionOf(viewer) + Epsilon
                && Map.Sight.HasLineOfSight(viewer.Position, point);
        }

        /// <summary>Other active seats that have the actor in vision + LOS (cue recipients and witnesses).</summary>
        private List<byte> WitnessesOf(PlayerState actor)
        {
            var list = new List<byte>();
            foreach (PlayerState q in _players)
            {
                if (q != actor && q.IsActive && CanSee(q, actor.Position))
                {
                    list.Add(q.Seat);
                }
            }

            return list;
        }

        /// <summary>Null when the point is within range and line of sight.</summary>
        private RejectReason? CheckReach(Vec2 from, Vec2 to, float range)
        {
            if (Vec2.Distance(from, to) > range + Epsilon)
            {
                return RejectReason.OutOfRange;
            }

            if (!Map.Sight.HasLineOfSight(from, to))
            {
                return RejectReason.WallBetween;
            }

            return null;
        }

        /// <summary>Puts a seat Out: cancels its actions, drops a body pile (poison) and moves its tasks.</summary>
        private void MakeOut(PlayerState p, OutCause cause)
        {
            p.Status = PlayerStatus.Out;
            p.OutCause = cause;
            _sessions.Remove(p.Seat);
            CancelCleaningBy(p.Seat);
            Fire.Release(p.Seat);
            if (Machine.RepairSeat == p.Seat)
            {
                Machine.CancelRepair();
            }

            if (Smog.RepairSeat == p.Seat)
            {
                Smog.CancelRepair();
                _smogRepairConsole = null;
            }

            if (cause == OutCause.Poison)
            {
                string zone = Map.ZoneAt(p.Position);
                Piles.AddBody(p.Position, zone, MatchTime, p.Seat);
                AddTimeline(TimelineKind.PoisonedOut, p.Seat, null, zone);
            }
            else if (cause == OutCause.Gas)
            {
                Piles.AddBody(p.Position, Map.ZoneAt(p.Position), MatchTime, p.Seat);
            }

            Emit(new YouAreOutEvent(p.Seat, cause));

            if (p.IsCitizen)
            {
                foreach (TaskState t in Tasks.Reassign(p.Seat, ActiveCitizenSeats()))
                {
                    Emit(new TaskAddedEvent(t.Owner, TaskInfo.From(t)));
                }
            }
        }

        private void ClearSightMemory()
        {
            for (int i = 0; i < _lastSeenAt.Length; i++)
            {
                _lastSeenAt[i] = float.NegativeInfinity;
            }
        }

        /// <summary>Remembers, per viewer and target, the last tick the target was visible.</summary>
        private void UpdateSightMemory()
        {
            int n = _players.Count;
            for (int i = 0; i < n; i++)
            {
                PlayerState viewer = _players[i];
                if (!viewer.IsActive)
                {
                    continue;
                }

                for (int j = 0; j < n; j++)
                {
                    PlayerState target = _players[j];
                    if (i == j || !target.IsActive)
                    {
                        continue;
                    }

                    if (CanSee(viewer, target.Position))
                    {
                        _lastSeenAt[i * n + j] = MatchTime;
                        _lastSeenPos[i * n + j] = target.Position;
                    }
                }
            }
        }

        private sealed class TaskSession
        {
            public TaskSession(int taskId, int step, string consoleId, float openedAt)
            {
                TaskId = taskId;
                Step = step;
                ConsoleId = consoleId;
                OpenedAt = openedAt;
            }

            public int TaskId { get; }

            public int Step { get; }

            public string ConsoleId { get; }

            /// <summary>ActiveTime when use_ok was sent.</summary>
            public float OpenedAt { get; }
        }

        private sealed class CleanJob
        {
            public CleanJob(int pileId, byte seat)
            {
                PileId = pileId;
                Seat = seat;
            }

            public int PileId { get; }

            public byte Seat { get; }

            public float Elapsed { get; set; }
        }
    }
}
