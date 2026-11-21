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

    /// <summary>Win order of spec section 7: polluters out, meter full, deadline, parity. Returns null to continue.</summary>
    public static class WinEvaluator
    {
        public static WinResult? Evaluate(
            int activePolluters,
            int activeCitizens,
            float meter,
            bool deadlineReached)
        {
            if (activePolluters == 0)
            {
                return new WinResult(Winner.Citizens, GameOverReason.PollutersVotedOut);
            }

            if (meter >= PollutionMeter.Max)
            {
                return new WinResult(Winner.Citizens, GameOverReason.MeterFull);
            }

            if (deadlineReached)
            {
                return new WinResult(Winner.Polluters, GameOverReason.Deadline);
            }

            if (activePolluters >= activeCitizens)
            {
                return new WinResult(Winner.Polluters, GameOverReason.Parity);
            }

            return null;
        }
    }
}
