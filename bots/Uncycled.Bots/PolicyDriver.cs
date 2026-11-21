using System;
using Sim.Match;
using Sim.World;

namespace Uncycled.Bots
{
    /// <summary>
    /// Turns the network's policy index into an executor decision, one driver per seat (the training host and the
    /// live NeuralBrain share it). Choosing the same index again while the executor is still running the action that
    /// index started keeps the running target: rank-relative hints ("second nearest spot", "nearest zone") would
    /// otherwise be re-resolved every decision, change as the bot walks and make the executor turn around. A
    /// different index, or an executor that is no longer busy, resolves afresh.
    /// </summary>
    public sealed class PolicyDriver
    {
        private int _index = -1;
        private PolicyChoice _choice;

        /// <summary>The choice the executor is running (or was last started with).</summary>
        public PolicyChoice Choice => _choice;

        /// <summary>Call at every decision with the index the network picked (it must be allowed by PolicyActionsV2.Mask).</summary>
        public PolicyChoice Begin(int index, SeatView view, BotMemory mem, MapData map, ActionExecutor exec, float now)
        {
            if (exec == null)
            {
                throw new ArgumentNullException(nameof(exec));
            }

            if (index == _index && exec.Busy && exec.Current == _choice.Action)
            {
                // same pick, still running it: clear the one-shot hints so the running target stays, and let Begin continue it
                new PolicyChoice(_choice.Action).ApplyTo(exec);
                exec.Begin(_choice.Action, view, map, now);
                return _choice;
            }

            _choice = PolicyActionsV2.Resolve(index, view, mem, map);
            _index = index;
            _choice.ApplyTo(exec);
            exec.Begin(_choice.Action, view, map, now);
            return _choice;
        }
    }
}
