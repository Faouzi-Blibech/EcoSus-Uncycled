using System;
using Sim.Match;

namespace Uncycled.Bots
{
    /// <summary>
    /// One bot seat's brain. Same call as the room's bot hook: it reads only
    /// engine.ViewFor(seat), engine.Witnesses.SeenBy(seat) and its own memory, and sends the same
    /// commands a human would.
    /// </summary>
    public interface IBotBrain
    {
        void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send);
    }
}
