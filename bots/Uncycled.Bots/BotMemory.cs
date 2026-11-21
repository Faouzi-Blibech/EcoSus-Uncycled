using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Bots.Meeting;

namespace Uncycled.Bots
{
    /// <summary>One time a seat was in sight and where.</summary>
    public readonly struct Sighting
    {
        public Sighting(float time, string zone, Vec2 position)
        {
            Time = time;
            Zone = zone;
            Position = position;
        }

        public float Time { get; }

        public string Zone { get; }

        public Vec2 Position { get; }
    }

    /// <summary>
    /// What one bot seat remembers: who it saw, where and when, and the hits and dumps it witnessed.
    /// Fed only with its own SeatView and WitnessLog.SeenBy(self), so it never holds hidden state.
    /// </summary>
    public sealed class BotMemory : IMeetingObserver
    {
        private const int MaxSightingsPerSeat = 120;
        private const float MinSightingGapS = 0.5f;
        private const float TakeoverGraceS = 1f;

        /// <summary>Seconds a seen player must stand at a dirty spot, and leave it dirty, to count as a fake clean (a real clean takes at least min_s).</summary>
        public const float FakeCleanDwellS = 3.5f;
        private const float SpotDwellMargin = 0.4f;

        /// <summary>A fake-clean tell is dropped when the leaver shows up at the smog panel or the jammed machine within this many seconds: it left to repair.</summary>
        private const float RepairDropWindowS = 25f;

        private readonly Dictionary<byte, List<Sighting>> _sightings = new Dictionary<byte, List<Sighting>>();
        private readonly List<Sighting> _own = new List<Sighting>();
        private readonly Dictionary<byte, float> _hitAtPlay = new Dictionary<byte, float>();
        private readonly HashSet<byte> _out = new HashSet<byte>();
        private readonly List<WitnessRecord> _witnessed = new List<WitnessRecord>();
        private readonly List<WitnessRecord> _fakeCleans = new List<WitnessRecord>();
        private readonly Dictionary<byte, KeyValuePair<string, float>> _dwell = new Dictionary<byte, KeyValuePair<string, float>>();
        private MatchPhase? _prevPhase;
        private bool _prevAlarm;
        private bool _prevRepairAlarm;
        private float _repairAlarmAt;
        private string? _prevGasRoom;
        private float? _lastMeetingEnd;
        private float? _prevMatchTime;
        private float _playTime;
        private readonly Dictionary<byte, HeatState> _heat = new Dictionary<byte, HeatState>();
        private int _meetingsRecorded;

        public BotMemory(MapData map, MatchConfig config)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public MapData Map { get; }

        public MatchConfig Config { get; }

        public IReadOnlyList<WitnessRecord> Witnessed => _witnessed;

        /// <summary>
        /// What this bot worked out from its own view: a player seen standing at a dirty spot for a clean's
        /// time that left it dirty (a Trasher fake clean). Recorded as dump-kind records: like a witnessed
        /// act they point at the actor, but they come from watching, not from an engine log.
        /// </summary>
        public IReadOnlyList<WitnessRecord> Inferred => _fakeCleans;

        /// <summary>
        /// The fire console this bot is holding right now (set by the executor), so a mask can tell
        /// its own hold from a hold by another seat. Null when it holds none.
        /// </summary>
        public string? HeldFireConsole { get; set; }

        /// <summary>The upgrade offer open for this seat (set by the brain from the engine before it decides: the popup a human sees); null when none.</summary>
        public IReadOnlyList<UpgradeKind>? UpgradeOffer { get; set; }

        /// <summary>
        /// The room whose gas warning runs (the public gas warning, set by the brain from the engine's
        /// wire extras before Observe, the same for every seat); null when none runs.
        /// </summary>
        public string? GasWarnedRoom { get; set; }

        /// <summary>Match time at which the meeting in progress started, as this bot saw it (the phase turning to Meeting); null before the first meeting.</summary>
        public float? MeetingStartedAt { get; private set; }

