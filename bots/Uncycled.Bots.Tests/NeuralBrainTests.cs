using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Sim.Core;
using Sim.Match;
using Uncycled.Bots.Neural;
using Uncycled.Train;
using Xunit;

namespace Uncycled.Bots.Tests
{
    public sealed class NeuralBrainTests
    {
        private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        private static readonly string TinyOnnx = Path.Combine(Fixtures, "tiny.onnx");

        private sealed class Case
        {
            public float[] Obs = Array.Empty<float>();
            public bool[] Mask = Array.Empty<bool>();
            public int Argmax;
        }

        private static string TinyHash()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "tiny.json")));
            return doc.RootElement.GetProperty("schema_hash").GetString()!;
        }

        private static List<Case> LoadCases()
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "tiny_cases.json")));
            var list = new List<Case>();
            foreach (JsonElement c in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                var obs = new List<float>();
                foreach (JsonElement x in c.GetProperty("obs").EnumerateArray())
                {
                    obs.Add(x.GetSingle());
                }

                var mask = new List<bool>();
                foreach (JsonElement x in c.GetProperty("mask").EnumerateArray())
                {
                    mask.Add(x.GetInt32() != 0);
                }

                list.Add(new Case { Obs = obs.ToArray(), Mask = mask.ToArray(), Argmax = c.GetProperty("argmax").GetInt32() });
            }

            return list;
        }

        private static OnnxPolicy LoadTiny()
        {
            Assert.True(OnnxPolicy.TryLoad(TinyOnnx, TinyHash(), out OnnxPolicy? policy, out string reason), reason);
            return policy!;
        }

        /// <summary>A models dir holding citizen.onnx + citizen.json copied from the fixture, edited by the caller.</summary>
        private static string ModelsDir(Action<string>? edit = null, string? onnxBytesFrom = null)
        {
            string dir = Path.Combine(Path.GetTempPath(), "uncycled-models-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (onnxBytesFrom != null)
            {
                File.Copy(onnxBytesFrom, Path.Combine(dir, "citizen.onnx"));
                File.Copy(Path.Combine(Fixtures, "tiny.json"), Path.Combine(dir, "citizen.json"));
            }

            edit?.Invoke(dir);
            return dir;
        }

        private static BotsSettings Neural() => new BotsSettings { Brain = "neural" };

        private static void ThinkSome(IBotBrain brain, TestMatch match, byte seat, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                brain.Think(match.Engine, seat, TestMatch.Dt, cmd => match.Engine.Apply(seat, cmd));
                match.Tick();
            }
        }

        private sealed class ThrowingPolicy : IPolicy
        {
            public int Calls;

            public int Act(float[] obs, bool[] mask, float temperature, IRandom rng)
            {
                Calls++;
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class CountingBrain : IBotBrain
        {
            public int Thinks;

            public void Think(MatchEngine engine, byte seat, float dt, Action<ICommand> send)
            {
                Thinks++;
            }
        }

        [Fact]
        public void OnnxPolicy_MatchesPythonArgmax()
        {
            OnnxPolicy policy = LoadTiny();
            List<Case> cases = LoadCases();
            Assert.Equal(20, cases.Count);
            var rng = new XorShiftRandom(1);
            for (int i = 0; i < cases.Count; i++)
            {
                Assert.Equal(cases[i].Argmax, policy.Act(cases[i].Obs, cases[i].Mask, 0f, rng));
            }
        }

        [Fact]
        public void OnnxPolicy_NeverSamplesAMaskedAction()
        {
            OnnxPolicy policy = LoadTiny();
            var rng = new XorShiftRandom(5);
            foreach (Case c in LoadCases())
            {
                for (int k = 0; k < 20; k++)
                {
                    int a = policy.Act(c.Obs, c.Mask, 1f, rng);
                    Assert.True(c.Mask[a], "sampled masked action " + a);
                }
            }
        }

        [Fact]
        public void NeuralBrain_FallsBackOnMissingFile()
        {
            string dir = ModelsDir();
            Assert.False(OnnxPolicy.TryLoad(Path.Combine(dir, "citizen.onnx"), ObservationBuilder.SchemaHash, out OnnxPolicy? p, out string reason));
            Assert.Null(p);
            Assert.False(string.IsNullOrEmpty(reason));
            AssertScriptedAndPlays(NeuralBrainFactory.For(Sim.Core.Role.Citizen, Neural(), dir, new XorShiftRandom(1)));
        }

        [Fact]
        public void NeuralBrain_FallsBackOnSchemaMismatch()
        {
            // the right network, but its sidecar names another observation layout
            string dir = ModelsDir(d =>
            {
                string json = File.ReadAllText(Path.Combine(d, "citizen.json")).Replace(TinyHash(), new string('0', 64));
                File.WriteAllText(Path.Combine(d, "citizen.json"), json);
            }, TinyOnnx);
            Assert.False(OnnxPolicy.TryLoad(Path.Combine(dir, "citizen.onnx"), ObservationBuilder.SchemaHash, out _, out string reason));
            Assert.Contains("schema", reason, StringComparison.OrdinalIgnoreCase);
            AssertScriptedAndPlays(NeuralBrainFactory.For(Sim.Core.Role.Citizen, Neural(), dir, new XorShiftRandom(1)));
        }

        [Fact]
        public void NeuralBrain_FallsBackOnCorruptFile()
        {
            string dir = ModelsDir(d => File.WriteAllBytes(Path.Combine(d, "citizen.onnx"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            File.Copy(Path.Combine(Fixtures, "tiny.json"), Path.Combine(dir, "citizen.json"));
            Assert.False(OnnxPolicy.TryLoad(Path.Combine(dir, "citizen.onnx"), ObservationBuilder.SchemaHash, out _, out string reason));
            Assert.False(string.IsNullOrEmpty(reason));
            AssertScriptedAndPlays(NeuralBrainFactory.For(Sim.Core.Role.Citizen, Neural(), dir, new XorShiftRandom(1)));
        }

        [Fact]
        public void NeuralBrainFactory_UsesTheModelWhenItLoads()
        {
            string dir = ModelsDir(null, TinyOnnx);
            IBotBrain brain = NeuralBrainFactory.For(Sim.Core.Role.Citizen, Neural(), dir, new XorShiftRandom(1));
            Assert.IsType<NeuralBrain>(brain);
            // the same files with brain=scripted in bots.json: the switch wins
            Assert.IsNotType<NeuralBrain>(NeuralBrainFactory.For(Sim.Core.Role.Citizen, new BotsSettings { Brain = "scripted" }, dir, new XorShiftRandom(1)));
            // no polluter model in the folder: the polluter is scripted
            Assert.IsNotType<NeuralBrain>(NeuralBrainFactory.For(Sim.Core.Role.Polluter, Neural(), dir, new XorShiftRandom(1)));
        }

        [Fact]
        public void NeuralBrainFactory_ReportsAnUnusableModelToTheGivenLog()
        {
            var lines = new System.Collections.Generic.List<string>();
            IBotBrain brain = NeuralBrainFactory.For(Sim.Core.Role.Citizen, Neural(), ModelsDir(), new XorShiftRandom(1), null, lines.Add);
            Assert.IsNotType<NeuralBrain>(brain);
            Assert.Single(lines);
            Assert.Contains("citizen", lines[0], StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertScriptedAndPlays(IBotBrain brain)
        {
            Assert.IsNotType<NeuralBrain>(brain);
            TestMatch match = TestMatch.Create(3);
            byte seat = match.FirstSeatOf(Sim.Core.Role.Citizen);
            ThinkSome(brain, match, seat, 200);
        }

        [Fact]
        public void NeuralBrain_SwitchesToFallbackWhenThePolicyThrows()
        {
            TestMatch match = TestMatch.Create(4);
            byte seat = match.FirstSeatOf(Sim.Core.Role.Citizen);
            var policy = new ThrowingPolicy();
            var fallback = new CountingBrain();
            var logs = new List<string>();
            var brain = new NeuralBrain(policy, fallback, new BotsSettings { DecisionIntervalS = 0f }, new XorShiftRandom(1), null, logs.Add);

            ThinkSome(brain, match, seat, 100);

            Assert.True(brain.UsingFallback);
            Assert.Equal(1, policy.Calls);
            Assert.Equal(100, fallback.Thinks);
            Assert.Single(logs);
        }

        [Fact]
        public void NeuralBrain_PlaysWithTheModelAndStaysNeural()
        {
            TestMatch match = TestMatch.Create(6);
            byte seat = match.FirstSeatOf(Sim.Core.Role.Citizen);
            var fallback = new CountingBrain();
            var brain = new NeuralBrain(LoadTiny(), fallback, new BotsSettings(), new XorShiftRandom(2), match.Content.Tasks);
            int moves = 0;
            for (int i = 0; i < 1200; i++)
            {
                brain.Think(match.Engine, seat, TestMatch.Dt, cmd =>
                {
                    if (cmd is MoveCommand)
                    {
                        moves++;
                    }

                    match.Engine.Apply(seat, cmd);
                });
                match.Tick();
            }

            Assert.False(brain.UsingFallback);
            Assert.Equal(0, fallback.Thinks);
            Assert.True(moves > 0, "the neural bot never moved");
        }

        [Fact(Skip = "tiny.onnx is a v1 model (154 -> 30) and the host now refuses v1 networks: restore with the v2 fixture (regenerated after the user says go to the 20k-step smoke train) and the NeuralBrain v2 merge (bots/nn-neural)")]
        public void Eval_OnnxBrainPlaysAndIsDeterministic()
        {
            string content = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            HostOptions options = HostOptions.Parse(new[] { "--eval", "--content", content, "--seeds", "1-3", "--citizen", "onnx:" + TinyOnnx, "--polluter", "scripted" });
            Uncycled.Server.Content.ContentBundle bundle = Uncycled.Server.Content.ContentLoader.Load(content);
            var settings = new BotsSettings();
            IReadOnlyList<string> a = EvalRunner.Run(options, bundle, HostBrains.ForEval(options, settings, bundle));
            IReadOnlyList<string> b = EvalRunner.Run(options, bundle, HostBrains.ForEval(options, settings, bundle));
            Assert.Equal(3, a.Count);
            Assert.Equal(a, b);
            Assert.Contains("\"winner\":", a[0]);
        }

        [Fact]
        public void Eval_BadOnnxPathIsAnErrorNotAFallback()
        {
            string content = Uncycled.Server.Content.ContentLoader.ResolveDir(Environment.GetEnvironmentVariable("UNCYCLED_CONTENT_DIR"));
            HostOptions options = HostOptions.Parse(new[] { "--eval", "--content", content, "--polluter", "onnx:/no/such/model.onnx" });
            Uncycled.Server.Content.ContentBundle bundle = Uncycled.Server.Content.ContentLoader.Load(content);
            Assert.Throws<ArgumentException>(() => HostBrains.ForEval(options, new BotsSettings(), bundle));
        }

        [Fact]
        public void NeuralBrain_DecisionUnder1ms()
        {
            OnnxPolicy policy = LoadTiny();
            Case c = LoadCases()[0];
            var rng = new XorShiftRandom(9);
            for (int i = 0; i < 50; i++)
            {
                policy.Act(c.Obs, c.Mask, 0f, rng);
            }

            var ms = new double[1000];
            for (int i = 0; i < ms.Length; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                policy.Act(c.Obs, c.Mask, 0.4f, rng);
                ms[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            }

            Array.Sort(ms);
            double median = ms[ms.Length / 2];
            Console.WriteLine("Act median " + median.ToString("F4") + " ms, p99 " + ms[(int)(ms.Length * 0.99)].ToString("F4") + " ms");
            Assert.True(median < 1.0, "median Act time " + median + " ms");
        }
    }
}
