using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>Poison applied by a validated hit (04 section 4).</summary>
    public static class PoisonRule
    {
        public const int MaxPoison = 100;

        /// <summary>
        /// Adds poison to the victim and starts its immunity window. Returns true when the victim
        /// reaches 100 (the caller then puts the victim Out).
        /// </summary>
        public static bool Apply(PlayerState victim, int poisonPerHit, float immunitySeconds, float activeTimeNow)
        {
            int level = victim.Poison + poisonPerHit;
            if (level > MaxPoison)
            {
                level = MaxPoison;
            }

            victim.Poison = level;
            victim.ImmuneUntil = activeTimeNow + immunitySeconds;
            return level >= MaxPoison;
        }

        public static bool IsImmune(PlayerState victim, float activeTimeNow) => activeTimeNow < victim.ImmuneUntil;
    }
}
