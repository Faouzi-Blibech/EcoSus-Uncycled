using System.Collections.Generic;
using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>A dump pile or a body pile (04 section 5).</summary>
    public sealed class Pile
    {
        internal Pile(int id, PileKind kind, Vec2 position, string zoneId, float createdAt, WasteType? waste, byte? victim, byte? dumpedBy)
        {
            Id = id;
            Kind = kind;
            Position = position;
            ZoneId = zoneId;
            CreatedAt = createdAt;
            Waste = waste;
            Victim = victim;
            DumpedBy = dumpedBy;
            State = PileState.Fresh;
        }

        public int Id { get; }

        public PileKind Kind { get; }

        public Vec2 Position { get; }

        public string ZoneId { get; }

        /// <summary>MatchTime of creation.</summary>
        public float CreatedAt { get; }

        /// <summary>Signature of the dumper. Null for body piles.</summary>
        public WasteType? Waste { get; }

        /// <summary>Seat poisoned out. Null for dump piles.</summary>
        public byte? Victim { get; }

        /// <summary>Server-side only (timeline); never put in a clue or view.</summary>
        public byte? DumpedBy { get; }

        public PileState State { get; internal set; }

        /// <summary>Still on the map (Fresh or Reported).</summary>
        public bool IsPresent => State == PileState.Fresh || State == PileState.Reported;

        public bool IsCleanable => Kind == PileKind.Dump && IsPresent;

        public ClueInfo ToClue() => new ClueInfo(Id, Kind, ZoneId, CreatedAt, Waste, Victim);
    }

    /// <summary>What a meeting publishes about a pile: zone, time, waste type or victim.</summary>
    public sealed class ClueInfo
    {
        public ClueInfo(int pileId, PileKind kind, string zoneId, float time, WasteType? waste, byte? victim)
        {
            PileId = pileId;
            Kind = kind;
            ZoneId = zoneId;
            Time = time;
            Waste = waste;
            Victim = victim;
        }

        public int PileId { get; }

        public PileKind Kind { get; }

        public string ZoneId { get; }

        public float Time { get; }

        public WasteType? Waste { get; }

        public byte? Victim { get; }
    }

    public sealed class PileRegistry
    {
        private readonly List<Pile> _piles = new List<Pile>();
        private int _nextId = 1;

        /// <summary>Every pile ever created, in creation order (including cleaned/removed).</summary>
        public IReadOnlyList<Pile> All => _piles;

        public Pile? Find(int id)
        {
            foreach (Pile p in _piles)
            {
                if (p.Id == id)
                {
                    return p;
                }
            }

            return null;
        }

        internal Pile AddDump(Vec2 position, string zoneId, float matchTime, WasteType waste, byte dumper)
        {
            var pile = new Pile(_nextId++, PileKind.Dump, position, zoneId, matchTime, waste, null, dumper);
            _piles.Add(pile);
            return pile;
        }

        internal Pile AddBody(Vec2 position, string zoneId, float matchTime, byte victim)
        {
            var pile = new Pile(_nextId++, PileKind.Body, position, zoneId, matchTime, null, victim, null);
            _piles.Add(pile);
            return pile;
        }

        /// <summary>Marks every Fresh pile Reported and returns them (meeting start).</summary>
        internal List<Pile> ReportAllFresh()
        {
            var reported = new List<Pile>();
            foreach (Pile p in _piles)
            {
                if (p.State == PileState.Fresh)
                {
                    p.State = PileState.Reported;
                    reported.Add(p);
                }
            }

            return reported;
        }

        /// <summary>Removes reported body piles (meeting end). Dump piles stay.</summary>
        internal void RemoveReportedBodies()
        {
            foreach (Pile p in _piles)
            {
                if (p.Kind == PileKind.Body && p.State == PileState.Reported)
                {
                    p.State = PileState.Removed;
                }
            }
        }

        internal void MarkCleaned(Pile pile)
        {
            pile.State = PileState.Cleaned;
        }
    }
}
