using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Uncycled.Bots
{
    /// <summary>
    /// Bot settings, read from bots/bots.json (flat keys; a missing file or key gives the default).
    /// Read with a tiny reader so the library needs no JSON package.
    /// </summary>
    public sealed class BotsSettings
    {
        public float DecisionIntervalS { get; set; } = 0.5f;

        public float ReactionMinS { get; set; } = 0.4f;

        public float ReactionMaxS { get; set; } = 1.2f;

        public float Skill { get; set; } = 0.6f;

        /// <summary>Seconds after which a Follow or Avoid action ends by itself.</summary>
        public float FollowCapS { get; set; } = 4f;

        /// <summary>Most Clef-flash meeting calls per UTC day (bots.json meeting.daily_call_cap).</summary>
        public int MeetingDailyCallCap { get; set; } = 5000;

        /// <summary>Seconds a meeting model call may take before the rule path answers (bots.json meeting.timeout_s).</summary>
        public float MeetingTimeoutS { get; set; } = 3f;

        /// <summary>Which meeting decisions the model makes: "none" (default: rules only, the model is off even with keys), "vote" (rules talk, the model votes) or "all" (the model talks and votes).</summary>
        public string MeetingClefFor { get; set; } = "none";

        /// <summary>Chance (0..1) that a Cleaner that sees a Trasher act (re-pollute, machine jam, gas warning) walks to the emergency table and calls a meeting; one roll per act (bots.json emergency_on_witness_chance).</summary>
        public float EmergencyOnWitnessChance { get; set; } = 0.7f;

        /// <summary>Human pace (bots.json "pace" block).</summary>
        public PaceSettings Pace { get; } = new PaceSettings();

        /// <summary>"scripted" or "neural".</summary>
        public string Brain { get; set; } = "scripted";

        public static BotsSettings Load(string path)
        {
            var s = new BotsSettings();
            string text;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return s;
                }

                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return s;
            }

            s.DecisionIntervalS = Number(text, "decision_interval_s", s.DecisionIntervalS);
            s.ReactionMinS = Number(text, "reaction_min_s", s.ReactionMinS);
            s.ReactionMaxS = Number(text, "reaction_max_s", s.ReactionMaxS);
            s.Skill = Number(text, "skill", s.Skill);
            s.FollowCapS = Number(text, "follow_cap_s", s.FollowCapS);
            s.EmergencyOnWitnessChance = Number(text, "emergency_on_witness_chance", s.EmergencyOnWitnessChance);
            s.MeetingDailyCallCap = (int)Number(text, "daily_call_cap", s.MeetingDailyCallCap);
            s.MeetingTimeoutS = Number(text, "timeout_s", s.MeetingTimeoutS);
            Match clefFor = Regex.Match(text, "\"clef_for\"\\s*:\\s*\"([^\"]*)\"");
            if (clefFor.Success && clefFor.Groups[1].Value.Length > 0)
            {
                s.MeetingClefFor = clefFor.Groups[1].Value.ToLowerInvariant();
            }

            s.Pace.TaskTimeFactorMin = Number(text, "task_time_factor_min", s.Pace.TaskTimeFactorMin);
            s.Pace.TaskTimeFactorMax = Number(text, "task_time_factor_max", s.Pace.TaskTimeFactorMax);
            s.Pace.PauseChance = Number(text, "pause_chance", s.Pace.PauseChance);
            s.Pace.PauseMinS = Number(text, "pause_min_s", s.Pace.PauseMinS);
            s.Pace.PauseMaxS = Number(text, "pause_max_s", s.Pace.PauseMaxS);
            s.Pace.WrongZoneChance = Number(text, "wrong_zone_chance", s.Pace.WrongZoneChance);
            s.Pace.GroupChance = Number(text, "group_chance", s.Pace.GroupChance);
            s.Pace.TaskFailChance = Number(text, "task_fail_chance", s.Pace.TaskFailChance);
            Match brain = Regex.Match(text, "\"brain\"\\s*:\\s*\"([^\"]*)\"");
            if (brain.Success && brain.Groups[1].Value.Length > 0)
            {
                s.Brain = brain.Groups[1].Value;
            }

            return s;
        }

        private static float Number(string text, string key, float fallback)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*(-?[0-9]+(\\.[0-9]+)?([eE][-+]?[0-9]+)?)");
            if (m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                return v;
            }

            return fallback;
        }
    }
}
