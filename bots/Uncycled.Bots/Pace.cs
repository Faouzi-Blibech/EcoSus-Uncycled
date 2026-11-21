using System;
using Sim.Core;

namespace Uncycled.Bots
{
    /// <summary>Human-paced play (bots/bots.json "pace" block). Every value is a named setting.</summary>
    public sealed class PaceSettings
    {
        /// <summary>A task step takes the task's min_s times a factor drawn per bot per task in [min, max].</summary>
        public float TaskTimeFactorMin { get; set; } = 1.5f;

        public float TaskTimeFactorMax { get; set; } = 2.5f;

        /// <summary>Chance of a short pause before an action, and its length range in seconds.</summary>
        public float PauseChance { get; set; } = 0.3f;

        public float PauseMinS { get; set; } = 0.5f;

        public float PauseMaxS { get; set; } = 2f;

        /// <summary>Chance to head to a wrong zone first before walking to the right task.</summary>
        public float WrongZoneChance { get; set; } = 0.1f;

        /// <summary>Chance to prefer a task in a zone where another player is in sight, or to follow that player.</summary>
        public float GroupChance { get; set; } = 0.3f;
    }

    /// <summary>
    /// The dice of the pace rules, drawn from the brain's own random source so a seed gives the same
    /// pace again. Used by the action executor, so it applies to every brain.
    /// </summary>
    public sealed class PacePolicy
    {
        private readonly IRandom _rng;
        private readonly System.Collections.Generic.Dictionary<int, float> _taskFactor = new System.Collections.Generic.Dictionary<int, float>();

        public PacePolicy(PaceSettings settings, IRandom rng)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public PaceSettings Settings { get; }

        /// <summary>The factor on min_s for this bot and this task: drawn once, then kept.</summary>
        public float TaskTimeFactor(int taskId)
        {
            if (!_taskFactor.TryGetValue(taskId, out float f))
            {
                f = DrawFactor();
                _taskFactor[taskId] = f;
            }

            return f;
        }

        /// <summary>A fresh factor (fake tasks have no task id).</summary>
        public float DrawFactor()
        {
            float lo = Settings.TaskTimeFactorMin;
            float hi = Math.Max(lo, Settings.TaskTimeFactorMax);
            return lo + _rng.NextFloat() * (hi - lo);
        }

        /// <summary>Seconds to wait before the next action; 0 for no pause.</summary>
        public float PauseS()
        {
            if (_rng.NextFloat() >= Settings.PauseChance)
            {
                return 0f;
            }

            float lo = Settings.PauseMinS;
            float hi = Math.Max(lo, Settings.PauseMaxS);
            return lo + _rng.NextFloat() * (hi - lo);
        }

        public bool WrongZone() => _rng.NextFloat() < Settings.WrongZoneChance;

        public bool Group() => _rng.NextFloat() < Settings.GroupChance;

        public int NextInt(int maxExclusive) => _rng.NextInt(maxExclusive);
    }
}
