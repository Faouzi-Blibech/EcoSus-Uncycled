using System.Collections.Generic;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Xunit;

namespace Sim.Tests
{
    public class RulesTests
    {
        [Theory]
        [InlineData(0, 34, false)]
        [InlineData(34, 68, false)]
        [InlineData(68, 100, true)]
        public void Poison_adds_34_and_goes_out_at_100(int start, int expected, bool expectOut)
        {
            var victim = new PlayerState(1, "v", 1) { Poison = start };
            bool isOut = PoisonRule.Apply(victim, 34, 25f, 100f);
            Assert.Equal(expected, victim.Poison);
            Assert.Equal(expectOut, isOut);
            Assert.Equal(125f, victim.ImmuneUntil);
        }

        [Theory]
        [InlineData(100f, true)]
        [InlineData(124.9f, true)]
        [InlineData(125f, false)]
        [InlineData(200f, false)]
        public void Immunity_window_lasts_25_seconds(float now, bool immune)
        {
            var victim = new PlayerState(1, "v", 1);
            PoisonRule.Apply(victim, 34, 25f, 100f);
            Assert.Equal(immune, PoisonRule.IsImmune(victim, now));
        }

        [Theory]
        [InlineData(new float[] { 5f, 5f }, 10f)]
        [InlineData(new float[] { -2.5f }, 0f)]
        [InlineData(new float[] { 90f, 20f }, 100f)]
        [InlineData(new float[] { 100f, -2.5f }, 97.5f)]
        [InlineData(new float[] { 150f, -200f, 1f }, 1f)]
        public void Meter_clamps_between_0_and_100(float[] adds, float expected)
        {
            var meter = new PollutionMeter();
            foreach (float a in adds)
            {
                meter.Add(a);
            }

            Assert.Equal(expected, meter.Value, 3);
            Assert.Equal(expected >= 100f, meter.IsFull);
        }

        // v2 win order (spec section 7): polluters out, meter full, deadline, parity. No task or pollution rule.
        [Theory]
        [InlineData(0, 4, 50f, false, "citizens", "polluters_voted_out")]
        [InlineData(0, 4, 100f, true, "citizens", "polluters_voted_out")]
        [InlineData(1, 4, 100f, false, "citizens", "meter_full")]
        [InlineData(1, 4, 99f, true, "polluters", "deadline")]
        [InlineData(1, 4, 100f, true, "citizens", "meter_full")]
        [InlineData(2, 2, 50f, false, "polluters", "parity")]
        [InlineData(1, 4, 99f, false, null, null)]
        public void Win_order(int polluters, int citizens, float meter, bool deadline, string? winner, string? reason)
        {
            WinResult? r = WinEvaluator.Evaluate(polluters, citizens, meter, deadline);
            if (winner == null)
            {
                Assert.Null(r);
                return;
            }

            Assert.NotNull(r);
            Assert.Equal(winner == "citizens" ? Winner.Citizens : Winner.Polluters, r!.Value.Winner);
            Assert.Equal(reason, WireNames.Of(r.Value.Reason));
        }

        // Votes: seat number, "s" = skip, "m" = missed (timed out).
        [Theory]
        [InlineData("1,1,2", true, 1, false)]          // plurality
        [InlineData("1,1,2,2", true, -1, true)]        // tie between seats
        [InlineData("1,s,s", true, -1, false)]         // skip majority
        [InlineData("1,1,s,s", true, -1, true)]        // seat ties skip
        [InlineData("1,1,m,m,m", true, -1, false)]     // missed counts as skip
        [InlineData("1,1,m,m,m", false, 1, false)]     // missed ignored when option off
        [InlineData("m,m", true, -1, false)]           // nobody voted
        [InlineData("3", true, 3, false)]
        public void Vote_tally(string votes, bool missedIsSkip, int expectedEjected, bool expectedTie)
        {
            var records = new List<VoteRecord>();
            byte voter = 0;
            foreach (string v in votes.Split(','))
            {
                if (v == "s")
                {
                    records.Add(new VoteRecord(voter, null, false));
                }
                else if (v == "m")
                {
                    records.Add(new VoteRecord(voter, null, true));
                }
                else
                {
                    records.Add(new VoteRecord(voter, byte.Parse(v), false));
                }

                voter++;
            }

            MeetingResult r = VoteTally.Tally(records, missedIsSkip);
            if (expectedEjected < 0)
            {
                Assert.Null(r.Ejected);
            }
            else
            {
                Assert.Equal((byte?)expectedEjected, r.Ejected);
            }

            Assert.Equal(expectedTie, r.Tie);
        }