        /// <summary>Match time of this bot's first sighting of the current machine jam; null while the machine runs.</summary>
        public float? MachineJamSeenAt { get; private set; }

        /// <summary>True once this seat called its one free meeting for the current infection (cleared when it is no longer infected).</summary>
        public bool InfectedCallUsed { get; set; }

        /// <summary>Seconds of Playing this memory has watched (like the engine's ActiveTime, from when it was made).</summary>
        public float PlayTime => _playTime;

        /// <summary>Feeds one look: the view, the match time and the witness records of this seat so far.</summary>
        public void Observe(SeatView view, float matchTime, IReadOnlyList<WitnessRecord> seenByMe)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (_prevPhase == null && matchTime > TakeoverGraceS)
            {
                // Made mid-match: a meeting before now is unknown, so assume one just ended (the
                // emergency wait then holds for the engine's minimum, as for a rejoining human).
                _lastMeetingEnd = matchTime;
            }

            if (_prevPhase == MatchPhase.Playing && view.Phase == MatchPhase.Playing
                && _prevMatchTime != null && matchTime > _prevMatchTime.Value)
            {
                _playTime += matchTime - _prevMatchTime.Value;
            }

            _prevMatchTime = matchTime;
            if (view.Me.InfectedS == null)
            {
                InfectedCallUsed = false;
            }

            if (view.Phase == MatchPhase.Playing && (_prevPhase == MatchPhase.Meeting || _prevPhase == MatchPhase.Ejection))
            {
                _lastMeetingEnd = matchTime;
            }

            if (view.Phase == MatchPhase.Meeting && _prevPhase != MatchPhase.Meeting)
            {
                MeetingStartedAt = matchTime;
            }

            _prevPhase = view.Phase;
            _inMeeting = view.Phase == MatchPhase.Meeting && view.Meeting != null;
            if (_inMeeting)
            {
                _livingNow = view.Meeting!.Living.Count > 0 ? view.Meeting.Living.Count : Math.Max(1, Config.Seats - _out.Count);
            }

            // an alarm (smog, fire, machine jam) or a gas warning that starts pulls honest Cleaners off their
            // spots: what they were cleaning is interrupted, not faked, so every dwell restarts from now
            bool alarm = view.Sabotage != null || view.MachineJammed;
            bool gasStarted = GasWarnedRoom != null && GasWarnedRoom != _prevGasRoom;
            if ((alarm && !_prevAlarm) || gasStarted)
            {
                _dwell.Clear();
            }

            if (!view.MachineJammed)
            {
                MachineJamSeenAt = null;
            }
            else if (MachineJamSeenAt == null)
            {
                MachineJamSeenAt = matchTime;
            }

            bool repairAlarm = (view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Smog) || view.MachineJammed;
            if (repairAlarm && !_prevRepairAlarm)
            {
                _repairAlarmAt = matchTime;
            }

            _prevRepairAlarm = repairAlarm;
            _prevAlarm = alarm;
            _prevGasRoom = GasWarnedRoom;

            if (view.Phase == MatchPhase.Playing)
            {
                RecordOwn(view, matchTime); // never in a meeting: the own zone history is the alibi, and 120 slots fill in 60 s
                WatchSpots(view, matchTime);
            }
            else
            {
                _dwell.Clear(); // a meeting moves everyone: nobody left a spot
            }

            foreach (PlayerView p in view.Players)
            {
                if (p.IsOut)
                {
                    _out.Add(p.Seat);
                    continue;
                }

                if (!_sightings.TryGetValue(p.Seat, out List<Sighting>? list))
                {
                    list = new List<Sighting>();
                    _sightings[p.Seat] = list;
                }

                string zone = Map.ZoneAt(p.Position);
                if (list.Count > 0 && matchTime - list[list.Count - 1].Time < MinSightingGapS)
                {
                    list[list.Count - 1] = new Sighting(matchTime, zone, p.Position);
                    continue;
                }

                list.Add(new Sighting(matchTime, zone, p.Position));
                if (list.Count > MaxSightingsPerSeat)
                {
                    list.RemoveAt(0);
                }
            }

