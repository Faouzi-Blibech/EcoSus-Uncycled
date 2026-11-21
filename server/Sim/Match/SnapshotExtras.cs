namespace Sim.Match
{
    /// <summary>
    /// Per-seat numbers the snapshot carries besides the SeatView. Kept out of SeatView on purpose: the
    /// bots' observation schema hash covers the public shape of the view types, and these fields are
    /// for the wire only.
    /// </summary>
    public sealed class SnapshotExtras
    {
        public SnapshotExtras(float smogLevel, float? repairElapsed)
        {
            SmogLevel = smogLevel;
            RepairElapsed = repairElapsed;
        }

        /// <summary>0..1 smog strength citizen vision follows; still above 0 for a moment after the smog ends (the vision eases back).</summary>
        public float SmogLevel { get; }

        /// <summary>Seconds of the smog repair so far; only for the seat that is repairing, null for everyone else.</summary>
        public float? RepairElapsed { get; }
    }
}
