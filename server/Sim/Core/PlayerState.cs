namespace Sim.Core
{
    /// <summary>
    /// One seat in a match. Read-only from outside Sim; only the engine changes it.
    /// Timers are "ready at" timestamps on the engine's ActiveTime clock.
    /// </summary>
    public sealed class PlayerState
    {
        public PlayerState(byte seat, string name, byte color)
        {
            Seat = seat;
            Name = name;
            Color = color;
            LastMoveSeq = -1;
        }

        public byte Seat { get; }

        public string Name { get; }

        public byte Color { get; }

        public Role Role { get; internal set; }

        public PlayerStatus Status { get; internal set; }

        public OutCause? OutCause { get; internal set; }

        public Vec2 Position { get; internal set; }

        /// <summary>Waste type of this polluter's dump piles. Null for citizens.</summary>
        public WasteType? Signature { get; internal set; }

        /// <summary>Poison level 0-100 (citizens only).</summary>
        public int Poison { get; internal set; }

        /// <summary>ActiveTime until which this citizen cannot be hit again.</summary>
        public float ImmuneUntil { get; internal set; }

        /// <summary>ActiveTime at which this polluter may hit again.</summary>
        public float HitReadyAt { get; internal set; }

        /// <summary>ActiveTime at which this polluter may dump again.</summary>
        public float DumpReadyAt { get; internal set; }

        /// <summary>Highest accepted move sequence number (-1 = none yet).</summary>
        public long LastMoveSeq { get; internal set; }

        /// <summary>Bags carried (citizens only).</summary>
        public int Bags { get; internal set; }

        /// <summary>Most bags this seat can carry (citizens only).</summary>
        public int BagCap { get; internal set; }

        /// <summary>True once the seat owns the Cleaning Tool (cleans in ToolTimeMult of the time).</summary>
        public bool HasTool { get; internal set; }

        /// <summary>Re-pollutes done so far (Trashers only).</summary>
        public int Repollutes { get; internal set; }

        /// <summary>The ability picked after the unlock (Trashers only); None until then.</summary>
        public TrasherAbility Ability { get; internal set; }

        /// <summary>True from the unlock until the Trasher picks (ability_unlocked sent, nothing picked yet).</summary>
        public bool AbilityChoiceOpen { get; internal set; }

        /// <summary>ActiveTime at which this Trasher may re-pollute again.</summary>
        public float RepolluteReadyAt { get; internal set; }

        /// <summary>ActiveTime at which this Trasher may use the Toxic Gas again.</summary>
        public float GasReadyAt { get; internal set; }

        /// <summary>ActiveTime at which this seat may dash again (every seat).</summary>
        public float DashReadyAt { get; internal set; }

        /// <summary>Spots cleaned since the last dash refill (dash.cleans_refill refills it).</summary>
        public int CleansSinceDash { get; internal set; }
        /// <summary>True once the seat owns the Backpack (bag capacity BackpackCap).</summary>
        public bool HasBackpack { get; internal set; }

        /// <summary>Heals held (citizens only, at most one).</summary>
        public int Heals { get; internal set; }

        /// <summary>Masks held (citizens only, at most one).</summary>
        public int Masks { get; internal set; }

        /// <summary>The items of the open Recycling Machine offer; null when no offer waits for a pick.</summary>
        public System.Collections.Generic.IReadOnlyList<UpgradeKind>? UpgradeOffer { get; internal set; }

        /// <summary>True while a Recycling Machine upgrade offer waits for a pick.</summary>
        public bool UpgradeOfferOpen => UpgradeOffer != null;

        /// <summary>Movement distance budget in tiles.</summary>
        public float MoveBudget { get; internal set; }

        /// <summary>ActiveTime at which the infection kills this Cleaner (null = not infected). Runs on active time, so a meeting freezes it.</summary>
        public float? InfectedDeathAt { get; internal set; }

        /// <summary>True once the infected Cleaner used the free meeting call of the current infection.</summary>
        public bool InfectedMeetingUsed { get; internal set; }

        /// <summary>ActiveTime at which this Trasher may plant a virus again (set when a planted virus triggers).</summary>
        public float VirusReadyAt { get; internal set; }

        public bool IsActive => Status == PlayerStatus.Active;

        public bool IsPolluter => Role == Role.Polluter;

        public bool IsCitizen => Role == Role.Citizen;
    }
}