            if (view.Meeting != null)
            {
                foreach (ClueInfo clue in view.Meeting.Clues)
                {
                    if (clue.Kind == PileKind.Body && clue.Victim != null)
                    {
                        _out.Add(clue.Victim.Value);
                    }
                }
            }

            if (seenByMe.Count < _witnessed.Count)
            {
                _witnessed.Clear();
            }

            for (int i = _witnessed.Count; i < seenByMe.Count; i++)
            {
                WitnessRecord r = seenByMe[i];
                _witnessed.Add(r);
                if (r.Act == ActKind.Hit && r.Victim != null)
                {
                    // The victim is immune for VictimImmunityS of play time after any polluter's hit.
                    _hitAtPlay[r.Victim.Value] = _playTime - (matchTime - r.At);
                }
            }
        }

        /// <summary>
        /// Tracks who stands at a dirty spot. A player that leaves a spot still dirty after a clean time is
        /// a fake cleaner; one that stays until the spot turns clean is honest (the tracker is dropped).
        /// </summary>
        private void WatchSpots(SeatView view, float matchTime)
        {
            var present = new HashSet<byte>();
            foreach (PlayerView p in view.Players)
            {
                if (p.IsOut)
                {
                    _dwell.Remove(p.Seat);
                    continue;
                }

                present.Add(p.Seat);
                if (_fakeCleans.Count > 0 && AtRepairConsole(view, p.Position))
                {
                    // the leaver went to repair the smog or the jam: it left its spot for the alarm, not to fake (only a leave made after
                    // the alarm began: a Trasher cannot wipe an earlier record by jamming and standing at the machine)
                    _fakeCleans.RemoveAll(r => r.Actor == p.Seat && r.At >= _repairAlarmAt && matchTime - r.At <= RepairDropWindowS);
                }

                string? spot = null;
                foreach (SpotView s in view.Spots)
                {
                    Sim.World.ConsoleDef? c = s.State == SpotState.Dirty ? Map.FindConsole(s.Id) : null;
                    if (c != null && Vec2.Distance(p.Position, c.Position) <= c.Range + SpotDwellMargin)
                    {
                        spot = s.Id;
                        break;
                    }
                }

                bool tracked = _dwell.TryGetValue(p.Seat, out KeyValuePair<string, float> d);
                if (spot != null)
                {
                    if (!tracked || d.Key != spot)
                    {
                        _dwell[p.Seat] = new KeyValuePair<string, float>(spot, matchTime);
                    }

                    continue;
                }

                if (!tracked)
                {
                    continue;
                }

                _dwell.Remove(p.Seat);
                bool stillDirty = false;
                foreach (SpotView s in view.Spots)
                {
                    stillDirty |= s.Id == d.Key && s.State == SpotState.Dirty;
                }

                if (stillDirty && matchTime - d.Value >= FakeCleanDwellS)
                {
                    Sim.World.ConsoleDef? c = Map.FindConsole(d.Key);
                    Vec2 at = c != null ? c.Position : p.Position;
                    _fakeCleans.Add(new WitnessRecord(ActKind.Dump, p.Seat, view.Me.Seat, matchTime, Map.ZoneAt(at), at, null, null));
                }
            }

            // a seat that went out of sight loses its tracker
            var gone = new List<byte>();
            foreach (byte seat in _dwell.Keys)
            {
                if (!present.Contains(seat))
                {
                    gone.Add(seat);
                }
            }

            foreach (byte seat in gone)
            {
                _dwell.Remove(seat);
            }
        }