        [Fact]
        public void Task_reassignment_keeps_the_total_and_picks_fewest_remaining()
        {
            var citizens = new List<byte> { 0, 1, 2, 3, 4, 5 };
            TaskLedger ledger = TaskLedger.Assign(citizens, TestWorld.TaskTypes(), 1, 2, 1, new XorShiftRandom(7));
            Assert.Equal(6 * 5, ledger.TotalSteps); // common 1 + short 1 + short 1 + long 2
            Assert.Equal(4, ledger.OwnedBy(0).Count);

            TaskState first = ledger.OwnedBy(0)[0];
            ledger.CompleteStep(first);
            int doneBefore = ledger.DoneSteps;

            List<TaskState> moved = ledger.Reassign(0, new List<byte> { 1, 2, 3, 4, 5 });
            Assert.Equal(3, moved.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, new[] { moved[0].Owner, moved[1].Owner, moved[2].Owner });
            Assert.Equal(30, ledger.TotalSteps);
            Assert.Equal(doneBefore, ledger.DoneSteps);
            Assert.Equal(0, ledger.RemainingFor(0));
            Assert.Single(ledger.OwnedBy(0)); // the done task stays with seat 0 and still counts
        }

        [Fact]
        public void Role_assignment_gives_distinct_signatures()
        {
            for (ulong seed = 1; seed < 30; seed++)
            {
                var players = new List<PlayerState>();
                for (byte i = 0; i < 8; i++)
                {
                    players.Add(new PlayerState(i, "p", i));
                }

                RoleAssigner.Assign(players, 2, new XorShiftRandom(seed));
                var sigs = new List<WasteType>();
                foreach (PlayerState p in players)
                {
                    if (p.IsPolluter)
                    {
                        Assert.NotNull(p.Signature);
                        Assert.DoesNotContain(p.Signature!.Value, sigs);
                        sigs.Add(p.Signature.Value);
                    }
                    else
                    {
                        Assert.Null(p.Signature);
                    }
                }

                Assert.Equal(2, sigs.Count);
            }
        }

        [Fact]
        public void RoleAssigner_ForcedPolluter_IsPolluter()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var players = new List<PlayerState>();
                for (byte i = 0; i < 8; i++)
                {
                    players.Add(new PlayerState(i, "p", i));
                }

                byte forced = (byte)(seed % 8);
                RoleAssigner.Assign(players, 2, new XorShiftRandom(seed), forcedPolluter: forced);
                Assert.True(players[forced].IsPolluter, "seed " + seed);
                Assert.Equal(2, players.FindAll(p => p.IsPolluter).Count);
            }
        }

        [Fact]
        public void RoleAssigner_ForcedCitizen_IsCitizen()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var players = new List<PlayerState>();
                for (byte i = 0; i < 8; i++)
                {
                    players.Add(new PlayerState(i, "p", i));
                }

                byte forced = (byte)(seed % 8);
                RoleAssigner.Assign(players, 2, new XorShiftRandom(seed), forcedCitizen: forced);
                Assert.False(players[forced].IsPolluter, "seed " + seed);
                Assert.Equal(2, players.FindAll(p => p.IsPolluter).Count);
            }
        }

        [Fact]
        public void XorShift_is_deterministic()
        {
            var a = new XorShiftRandom(123);
            var b = new XorShiftRandom(123);
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(a.NextInt(1000), b.NextInt(1000));
            }

            float f = new XorShiftRandom(5).NextFloat();
            Assert.InRange(f, 0f, 0.99999f);
        }
    }
}
