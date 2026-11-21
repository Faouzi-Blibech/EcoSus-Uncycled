using System.Collections.Generic;
using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>One seat that had the actor in vision + line of sight at the moment of an act.</summary>
    public sealed class WitnessRecord
    {
        public WitnessRecord(ActKind act, byte actor, byte witness, float at, string zoneId, Vec2 actorPosition, int? pileId, byte? victim)
        {
            Act = act;
            Actor = actor;
            Witness = witness;
            At = at;
            ZoneId = zoneId;
            ActorPosition = actorPosition;
            PileId = pileId;
            Victim = victim;
        }

        public ActKind Act { get; }

        public byte Actor { get; }

        public byte Witness { get; }

        /// <summary>MatchTime of the act.</summary>
        public float At { get; }

        public string ZoneId { get; }

        public Vec2 ActorPosition { get; }

        /// <summary>The dump pile, for dumps.</summary>
        public int? PileId { get; }

        /// <summary>The hit citizen, for hits.</summary>
        public byte? Victim { get; }
    }

    /// <summary>
    /// Server-side record of who saw which hit or dump. Read by bots for testimony.
    /// Never placed in a SeatView or GameEvent.
    /// </summary>
    public sealed class WitnessLog
    {
        private readonly List<WitnessRecord> _records = new List<WitnessRecord>();

        public IReadOnlyList<WitnessRecord> All => _records;

        internal void Add(WitnessRecord record)
        {
            _records.Add(record);
        }

        /// <summary>Everything this seat witnessed, oldest first.</summary>
        public List<WitnessRecord> SeenBy(byte witness)
        {
            var list = new List<WitnessRecord>();
            foreach (WitnessRecord r in _records)
            {
                if (r.Witness == witness)
                {
                    list.Add(r);
                }
            }

            return list;
        }

        /// <summary>Every record where this seat was the actor.</summary>
        public List<WitnessRecord> About(byte actor)
        {
            var list = new List<WitnessRecord>();
            foreach (WitnessRecord r in _records)
            {
                if (r.Actor == actor)
                {
                    list.Add(r);
                }
            }

            return list;
        }

        /// <summary>What this seat witnessed at or after the given MatchTime.</summary>
        public List<WitnessRecord> SeenBySince(byte witness, float matchTime)
        {
            var list = new List<WitnessRecord>();
            foreach (WitnessRecord r in _records)
            {
                if (r.Witness == witness && r.At >= matchTime)
                {
                    list.Add(r);
                }
            }

            return list;
        }
    }
}
