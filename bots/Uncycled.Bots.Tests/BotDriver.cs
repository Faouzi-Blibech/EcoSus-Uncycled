using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Tests
{
    /// <summary>Test rig: one seat driven by the action executor against a real engine.</summary>
    internal sealed class BotDriver
    {
        private readonly TestMatch _match;

        public BotDriver(TestMatch match, byte seat)
        {
            _match = match;
            Seat = seat;
            Nav = new Navigator(match.Map.Walk, match.Config.SpeedTilesPerS);
            Executor = new ActionExecutor(Nav, new MoveSender());
            Memory = new BotMemory(match.Map, match.Config);
            Executor.Memory = Memory;
            Executor.TaskTypes = match.Content.Tasks;
        }

        public byte Seat { get; }

        public Navigator Nav { get; }

        public ActionExecutor Executor { get; }

        public BotMemory Memory { get; }

        public List<KeyValuePair<ICommand, CommandResult>> Sent { get; } = new List<KeyValuePair<ICommand, CommandResult>>();

        public SeatView View => _match.Engine.ViewFor(Seat);

        public void Begin(BotAction action)
        {
            Executor.Begin(action, View, _match.Map, _match.Engine.MatchTime);
        }

        /// <summary>One executor step, then one engine tick.</summary>
        public void Step()
        {
            SeatView view = View;
            Memory.Observe(view, _match.Engine.MatchTime, _match.Engine.Witnesses.SeenBy(Seat));
            Executor.Step(_match.Engine, Seat, view, TestMatch.Dt, _match.Engine.MatchTime, Send);
            _match.Tick();
        }

        /// <summary>Steps until the executor is idle or the time runs out; returns the seconds used.</summary>
        public float RunUntilIdle(float maxSeconds)
        {
            int max = (int)Math.Round(maxSeconds / TestMatch.Dt);
            int i = 0;
            while (i < max && (i == 0 || Executor.Busy))
            {
                Step();
                i++;
            }

            return i * TestMatch.Dt;
        }

        private void Send(ICommand command)
        {
            Sent.Add(new KeyValuePair<ICommand, CommandResult>(command, _match.Engine.Apply(Seat, command)));
        }
    }
}
