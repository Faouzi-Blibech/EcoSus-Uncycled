using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public class DecisionSpreadTests
    {
        [Fact(Skip = "v1 rule, out of play in v2")]
        public void First_decisions_of_the_seats_do_not_all_land_on_the_same_tick()
        {
            // No reaction delay and no pace pause (the human pace waits up to 2 s before an action), so the first
            // command of a seat shows the tick of its first decision.
            // Without a random first offset every seat decides on the very first tick.
            TestMatch m = TestMatch.Create(3);
            var settings = new BotsSettings { ReactionMinS = 0f, ReactionMaxS = 0f, Pace = { PauseChance = 0f } };
            var brains = new IBotBrain[m.Config.Seats];
            for (int seat = 0; seat < brains.Length; seat++)
            {
                brains[seat] = BrainFactory.Scripted(m.Engine.Find((byte)seat)!.Role, settings, new XorShiftRandom((ulong)(seat + 1) * 7919UL));
            }

            var first = new Dictionary<int, float>();
            for (int i = 0; i < 40 && first.Count < brains.Length; i++)
            {
                for (int seat = 0; seat < brains.Length; seat++)
                {
                    int s = seat;
                    brains[seat].Think(m.Engine, (byte)seat, TestMatch.Dt, cmd =>
                    {
                        if (cmd is MoveCommand && !first.ContainsKey(s))
                        {
                            first[s] = m.Engine.MatchTime;
                        }

                        m.Engine.Apply((byte)s, cmd);
                    });
                }

                m.Tick();
            }

            Assert.True(first.Count >= 4, "too few seats moved: " + first.Count);
            Assert.True(first.Values.Distinct().Count() >= 3, "first moves at " + string.Join(",", first.Values));
            Assert.True(first.Values.Max() - first.Values.Min() <= settings.DecisionIntervalS + 0.2f, "first moves at " + string.Join(",", first.Values));
        }
    }
}
