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
        Virus,
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
        Machine,
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
        Machine,
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
        Repollute,
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
        MeterFull,
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
        TooLong,
        TextLimit,
        PhraseLimit,
        BagsFull,
        SpotClean,
        SpotDirty,
        NotUnlocked,
        NoBags,
        MachineJammed,
        NoItem,
    }

    /// <summary>Recycling Machine upgrades (spec section 5). Same names as protocol 2 (pick_upgrade.item).</summary>
    public enum UpgradeKind
    {
        Backpack,
        Tool,
        Heal,
        Mask,
    }

    /// <summary>A cleaning spot: every task console is one (spec section 5).</summary>
    public enum SpotState
    {
        Dirty,
        Clean,
    }

    /// <summary>The lethal ability a Trasher picks after the third re-pollute (spec section 6).</summary>
    public enum TrasherAbility
    {
        None,
        Virus,
        Gas,
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
        Infected,
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
                case RejectReason.TooLong: return "too_long";
                case RejectReason.TextLimit: return "text_limit";
                case RejectReason.PhraseLimit: return "phrase_limit";
                case RejectReason.BagsFull: return "bags_full";
                case RejectReason.SpotClean: return "spot_clean";
                case RejectReason.SpotDirty: return "spot_dirty";
                case RejectReason.NotUnlocked: return "not_unlocked";
                case RejectReason.NoBags: return "no_bags";
                case RejectReason.MachineJammed: return "machine_jammed";
                case RejectReason.NoItem: return "no_item";
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
                case GameOverReason.MeterFull: return "meter_full";
                default: return "deadline";
            }
        }
    }
}
