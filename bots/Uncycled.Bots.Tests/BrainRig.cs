using System;
using System.Collections.Generic;
using Sim.Core;
using Sim.Match;

namespace Uncycled.Bots.Tests
{
    /// <summary>One sent command: who, when, in which phase, and what the engine said.</summary>
    internal sealed class SentCommand
    {
        public SentCommand(byte seat, float matchTime, MatchPhase phase, ICommand command, CommandResult result)
        {
            Seat = seat;
            MatchTime = matchTime;
            Phase = phase;
            Command = command;
            Result = result;
        }

        public byte Seat { get; }

        public float MatchTime { get; }

        public MatchPhase Phase { get; }

        public ICommand Command { get; }

        public CommandResult Result { get; }
    }

    /// <summary>Eight scripted brains (one per seat, by its real role) playing a real match.</summary>
    internal sealed class BrainRig
    {
        private readonly TestMatch _match;
        private readonly Action<BotsSettings>? _tweak;

        public BrainRig(TestMatch match, ulong seed, Action<BotsSettings>? tweak = null)
        {
            _match = match;
            _tweak = tweak;
            Brains = new IBotBrain[match.Config.Seats];
            for (int seat = 0; seat < Brains.Length; seat++)
            {
                Brains[seat] = MakeBrain((byte)seat, seed);
            }
        }

        public IBotBrain[] Brains { get; }

        public List<SentCommand> Sent { get; } = new List<SentCommand>();

        /// <summary>Seats whose brain is not driven (the test drives them by hand).</summary>
        public HashSet<byte> Skip { get; } = new HashSet<byte>();

        public IBotBrain MakeBrain(byte seat, ulong seed)
        {
            // the test peeks at the role only to pick the matching brain, as the host does
            Role role = _match.Engine.Find(seat)!.Role;
            var settings = new BotsSettings();
            _tweak?.Invoke(settings);
            return BrainFactory.Scripted(role, settings, new XorShiftRandom(seed * 1000 + seat + 1));
        }

        /// <summary>Every brain thinks once, then the engine ticks once.</summary>
        public void Step()
        {
            for (int seat = 0; seat < Brains.Length; seat++)
            {
                byte s = (byte)seat;
                if (Skip.Contains(s))
                {
                    continue;
                }

                Brains[seat].Think(_match.Engine, s, TestMatch.Dt, cmd => Record(s, cmd));
            }

            _match.Tick();
        }

        public void Record(byte seat, ICommand cmd)
        {
            MatchPhase phase = _match.Engine.Phase;
            float t = _match.Engine.MatchTime;
            Sent.Add(new SentCommand(seat, t, phase, cmd, _match.Engine.Apply(seat, cmd)));
        }

        public void RunUntil(Func<bool> done, float maxSeconds)
        {
            int max = (int)Math.Round(maxSeconds / TestMatch.Dt);
            for (int i = 0; i < max && !done(); i++)
            {
                Step();
            }
        }
    }
}
