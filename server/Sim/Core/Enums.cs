namespace Sim.Core
{
    public enum Role
    {
        Citizen,
        Polluter,
    }

    public enum PlayerStatus
    {
        Active,
        Out,
    }

    public enum OutCause
    {
        Poison,
        Vote,
    }

    public enum MatchPhase
    {
        Intro,
        Playing,
        Meeting,
        Ejection,
        Ended,
    }

    public enum MeetingStage
    {
        Discussion,
        Voting,
        Results,
    }

    public enum MeetingCause
    {
        Report,
        Emergency,
    }

    /// <summary>Polluter signature. Each polluter gets a different one at match start.</summary>
    public enum WasteType
    {
        Plastic,
        Glass,
        Paper,
        Metal,
        Organic,
        Electronic,
    }

    public enum PileKind
    {
        Dump,
        Body,
    }

    public enum PileState
    {
        Fresh,
        Reported,
        Cleaned,
        Removed,
    }

    public enum SabotageKind
    {
        Smog,
        Fire,
    }

    public enum SabotageEndResult
    {
        Fixed,
        Expired,

        /// <summary>A meeting started while the fire was burning. Not in protocol v0 yet.</summary>
        Cleared,
    }

    public enum ConsoleKind
    {
        Task,
        Emergency,
        FireFix,
        SmogFix,
    }

    public enum TaskCategory
    {
        Common,
        Short,
        Long,
    }

    public enum CueKind
    {
        Hit,
        Dump,
    }

    public enum ActKind
    {
        Hit,
        Dump,
    }

    public enum Winner
    {
        Citizens,
        Polluters,
    }

    /// <summary>Same names as protocol.md section 7 (game_over.reason).</summary>
    public enum GameOverReason
    {
        PollutersVotedOut,
        TasksDone,
        PollutionFull,
        Deadline,
        Parity,
    }

    /// <summary>Same names as protocol.md section 7 (action_rejected.reason).</summary>
    public enum RejectReason
    {
        OnCooldown,
        OutOfRange,
        WallBetween,
        WrongPhase,
        NotAllowed,
        TargetInvalid,
        TargetImmune,
        AlreadyReported,
        AlreadyCleaning,
        FireActive,
        NoEmergenciesLeft,
        RateLimited,
    }

    public enum DeadlineClock
    {
        MatchTime,
        ActiveTime,
    }

    public enum TimelineKind
    {
        Dump,
        Hit,
        PoisonedOut,
        Report,
        Meeting,
        Ejection,
    }

    public enum ChatArgKind
    {
        Seat,
        Zone,
    }

    /// <summary>snake_case wire names used by protocol.md.</summary>
    public static class WireNames
    {
        public static string Of(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.OnCooldown: return "on_cooldown";
                case RejectReason.OutOfRange: return "out_of_range";
                case RejectReason.WallBetween: return "wall_between";
                case RejectReason.WrongPhase: return "wrong_phase";
                case RejectReason.NotAllowed: return "not_allowed";
                case RejectReason.TargetInvalid: return "target_invalid";
                case RejectReason.TargetImmune: return "target_immune";
                case RejectReason.AlreadyReported: return "already_reported";
                case RejectReason.AlreadyCleaning: return "already_cleaning";
                case RejectReason.FireActive: return "fire_active";
                case RejectReason.NoEmergenciesLeft: return "no_emergencies_left";
                case RejectReason.RateLimited: return "rate_limited";
                default: return "not_allowed";
            }
        }

        public static string Of(GameOverReason reason)
        {
            switch (reason)
            {
                case GameOverReason.PollutersVotedOut: return "polluters_voted_out";
                case GameOverReason.TasksDone: return "tasks_done";
                case GameOverReason.PollutionFull: return "pollution_full";
                case GameOverReason.Deadline: return "deadline";
                case GameOverReason.Parity: return "parity";
                default: return "deadline";
            }
        }
    }
}
