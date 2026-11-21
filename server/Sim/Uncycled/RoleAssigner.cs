using System;
using System.Collections.Generic;
using Sim.Core;

namespace Sim.Uncycled
{
    /// <summary>Picks the polluters and gives each a distinct waste-type signature.</summary>
    public static class RoleAssigner
    {
        /// <summary>
        /// Sets Role and Signature on every player. Polluters are drawn uniformly; signatures are
        /// drawn without replacement and handed out in seat order.
        /// </summary>
        public static void Assign(IReadOnlyList<PlayerState> players, int polluterCount, IRandom rng)
        {
            if (polluterCount < 0 || polluterCount >= players.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(polluterCount), "Need at least one citizen.");
            }

            var wasteTypes = (WasteType[])Enum.GetValues(typeof(WasteType));
            if (polluterCount > wasteTypes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(polluterCount), "Not enough waste types for every polluter.");
            }

            var order = new int[players.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            rng.Shuffle(order);
            var isPolluter = new bool[players.Count];
            for (int i = 0; i < polluterCount; i++)
            {
                isPolluter[order[i]] = true;
            }

            rng.Shuffle(wasteTypes);
            int nextWaste = 0;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState p = players[i];
                if (isPolluter[i])
                {
                    p.Role = Role.Polluter;
                    p.Signature = wasteTypes[nextWaste++];
                }
                else
                {
                    p.Role = Role.Citizen;
                    p.Signature = null;
                }
            }
        }
    }
}
