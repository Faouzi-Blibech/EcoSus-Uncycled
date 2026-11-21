using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;

namespace Uncycled.Bots
{
    /// <summary>
    /// Scripted polluter (team doc 06 section 3): the rules live in PolluterTactics (hit only when the
    /// target is alone, smog before a hit, dump out of sight, fake tasks near citizens, careful when the
    /// table is suspicious). This class plugs them into the scripted brain's plumbing.
    /// Votes by suspicion and never votes a partner.
    /// </summary>
    public sealed class ScriptedPolluter : ScriptedBrain
    {
        private readonly PolluterTactics _tactics;

        public ScriptedPolluter(BotsSettings settings, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null)
            : base(settings, rng, taskTypes)
        {
            _tactics = new PolluterTactics(rng);
        }

        internal override void Started(BotAction action, SeatView view)
        {
            _tactics.Started(action, view);
        }

        internal override Choice Choose(SeatView view, bool[] mask)
        {
            PolluterDecision d = _tactics.Decide(view, mask, Mem, Exec.Busy, Exec.Current);
            if (d.Action == null)
            {
                return Choice.Keep;
            }

            return d.Urgent ? Choice.Now(d.Action.Value) : Choice.Do(d.Action.Value);
        }
    }
}
