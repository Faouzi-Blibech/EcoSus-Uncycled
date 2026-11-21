using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;

namespace Uncycled.Bots
{
    /// <summary>
    /// Scripted Trasher (v2): the rules live in PolluterTactics (re-pollute unseen, ability, sabotage, fake
    /// cleans near Cleaners, careful when the table is suspicious). This class plugs them into the scripted
    /// brain's plumbing and hands the executor its choices (spot, ability, room).
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

        internal override void Prepare(BotAction action, SeatView view)
        {
            switch (action)
            {
                case BotAction.Repollute:
                    Exec.SpotHint = _tactics.ChooseSpot(view, Mem, SpotState.Clean);
                    break;
                case BotAction.PlantVirus:
                    Exec.SpotHint = _tactics.ChooseSpot(view, Mem, SpotState.Dirty);
                    break;
                case BotAction.PickAbility:
                    Exec.AbilityChoice = _tactics.ChooseAbility();
                    break;
                case BotAction.GasRoom:
                    Exec.RoomHint = PolluterTactics.ChooseRoom(view, Mem.Map);
                    break;
            }

            // someone watching: wait a moment and act anyway; a careful Trasher gives the act up instead
            bool careful = PolluterTactics.IsCareful(Mem, view);
            Exec.ActPatienceS = careful ? PolluterTactics.CarefulActS : PolluterTactics.ImpatientActS;
            Exec.AbortWhenWatched = careful;
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
