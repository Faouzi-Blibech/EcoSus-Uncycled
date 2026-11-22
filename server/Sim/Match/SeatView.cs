using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Sim.Match
{
    /// <summary>Lobby data the engine starts with.</summary>
    public sealed class SeatSetup
    {
        public SeatSetup(byte seat, string name, byte color)
        {
            Seat = seat;
            Name = name ?? "";
            Color = color;
        }

        public byte Seat { get; }

        public string Name { get; }

        public byte Color { get; }
    }

    /// <summary>Another seat as this viewer may see it.</summary>
    public sealed class PlayerView
    {
        public PlayerView(byte seat, Vec2 position, byte color, bool isOut)
        {
            Seat = seat;
            Position = position;
            Color = color;
            IsOut = isOut;
        }

        public byte Seat { get; }

        public Vec2 Position { get; }

        public byte Color { get; }

        public bool IsOut { get; }
    }

    public sealed class PileView
    {
        public PileView(int id, Vec2 position, PileKind kind, PileState state, byte? victim = null)
        {
            Id = id;
            Position = position;
            Kind = kind;
            State = state;
            Victim = victim;
        }

        public int Id { get; }

        public Vec2 Position { get; }

        public PileKind Kind { get; }

        public PileState State { get; }

        /// <summary>Body piles: the seat that fell (its colour is drawn on the body). Null for waste.</summary>
        public byte? Victim { get; }
    }

    /// <summary>A spot's public state: every seat sees every spot (positions come from the map).</summary>
    public sealed class SpotView
    {
        public SpotView(string id, SpotState state)
        {
            Id = id;
            State = state;
        }

        public string Id { get; }

        public SpotState State { get; }
    }

    /// <summary>Remaining cooldowns of a polluter, in seconds.</summary>
    public sealed class CooldownView
    {
        public CooldownView(float hit, float dump, float sabotage, float repollute = 0f, float virus = 0f, float gas = 0f)
        {
            Repollute = repollute;
            Virus = virus;
            Gas = gas;
            Hit = hit;
            Dump = dump;
            Sabotage = sabotage;
        }

        public float Hit { get; }

        public float Dump { get; }

        public float Sabotage { get; }

        /// <summary>Seconds until the next re-pollute (v2).</summary>
        public float Repollute { get; }

        /// <summary>Seconds until the next virus (v2; 0 until the virus rule fills it).</summary>
        public float Virus { get; }

        /// <summary>Seconds until the next gas (v2; 0 until the gas rule fills it).</summary>
        public float Gas { get; }
    }

    /// <summary>The viewer's own private data.</summary>
    public sealed class MyView
    {
        public MyView(
            byte seat,
            Role role,
            IReadOnlyList<byte> partners,
            WasteType? signature,
            PlayerStatus status,
            Vec2 position,
            float vision,
            int? poison,
            CooldownView? cooldowns,
            IReadOnlyList<TaskInfo> tasks,
            int bags = 0,
            int bagCap = 0,
            bool hasTool = false,
            int repollutes = 0,
            TrasherAbility ability = TrasherAbility.None,
            int heals = 0,
            int masks = 0,
            float? infectedS = null)
        {
            InfectedS = infectedS;
            Repollutes = repollutes;
            Ability = ability;
            Bags = bags;
            BagCap = bagCap;
            HasTool = hasTool;
            Heals = heals;
            Masks = masks;
            Seat = seat;
            Role = role;
            Partners = partners;
            Signature = signature;
            Status = status;
            Position = position;
            Vision = vision;
            Poison = poison;
            Cooldowns = cooldowns;
            Tasks = tasks;
        }

        public byte Seat { get; }

        public Role Role { get; }

        /// <summary>Other polluters; empty for citizens.</summary>
        public IReadOnlyList<byte> Partners { get; }

        /// <summary>Polluters only.</summary>
        public WasteType? Signature { get; }

        public PlayerStatus Status { get; }

        public Vec2 Position { get; }

        public float Vision { get; }

        /// <summary>Citizens only.</summary>
        public int? Poison { get; }

        /// <summary>Polluters only.</summary>
        public CooldownView? Cooldowns { get; }

        /// <summary>Own tasks (empty for polluters).</summary>
        public IReadOnlyList<TaskInfo> Tasks { get; }

        /// <summary>Bags carried. Private to the carrier; the wire sends it to citizens only.</summary>
        public int Bags { get; }

        public int BagCap { get; }

        public bool HasTool { get; }

        /// <summary>Re-pollutes done so far. Trashers only (the wire never sends it to a Cleaner).</summary>
        public int Repollutes { get; }

        /// <summary>The picked ability, None before. Trashers only.</summary>
        public TrasherAbility Ability { get; }
        /// <summary>Heals held (0 or 1). Private to the carrier; the wire sends it to citizens only.</summary>
        public int Heals { get; }

        /// <summary>Masks held (0 or 1). Private to the carrier; the wire sends it to citizens only.</summary>
        public int Masks { get; }

        /// <summary>Seconds until the virus puts this Cleaner out; null unless the viewer is infected. Never about another seat.</summary>
        public float? InfectedS { get; }
    }

    public sealed class SabotageView
    {
        public SabotageView(SabotageKind kind, float? countdown, IReadOnlyList<string> held, float smogLevel)
        {
            Kind = kind;
            Countdown = countdown;
            Held = held;
            SmogLevel = smogLevel;
        }

        public SabotageKind Kind { get; }

        /// <summary>Fire only.</summary>
        public float? Countdown { get; }

        /// <summary>Fire consoles currently held.</summary>
        public IReadOnlyList<string> Held { get; }

        /// <summary>0..1 smog strength (0 for fire).</summary>
        public float SmogLevel { get; }
    }

    /// <summary>Meeting state any seat may know (for rejoin): never who voted for whom.</summary>
    public sealed class MeetingView
    {
        public MeetingView(MeetingStage stage, float endsIn, byte calledBy, MeetingCause cause, IReadOnlyList<ClueInfo> clues, IReadOnlyList<byte> voted, bool myVoteCast, byte? myVote, float taskBar, int myChatCount = 0, IReadOnlyList<byte>? living = null, int myText = 0)
        {
            MyText = myText;
            Stage = stage;
            EndsIn = endsIn;
            CalledBy = calledBy;
            Cause = cause;
            Clues = clues;
            Voted = voted;
            MyVoteCast = myVoteCast;
            MyVote = myVote;
            TaskBar = taskBar;
            MyChatCount = myChatCount;
            Living = living ?? new List<byte>();
        }

        public MeetingStage Stage { get; }

        public float EndsIn { get; }

        public byte CalledBy { get; }

        public MeetingCause Cause { get; }

        public IReadOnlyList<ClueInfo> Clues { get; }

        public IReadOnlyList<byte> Voted { get; }

        public bool MyVoteCast { get; }

        /// <summary>Own vote when MyVoteCast; null = skip.</summary>
        public byte? MyVote { get; }

        public float TaskBar { get; }

        /// <summary>Chat phrases this seat has sent in this meeting (counts toward PhrasesPerMeeting).</summary>
        public int MyChatCount { get; }

        /// <summary>Seats that were active when this meeting started (public: meeting_started carries it too).</summary>
        public IReadOnlyList<byte> Living { get; }

        /// <summary>Typed chat lines this seat has sent in this meeting (counts toward TextPerMeeting).</summary>
        public int MyText { get; }
    }

    /// <summary>A running gas warning: the warned room and the active seconds until it resolves.</summary>
    public sealed class GasView
    {
        public GasView(string room, float inS)
        {
            Room = room;
            InS = inS;
        }

        public string Room { get; }

        public float InS { get; }
    }

    /// <summary>Everything one seat may know right now. Becomes the snapshot message.</summary>
    public sealed class SeatView
    {
        public SeatView(
            byte seat,
            MatchPhase phase,
            float phaseTimeLeft,
            float matchTime,
            float deadlineS,
            DeadlineClock deadlineClock,
            float deadlineClockValue,
            float meter,
            int emergenciesLeft,
            MyView me,
            IReadOnlyList<PlayerView> players,
            IReadOnlyList<PileView> piles,
            SabotageView? sabotage,
            MeetingView? meeting,
            GameOverEvent? gameOver,
            IReadOnlyList<ChatEvent>? ghostChat = null,
            IReadOnlyList<SpotView>? spots = null,
            bool machineJammed = false,
            GasView? gas = null)
        {
            Gas = gas;
            MachineJammed = machineJammed;
            Spots = spots ?? new List<SpotView>();
            GhostChat = ghostChat ?? new List<ChatEvent>();
            Seat = seat;
            Phase = phase;
            PhaseTimeLeft = phaseTimeLeft;
            MatchTime = matchTime;
            DeadlineS = deadlineS;
            DeadlineClock = deadlineClock;
            DeadlineClockValue = deadlineClockValue;
            Meter = meter;
            EmergenciesLeft = emergenciesLeft;
            Me = me;
            Players = players;
            Piles = piles;
            Sabotage = sabotage;
            Meeting = meeting;
            GameOver = gameOver;
        }

        public byte Seat { get; }

        public MatchPhase Phase { get; }

        /// <summary>Seconds left in Intro, the meeting stage or Ejection; 0 otherwise.</summary>
        public float PhaseTimeLeft { get; }

        public float MatchTime { get; }

        public float DeadlineS { get; }

        public DeadlineClock DeadlineClock { get; }

        /// <summary>Current value of the clock the deadline runs on.</summary>
        public float DeadlineClockValue { get; }

        public float Meter { get; }

        /// <summary>Match-wide count.</summary>
        public int EmergenciesLeft { get; }

        public MyView Me { get; }

        /// <summary>Other seats in vision + LOS (or within the lost-sight grace). Empty for Out seats.</summary>
        public IReadOnlyList<PlayerView> Players { get; }

        /// <summary>Piles in vision + LOS. Empty for Out seats.</summary>
        public IReadOnlyList<PileView> Piles { get; }

        public SabotageView? Sabotage { get; }

        public MeetingView? Meeting { get; }

        public GameOverEvent? GameOver { get; }

        /// <summary>Every spot and whether it is dirty or clean, for every seat.</summary>
        public IReadOnlyList<SpotView> Spots { get; }

        /// <summary>The Recycling Machine is jammed (public: every seat sees the alarm). Deliveries are refused until it is fixed.</summary>
        public bool MachineJammed { get; }

        /// <summary>The running gas warning (public: every seat sees it); null when none runs.</summary>
        public GasView? Gas { get; }

        /// <summary>Ghost chat so far, oldest first. Non-empty only in the view of an out seat.</summary>
        public IReadOnlyList<ChatEvent> GhostChat { get; }
    }
}
