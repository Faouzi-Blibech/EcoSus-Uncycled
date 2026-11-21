using System.Collections.Generic;
using Sim.Core;

namespace Sim.Match
{
    /// <summary>
    /// Every gameplay number. Defaults equal content/tuning.json (07 section 2.1) and 04.
    /// Values marked "not in tuning.json" come from 04 or are engine choices; the server may add
    /// them to tuning.json later. Times in seconds, distances in tiles, meter in percent.
    /// </summary>
    public sealed class MatchConfig
    {
        // seats
        public int Seats { get; set; } = 8;
        public int Polluters { get; set; } = 2;

        /// <summary>Room sizes the host may pick (rooms.sizes).</summary>
        public int[] RoomSizes { get; set; } = { 6, 8, 10 };

        /// <summary>Size of a new room (rooms.default_size).</summary>
        public int DefaultSize { get; set; } = 8;

        /// <summary>Trashers (Polluters) per room size (rooms.trashers); a size missing here falls back to Polluters.</summary>
        public Dictionary<int, int> RoomTrashers { get; set; } = new Dictionary<int, int> { { 6, 1 }, { 8, 2 }, { 10, 2 } };

        /// <summary>Trashers in a room of this size.</summary>
        public int TrashersFor(int size) => RoomTrashers.TryGetValue(size, out int n) ? n : Polluters;

        /// <summary>A copy of this config for one match of the given room size: Seats and Polluters follow the size.</summary>
        public MatchConfig ForSize(int size)
        {
            var c = (MatchConfig)MemberwiseClone();
            c.Seats = size;
            c.Polluters = TrashersFor(size);
            return c;
        }

        // movement
        public float SpeedTilesPerS { get; set; } = 2.5f;

        /// <summary>Move budget refill = speed x this factor per second (08 section 2; not in tuning.json).</summary>
        public float MoveBudgetFactor { get; set; } = 1.2f;

        /// <summary>Move budget cap in tiles (08 section 2; not in tuning.json).</summary>
        public float MoveBudgetCap { get; set; } = 1.5f;

        // vision
        public float VisionCitizen { get; set; } = 4.0f;
        public float VisionPolluter { get; set; } = 5.0f;
        public float SmogMin { get; set; } = 1.0f;

        /// <summary>A citizen inside a dark room sees this fraction of the normal vision (vision.dark_mult).</summary>
        public float DarkMult { get; set; } = 0.5f;

        /// <summary>Seconds for citizen vision to ease to SmogMin (04 section 8; not in tuning.json).</summary>
        public float SmogEaseS { get; set; } = 2.0f;

        /// <summary>Seconds a citizen must stay at the smog panel (sabotage.smog_fix_s).</summary>
        public float SmogFixS { get; set; } = 3.0f;

        public float LostSightGraceS { get; set; } = 0.3f;

        // poison
        public int PoisonPerHit { get; set; } = 34;
        public float HitRange { get; set; } = 1.0f;
        public float HitCooldownS { get; set; } = 25f;

        /// <summary>First hit and first dump are possible this long after Playing starts.</summary>
        public float StartCooldownS { get; set; } = 10f;

        public float VictimImmunityS { get; set; } = 25f;

        // dump and clean
        public float DumpCooldownS { get; set; } = 40f;
        public float DumpMeterAdd { get; set; } = 5f;
        public float CleanMeterSub { get; set; } = 2.5f;
        public float CleanS { get; set; } = 3f;

        /// <summary>Clean range in tiles (dump.clean_range).</summary>
        public float CleanRange { get; set; } = 1.0f;

        // meter
        public float MeterPassivePerS { get; set; } = 0.08f;
        public float FireExpiredAdd { get; set; } = 20f;

        // match
        public float DeadlineS { get; set; } = 600f;

        /// <summary>The room goes back to the lobby this long after game_over.</summary>
        public float ReturnToLobbyS { get; set; } = 15f;

        /// <summary>Clock the deadline runs on. ActiveTime: meetings and ejections do not count, so stalling cannot win.</summary>
        public DeadlineClock DeadlineClock { get; set; } = DeadlineClock.ActiveTime;

        /// <summary>Role reveal (04 section 1; not in tuning.json).</summary>
        public float IntroS { get; set; } = 5f;

        /// <summary>Ejection screen (04 section 1; not in tuning.json).</summary>
        public float EjectionS { get; set; } = 4f;

        // report
        /// <summary>Report range in tiles (report_range).</summary>
        public float ReportRange { get; set; } = 3f;

        // meeting
        public float DiscussionS { get; set; } = 60f;
        public float VotingS { get; set; } = 30f;
        public float ResultsS { get; set; } = 5f;
        public int EmergenciesPerMatch { get; set; } = 2;
        public float EmergencyMinSinceMeetingS { get; set; } = 15f;
        public bool MissedVoteIsSkip { get; set; } = true;

        /// <summary>Ejection reveals whether the seat was a polluter (04 section 10).</summary>
        public bool ConfirmEject { get; set; } = true;

        /// <summary>Chat phrases per seat per meeting (tuning.json meeting.phrases_per_meeting).</summary>
        public int PhrasesPerMeeting { get; set; } = 5;

        /// <summary>Longest typed chat line after normalisation, in Unicode code points (as a Godot LineEdit counts) (over-length is rejected).</summary>
        public int TextMaxChars { get; set; } = 120;

        /// <summary>Typed chat lines per seat per meeting (separate from the phrases).</summary>
        public int TextPerMeeting { get; set; } = 5;

        /// <summary>Minimum seconds between two chat lines of one seat (typed, phrase or ghost).</summary>
        public float ChatMinGapS { get; set; } = 2f;

        /// <summary>Ghost chat lines kept for the welcome of out seats (oldest dropped first; tuning.json meeting.ghost_chat_keep).</summary>
        public int GhostChatKeep { get; set; } = 100;

        /// <summary>Most ghost lines one welcome carries, the newest kept (tuning.json meeting.ghost_welcome_max_lines).</summary>
        public int GhostWelcomeMaxLines { get; set; } = 50;

        /// <summary>Most bytes of encoded JSON the ghost lines of one welcome may take (tuning.json meeting.ghost_welcome_max_bytes).</summary>
        public int GhostWelcomeMaxBytes { get; set; } = 16384;

        /// <summary>Max arguments in one chat phrase (engine choice; not in tuning.json).</summary>
        public int PhraseMaxArgs { get; set; } = 3;

        // sabotage
        public float SabotageSharedCooldownS { get; set; } = 30f;

        /// <summary>The first sabotage is possible this long after Playing starts (rejected as on_cooldown before).</summary>
        public float SabotageStartCooldownS { get; set; } = 10f;
        public float FireCountdownS { get; set; } = 45f;

        // spots and bags (v2; tuning.json spots.* and bags.*)
        public int BagCap { get; set; } = 1;

        public int BackpackCap { get; set; } = 3;

        /// <summary>Clean time multiplier with the Cleaning Tool (spots.tool_time_mult).</summary>
        public float ToolTimeMult { get; set; } = 0.5f;

        /// <summary>Meter added per delivered bag (spots.bag_meter_add).</summary>
        public float BagMeterAdd { get; set; } = 10f;

        /// <summary>Meter removed when a Trasher re-pollutes a spot (spots.repollute_meter_sub).</summary>
        public float RepolluteMeterSub { get; set; } = 10f;

        // tasks per citizen
        public int TasksCommon { get; set; } = 1;
        public int TasksShort { get; set; } = 2;
        public int TasksLong { get; set; } = 1;
    }
}
