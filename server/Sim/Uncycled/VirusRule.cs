using System;

namespace Sim.Uncycled
{
    /// <summary>The infection countdown of the virus (spec section 6): it gets deadlier the longer the virus sat planted.</summary>
    public static class VirusRule
    {
        /// <summary>Seconds to live: lerp(startS, fatalS, clamp(age / growS, 0, 1)). Age is active time since planting.</summary>
        public static float Countdown(float age, float startS, float fatalS, float growS)
        {
            if (growS <= 0f)
            {
                return fatalS;
            }

            float t = Math.Max(0f, Math.Min(1f, age / growS));
            return startS + (fatalS - startS) * t;
        }
    }
}
