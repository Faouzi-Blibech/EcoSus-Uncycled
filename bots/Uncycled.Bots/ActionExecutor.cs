using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Turns a chosen BotAction into commands: walks the path with the Navigator, then sends the
    /// action command (use and task step, report, clean, hold fire, hit, dump...).
    ///
    /// Call pattern (the decision contract): call Begin at every decision, with the chosen action.
    /// Begin with the action that is already running and not finished keeps it running (nothing is
    /// re-sent, no timer restarts). Any other action abandons the running one cleanly: a fire hold is
    /// released, a clean job is cancelled, a pending task step is dropped. Follow and Avoid end by
    /// themselves after FollowCapS seconds. Step is called every tick; outside Playing it sends
    /// nothing (a meeting cancels everything). The executor never reads the engine: it uses the
    /// SeatView it is given, the MatchConfig (from Memory) and the task definitions (TaskTypes).
    /// </summary>
    public sealed class ActionExecutor
    {
        /// <summary>Wait used for a task step when the task definitions were not supplied (the longest in tasks.json).</summary>
        public const float FallbackTaskMinS = 6f;

        private const float TaskMarginS = 0.1f;
        private const float FakeTaskS = 5f;
        private const float DefaultFollowCapS = 4f;
        private const float FollowRangeMargin = 0.3f;
        private const float MaxFollowDistance = 1.5f;
        private const float FollowReplanDistance = 1.5f;
        private const float RangeMargin = 0.1f;
        private const float HoldGraceS = 1.5f;
        private const float CleanExtraS = 1.5f;
        private const float SmogExtraS = 1f;
        private const float MachineFixExtraS = 2f;
        private const float SpotCrowdRadius = 2f;
        private const int AvoidDirections = 16;
        private const float AvoidRadiusMin = 5f;
        private const float AvoidRadiusMax = 8f;
        private const int SlotCount = 4;

        private readonly Navigator _nav;
        private readonly MoveSender _moves;
        private readonly MatchConfig? _config;

        private BotAction _action = BotAction.Stay;
        private MapData? _map;
        private float _elapsed;
        private int _stage;
        private float _timer;
        private float _pause;
        private Vec2? _afterDetour;
        private float _fakeS = FakeTaskS;
        private bool _citizenStep;
        private bool _prevFire;
        private bool _prevSmog;
        private int _prevPoison;
        private bool _sessionOpen;
        private bool _holdingMachine;
        private bool _infectedCall;
        private float _waited;
        private UpgradeKind? _upgrade;
        private TrasherAbility? _ability;
        private string? _room;
        private string? _zoneId;

        private byte? _targetSeat;
        private int _targetPile = -1;
        private string? _consoleId;
        private int _taskId;
        private string? _taskTypeId;

        private string? _heldFire;
        private int _cleaningPile = -1;
        private string? _pendingRelease;
        private int _pendingCancelPile = -1;

        public ActionExecutor(Navigator nav, MoveSender moves)
        {
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
            _moves = moves ?? throw new ArgumentNullException(nameof(moves));
        }

        /// <summary>For callers that keep no BotMemory for the executor: the config and the task definitions.</summary>
        public ActionExecutor(Navigator nav, MoveSender moves, MatchConfig config, IReadOnlyList<TaskTypeDef>? taskTypes)
            : this(nav, moves)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            TaskTypes = taskTypes;
        }

        /// <summary>
        /// The bot's memory: gives the executor the match config, and receives own hits and the fire
        /// console this bot holds (so the masks know them). Set it before the first Begin.
        /// </summary>
        public BotMemory? Memory { get; set; }

        /// <summary>Task definitions by type id (tasks.json): the minimum seconds of a task step.</summary>
        public IReadOnlyList<TaskTypeDef>? TaskTypes { get; set; }

        /// <summary>Human pace (task step time, pauses, detours, group tendency); null = no pace (exact, fast).</summary>
        public PacePolicy? Pace { get; set; }

        /// <summary>
        /// Choices the brain makes before Begin (they are read once and cleared): the spot to walk to, the
        /// upgrade to pick, the ability to pick, the gas room. A hint that does not fit is ignored.
        /// </summary>
        public string? SpotHint { get; set; }

        public UpgradeKind? UpgradeChoice { get; set; }

        public TrasherAbility? AbilityChoice { get; set; }

        public string? RoomHint { get; set; }

        /// <summary>GoZoneAt: the id of the zone to walk to.</summary>
        public string? ZoneHint { get; set; }

        /// <summary>Re-pollute and plant: seconds to wait at the spot while someone watches, before acting anyway.</summary>
        public float ActPatienceS { get; set; } = 3f;

        /// <summary>Re-pollute and plant: give up instead of acting when still watched after the patience (a careful Trasher).</summary>
        public bool AbortWhenWatched { get; set; }

        /// <summary>Seconds after which Follow and Avoid end by themselves.</summary>
        public float FollowCapS { get; set; } = DefaultFollowCapS;

        /// <summary>True while the current action has not finished.</summary>

        public bool Busy { get; private set; }
        /// <summary>
        /// True while a citizen works a task step it has opened (the session is open at the console).
        /// It is not asked for decisions then: Begin is ignored, and only a phase change out of Playing,
        /// a sabotage alarm that starts, or an own hit (poison rising) stops it.
        /// </summary>
        public bool Committed => Busy && ((_citizenStep && _action == BotAction.UseHere && _stage >= 1) || _sessionOpen);

        /// <summary>Task steps that failed by chance (pace task_fail_chance) and were worked again.</summary>
        public int StepFailures { get; private set; }

        public BotAction Current => _action;

        private MatchConfig Cfg => Memory?.Config ?? _config
            ?? throw new InvalidOperationException("ActionExecutor needs a MatchConfig: set Memory or use the 4-argument constructor.");

        public void Begin(BotAction action, SeatView view, MapData map, float now)
        {
            if (Committed && ActionQuery.Playing(view))
            {
                // a committed citizen works its step to the end; the host and the brains do not ask it
                return;
            }

            if (Busy && action == _action && ActionQuery.Playing(view) && !HintRestarts())
            {
                // the same action keeps running; a hint that names the target it already has is spent
                SpotHint = null;
                ZoneHint = null;
                return;
            }

            if (_heldFire != null && action != BotAction.HoldFire)
            {
                _pendingRelease = _heldFire;
                _heldFire = null;
                if (Memory != null)
                {
                    Memory.HeldFireConsole = null;
                }
            }

            if (_cleaningPile >= 0)
            {
                _pendingCancelPile = _cleaningPile;
                _cleaningPile = -1;
            }

            if (_holdingMachine && action != BotAction.FixMachine)
            {
                _pendingRelease = _consoleId;
                _holdingMachine = false;
            }

            string? spotHint = SpotHint;
            _upgrade = UpgradeChoice;
            _ability = AbilityChoice;
            _room = RoomHint;
            _zoneId = ZoneHint;
            ZoneHint = null;
            SpotHint = null;
            UpgradeChoice = null;
            AbilityChoice = null;
            RoomHint = null;
            _sessionOpen = false;
            _infectedCall = false;
            _waited = 0f;
            _nav.Clear();
            _map = map;
            _action = action;
            _elapsed = 0f;
            _stage = 0;
            _timer = 0f;
            _targetSeat = null;
            _targetPile = -1;
            _consoleId = null;
            _taskId = -1;
            _taskTypeId = null;
            _pause = 0f;
            _afterDetour = null;
            _fakeS = FakeTaskS;
            _citizenStep = false;
            Busy = action != BotAction.Stay;
            if (!Busy || !ActionQuery.Playing(view))
            {
                Busy = false;
                return;
            }

            Vec2 me = view.Me.Position;
            bool polluter = view.Me.Role == Role.Polluter;
            switch (action)
            {
                case BotAction.GoNextTask:
                case BotAction.GoTask2:
                {
                    int n = action == BotAction.GoNextTask ? 0 : 1;
                    Vec2? goal;
                    if (polluter)
                    {
                        List<ConsoleDef> consoles = ActionQuery.TaskConsolesNearestFirst(view, map);
                        goal = n < consoles.Count ? consoles[n].Position : (Vec2?)null;
                    }
                    else
                    {
                        List<TaskInfo> open = ActionQuery.OpenTasksNearestFirst(view, map);
                        ConsoleDef? c = n < open.Count ? map.FindConsole(open[n].ConsoleId) : null;
                        if (Pace != null && c != null && Pace.Group())
                        {
                            // group tendency: a task where another player is in sight, else tag along for a moment
                            ConsoleDef? together = TaskWithAPlayerInSight(view, map, open);
                            if (together != null)
                            {
                                c = together;
                            }
                            else if (ActionQuery.Slots(view).Count > 0)
                            {
                                Begin(BotAction.FollowSlot0, view, map, now);
                                return;
                            }
                        }

                        goal = c?.Position;
                    }

                    if (Pace != null && goal != null && Pace.WrongZone())
                    {
                        Vec2? detour = WrongZoneGoal(me, goal.Value, map);
                        if (detour != null)
                        {
                            _afterDetour = goal;
                            goal = detour;
                        }
                    }

                    StartWalk(me, goal, map, now);
                    break;
                }

                case BotAction.GoZone0:
                case BotAction.GoZone1:
                case BotAction.GoZone2:
                {
                    int z = action - BotAction.GoZone0;
                    StartWalk(me, z < map.Zones.Count ? ActionQuery.ZoneCentre(map, z) : (Vec2?)null, map, now);
                    break;
                }

                case BotAction.GoZoneAt:
                {
                    int z = ZoneIndex(map, _zoneId);
                    StartWalk(me, z >= 0 ? ActionQuery.ZoneCentre(map, z) : (Vec2?)null, map, now);
                    break;
                }

                case BotAction.GoEmergency:
                    StartWalk(me, ActionQuery.NthOfKind(map, ConsoleKind.Emergency, 0)?.Position, map, now);
                    break;
                case BotAction.GoFireA:
                    StartWalk(me, ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 0)?.Position, map, now);
                    break;
                case BotAction.GoFireB:
                    StartWalk(me, ActionQuery.NthOfKind(map, ConsoleKind.FireFix, 1)?.Position, map, now);
                    break;
                case BotAction.GoSmogPanel:
                    StartWalk(me, ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0)?.Position, map, now);
                    break;
                case BotAction.LeaveGas:
                {
                    ZoneDef? warned = Memory != null ? ActionQuery.WarnedGasRoomHere(view, Memory, map) : null;
                    StartWalk(me, warned != null ? ActionQuery.NearestCellOutside(map, warned, me) : (Vec2?)null, map, now);
                    break;
                }

                case BotAction.GoDirtySpot:
                case BotAction.FinishSpot:
                    BeginSpotAction(ChooseSpot(view, map, SpotState.Dirty, action == BotAction.FinishSpot, polluter, spotHint), view, map, now);
                    if (Busy && polluter && Pace != null)
                    {
                        // a fake clean lasts like a real one at this spot, so pace is no tell
                        _fakeS = MinSecondsOfConsole(map.FindConsole(_consoleId ?? "")) * Pace.DrawFactor();
                    }

                    break;
                case BotAction.Repollute:
                    BeginSpotAction(ChooseSpot(view, map, SpotState.Clean, false, polluter, spotHint), view, map, now);
                    break;
                case BotAction.PlantVirus:
                    BeginSpotAction(ChooseSpot(view, map, SpotState.Dirty, false, polluter, spotHint), view, map, now);
                    break;

                case BotAction.GoMachine:
                case BotAction.Deliver:
                case BotAction.FixMachine:
                    BeginConsoleAction(ActionQuery.MachineConsole(map), view, map, now);
                    break;

                case BotAction.PickUpgrade:
                case BotAction.PickAbility:
                case BotAction.HealSelf:
                case BotAction.GasRoom:
                case BotAction.SabotageMachine:
                    break;

                case BotAction.FollowSlot0:
                case BotAction.FollowSlot1:
                case BotAction.FollowSlot2:
                case BotAction.FollowSlot3:
                case BotAction.AvoidSlot0:
                case BotAction.AvoidSlot1:
                case BotAction.AvoidSlot2:
                case BotAction.AvoidSlot3:
                    BeginSlotAction(action, view, map, now);
                    break;

                case BotAction.ReportNearest:
                    BeginPileAction(ActionQuery.NearestPile(view, ActionQuery.IsFresh), view, map, now);
                    break;
                case BotAction.CleanNearest:
                    BeginPileAction(ActionQuery.NearestPile(view, ActionQuery.IsCleanable), view, map, now);
                    break;

                case BotAction.CallMeeting:
                    if (ActionQuery.Infected(view))
                    {
                        // the infected may call one meeting from anywhere: no walk to the console
                        _infectedCall = true;
                        if (Memory != null)
                        {
                            Memory.InfectedCallUsed = true;
                        }

                        break;
                    }

                    BeginConsoleAction(
                        ActionQuery.ConsoleInRange(view, map, ConsoleKind.Emergency) ?? ActionQuery.NthOfKind(map, ConsoleKind.Emergency, 0),
                        view, map, now);
                    break;
                case BotAction.HoldFire:
                    BeginConsoleAction(NearestFireConsole(view, map), view, map, now);
                    break;
                case BotAction.RepairSmog:
                    BeginConsoleAction(ActionQuery.NthOfKind(map, ConsoleKind.SmogFix, 0), view, map, now);
                    break;

                case BotAction.ReleaseFire:
                    if (_pendingRelease == null)
                    {
                        _pendingRelease = Memory?.HeldFireConsole;
                        if (Memory != null)
                        {
                            Memory.HeldFireConsole = null;
                        }
                    }

                    Busy = false;
                    break;

                case BotAction.UseHere:
                {
                    TaskInfo? task = ActionQuery.OpenTaskInRange(view, map, out ConsoleDef? console);
                    if (task == null || console == null)
                    {
                        Busy = false;
                        break;
                    }

                    _taskId = task.Id;
                    _taskTypeId = task.TypeId;
                    _consoleId = console.Id;
                    _citizenStep = !polluter;
                    break;
                }

                case BotAction.HitNearest:
                case BotAction.DumpHere:
                case BotAction.SabotageFire:
                case BotAction.SabotageSmog:
                    break;
                case BotAction.FakeTaskHere:
                    if (Pace != null)
                    {
                        // a fake task lasts like a real step at this console, so pace is no tell
                        _fakeS = MinSecondsOfConsole(ActionQuery.TaskConsoleInRange(view, map)) * Pace.DrawFactor();
                    }

                    break;
            }

            if (Busy && Pace != null && Pauses(action))
            {
                _pause = Pace.PauseS();
            }
        }

        /// <summary>One tick of the current action. Sends nothing outside Playing.</summary>
        public void Step(SeatView view, float dt, float now, Action<ICommand> send)
        {
            if (!ActionQuery.Playing(view))
            {
                Reset();
                return;
            }

            if (_pendingRelease != null)
            {
                send(new FixCommand(_pendingRelease, false));
                _pendingRelease = null;
            }

            if (_pendingCancelPile >= 0)
            {
                send(new CleanCancelCommand(_pendingCancelPile));
                _pendingCancelPile = -1;
            }

            if (Committed && StopTriggered(view))
            {
                // the step is lost: the session stays open at the server and is never finished
                Busy = false;
                _stage = 0;
                return;
            }

            if (!Busy || _map == null)
            {
                return;
            }

            if (_pause > 0f)
            {
                _pause -= dt;
                return;
            }

            _elapsed += dt;
            switch (_action)
            {
                case BotAction.GoNextTask:
                case BotAction.GoTask2:
                case BotAction.GoZone0:
                case BotAction.GoZone1:
                case BotAction.GoZone2:
                case BotAction.GoZoneAt:
                case BotAction.GoEmergency:
                case BotAction.GoFireA:
                case BotAction.GoFireB:
                case BotAction.GoSmogPanel:
                case BotAction.LeaveGas:
                    WalkStep(view, dt, now, send);
                    break;

                case BotAction.AvoidSlot0:
                case BotAction.AvoidSlot1:
                case BotAction.AvoidSlot2:
                case BotAction.AvoidSlot3:
                    Busy = _elapsed <= FollowCapS && !Walk(view, dt, now, send);
                    break;

                case BotAction.FollowSlot0:
                case BotAction.FollowSlot1:
                case BotAction.FollowSlot2:
                case BotAction.FollowSlot3:
                    StepFollow(view, dt, now, send);
                    break;

                case BotAction.ReportNearest:
                    StepReport(view, dt, now, send);
                    break;
                case BotAction.CleanNearest:
                    StepClean(view, dt, now, send);
                    break;
                case BotAction.CallMeeting when _infectedCall:
                    send(new CallMeetingCommand());
                    Busy = false;
                    break;
                case BotAction.CallMeeting:
                case BotAction.HoldFire:
                case BotAction.RepairSmog:
                    StepConsoleAction(view, dt, now, send);
                    break;

                case BotAction.GoDirtySpot:
                case BotAction.FinishSpot:
                    StepSpot(view, dt, now, send);
                    break;
                case BotAction.Repollute:
                case BotAction.PlantVirus:
                    StepSpotAct(view, dt, now, send);
                    break;
                case BotAction.GoMachine:
                case BotAction.Deliver:
                    StepDeliver(view, dt, now, send);
                    break;
                case BotAction.FixMachine:
                    StepFixMachine(view, dt, now, send);
                    break;
                case BotAction.PickUpgrade:
                    Busy = false;
                    if (_upgrade != null || (Memory?.UpgradeOffer != null && Memory.UpgradeOffer.Count > 0))
                    {
                        send(new PickUpgradeCommand(_upgrade ?? ActionQuery.UpgradeByRule(Memory!.UpgradeOffer!)));
                    }

                    break;
                case BotAction.PickAbility:
                    send(new PickAbilityCommand(_ability ?? ActionQuery.AbilityByRule(view, _map ?? Memory!.Map)));
                    Busy = false;
                    break;
                case BotAction.HealSelf:
                    send(new HealCommand(view.Me.Seat));
                    Busy = false;
                    break;
                case BotAction.GasRoom:
                    Busy = false;
                    // no brain hint (a policy): the gas room holding the most players, as the scripted brain picks it
                    string? gasRoom = _room ?? ActionQuery.GasRoomWithPlayers(view, _map ?? Memory!.Map, out _)?.Id;
                    if (gasRoom != null)
                    {
                        send(new GasCommand(gasRoom));
                    }

                    break;
                case BotAction.SabotageMachine:
                    send(new SabotageCommand(SabotageKind.Machine));
                    Busy = false;
                    break;
                case BotAction.UseHere:
                    StepUse(view, dt, send);
                    break;

                case BotAction.HitNearest:
                    StepHit(view, send);
                    break;
                case BotAction.DumpHere:
                    send(new DumpCommand());
                    Busy = false;
                    break;
                case BotAction.SabotageFire:
                    send(new SabotageCommand(SabotageKind.Fire));
                    Busy = false;
                    break;
                case BotAction.SabotageSmog:
                    send(new SabotageCommand(SabotageKind.Smog));
                    Busy = false;
                    break;
                case BotAction.FakeTaskHere:
                    Busy = _elapsed < _fakeS;
                    break;
                default:
                    Busy = false;
                    break;
            }
        }

        /// <summary>
        /// Same as Step. Kept for callers written against the first API; the engine and the seat are
        /// not read (the executor works from the SeatView only).
        /// </summary>
        public void Step(MatchEngine engine, byte seat, SeatView view, float dt, float now, Action<ICommand> send)
        {
            Step(view, dt, now, send);
        }

        /// <summary>
        /// Tells the executor the engine rejected a command it sent, so it can end the action instead
        /// of waiting out a timeout (another seat was repairing or cleaning, the fire was already held...).
        /// A caller that does not see command results simply never calls it.
        /// </summary>
        public void NotifyResult(ICommand command, CommandResult result)
        {
            if (result.IsOk)
            {
                return;
            }

            switch (command)
            {
                case HitCommand hit:
                    // StepHit noted the hit before the engine answered. Only an immunity rejection means the
                    // target really is in its window; any other rejection must not block the target for 25 s.
                    if (result.Reason != RejectReason.TargetImmune)
                    {
                        Memory?.ForgetOwnHit(hit.Target);
                    }

                    break;
                case CleanStartCommand c when _action == BotAction.CleanNearest && c.PileId == _cleaningPile:
                    _cleaningPile = -1;
                    Busy = false;
                    break;
                case FixCommand f when f.Holding && _action == BotAction.HoldFire:
                    _heldFire = null;
                    if (Memory != null)
                    {
                        Memory.HeldFireConsole = null;
                    }

                    Busy = false;
                    break;
                case FixCommand f when f.Holding && _action == BotAction.RepairSmog:
                    Busy = false;
                    break;
                case UseCommand _ when _action == BotAction.UseHere:
                    Busy = false;
                    break;
                case UseCommand _ when _action == BotAction.GoDirtySpot || _action == BotAction.FinishSpot:
                    _sessionOpen = false;
                    Busy = false;
                    break;
                case FixCommand f when f.Holding && _action == BotAction.FixMachine:
                    _holdingMachine = false;
                    Busy = false;
                    break;
            }
        }

        // ---------- walking ----------

        private void StartWalk(Vec2 from, Vec2? goal, MapData map, float now)
        {
            if (goal == null)
            {
                Busy = false;
                return;
            }

            Vec2 target = MapDistances.For(map).NearestWalkable(goal.Value);
            if (!_nav.SetGoal(from, target, now))
            {
                Busy = false;
            }
        }

        /// <summary>One step along the path; true when the walk is over (arrived, expired or no goal).</summary>
        private bool Walk(SeatView view, float dt, float now, Action<ICommand> send)
        {
            Vec2? next = _nav.Next(view.Me.Position, dt, now);
            if (next == null)
            {
                return true;
            }

            send(_moves.Next(next.Value));
            return false;
        }

        private void BeginSlotAction(BotAction action, SeatView view, MapData map, float now)
        {
            bool follow = action >= BotAction.FollowSlot0 && action <= BotAction.FollowSlot3;
            int slot = follow ? action - BotAction.FollowSlot0 : action - BotAction.AvoidSlot0;
            List<PlayerView> slots = ActionQuery.Slots(view);
            if (slot >= slots.Count || slot >= SlotCount)
            {
                Busy = false;
                return;
            }

            PlayerView target = slots[slot];
            _targetSeat = target.Seat;
            if (follow)
            {
                if (Vec2.Distance(view.Me.Position, target.Position) > FollowDistance(view))
                {
                    StartWalk(view.Me.Position, target.Position, map, now);
                }

                return;
            }

            StartWalk(view.Me.Position, FleeGoal(view.Me.Position, target.Position, map), map, now);
        }

        /// <summary>How close to a followed seat the bot comes: inside hit range for a polluter, so it can strike.</summary>
        private float FollowDistance(SeatView view)
        {
            if (view.Me.Role == Role.Polluter)
            {
                return Math.Max(0.3f, Cfg.HitRange - FollowRangeMargin);
            }

            return MaxFollowDistance;
        }

        private void StepFollow(SeatView view, float dt, float now, Action<ICommand> send)
        {
            if (_elapsed > FollowCapS)
            {
                _nav.Clear();
                Busy = false;
                return;
            }

            PlayerView? target = null;
            foreach (PlayerView p in view.Players)
            {
                if (p.Seat == _targetSeat && !p.IsOut)
                {
                    target = p;
                }
            }

            if (target == null)
            {
                Busy = false;
                return;
            }

            if (Vec2.Distance(view.Me.Position, target.Position) <= FollowDistance(view))
            {
                _nav.Clear();
                return;
            }

            if (!_nav.HasGoal || Vec2.Distance(_nav.Goal, target.Position) > FollowReplanDistance)
            {
                Vec2 goal = MapDistances.For(_map!).NearestWalkable(target.Position);
                if (!_nav.SetGoal(view.Me.Position, goal, now))
                {
                    Busy = false;
                    return;
                }
            }

            Walk(view, dt, now, send);
        }

        private static Vec2? FleeGoal(Vec2 me, Vec2 threat, MapData map)
        {
            MapDistances dist = MapDistances.For(map);
            Vec2? best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < AvoidDirections; i++)
            {
                double angle = 2.0 * Math.PI * i / AvoidDirections;
                for (float r = AvoidRadiusMin; r <= AvoidRadiusMax; r += (AvoidRadiusMax - AvoidRadiusMin))
                {
                    var raw = new Vec2(me.X + (float)Math.Cos(angle) * r, me.Y + (float)Math.Sin(angle) * r);
                    if (raw.X < 0f || raw.Y < 0f || raw.X >= map.Width || raw.Y >= map.Height)
                    {
                        continue;
                    }

                    Vec2 cell = dist.NearestWalkable(raw);
                    int path = dist.Path(me, cell);
                    if (path == MapDistances.Unreachable || path > 2 * AvoidRadiusMax)
                    {
                        continue;
                    }

                    float score = Vec2.Distance(cell, threat);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = cell;
                    }
                }
            }

            return best;
        }

        // ---------- piles ----------

        private void BeginPileAction(PileView? pile, SeatView view, MapData map, float now)
        {
            if (pile == null)
            {
                Busy = false;
                return;
            }

            _targetPile = pile.Id;
            StartWalk(view.Me.Position, pile.Position, map, now);
        }

        private PileView? FindPile(SeatView view)
        {
            foreach (PileView p in view.Piles)
            {
                if (p.Id == _targetPile)
                {
                    return p;
                }
            }

            return null;
        }

        /// <summary>Within the given range of a point and in line of sight, as the engine requires.</summary>
        private bool Reaches(SeatView view, Vec2 point, float range) =>
            Vec2.Distance(view.Me.Position, point) <= range - RangeMargin && _map!.Sight.HasLineOfSight(view.Me.Position, point);

        private void StepReport(SeatView view, float dt, float now, Action<ICommand> send)
        {
            PileView? pile = FindPile(view);
            if (pile == null || pile.State != PileState.Fresh)
            {
                Busy = false;
                return;
            }

            if (Reaches(view, pile.Position, Cfg.ReportRange))
            {
                send(new ReportCommand(pile.Id));
                Busy = false;
                return;
            }

            if (_nav.HasGoal)
            {
                Walk(view, dt, now, send);
                return;
            }

            // the walk ended (arrived or expired) without getting in reach: give up, do not send out of range
            Busy = false;
        }

        private void StepClean(SeatView view, float dt, float now, Action<ICommand> send)
        {
            PileView? pile = FindPile(view);
            if (pile == null || !ActionQuery.IsCleanable(pile))
            {
                _cleaningPile = -1;
                Busy = false;
                return;
            }

            if (_cleaningPile < 0)
            {
                if (Reaches(view, pile.Position, Cfg.CleanRange))
                {
                    send(new CleanStartCommand(pile.Id));
                    _cleaningPile = pile.Id;
                    _timer = 0f;
                    return;
                }

                if (_nav.HasGoal)
                {
                    Walk(view, dt, now, send);
                    return;
                }

                Busy = false;
                return;
            }

            _timer += dt;
            if (_timer > Cfg.CleanS + CleanExtraS)
            {
                _cleaningPile = -1;
                Busy = false;
            }
        }

        // ---------- consoles ----------

        /// <summary>The fire console to hold: one in reach first, else the nearest; never one held by someone else.</summary>
        private ConsoleDef? NearestFireConsole(SeatView view, MapData map)
        {
            MapDistances dist = MapDistances.For(map);
            ConsoleDef? best = null;
            int bestDist = int.MaxValue;
            foreach (ConsoleDef c in map.ConsolesOfKind(ConsoleKind.FireFix))
            {
                bool heldByOther = view.Sabotage != null && ActionQuery.ContainsId(view.Sabotage.Held, c.Id) && c.Id != _heldFire;
                if (heldByOther)
                {
                    continue;
                }

                int walk = ActionQuery.InRange(map, view.Me.Position, c) ? 0 : dist.Path(view.Me.Position, c.Position);
                int d = walk == MapDistances.Unreachable ? int.MaxValue : walk;
                if (d != int.MaxValue && d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }

            return best;
        }

        private void BeginConsoleAction(ConsoleDef? console, SeatView view, MapData map, float now)
        {
            if (console == null)
            {
                Busy = false;
                return;
            }

            _consoleId = console.Id;
            if (!ActionQuery.InRange(map, view.Me.Position, console))
            {
                StartWalk(view.Me.Position, console.Position, map, now);
            }
        }

        private void StepConsoleAction(SeatView view, float dt, float now, Action<ICommand> send)
        {
            ConsoleDef? console = _map!.FindConsole(_consoleId ?? "");
            if (console == null)
            {
                Busy = false;
                return;
            }

            if (_stage == 0)
            {
                bool inRange = ActionQuery.InRange(_map, view.Me.Position, console);
                if (!inRange && _nav.HasGoal)
                {
                    Walk(view, dt, now, send);
                    return;
                }

                if (!inRange)
                {
                    Busy = false;
                    return;
                }

                _stage = 1;
                _timer = 0f;
                switch (_action)
                {
                    case BotAction.CallMeeting:
                        send(new CallMeetingCommand());
                        Busy = false;
                        return;
                    case BotAction.HoldFire:
                        send(new FixCommand(console.Id, true));
                        _heldFire = console.Id;
                        if (Memory != null)
                        {
                            Memory.HeldFireConsole = console.Id;
                        }

                        return;
                    default:
                        send(new FixCommand(console.Id, true));
                        return;
                }
            }

            _timer += dt;
            if (_action == BotAction.HoldFire)
            {
                bool stillHeld = view.Sabotage != null
                    && view.Sabotage.Kind == SabotageKind.Fire
                    && ActionQuery.ContainsId(view.Sabotage.Held, console.Id);
                if (!ActionQuery.FireActive(view) || (_timer > HoldGraceS && !stillHeld))
                {
                    _heldFire = null;
                    if (Memory != null)
                    {
                        Memory.HeldFireConsole = null;
                    }

                    Busy = false;
                }

                return;
            }

            // RepairSmog: stay on the panel until the smog is fixed or the time is up.
            if (!ActionQuery.SmogActive(view) || _timer > Cfg.SmogFixS + SmogExtraS)
            {
                Busy = false;
            }
        }

        // ---------- tasks ----------

        /// <summary>Walks one step; after a wrong-zone detour arrives, heads for the real goal.</summary>
        private void WalkStep(SeatView view, float dt, float now, Action<ICommand> send)
        {
            bool done = Walk(view, dt, now, send);
            if (done && _afterDetour != null)
            {
                Vec2 goal = _afterDetour.Value;
                _afterDetour = null;
                Busy = true;
                StartWalk(view.Me.Position, goal, _map!, now);
                return;
            }

            Busy = !done;
        }

        /// <summary>Actions that may be preceded by a short human pause (not reports, cleaning, fixes, acts).</summary>
        private static bool Pauses(BotAction action)
        {
            switch (action)
            {
                case BotAction.GoNextTask:
                case BotAction.GoTask2:
                case BotAction.GoZone0:
                case BotAction.GoZone1:
                case BotAction.GoZone2:
                case BotAction.FollowSlot0:
                case BotAction.FollowSlot1:
                case BotAction.FollowSlot2:
                case BotAction.FollowSlot3:
                case BotAction.UseHere:
                case BotAction.FakeTaskHere:
                case BotAction.GoDirtySpot:
                case BotAction.FinishSpot:
                case BotAction.GoMachine:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The first open task (nearest first) whose console lies in a zone where another player is in sight.</summary>
        private static ConsoleDef? TaskWithAPlayerInSight(SeatView view, MapData map, List<TaskInfo> open)
        {
            var zones = new HashSet<string>();
            foreach (PlayerView p in ActionQuery.Slots(view))
            {
                zones.Add(map.ZoneAt(p.Position));
            }

            foreach (TaskInfo t in open)
            {
                ConsoleDef? c = map.FindConsole(t.ConsoleId);
                if (c != null && zones.Contains(map.ZoneAt(c.Position)))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>A zone centre that is neither where the real goal is nor where we stand, or null.</summary>
        private Vec2? WrongZoneGoal(Vec2 me, Vec2 goal, MapData map)
        {
            string goalZone = map.ZoneAt(goal);
            string here = map.ZoneAt(me);
            var candidates = new List<int>();
            for (int z = 0; z < map.Zones.Count; z++)
            {
                if (map.Zones[z].Id != goalZone && map.Zones[z].Id != here)
                {
                    candidates.Add(z);
                }
            }

            return candidates.Count == 0 ? (Vec2?)null : ActionQuery.ZoneCentre(map, candidates[Pace!.NextInt(candidates.Count)]);
        }

        private float MinSecondsOfConsole(ConsoleDef? console)
        {
            if (TaskTypes != null && console != null)
            {
                foreach (TaskTypeDef t in TaskTypes)
                {
                    if (System.Linq.Enumerable.Contains(t.ConsoleIds, console.Id))
                    {
                        return t.MinSeconds;
                    }
                }
            }

            return FallbackTaskMinS;
        }

        private float MinSecondsOf(string? typeId)
        {
            if (TaskTypes != null && typeId != null)
            {
                foreach (TaskTypeDef t in TaskTypes)
                {
                    if (t.Id == typeId)
                    {
                        return t.MinSeconds;
                    }
                }
            }

            return FallbackTaskMinS;
        }

        private void StepUse(SeatView view, float dt, Action<ICommand> send)
        {
            if (_stage == 0)
            {
                if (_consoleId == null)
                {
                    Busy = false;
                    return;
                }

                send(new UseCommand(_consoleId));
                _timer = MinSecondsOf(_taskTypeId) * (Pace != null ? Pace.TaskTimeFactor(_taskId) : 1f) + TaskMarginS;
                _stage = 1;
                _prevFire = ActionQuery.FireActive(view);
                _prevSmog = ActionQuery.SmogActive(view);
                _prevPoison = view.Me.Poison ?? 0;
                return;
            }

            _timer -= dt;
            if (_timer > 0f)
            {
                return;
            }

            if (_citizenStep && Pace != null && Pace.TaskFails())
            {
                // the step went wrong: no TaskStep, the same session is worked again for a fresh paced time
                StepFailures++;
                _timer = MinSecondsOf(_taskTypeId) * Pace.DrawFactor() + TaskMarginS;
                return;
            }

            send(new TaskStepCommand(_taskId));
            Busy = false;
        }

        /// <summary>
        /// A committed step ends when an alarm starts (fire or smog turns active), the room it stands in is
        /// warned of gas (no mask), or the bot is hit (own poison rises). Only the own SeatView is read; a meeting is handled by Step (not Playing).
        /// </summary>
        private bool StopTriggered(SeatView view)
        {
            bool fire = ActionQuery.FireActive(view);
            bool smog = ActionQuery.SmogActive(view);
            int poison = view.Me.Poison ?? 0;
            // the gas warning of the room this Cleaner stands in (no mask): the step is dropped, leave the room
            bool gas = Memory != null && ActionQuery.WarnedGasRoomHere(view, Memory, _map ?? Memory.Map) != null;
            bool stop = (fire && !_prevFire) || (smog && !_prevSmog) || poison > _prevPoison || gas;
            _prevFire = fire;
            _prevSmog = smog;
            _prevPoison = poison;
            return stop;
        }

        // ---------- v2: spots, machine ----------

        private static int ZoneIndex(MapData map, string? id)
        {
            for (int z = 0; id != null && z < map.Zones.Count; z++)
            {
                if (map.Zones[z].Id == id)
                {
                    return z;
                }
            }

            return -1;
        }

        /// <summary>A pending spot or zone hint that names another target than the one running (Begin of the same action restarts it).</summary>
        private bool HintRestarts() => (SpotHint != null && SpotHint != _consoleId) || (ZoneHint != null && ZoneHint != _zoneId);

        private static bool SpotIs(SeatView view, string id, SpotState state)
        {
            foreach (SpotView s in view.Spots)
            {
                if (s.Id == id)
                {
                    return s.State == state;
                }
            }

            return false;
        }

        /// <summary>
        /// The spot to work on: the hint when it has the state, else one in reach (finishing), else the nearest
        /// walking distance. A Cleaner leaves a spot where someone else already stands (they are cleaning it)
        /// unless every spot is taken.
        /// </summary>
        private ConsoleDef? ChooseSpot(SeatView view, MapData map, SpotState state, bool inRangeOnly, bool polluter, string? hint)
        {
            // a Cleaner without a mask keeps out of the room whose gas warning runs
            ZoneDef? avoid = !polluter && Memory != null ? ActionQuery.WarnedRoomToAvoid(view, Memory, map) : null;
            if (hint != null)
            {
                ConsoleDef? hinted = map.FindConsole(hint);
                if (hinted != null && SpotIs(view, hint, state) && (avoid == null || !avoid.Contains(hinted.Position)))
                {
                    return hinted;
                }
            }

            if (inRangeOnly)
            {
                ConsoleDef? inRange = ActionQuery.SpotInRange(view, map, state);
                return inRange != null && avoid != null && avoid.Contains(inRange.Position) ? null : inRange;
            }

            List<ConsoleDef> spots = ActionQuery.SpotsNearestFirst(view, map, state);
            if (avoid != null)
            {
                spots = spots.FindAll(c => !avoid.Contains(c.Position));
            }

            foreach (ConsoleDef c in spots)
            {
                if (polluter || !ActionQuery.SomeoneNear(view, c.Position, SpotCrowdRadius))
                {
                    return c;
                }
            }

            return spots.Count > 0 ? spots[0] : null;
        }

        /// <summary>True while a walk is under way whose goal lies inside the zone (a Cleaner heading into a warned gas room).</summary>
        public bool WalksInto(ZoneDef zone) => Busy && _nav.HasGoal && zone.Contains(_nav.Goal);

        private void BeginSpotAction(ConsoleDef? spot, SeatView view, MapData map, float now)
        {
            if (spot == null)
            {
                Busy = false;
                return;
            }

            _consoleId = spot.Id;
            if (!ActionQuery.InRange(map, view.Me.Position, spot))
            {
                StartWalk(view.Me.Position, spot.Position, map, now);
            }
        }

        /// <summary>Walks toward the console; true once it is in reach. Ends the action when the walk is over and it is still out of reach.</summary>
        private bool Arrive(ConsoleDef console, SeatView view, float dt, float now, Action<ICommand> send)
        {
            if (ActionQuery.InRange(_map!, view.Me.Position, console))
            {
                _nav.Clear();
                return true;
            }

            if (_nav.HasGoal)
            {
                Walk(view, dt, now, send);
                return false;
            }

            Busy = false;
            return false;
        }

        /// <summary>
        /// A Cleaner: walk to the spot, use it, wait the clean time (min_s, halved with the tool, at a human pace), then spot_done.
        /// A Trasher: walk to the spot and stand there as long as a real clean would take (the fake clean sends nothing).
        /// </summary>
        private void StepSpot(SeatView view, float dt, float now, Action<ICommand> send)
        {
            ConsoleDef? console = _map!.FindConsole(_consoleId ?? "");
            if (console == null)
            {
                Busy = false;
                return;
            }

            bool polluter = view.Me.Role == Role.Polluter;
            if (_stage == 0)
            {
                if (!polluter && SpotIs(view, console.Id, SpotState.Clean))
                {
                    Busy = false; // someone else got there first
                    return;
                }

                if (!Arrive(console, view, dt, now, send))
                {
                    return;
                }

                if (polluter)
                {
                    _stage = 1;
                    _timer = _fakeS;
                    return;
                }

                if (view.Me.Bags >= view.Me.BagCap)
                {
                    Busy = false;
                    return;
                }

                send(new UseCommand(console.Id));
                _sessionOpen = true;
                _stage = 1;
                _timer = CleanTimeS(view, console);
                return;
            }

            if (polluter)
            {
                _timer -= dt;
                Busy = _timer > 0f;
                return;
            }

            if (SpotIs(view, console.Id, SpotState.Clean))
            {
                _sessionOpen = false;
                Busy = false; // another Cleaner finished it
                return;
            }

            _timer -= dt;
            if (_timer > 0f)
            {
                return;
            }

            if (Pace != null && Pace.TaskFails())
            {
                StepFailures++;
                _timer = CleanTimeS(view, console);
                return;
            }

            send(new SpotDoneCommand(console.Id));
            _sessionOpen = false;
            Busy = false;
        }

        private float CleanTimeS(SeatView view, ConsoleDef console)
        {
            float tool = view.Me.HasTool ? Cfg.ToolTimeMult : 1f;
            return MinSecondsOfConsole(console) * tool * (Pace != null ? Pace.DrawFactor() : 1f) + TaskMarginS;
        }

        /// <summary>Re-pollute or plant: walk to the spot, wait a little while someone watches, then act (or give up when careful).</summary>
        private void StepSpotAct(SeatView view, float dt, float now, Action<ICommand> send)
        {
            ConsoleDef? console = _map!.FindConsole(_consoleId ?? "");
            if (console == null)
            {
                Busy = false;
                return;
            }

            bool repollute = _action == BotAction.Repollute;
            if (!SpotIs(view, console.Id, repollute ? SpotState.Clean : SpotState.Dirty))
            {
                Busy = false; // the spot changed while we walked
                return;
            }

            if (!Arrive(console, view, dt, now, send))
            {
                return;
            }

            if (ActionQuery.Watchers(view, Cfg, _map) > 0)
            {
                _waited += dt;
                if (_waited < ActPatienceS)
                {
                    return;
                }

                if (AbortWhenWatched)
                {
                    Busy = false;
                    return;
                }
            }

            send(repollute ? (ICommand)new RepolluteCommand(console.Id) : new PlantVirusCommand(console.Id));
            Busy = false;
        }

        /// <summary>Walk to the machine and deliver every bag (a jammed machine ends the action: the brain fixes it first).</summary>
        private void StepDeliver(SeatView view, float dt, float now, Action<ICommand> send)
        {
            ConsoleDef? console = _map!.FindConsole(_consoleId ?? "");
            if (console == null)
            {
                Busy = false;
                return;
            }

            if (!Arrive(console, view, dt, now, send))
            {
                return;
            }

            Busy = false;
            if (!view.MachineJammed && view.Me.Bags > 0)
            {
                send(new UseCommand(console.Id));
            }
        }

        /// <summary>Walk to the machine and hold fix until the jam is gone (machine.fix_s of holding).</summary>
        private void StepFixMachine(SeatView view, float dt, float now, Action<ICommand> send)
        {
            ConsoleDef? console = _map!.FindConsole(_consoleId ?? "");
            if (console == null || !view.MachineJammed)
            {
                _holdingMachine = false;
                Busy = false;
                return;
            }

            if (_stage == 0)
            {
                if (!Arrive(console, view, dt, now, send))
                {
                    return;
                }

                send(new FixCommand(console.Id, true));
                _holdingMachine = true;
                _stage = 1;
                _timer = 0f;
                return;
            }

            _timer += dt;
            if (_timer > Cfg.MachineFixS + MachineFixExtraS)
            {
                _pendingRelease = console.Id;
                _holdingMachine = false;
                Busy = false;
            }
        }

        // ---------- polluter acts ----------

        private void StepHit(SeatView view, Action<ICommand> send)
        {
            Busy = false;
            BotMemory mem = Memory ?? new BotMemory(_map!, Cfg);
            PlayerView? target = ActionQuery.HitTarget(view, mem, Cfg);
            if (target == null)
            {
                return;
            }

            send(new HitCommand(target.Seat));
            Memory?.NoteOwnHit(target.Seat, view.MatchTime);
        }

        private void Reset()
        {
            _nav.Clear();
            Busy = false;
            _heldFire = null;
            _cleaningPile = -1;
            _pendingRelease = null;
            _pendingCancelPile = -1;
            _sessionOpen = false;
            _holdingMachine = false;
            _infectedCall = false;
            _action = BotAction.Stay;
            if (Memory != null)
            {
                Memory.HeldFireConsole = null;
            }
        }
    }
}
