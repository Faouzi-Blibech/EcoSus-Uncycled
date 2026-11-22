using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>One entry of --opponents: "scripted" or "onnx:&lt;path&gt;" and its weight (above 0).</summary>
    public sealed class OpponentSpec
    {
        public OpponentSpec(string spec, double weight)
        {
            Spec = spec;
            Weight = weight;
        }

        public string Spec { get; }

        public double Weight { get; }
    }

    /// <summary>
    /// Opponent mix for training (--opponents "onnx:a.onnx=0.6,scripted=0.3,onnx:b.onnx=0.1"). One brain kind is
    /// drawn per match, from a generator seeded by the match seed alone, so a match is deterministic per seed
    /// and the draw does not disturb the match's own random stream. The brains are built as in --eval:
    /// NeuralBrain with a scripted fallback, same BotsSettings.
    /// </summary>
    public sealed class OpponentMix
    {
        private const string OnnxPrefix = "onnx:";
        private const ulong SeedSalt = 0x5DEECE66DUL;
        private const int LogEveryMatches = 200;

        private readonly List<OpponentSpec> _specs;
        private readonly OnnxPolicy?[] _policies;
        private readonly double[] _cumulative;
        private readonly long[] _counts;
        private readonly BotsSettings _settings;
        private readonly ContentBundle _content;
        private readonly Action<string>? _log;
        private readonly double _sum;
        private long _total;

        private OpponentMix(List<OpponentSpec> specs, OnnxPolicy?[] policies, BotsSettings settings, ContentBundle content, Action<string>? log)
        {
            _specs = specs;
            _policies = policies;
            _settings = settings;
            _content = content;
            _log = log;
            _counts = new long[specs.Count];
            _cumulative = new double[specs.Count];
            foreach (OpponentSpec s in specs)
            {
                _sum += s.Weight;
            }

            double run = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                run += specs[i].Weight / _sum;
                _cumulative[i] = run;
            }
        }

        public int Count => _specs.Count;

        public bool IsOnnx(int index) => _policies[index] != null;

        /// <summary>Weight of the entry, normalised so the weights add up to 1.</summary>
        public double Weight(int index) => _specs[index].Weight / _sum;

        /// <summary>Matches started per entry so far (a copy).</summary>
        public long[] Counts
        {
            get
            {
                var copy = new long[_counts.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    copy[i] = Interlocked.Read(ref _counts[i]);
                }

                return copy;
            }
        }

        /// <summary>Syntax check only (no model is loaded): comma list of spec=weight, weight after the last '='.</summary>
        public static List<OpponentSpec> ParseSpecs(string text)
        {
            var list = new List<OpponentSpec>();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("--opponents needs a list like onnx:<path>=0.6,scripted=0.4");
            }

            foreach (string part in text.Split(','))
            {
                string item = part.Trim();
                int eq = item.LastIndexOf('=');
                if (eq <= 0 || eq == item.Length - 1)
                {
                    throw new ArgumentException("--opponents entry '" + item + "' must look like scripted=<weight> or onnx:<path>=<weight>");
                }

                string spec = item.Substring(0, eq).Trim();
                string weightText = item.Substring(eq + 1).Trim();
                if (!double.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out double weight) || double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0)
                {
                    throw new ArgumentException("--opponents weight '" + weightText + "' for '" + spec + "' must be a number above 0");
                }

                if (spec != "scripted" && !(spec.StartsWith(OnnxPrefix, StringComparison.Ordinal) && spec.Length > OnnxPrefix.Length))
                {
                    throw new ArgumentException("--opponents entry '" + spec + "' must be scripted or onnx:<path>");
                }

                list.Add(new OpponentSpec(spec, weight));
            }

            return list;
        }

        /// <summary>Parses and loads every model. A model that does not load is an ArgumentException (exit 2), never a fallback.</summary>
        public static OpponentMix Parse(string text, BotsSettings settings, ContentBundle content, Action<string>? log = null)
        {
            List<OpponentSpec> specs = ParseSpecs(text);
            var policies = new OnnxPolicy?[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                if (specs[i].Spec == "scripted")
                {
                    continue;
                }

                string path = specs[i].Spec.Substring(OnnxPrefix.Length);
                if (!OnnxPolicy.TryLoad(path, SchemaV2.Hash, out OnnxPolicy? policy, out string reason) || policy == null)
                {
                    throw new ArgumentException("--opponents model not usable (" + path + "): " + reason);
                }

                policies[i] = policy;
            }

            return new OpponentMix(specs, policies, settings, content, log);
        }

        /// <summary>The entry this match plays against; a pure function of the match seed.</summary>
        public int Pick(ulong seed)
        {
            var rng = new XorShiftRandom(seed * 0x9E3779B97F4A7C15UL + SeedSalt);
            rng.NextInt(1 << 20);
            double u = rng.NextInt(1 << 20) / (double)(1 << 20);
            for (int i = 0; i < _cumulative.Length; i++)
            {
                if (u < _cumulative[i])
                {
                    return i;
                }
            }

            return _cumulative.Length - 1;
        }

        /// <summary>Counts one started match; logs the counts every few hundred matches.</summary>
        public void Record(int index)
        {
            Interlocked.Increment(ref _counts[index]);
            long total = Interlocked.Increment(ref _total);
            if (_log != null && total % LogEveryMatches == 0)
            {
                _log(Summary());
            }
        }

        public IBotBrain Create(int index, Role role, IRandom rng)
        {
            IBotBrain scripted = BrainFactory.Scripted(role, _settings, rng, _content.Tasks);
            OnnxPolicy? policy = _policies[index];
            return policy == null ? scripted : new NeuralBrain(policy, scripted, _settings, rng, _content.Tasks);
        }

        public string Summary()
        {
            long[] counts = Counts;
            long total = 0;
            foreach (long c in counts)
            {
                total += c;
            }

            var parts = new List<string>();
            for (int i = 0; i < counts.Length; i++)
            {
                string name = _specs[i].Spec == "scripted" ? "scripted" : "onnx:" + Path.GetFileName(_specs[i].Spec.Substring(OnnxPrefix.Length));
                parts.Add(name + " " + counts[i]);
            }

            return "opponents: " + total + " matches (" + string.Join(", ", parts) + ")";
        }
    }
}
