using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Xunit;
using Xunit.Abstractions;

namespace Uncycled.Bots.Tests
{
    public class ObservationTests
    {
        private readonly ITestOutputHelper _out;

        public ObservationTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static float[] ObservationFor(TestMatch m, byte seat)
        {
            var mem = new BotMemory(m.Map, m.Config);
            SeatView view = m.Engine.ViewFor(seat);
            mem.Observe(view, m.Engine.MatchTime, m.Engine.Witnesses.SeenBy(seat));
            var into = new float[ObservationBuilder.Size];
            ObservationBuilder.Build(view, mem, m.Map, m.Config, m.Engine.MatchTime, into);
            return into;
        }

        /// <summary>Walks a seat to a goal with the navigator alone (no executor, no memory).</summary>
        private static void WalkTo(TestMatch m, byte seat, string consoleId, float seconds)
        {
            var nav = new Navigator(m.Map.Walk, m.Config.SpeedTilesPerS);
            var sender = new MoveSender();
            Assert.True(nav.SetGoal(m.Engine.ViewFor(seat).Me.Position, m.Map.FindConsole(consoleId)!.Position, m.Engine.MatchTime));
            int steps = (int)Math.Round(seconds / TestMatch.Dt);
            for (int i = 0; i < steps; i++)
            {
                Vec2? next = nav.Next(m.Engine.ViewFor(seat).Me.Position, TestMatch.Dt, m.Engine.MatchTime);
                if (next != null)
                {
                    m.Engine.Apply(seat, sender.Next(next.Value));
                }

                m.Tick();
            }
        }

        [Fact]
        public void Observation_SizeAndHashStable()
        {
            TestMatch m = TestMatch.Create();
            m.TickFor(3f);

            Assert.Equal(ObservationBuilder.FieldNames.Count, ObservationBuilder.Size);
            Assert.Equal(ObservationBuilder.Size, ObservationBuilder.FieldNames.Distinct().Count());
            string hash = ObservationBuilder.SchemaHash;
            _out.WriteLine("ObservationBuilder.Size=" + ObservationBuilder.Size + " SchemaHash=" + hash);
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[0-9a-f]{64}$", hash);
            Assert.Equal(hash, ObservationBuilder.SchemaHash);

            for (byte seat = 0; seat < 8; seat++)
            {
                float[] obs = ObservationFor(m, seat);
                for (int i = 0; i < obs.Length; i++)
                {
                    Assert.True(obs[i] >= -1f && obs[i] <= 1f, ObservationBuilder.FieldNames[i] + " = " + obs[i]);
                }
            }
        }

        [Fact]
        public void Observation_HidesUnseenPlayer()
        {
            // Seat 7 walks to opposite ends of the map in the two runs and ends out of seat 0's sight.
            TestMatch west = TestMatch.Create(seed: 5);
            TestMatch east = TestMatch.Create(seed: 5);
            WalkTo(west, 7, "fire_a", 14f);
            WalkTo(east, 7, "fire_b", 14f);

            Vec2 westPos = west.Engine.ViewFor(7).Me.Position;
            Vec2 eastPos = east.Engine.ViewFor(7).Me.Position;
            Assert.True(Vec2.Distance(westPos, eastPos) > 25f);
            Assert.DoesNotContain(west.Engine.ViewFor(0).Players, p => p.Seat == 7);
            Assert.DoesNotContain(east.Engine.ViewFor(0).Players, p => p.Seat == 7);

            float[] a = ObservationFor(west, 0);
            float[] b = ObservationFor(east, 0);

            Assert.Equal(a, b);
        }
    }
}
