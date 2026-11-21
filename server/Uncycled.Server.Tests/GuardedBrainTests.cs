using System;
using System.Collections.Generic;
using System.Threading;
using Sim.Match;
using Uncycled.Bots;
using Uncycled.Server.Bots;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>The slow-brain rule on its own: strikes are counted per tick, over a window, and a stall of the whole tick is nobody's fault.</summary>
    public class GuardedBrainTests
    {
        private const int Bots = 7;

        private sealed class Toggle : IBotBrain
        {
            public bool Slow;

            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
                if (Slow)
                {
                    Thread.Sleep(5);
                }
            }
        }

        private sealed class Seven
        {
            public readonly Toggle[] Toggles = new Toggle[Bots];
            public readonly GuardedBrain[] Brains = new GuardedBrain[Bots];
            public readonly CountingLog Log = new CountingLog();

            public Seven()
            {
                for (int i = 0; i < Bots; i++)
                {
                    Toggles[i] = new Toggle();
                    Brains[i] = new GuardedBrain(Toggles[i], () => new Toggle(), Log, "seat " + i, 1.0, 3);
                }
            }

            /// <summary>One room tick: the seats in `slow` overrun the budget, the others do not.</summary>
            public void Tick(params int[] slow)
            {
                for (int i = 0; i < Bots; i++)
                {
                    Toggles[i].Slow = Array.IndexOf(slow, i) >= 0;
                    Brains[i].Think(null!, (byte)i, 0.05f, _ => { });
                }

                GuardedBrain.SettleTick(Brains);
            }

            public int Replaced
            {
                get
                {
                    int n = 0;
                    foreach (GuardedBrain b in Brains)
                    {
                        if (b.Stage != 0)
                        {
                            n++;
                        }
                    }

                    return n;
                }
            }
        }

        [Fact]
        public void A_brain_is_replaced_on_exactly_the_third_slow_tick()
        {
            var s = new Seven();
            s.Tick(2);
            s.Tick(2);
            Assert.Equal(0, s.Brains[2].Stage);

            s.Tick(2);

            Assert.Equal(1, s.Brains[2].Stage);
            Assert.Equal(1, s.Replaced);
            Assert.Equal(1, s.Log.Warnings);
        }

        [Fact]
        public void Slow_ticks_spread_over_more_than_one_window_do_not_add_up()
        {
            var s = new Seven();
            s.Tick(2);
            s.Tick(2);
            for (int i = 0; i < 610; i++)
            {
                s.Tick();
            }

            s.Tick(2);

            Assert.Equal(0, s.Replaced);
        }

        [Fact]
        public void A_tick_where_most_bots_overran_is_a_whole_tick_pause_and_nobody_is_struck()
        {
            var s = new Seven();
            for (int i = 0; i < 6; i++)
            {
                s.Tick(0, 1, 2, 3, 4, 5, 6);
            }

            Assert.Equal(0, s.Replaced);
            Assert.Equal(0, s.Log.Warnings);
        }

        [Fact]
        public void Two_slow_brains_among_seven_are_still_replaced_and_the_others_keep_playing()
        {
            var s = new Seven();
            for (int i = 0; i < 3; i++)
            {
                s.Tick(1, 4);
            }

            Assert.Equal(1, s.Brains[1].Stage);
            Assert.Equal(1, s.Brains[4].Stage);
            Assert.Equal(2, s.Replaced);
        }
    }
}
