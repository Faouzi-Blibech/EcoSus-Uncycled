namespace Sim.Match
{
    /// <summary>
    /// Per-seat numbers the snapshot carries besides the SeatView. Kept out of SeatView on purpose: the
    /// bots' observation schema hash covers the public shape of the view types, and these fields are
    /// for the wire only.
    /// </summary>
    public sealed class SnapshotExtras
    {
        public SnapshotExtras(float smogLevel, float? repairElapsed, bool machineJammed = false, float? machineRepairElapsed = null, string? gasRoom = null, float gasInS = 0f, float dashS = 0f)
        {
            DashS = dashS;
            GasRoom = gasRoom;
            GasInS = gasInS;
            MachineJammed = machineJammed;
            MachineRepairElapsed = machineRepairElapsed;
            SmogLevel = smogLevel;
            RepairElapsed = repairElapsed;
        }

        /// <summary>0..1 smog strength citizen vision follows; still above 0 for a moment after the smog ends (the vision eases back).</summary>
        public float SmogLevel { get; }

        /// <summary>Seconds until this seat's dash is ready (0 = ready). Wire-only, kept out of SeatView so the bots' schema does not change.</summary>
        public float DashS { get; }

        /// <summary>Seconds of the smog repair so far; only for the seat that is repairing, null for everyone else.</summary>
        public float? RepairElapsed { get; }

        /// <summary>The Recycling Machine is jammed (snapshot machine.jammed, every seat).</summary>
        public bool MachineJammed { get; }

        /// <summary>Seconds of the machine repair so far; only for the seat that is repairing (snapshot machine.repair_t).</summary>
        public float? MachineRepairElapsed { get; }

        /// <summary>Room whose gas warning runs (snapshot gas.room, every seat); null when none runs.</summary>
        public string? GasRoom { get; }

        /// <summary>Seconds until that warning ends (snapshot gas.in_s).</summary>
        public float GasInS { get; }
    }
}
