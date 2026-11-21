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
        /// drawn without replacement and handed out in seat order. A forced seat (testing only) is
        /// made a polluter or a citizen; the polluter count stays the same.
        /// </summary>
        public static void Assign(IReadOnlyList<PlayerState> players, int polluterCount, IRandom rng, byte? forcedPolluter = null, byte? forcedCitizen = null)
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
            if (forcedPolluter.HasValue)
            {
                Swap(order, 0, IndexOfSeat(players, order, forcedPolluter.Value));
            }
            else if (forcedCitizen.HasValue)
            {
                int at = IndexOfSeat(players, order, forcedCitizen.Value);
                if (at < polluterCount)
                {
                    Swap(order, at, polluterCount);
                }
            }

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

        private static int IndexOfSeat(IReadOnlyList<PlayerState> players, int[] order, byte seat)
        {
            for (int i = 0; i < order.Length; i++)
            {
                if (players[order[i]].Seat == seat)
                {
                    return i;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(seat), "No player in that seat.");
        }

        private static void Swap(int[] order, int a, int b)
        {
            int t = order[a];
            order[a] = order[b];
            order[b] = t;
        }
    }
}