        /// <summary>True when a position is in range of the smog panel while smog is on, or of the machine while it is jammed.</summary>
        private bool AtRepairConsole(SeatView view, Vec2 at)
        {
            bool smog = view.Sabotage != null && view.Sabotage.Kind == SabotageKind.Smog;
            if (!smog && !view.MachineJammed)
            {
                return false;
            }

            foreach (Sim.World.ConsoleDef c in Map.Consoles)
            {
                bool repair = (smog && c.Kind == ConsoleKind.SmogFix) || (view.MachineJammed && c.Kind == ConsoleKind.Machine);
                if (repair && Vec2.Distance(at, c.Position) <= c.Range + SpotDwellMargin)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Seconds since this seat was last in sight; null when never seen.</summary>
        public float? LastSeenAge(byte seat, float now)
        {
            Sighting? s = Last(seat);
            return s == null ? (float?)null : now - s.Value.Time;
        }

        public string? LastSeenZone(byte seat)
        {
            Sighting? s = Last(seat);
            return s?.Zone;
        }

        public Vec2? LastSeenPosition(byte seat)
        {
            Sighting? s = Last(seat);
            return s == null ? (Vec2?)null : s.Value.Position;
        }

        /// <summary>Every seat this bot has ever had in sight.</summary>
        public IEnumerable<byte> SeenSeats => _sightings.Keys;

        /// <summary>Where this bot itself has been (zone and time), oldest first.</summary>
        public IReadOnlyList<Sighting> OwnSightings => _own;

        private void RecordOwn(SeatView view, float matchTime)
        {
            string zone = Map.ZoneAt(view.Me.Position);
            if (_own.Count > 0 && matchTime - _own[_own.Count - 1].Time < MinSightingGapS)
            {
                _own[_own.Count - 1] = new Sighting(matchTime, zone, view.Me.Position);
                return;
            }

            _own.Add(new Sighting(matchTime, zone, view.Me.Position));
            if (_own.Count > MaxSightingsPerSeat)
            {
                _own.RemoveAt(0);
            }
        }

        public IReadOnlyList<Sighting> SightingsOf(byte seat)
        {
            return _sightings.TryGetValue(seat, out List<Sighting>? list) ? list : (IReadOnlyList<Sighting>)Array.Empty<Sighting>();
        }

        /// <summary>True when the bot has learned (from a body clue) that this seat is out.</summary>
        public bool IsKnownOut(byte seat) => _out.Contains(seat);

        public void MarkOut(byte seat)
        {
            _out.Add(seat);
        }

        /// <summary>Records that this bot poisoned a seat (the window runs on play time, so matchTime is only kept for callers).</summary>
        public void NoteOwnHit(byte victim, float matchTime)
        {
            _hitAtPlay[victim] = _playTime;
        }

        /// <summary>Undoes NoteOwnHit when the engine rejected the hit.</summary>
        public void ForgetOwnHit(byte victim)
        {
            _hitAtPlay.Remove(victim);
        }

        /// <summary>
        /// True while the seat is inside the immunity window after a hit this bot made or witnessed
        /// (any polluter's). Counted on play time like the engine's ActiveTime, so a meeting does not run it down.
        /// </summary>
        public bool RecentlyHit(byte seat)
        {
            return _hitAtPlay.TryGetValue(seat, out float t) && _playTime - t < Config.VictimImmunityS;
        }

        /// <summary>Seconds since the last meeting ended (or since play started when none was seen).</summary>
        public float SinceMeeting(float now) => now - (_lastMeetingEnd ?? 0f);

        // ---------- heat: what the table learned about each seat from meetings ----------

        /// <summary>A seat not accused for this many meetings (or never) counts as this many.</summary>
        public const int MeetingsSinceAccusedCap = 5;

        private const float HeatCarry = 0.5f;

        private bool _inMeeting;
        private int _livingNow;

        private struct HeatState
        {
            public float VoteShare;
            public float AccusationShare;
            public float Heat;
            public int SinceAccused;
        }

        /// <summary>Meetings this memory has been told about.</summary>
        public int MeetingsRecorded => _meetingsRecorded;

        /// <summary>Votes the seat got in the last meeting / living voters.</summary>
        public float HeatVotes(byte seat) => _heat.TryGetValue(seat, out HeatState h) ? h.VoteShare : 0f;

        /// <summary>Accusations against the seat in the last meeting / living players.</summary>
        public float HeatAccusations(byte seat) => _heat.TryGetValue(seat, out HeatState h) ? h.AccusationShare : 0f;

        /// <summary>
        /// The chat of the meeting in progress (set by whoever hosts the meeting: the talker's state builder, the
        /// training host). While Observe last saw a meeting, each distinct seat that accused a seat in it adds
        /// 1 / living to that seat's HeatOf, the same share Round 5 records when the meeting ends.
        /// </summary>
        public MeetingLog? Chat { get; set; }

        /// <summary>
        /// Last meeting's vote share plus accusation share plus half of the heat carried from before; inside a
        /// meeting also the chat accusations so far (see Chat). This one value is what the observation and the
        /// meeting talker read.
        /// </summary>
        public float HeatOf(byte seat)
        {
            float heat = _heat.TryGetValue(seat, out HeatState h) ? h.Heat : 0f;
            if (_inMeeting && Chat != null)
            {
                heat += Chat.AccusationsAgainst(seat) / (float)Math.Max(1, _livingNow);
            }

            return heat;
        }

        /// <summary>Meetings since the seat was last accused, capped; a seat never accused is at the cap.</summary>
        public int MeetingsSinceAccused(byte seat) => _heat.TryGetValue(seat, out HeatState h) ? h.SinceAccused : MeetingsSinceAccusedCap;

        public void OnMeetingEnded(MeetingOutcome outcome) => RecordMeeting(outcome);

        /// <summary>Folds one finished meeting into the heat of every seat.</summary>
        public void RecordMeeting(MeetingOutcome outcome)
        {
            if (outcome == null)
            {
                throw new ArgumentNullException(nameof(outcome));
            }

            _meetingsRecorded++;
            _inMeeting = false;
            float voters = Math.Max(1, outcome.Votes.Count);
            var votes = new Dictionary<byte, int>();
            foreach (VoteRecord v in outcome.Votes)
            {
                if (v.Target != null)
                {
                    votes[v.Target.Value] = (votes.TryGetValue(v.Target.Value, out int n) ? n : 0) + 1;
                }
            }

            var accused = new Dictionary<byte, int>();
            foreach (Accusation a in outcome.Accusations)
            {
                accused[a.Target] = (accused.TryGetValue(a.Target, out int n) ? n : 0) + 1;
            }

            var seats = new HashSet<byte>(_heat.Keys);
            seats.UnionWith(votes.Keys);
            seats.UnionWith(accused.Keys);
            foreach (byte seat in seats)
            {
                HeatState old = _heat.TryGetValue(seat, out HeatState h) ? h : new HeatState { SinceAccused = MeetingsSinceAccusedCap };
                var next = new HeatState
                {
                    VoteShare = (votes.TryGetValue(seat, out int vc) ? vc : 0) / voters,
                    AccusationShare = (accused.TryGetValue(seat, out int ac) ? ac : 0) / voters,
                    SinceAccused = accused.ContainsKey(seat) ? 0 : Math.Min(MeetingsSinceAccusedCap, old.SinceAccused + 1),
                };
                next.Heat = next.VoteShare + next.AccusationShare + HeatCarry * old.Heat;
                _heat[seat] = next;
            }
        }

        public void Clear()
        {
            _heat.Clear();
            _inMeeting = false;
            _meetingsRecorded = 0;
            _sightings.Clear();
            _own.Clear();
            _hitAtPlay.Clear();
            _prevMatchTime = null;
            _playTime = 0f;
            HeldFireConsole = null;
            UpgradeOffer = null;
            InfectedCallUsed = false;
            _out.Clear();
            _witnessed.Clear();
            _fakeCleans.Clear();
            _dwell.Clear();
            _prevPhase = null;
            _lastMeetingEnd = null;
        }

        private Sighting? Last(byte seat)
        {
            if (_sightings.TryGetValue(seat, out List<Sighting>? list) && list.Count > 0)
            {
                return list[list.Count - 1];
            }

            return null;
        }
    }
}
