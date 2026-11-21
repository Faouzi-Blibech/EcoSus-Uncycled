using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Uncycled;

namespace Uncycled.Bots
{
    public static class BrainFactory
    {
        /// <summary>The scripted brain for a role (the floor: always available, no model needed).</summary>
        public static IBotBrain Scripted(Role role, BotsSettings s, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null)
        {
            switch (role)
            {
                case Role.Polluter:
                    return new ScriptedPolluter(s, rng, taskTypes);
                case Role.Citizen:
                    return new ScriptedCitizen(s, rng, taskTypes);
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }
}
