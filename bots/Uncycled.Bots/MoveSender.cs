using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots
{
    /// <summary>Makes move commands with a strictly increasing sequence number (1, 2, 3...).</summary>
    public sealed class MoveSender
    {
        private long _seq;

        /// <param name="firstSeq">First sequence number; a brain that takes over a seat mid-match may start higher.</param>
        public MoveSender(long firstSeq = 1)
        {
            _seq = firstSeq - 1;
        }

        public MoveCommand Next(Vec2 position)
        {
            _seq++;
            return new MoveCommand(position, _seq);
        }
    }
}
