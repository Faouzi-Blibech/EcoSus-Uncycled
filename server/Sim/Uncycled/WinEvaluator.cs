using Sim.Core;

namespace Sim.Uncycled
{
    public readonly struct WinResult
    {
        public WinResult(Winner winner, GameOverReason reason)
        {
            Winner = winner;
            Reason = reason;
        }

        public Winner Winner { get; }

        public GameOverReason Reason { get; }
    }

    /// <summary>Win order of 04 section 9. Returns null to continue.</summary>
    public static class WinEvaluator
    {
        public static WinResult? Evaluate(
            int activePolluters,
            int activeCitizens,
            float meter,
            bool deadlineReached,
            bool allTaskStepsDone)
        {
            if (activePolluters == 0)
            {
                return new WinResult(Winner.Citizens, GameOverReason.PollutersVotedOut);
            }

            if (meter >= PollutionMeter.Max)
            {
                return new WinResult(Winner.Polluters, GameOverReason.PollutionFull);
            }

            if (deadlineReached)
            {
                return new WinResult(Winner.Polluters, GameOverReason.Deadline);
            }

            if (activePolluters >= activeCitizens)
            {
                return new WinResult(Winner.Polluters, GameOverReason.Parity);
            }

            if (allTaskStepsDone)
            {
                return new WinResult(Winner.Citizens, GameOverReason.TasksDone);
            }

            return null;
        }
    }
}
