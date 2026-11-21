using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Sim.Match
{
    /// <summary>
    /// Something the server must tell clients. Recipients are decided by the engine:
    /// null = every seat, otherwise exactly these seats (never empty: such events are not emitted).
    /// </summary>
    public abstract class GameEvent
    {
        protected GameEvent(IReadOnlyList<byte>? recipients)
        {
            Recipients = recipients;
        }

        public IReadOnlyList<byte>? Recipients { get; }

        public bool IsFor(byte seat)
        {
            if (Recipients == null)
            {
                return true;
            }

            foreach (byte s in Recipients)
            {
                if (s == seat)
                {
                    return true;
                }
            }

            return false;
        }

        internal static IReadOnlyList<byte> Only(byte seat) => new[] { seat };
    }

    /// <summary>Public, immutable copy of a task for events and views.</summary>
    public sealed class TaskInfo
    {
        public TaskInfo(int id, string typeId, int steps, int stepsDone, string consoleId)
        {
            Id = id;
            TypeId = typeId;
            Steps = steps;
            StepsDone = stepsDone;
            ConsoleId = consoleId;
        }

        public int Id { get; }

        public string TypeId { get; }

        public int Steps { get; }

        public int StepsDone { get; }

        /// <summary>Console of the next step.</summary>
        public string ConsoleId { get; }

        internal static TaskInfo From(TaskState t) => new TaskInfo(t.Id, t.Type.Id, t.Steps, t.StepsDone, t.CurrentConsoleId);
    }

    /// <summary>match_started. To one seat. Partners and Signature only for polluters.</summary>
    public sealed class MatchStartedEvent : GameEvent
    {
        public MatchStartedEvent(byte seat, Role role, IReadOnlyList<byte> partners, WasteType? signature, IReadOnlyList<TaskInfo> tasks, Vec2 spawn, float introS)
            : base(Only(seat))
        {
            Seat = seat;
            Role = role;
            Partners = partners;
            Signature = signature;
            Tasks = tasks;
            Spawn = spawn;
            IntroS = introS;
        }

        public byte Seat { get; }

        public Role Role { get; }

        /// <summary>Other polluters. Empty for citizens.</summary>
        public IReadOnlyList<byte> Partners { get; }

        public WasteType? Signature { get; }

        /// <summary>Empty for polluters.</summary>
        public IReadOnlyList<TaskInfo> Tasks { get; }

        public Vec2 Spawn { get; }

        public float IntroS { get; }
    }

    /// <summary>phase_changed {phase, stage?}. To everyone.</summary>
    public sealed class PhaseChangedEvent : GameEvent
    {
        public PhaseChangedEvent(MatchPhase phase, MeetingStage? stage)
            : base(null)
        {
            Phase = phase;
            Stage = stage;
        }

        public MatchPhase Phase { get; }

        public MeetingStage? Stage { get; }
    }

    /// <summary>correct {pos}. To the mover.</summary>
    public sealed class CorrectionEvent : GameEvent
    {
        public CorrectionEvent(byte seat, Vec2 position)
            : base(Only(seat))
        {
            Seat = seat;
            Position = position;
        }

        public byte Seat { get; }

        public Vec2 Position { get; }
    }

    /// <summary>use_ok. To the user. Task fields only for task consoles.</summary>
    public sealed class UseOkEvent : GameEvent
    {
        public UseOkEvent(byte seat, string consoleId, ConsoleKind kind, int? taskId, string? taskType, int? step)
            : base(Only(seat))
        {
            Seat = seat;
            ConsoleId = consoleId;
            Kind = kind;
            TaskId = taskId;
            TaskType = taskType;
            Step = step;
        }

        public byte Seat { get; }

        public string ConsoleId { get; }

        public ConsoleKind Kind { get; }

        public int? TaskId { get; }

        public string? TaskType { get; }

        /// <summary>0-based step index being opened.</summary>
        public int? Step { get; }
    }

    /// <summary>spot_cleaned. To the cleaner: the spot is clean and Bags is the new bag count.</summary>
    public sealed class SpotCleanedEvent : GameEvent
    {
        public SpotCleanedEvent(byte seat, string spot, int bags)
            : base(Only(seat))
        {
            Seat = seat;
            Spot = spot;
            Bags = bags;
        }

        public byte Seat { get; }

        public string Spot { get; }

        public int Bags { get; }
    }

    /// <summary>task_progress. To the owner.</summary>
    public sealed class TaskProgressEvent : GameEvent
    {
        public TaskProgressEvent(byte seat, int taskId, int stepsDone)
            : base(Only(seat))
        {
            Seat = seat;
            TaskId = taskId;
            StepsDone = stepsDone;
        }

        public byte Seat { get; }

        public int TaskId { get; }

        public int StepsDone { get; }
    }

    /// <summary>task_added. To the new owner of an orphaned task.</summary>
    public sealed class TaskAddedEvent : GameEvent
    {
        public TaskAddedEvent(byte seat, TaskInfo task)
            : base(Only(seat))
        {
            Seat = seat;
            Task = task;
        }

        public byte Seat { get; }

        public TaskInfo Task { get; }
    }

    /// <summary>clean_ok. To the cleaner.</summary>
    public sealed class CleanOkEvent : GameEvent
    {
        public CleanOkEvent(byte seat, int pileId, float duration)
            : base(Only(seat))
        {
            Seat = seat;
            PileId = pileId;
            Duration = duration;
        }

        public byte Seat { get; }

        public int PileId { get; }

        public float Duration { get; }
    }

    /// <summary>clean_done. To the cleaner.</summary>
    public sealed class CleanDoneEvent : GameEvent
    {
        public CleanDoneEvent(byte seat, int pileId)
            : base(Only(seat))
        {
            Seat = seat;
            PileId = pileId;
        }

        public byte Seat { get; }

        public int PileId { get; }
    }

    /// <summary>clean_cancelled. To the cleaner.</summary>
    public sealed class CleanCancelledEvent : GameEvent
    {
        public CleanCancelledEvent(byte seat, int pileId)
            : base(Only(seat))
        {
            Seat = seat;
            PileId = pileId;
        }

        public byte Seat { get; }

        public int PileId { get; }
    }

    /// <summary>
    /// cue {kind, seat, pos}. Only to other active seats that had the actor in vision + LOS.
    /// Pos is the victim (hit) or the new pile (dump).
    /// </summary>
    public sealed class CueEvent : GameEvent
    {
        public CueEvent(IReadOnlyList<byte> recipients, CueKind kind, byte actor, Vec2 position)
            : base(recipients)
        {
            Kind = kind;
            Actor = actor;
            Position = position;
        }

        public CueKind Kind { get; }

        public byte Actor { get; }

        public Vec2 Position { get; }
    }

    /// <summary>hit_ok {cooldown}. To the hitter.</summary>
    public sealed class HitOkEvent : GameEvent
    {
        public HitOkEvent(byte seat, float cooldown)
            : base(Only(seat))
        {
            Seat = seat;
            Cooldown = cooldown;
        }

        public byte Seat { get; }

        public float Cooldown { get; }
    }

    /// <summary>dump_ok {pile, cooldown}. To the dumper only.</summary>
    public sealed class DumpOkEvent : GameEvent
    {
        public DumpOkEvent(byte seat, int pileId, float cooldown)
            : base(Only(seat))
        {
            Seat = seat;
            PileId = pileId;
            Cooldown = cooldown;
        }

        public byte Seat { get; }

        public int PileId { get; }

        public float Cooldown { get; }
    }

    /// <summary>poisoned {level}. To the victim only.</summary>
    public sealed class PoisonedEvent : GameEvent
    {
        public PoisonedEvent(byte seat, int level)
            : base(Only(seat))
        {
            Seat = seat;
            Level = level;
        }

        public byte Seat { get; }

        public int Level { get; }
    }

    /// <summary>you_are_out {cause}. To that seat.</summary>
    public sealed class YouAreOutEvent : GameEvent
    {
        public YouAreOutEvent(byte seat, OutCause cause)
            : base(Only(seat))
        {
            Seat = seat;
            Cause = cause;
        }

        public byte Seat { get; }

        public OutCause Cause { get; }
    }

    /// <summary>meeting_started. To everyone.</summary>
    public sealed class MeetingStartedEvent : GameEvent
    {
        public MeetingStartedEvent(byte by, MeetingCause cause, int? reportedPileId, IReadOnlyList<ClueInfo> clues, float taskBar, float discussionS, IReadOnlyList<byte>? living = null)
            : base(null)
        {
            By = by;
            Cause = cause;
            ReportedPileId = reportedPileId;
            Clues = clues;
            TaskBar = taskBar;
            DiscussionS = discussionS;
            Living = living ?? new List<byte>();
        }

        public byte By { get; }

        public MeetingCause Cause { get; }

        public int? ReportedPileId { get; }

        /// <summary>Every pile that became Reported at this meeting start.</summary>
        public IReadOnlyList<ClueInfo> Clues { get; }

        /// <summary>Task bar 0..1.</summary>
        public float TaskBar { get; }

        public float DiscussionS { get; }

        /// <summary>Seats active at meeting start (the seats chat may name).</summary>
        public IReadOnlyList<byte> Living { get; }
    }

    /// <summary>
    /// chat. A phrase (PhraseId + Args) or a typed line (Text). To everyone, or, for a ghost line,
    /// only to the seats that were out when it was sent.
    /// </summary>
    public sealed class ChatEvent : GameEvent
    {
        public ChatEvent(byte seat, string? phraseId, IReadOnlyList<ChatArg> args)
            : this(seat, phraseId, args, null, false, null)
        {
        }

        public ChatEvent(byte seat, string? phraseId, IReadOnlyList<ChatArg> args, string? text, bool ghost, IReadOnlyList<byte>? recipients)
            : base(recipients)
        {
            Seat = seat;
            PhraseId = phraseId;
            Args = args;
            Text = text;
            Ghost = ghost;
        }

        public byte Seat { get; }

        /// <summary>The phrase id; null for a typed line.</summary>
        public string? PhraseId { get; }

        public IReadOnlyList<ChatArg> Args { get; }

        /// <summary>The filtered, normalised typed line; null for a phrase.</summary>
        public string? Text { get; }

        /// <summary>True for out-seat chat: only out seats receive it and bots never log it.</summary>
        public bool Ghost { get; }
    }

    /// <summary>voting_started. To everyone.</summary>
    public sealed class VotingStartedEvent : GameEvent
    {
        public VotingStartedEvent(float votingS)
            : base(null)
        {
            VotingS = votingS;
        }

        public float VotingS { get; }
    }

    /// <summary>vote_cast {seat}: who voted, never for whom. To everyone.</summary>
    public sealed class VoteCastEvent : GameEvent
    {
        public VoteCastEvent(byte seat)
            : base(null)
        {
            Seat = seat;
        }

        public byte Seat { get; }
    }

    /// <summary>One voter's final vote. Target null = skip (explicit or timed out).</summary>
    public readonly struct VoteRecord
    {
        public VoteRecord(byte voter, byte? target, bool missed)
        {
            Voter = voter;
            Target = target;
            Missed = missed;
        }

        public byte Voter { get; }

        public byte? Target { get; }

        /// <summary>The voter did not vote in time.</summary>
        public bool Missed { get; }
    }

    /// <summary>meeting_result. To everyone. Out seats are not listed (they cannot vote).</summary>
    public sealed class MeetingResultEvent : GameEvent
    {
        public MeetingResultEvent(IReadOnlyList<VoteRecord> votes, byte? ejected, bool tie)
            : base(null)
        {
            Votes = votes;
            Ejected = ejected;
            Tie = tie;
        }

        public IReadOnlyList<VoteRecord> Votes { get; }

        public byte? Ejected { get; }

        public bool Tie { get; }
    }

    /// <summary>ejection. To everyone. WasPolluter is null when ConfirmEject is off.</summary>
    public sealed class EjectionEvent : GameEvent
    {
        public EjectionEvent(byte seat, bool? wasPolluter)
            : base(null)
        {
            Seat = seat;
            WasPolluter = wasPolluter;
        }

        public byte Seat { get; }

        public bool? WasPolluter { get; }
    }

    /// <summary>round_resumed {spawn}. One per seat, to that seat.</summary>
    public sealed class RoundResumedEvent : GameEvent
    {
        public RoundResumedEvent(byte seat, Vec2 spawn)
            : base(Only(seat))
        {
            Seat = seat;
            Spawn = spawn;
        }

        public byte Seat { get; }

        public Vec2 Spawn { get; }
    }

    /// <summary>sabotage_started. To everyone. Countdown only for fire.</summary>
    public sealed class SabotageStartedEvent : GameEvent
    {
        public SabotageStartedEvent(SabotageKind kind, float? countdown)
            : base(null)
        {
            Kind = kind;
            Countdown = countdown;
        }

        public SabotageKind Kind { get; }

        public float? Countdown { get; }
    }

    /// <summary>sabotage_ended. To everyone.</summary>
    public sealed class SabotageEndedEvent : GameEvent
    {
        public SabotageEndedEvent(SabotageKind kind, SabotageEndResult result)
            : base(null)
        {
            Kind = kind;
            Result = result;
        }

        public SabotageKind Kind { get; }

        public SabotageEndResult Result { get; }
    }

    public readonly struct RoleEntry
    {
        public RoleEntry(byte seat, Role role)
        {
            Seat = seat;
            Role = role;
        }

        public byte Seat { get; }

        public Role Role { get; }
    }

    /// <summary>game_over. To everyone. The only event that reveals every role.</summary>
    public sealed class GameOverEvent : GameEvent
    {
        public GameOverEvent(Winner winner, GameOverReason reason, IReadOnlyList<RoleEntry> roles, IReadOnlyList<TimelineEntry> timeline, float matchTime)
            : base(null)
        {
            Winner = winner;
            Reason = reason;
            Roles = roles;
            Timeline = timeline;
            MatchTime = matchTime;
        }

        public Winner Winner { get; }

        public GameOverReason Reason { get; }

        public IReadOnlyList<RoleEntry> Roles { get; }

        public IReadOnlyList<TimelineEntry> Timeline { get; }

        public float MatchTime { get; }
    }
}
