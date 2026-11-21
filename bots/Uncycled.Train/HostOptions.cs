using System;
using System.Globalization;
using System.IO;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>Reward weights (spec 5.4). Python passes the values from bots/train/config.yaml.</summary>
    public sealed class RewardWeights
    {
        public float Win { get; set; } = 1f;

        public float Task { get; set; } = 0.05f;

        public float Report { get; set; } = 0.1f;

        public float Fix { get; set; } = 0.05f;

        public float Hit { get; set; } = 0.1f;

        public float Dump { get; set; } = 0.05f;

        /// <summary>Added to a learner polluter that is voted out; negative.</summary>
        public float Ejected { get; set; } = -0.3f;
    }

    /// <summary>Command line of Uncycled.Train.</summary>
    public sealed class HostOptions
    {
        public string ContentDir { get; set; } = ContentLoader.ResolveDir(null);

        public int Matches { get; set; } = 16;

        public ulong Seed { get; set; } = 1;

        public Role Learner { get; set; } = Role.Citizen;

        public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount - 2);

        /// <summary>Set by --bench: run scripted-vs-scripted with random learner actions for this long.</summary>
        public double? BenchSeconds { get; set; }

        /// <summary>First seed of the default evaluation range; training seeds count up from --seed (1) and should stay below.</summary>
        public const ulong EvalSeedStart = 100000;

        /// <summary>Explicit path of bots.json (--bots); null means bots/bots.json relative to the exe (bots/.build/train-win/).</summary>
        public string? BotsConfig { get; set; }

        /// <summary>Where warnings go (stderr by default).</summary>
        public Action<string> Warn { get; set; } = message => Console.Error.WriteLine("warning: " + message);

        /// <summary>Bot settings, loaded once. A missing file warns and gives the defaults.</summary>
        public BotsSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    string path = BotsConfig ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "bots.json"));
                    if (!File.Exists(path))
                    {
                        Warn("bots.json not found at " + path + ": using default bot settings (pass --bots <path>)");
                        _settings = new BotsSettings();
                    }
                    else
                    {
                        _settings = BotsSettings.Load(path);
                    }
                }

                return _settings;
            }
        }

        private BotsSettings? _settings;

        /// <summary>Set by --eval: play scripted-vs-scripted style matches for a seed range and print one JSON line per match.</summary>
        public bool Eval { get; set; }

        /// <summary>Brain per role in --eval: "scripted" or "onnx:path".</summary>
        public string CitizenBrain { get; set; } = "scripted";

        public string PolluterBrain { get; set; } = "scripted";

        /// <summary>--opponents: weighted mix for the seats the learner does not play, e.g. "onnx:a.onnx=0.6,scripted=0.4" (training only; null = all scripted).</summary>
        public string? Opponents { get; set; }

        public ulong SeedFrom { get; set; } = 1;

        public ulong SeedTo { get; set; } = 1;

        /// <summary>Set by --bakeoff: compare the meeting deciders on the votes of scripted matches (see BakeOff).</summary>
        public bool BakeOff { get; set; }

        /// <summary>--meetings: vote stages to sample in --bakeoff.</summary>
        public int Meetings { get; set; } = 300;

        /// <summary>--max-calls: most decisions asked of each decider in --bakeoff.</summary>
        public int MaxCalls { get; set; } = 2500;

        /// <summary>--out: file the --bakeoff report (JSON) is written to.</summary>
        public string? Out { get; set; }

        /// <summary>--deciders: comma list of rule, clef, skip for --bakeoff.</summary>
        public string Deciders { get; set; } = "rule,clef";

        public RewardWeights Rewards { get; } = new RewardWeights();

        public static HostOptions Parse(string[] args)
        {
            var o = new HostOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];
                if (flag == "--eval")
                {
                    o.Eval = true;
                    continue;
                }

                if (flag == "--bakeoff")
                {
                    o.BakeOff = true;
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("missing value for " + flag);
                }

                string value = args[++i];
                switch (flag)
                {
                    case "--content": o.ContentDir = value; break;
                    case "--matches": o.Matches = Positive(flag, value); break;
                    case "--seed": o.Seed = ulong.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--threads": o.Threads = Positive(flag, value); break;
                    case "--learner": o.Learner = ParseRole(value); break;
                    case "--bench": o.BenchSeconds = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--bots": o.BotsConfig = value; break;
                    case "--citizen": o.CitizenBrain = value; break;
                    case "--polluter": o.PolluterBrain = value; break;
                    case "--opponents": OpponentMix.ParseSpecs(value); o.Opponents = value; break;
                    case "--seeds": ParseSeeds(o, value); break;
                    case "--meetings": o.Meetings = Positive(flag, value); break;
                    case "--max-calls": o.MaxCalls = Positive(flag, value); break;
                    case "--out": o.Out = value; break;
                    case "--deciders": o.Deciders = value; break;
                    case "--rw-win": o.Rewards.Win = Float(value); break;
                    case "--rw-task": o.Rewards.Task = Float(value); break;
                    case "--rw-report": o.Rewards.Report = Float(value); break;
                    case "--rw-fix": o.Rewards.Fix = Float(value); break;
                    case "--rw-hit": o.Rewards.Hit = Float(value); break;
                    case "--rw-dump": o.Rewards.Dump = Float(value); break;
                    case "--rw-ejected": o.Rewards.Ejected = Float(value); break;
                    default: throw new ArgumentException("unknown argument " + flag);
                }
            }

            return o;
        }

        private static int Positive(string flag, string text)
        {
            int v = int.Parse(text, CultureInfo.InvariantCulture);
            if (v < 1)
            {
                throw new ArgumentException(flag + " must be at least 1");
            }

            return v;
        }

        private static void ParseSeeds(HostOptions o, string text)
        {
            string[] parts = text.Split('-');
            if (parts.Length != 2 || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong from) || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out ulong to) || to < from)
            {
                throw new ArgumentException("--seeds must look like A-B with A <= B");
            }

            o.SeedFrom = from;
            o.SeedTo = to;
        }

        private static float Float(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        private static Role ParseRole(string text)
        {
            switch (text)
            {
                case "citizen": return Role.Citizen;
                case "polluter": return Role.Polluter;
                default: throw new ArgumentException("--learner must be citizen or polluter");
            }
        }
    }
}
