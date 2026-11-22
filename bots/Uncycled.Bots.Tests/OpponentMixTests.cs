using System;
using System.Collections.Generic;
using System.IO;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public sealed class OpponentMixTests
    {
        private static readonly string ContentDir = ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
        private static readonly string TinyOnnx = Path.Combine(AppContext.BaseDirectory, "fixtures", "tiny.onnx");

        private static HostOptions Options(params string[] extra)
        {
            var args = new List<string> { "--content", ContentDir, "--matches", "1", "--seed", "7", "--threads", "1" };
            args.AddRange(extra);
            return HostOptions.Parse(args.ToArray());
        }

        private static OpponentMix Mix(string text)
        {
            return OpponentMix.Parse(text, new BotsSettings(), ContentLoader.Load(ContentDir));
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void Parse_ReadsKindsAndWeights()
        {
            OpponentMix mix = Mix("onnx:" + TinyOnnx + "=0.6,scripted=0.3,onnx:" + TinyOnnx + "=0.1");
            Assert.Equal(3, mix.Count);
            Assert.True(mix.IsOnnx(0));
            Assert.False(mix.IsOnnx(1));
            Assert.True(mix.IsOnnx(2));
            Assert.Equal(0.6, mix.Weight(0), 6);
            Assert.Equal(0.3, mix.Weight(1), 6);
        }

        [Fact]
        public void Parse_WindowsPathWithDriveColonAndEqualsInName()
        {
            // The weight is after the LAST '='; the path may hold ':' (drive letter).
            List<OpponentSpec> specs = OpponentMix.ParseSpecs(@"onnx:E:\models\a=b.onnx=2,scripted=1");
            Assert.Equal(@"onnx:E:\models\a=b.onnx", specs[0].Spec);
            Assert.Equal(2.0, specs[0].Weight);
            Assert.Equal("scripted", specs[1].Spec);
        }

        [Theory]
        [InlineData("")]
        [InlineData("scripted")]
        [InlineData("scripted=abc")]
        [InlineData("scripted=-1")]
        [InlineData("scripted=0")]
        [InlineData("scripted=NaN")]
        [InlineData("scripted=0.5,onnx:x.onnx=0")]
        [InlineData("scripted=0.5,")]
        [InlineData("random=1")]
        public void BadMixIsAnArgumentErrorAtParse(string text)
        {
            Assert.Throws<ArgumentException>(() => Options("--opponents", text));
        }

        [Fact]
        public void MissingModelIsAnErrorNotAFallback()
        {
            Assert.Throws<ArgumentException>(() => Mix("onnx:/no/such/model.onnx=0.5,scripted=0.5"));
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void Pick_IsDeterministicPerSeed()
        {
            OpponentMix a = Mix("onnx:" + TinyOnnx + "=0.5,scripted=0.5");
            OpponentMix b = Mix("onnx:" + TinyOnnx + "=0.5,scripted=0.5");
            for (ulong seed = 1; seed < 200; seed++)
            {
                Assert.Equal(a.Pick(seed), b.Pick(seed));
                Assert.Equal(a.Pick(seed), a.Pick(seed));
            }
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void Pick_FollowsTheWeightsOverManyMatches()
        {
            OpponentMix mix = Mix("onnx:" + TinyOnnx + "=0.6,scripted=0.3,onnx:" + TinyOnnx + "=0.1");
            var counts = new int[3];
            const int n = 20000;
            for (ulong seed = 1; seed <= n; seed++)
            {
                counts[mix.Pick(seed)]++;
            }

            Assert.InRange(counts[0] / (double)n, 0.57, 0.63);
            Assert.InRange(counts[1] / (double)n, 0.27, 0.33);
            Assert.InRange(counts[2] / (double)n, 0.08, 0.12);
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void Create_OnnxChoiceRunsNeuralBrainAndScriptedRunsScriptedBrain()
        {
            OpponentMix mix = Mix("onnx:" + TinyOnnx + "=0.5,scripted=0.5");
            Assert.IsType<NeuralBrain>(mix.Create(0, Role.Citizen, new XorShiftRandom(1)));
            Assert.IsAssignableFrom<ScriptedBrain>(mix.Create(1, Role.Citizen, new XorShiftRandom(1)));
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void MatchRunner_OpponentSeatsUseTheMatchChoiceAndCountIt()
        {
            ContentBundle content = ContentLoader.Load(ContentDir);
            var settings = new BotsSettings();
            OpponentMix mix = OpponentMix.Parse("onnx:" + TinyOnnx + "=0.5,scripted=0.5", settings, content);
            HostOptions o = Options("--learner", "polluter");
            var seen = new HashSet<string>();
            long matches = 0;
            for (int slot = 0; slot < 24; slot++)
            {
                var r = new MatchRunner(o, slot, content, HostBrains.Scripted(settings, content), mix);
                matches++;
                int choice = mix.Pick(r.Seed);
                int brains = 0;
                for (int seat = 0; seat < content.Config.Seats; seat++)
                {
                    IBotBrain? brain = r.BrainOfSeat(seat);
                    if (brain == null)
                    {
                        continue;
                    }

                    brains++;
                    bool neural = brain is NeuralBrain;
                    Assert.Equal(mix.IsOnnx(choice), neural);
                    seen.Add(neural ? "neural" : "scripted");
                }

                Assert.True(brains > 0);
            }

            Assert.Equal(2, seen.Count);
            long total = 0;
            foreach (long c in mix.Counts)
            {
                total += c;
            }

            Assert.Equal(matches, total);
        }

        [Fact]
        public void WithoutOpponents_NothingChanges()
        {
            ContentBundle content = ContentLoader.Load(ContentDir);
            var settings = new BotsSettings();
            HostOptions o = Options();
            var a = new MatchRunner(o, 0, content, HostBrains.Scripted(settings, content));
            var b = new MatchRunner(o, 0, content, HostBrains.Scripted(settings, content), null);
            Assert.Null(o.Opponents);
            Assert.Equal(Run(a), Run(b));
            for (int seat = 0; seat < content.Config.Seats; seat++)
            {
                IBotBrain? brain = a.BrainOfSeat(seat);
                Assert.True(brain == null || brain is ScriptedBrain);
            }
        }

        [Fact]
        public void ScriptedOnlyMix_PlaysLikeNoMix()
        {
            // The choice has its own generator, so sampling does not disturb the match rng.
            ContentBundle content = ContentLoader.Load(ContentDir);
            var settings = new BotsSettings();
            HostOptions o = Options();
            OpponentMix mix = OpponentMix.Parse("scripted=1", settings, content);
            var plain = new MatchRunner(o, 0, content, HostBrains.Scripted(settings, content));
            var mixed = new MatchRunner(o, 0, content, HostBrains.Scripted(settings, content), mix);
            Assert.Equal(Run(plain), Run(mixed));
        }

        private static string Run(MatchRunner r)
        {
            var sb = new System.Text.StringBuilder();
            var frames = new byte[r.AgentCount * r.FrameSize];
            var actions = new int[r.AgentCount];
            for (int step = 0; step < 150; step++)
            {
                r.WriteFrames(frames);
                sb.Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frames)));
                for (int a = 0; a < actions.Length; a++)
                {
                    int maskAt = a * r.FrameSize + r.MaskOffset;
                    actions[a] = 0;
                    for (int i = 1; i < ActionCatalogue.Count; i++)
                    {
                        if (frames[maskAt + i] != 0 && (step + a + i) % 5 == 0)
                        {
                            actions[a] = i;
                            break;
                        }
                    }
                }

                r.Advance(actions);
            }

            return sb.ToString();
        }
    }
}
