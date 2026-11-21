using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sim.Core;
using Sim.Match;
using Sim.Uncycled;
using Sim.World;
using Uncycled.Bots;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    /// <summary>The training host plays the v2 policy list: v2 observation, 35-action mask, hints reach the executor, v1 networks refused.</summary>
    public sealed class TrainHostV2Tests
    {
        private const int CleanNearest = 17;
        private const int CleanSecond = 18;

        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));

        private static HostOptions Options(params string[] extra)
        {
            var args = new List<string> { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1" };
            args.AddRange(extra);
            return HostOptions.Parse(args.ToArray());
        }

        private static MatchRunner NewRunner(HostOptions o, int slot = 0)
        {
            ContentBundle content = ContentLoader.Load(o.ContentDir);
            return new MatchRunner(o, slot, content, HostBrains.Scripted(BotsSettings.Load("none"), content));
        }

        private static byte[] Frames(MatchRunner r)
        {
            var buf = new byte[r.AgentCount * r.FrameSize];
            r.WriteFrames(buf);
            return buf;
        }

        [Fact]
        public void Handshake_IsV2()
        {
            MatchRunner r = NewRunner(Options());
            var ms = new MemoryStream();
            MatchRunner.WriteHandshake(ms, r.AgentCount);
            ms.Position = 0;
            Handshake h = PipeProtocol.ReadHandshake(ms);

            Assert.Equal(199, h.ObsSize);
            Assert.Equal(35, h.ActionCount);
            Assert.Equal(SchemaV2.Hash, h.SchemaHash);
            Assert.Equal(r.AgentCount, h.AgentCount);
            Assert.Equal(PipeProtocol.FrameSize(199, 35), r.FrameSize);
            Assert.Equal(199 * 4, r.MaskOffset);
        }

        [Theory]
        [InlineData("citizen")]
        [InlineData("polluter")]
        public void LearnerMask_IsThePolicyMask(string learner)
        {
            MatchRunner r = NewRunner(Options("--learner", learner, "--rw-win", "0"));
            var rng = new XorShiftRandom(21);
            int checkedFrames = 0;
            for (int d = 0; d < 600; d++)
            {
                byte[] f = Frames(r);
                for (int a = 0; a < r.AgentCount; a++)
                {
                    bool[] expected;
                    if (r.AgentCommitted(a))
                    {
                        expected = new bool[PolicyActionsV2.Count];
                        expected[0] = true;
                    }
                    else
                    {
                        expected = PolicyActionsV2.Mask(r.Engine.ViewFor(r.AgentSeat(a)), r.AgentMemory(a), r.Engine.Map);
                    }

                    int maskAt = a * r.FrameSize + r.MaskOffset;
                    for (int i = 0; i < PolicyActionsV2.Count; i++)
                    {
                        Assert.True(expected[i] == (f[maskAt + i] != 0), "decision " + d + " agent " + a + " index " + PolicyActionsV2.Names[i]);
                    }

                    checkedFrames++;
                }

                r.AdvanceRandom(rng);
            }

            Assert.True(checkedFrames > 1000);
        }

        [Theory]
        [InlineData("citizen")]
        [InlineData("polluter")]
        public void RandomPolicyLearner_PlaysAWholeMatch_NoInvalidAction(string learner)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                MatchRunner r = NewRunner(Options("--learner", learner, "--matches", "4"), slot);
                var rng = new XorShiftRandom(31 + (ulong)slot);
                int decisions = 0;
                while (r.MatchesFinished == 0 && decisions < 12000)
                {
                    r.AdvanceRandom(rng);
                    decisions++;
                }

                Assert.True(r.MatchesFinished >= 1, "slot " + slot + ": no match ended in " + decisions + " decisions");
                Assert.Equal(0, r.InvalidActions);
            }
        }

        /// <summary>Spot ids of the dirty spots nearest first by walking distance from the seat (ties by id).</summary>
        private static List<ConsoleDef> DirtyByDistance(MatchRunner r, int agent)
        {
            SeatView view = r.Engine.ViewFor(r.AgentSeat(agent));
            MapDistances dist = MapDistances.For(r.Engine.Map);
            return view.Spots
                .Where(s => s.State == SpotState.Dirty)
                .Select(s => r.Engine.Map.FindConsole(s.Id)!)
                .Select(c => new { c, d = dist.Path(view.Me.Position, c.Position) })
                .Where(x => x.d != MapDistances.Unreachable)
                .OrderBy(x => x.d).ThenBy(x => x.c.Id, StringComparer.Ordinal)
                .Select(x => x.c).ToList();
        }

        [Theory]
        [InlineData(CleanNearest, 0)]
        [InlineData(CleanSecond, 1)]
        public void ChosenHint_ReachesTheExecutor(int index, int rank)
        {
            MatchRunner r = NewRunner(Options("--rw-win", "0"));
            ConsoleDef target = DirtyByDistance(r, 0)[rank];
            ConsoleDef other = DirtyByDistance(r, 0)[1 - rank];
            Assert.NotEqual(target.Id, other.Id);

            // agent 0 asks for the clean-spot index every decision, the others stay: it must walk to the spot the hint names
            bool reached = false;
            for (int d = 0; d < 80 && !reached; d++)
            {
                byte[] f = Frames(r);
                Assert.True(r.AgentCommitted(0) || f[r.MaskOffset + index] != 0, "index " + index + " must be valid at decision " + d);
                var actions = new int[r.AgentCount];
                actions[0] = index;
                r.Advance(actions);
                Vec2 me = r.Engine.ViewFor(r.AgentSeat(0)).Me.Position;
                reached = Vec2.Distance(me, target.Position) <= target.Range;
                Assert.False(Vec2.Distance(me, other.Position) <= other.Range && !reached, "went to the other spot (" + other.Id + ") instead of " + target.Id);
            }

            Assert.Equal(0, r.InvalidActions);
            Assert.True(reached, "agent 0 never reached " + target.Id + " for policy index " + index);
        }

        [Fact]
        public void V1Opponent_IsRefused()
        {
            string tinyV1 = Path.Combine(AppContext.BaseDirectory, "fixtures", "tiny.onnx");
            Assert.True(File.Exists(tinyV1));
            ContentBundle content = ContentLoader.Load(ContentDir);

            ArgumentException mix = Assert.Throws<ArgumentException>(() => OpponentMix.Parse("onnx:" + tinyV1 + "=1", new BotsSettings(), content));
            Assert.Contains("schema hash mismatch", mix.Message);

            HostOptions eval = HostOptions.Parse(new[] { "--eval", "--content", ContentDir, "--seeds", "3-3", "--citizen", "onnx:" + tinyV1 });
            ArgumentException brain = Assert.Throws<ArgumentException>(() => HostBrains.ForEval(eval, BotsSettings.Load("none"), content));
            Assert.Contains("schema hash mismatch", brain.Message);
        }
    }
}
