using System;
using System.Collections.Generic;
using Sim.Match;

namespace Uncycled.Bots.Tests
{
    /// <summary>Walks several seats at once with the executor, one engine tick per step.</summary>
    internal static class Crowd
    {
        public static void Do(TestMatch m, IReadOnlyList<byte> seats, BotAction action, float seconds)
        {
            var execs = new List<ActionExecutor>();
            foreach (byte seat in seats)
            {
                var exec = new ActionExecutor(new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS), new MoveSender())
                {
                    Memory = new BotMemory(m.Map, m.Config),
                    TaskTypes = m.Content.Tasks,
                };
                exec.Begin(action, m.Engine.ViewFor(seat), m.Map, m.Engine.MatchTime);
                execs.Add(exec);
            }

            int steps = (int)Math.Round(seconds / TestMatch.Dt);
            for (int i = 0; i < steps; i++)
            {
                for (int k = 0; k < seats.Count; k++)
                {
                    byte seat = seats[k];
                    SeatView view = m.Engine.ViewFor(seat);
                    execs[k].Step(m.Engine, seat, view, TestMatch.Dt, m.Engine.MatchTime, c => m.Engine.Apply(seat, c));
                }

                m.Tick();
            }
        }
    }
}
