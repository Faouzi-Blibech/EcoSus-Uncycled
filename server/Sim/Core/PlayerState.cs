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

        /// <summary>Movement distance budget in tiles.</summary>
        public float MoveBudget { get; internal set; }

        public bool IsActive => Status == PlayerStatus.Active;

        public bool IsPolluter => Role == Role.Polluter;

        public bool IsCitizen => Role == Role.Citizen;
    }
}
