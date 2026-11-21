using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>One line of the game-over timeline. Revealed to everyone only at game over.</summary>
    public sealed class TimelineEntry
    {
        public TimelineEntry(float time, TimelineKind kind, byte? seat, byte? target, string? zoneId)
        {
            Time = time;
            Kind = kind;
            Seat = seat;
            Target = target;
            ZoneId = zoneId;
        }

        /// <summary>MatchTime.</summary>
        public float Time { get; }

        public TimelineKind Kind { get; }

        /// <summary>Actor: dumper, hitter, reporter, meeting caller, ejected or poisoned-out seat.</summary>
        public byte? Seat { get; }

        /// <summary>Hit victim, when relevant.</summary>
        public byte? Target { get; }

        public string? ZoneId { get; }
    }
}
