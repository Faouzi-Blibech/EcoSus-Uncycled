using System;
using Sim.Core;
using Uncycled.Bots;
using Uncycled.Bots.Neural;
using Uncycled.Server.Content;

namespace Uncycled.Train
{
    /// <summary>The one place the host picks brains for the seats the trainer does not control.</summary>
    public static class HostBrains
    {
        private const string OnnxPrefix = "onnx:";

        public static Func<Role, IRandom, IBotBrain> Scripted(BotsSettings settings, ContentBundle content)
        {
            return (role, rng) => BrainFactory.Scripted(role, settings, rng, content.Tasks);
        }

        /// <summary>
        /// Brains for --eval: per role "scripted" or "onnx:&lt;path&gt;". A model that does not load is an
        /// error here (never a silent fallback), because the numbers would be the scripted brain's.
        /// An onnx role plays through NeuralBrain, so a failing decision still falls back to scripted
        /// for that seat (logged on stderr).
        /// </summary>
        public static Func<Role, IRandom, IBotBrain> ForEval(HostOptions options, BotsSettings settings, ContentBundle content)
        {
            OnnxPolicy? citizen = LoadSpec(options.CitizenBrain, "--citizen");
            OnnxPolicy? polluter = LoadSpec(options.PolluterBrain, "--polluter");
            return (role, rng) =>
            {
                IBotBrain scripted = BrainFactory.Scripted(role, settings, rng, content.Tasks);
                OnnxPolicy? policy = role == Role.Polluter ? polluter : citizen;
                return policy == null ? scripted : new NeuralBrain(policy, scripted, settings, rng, content.Tasks);
            };
        }

        private static OnnxPolicy? LoadSpec(string spec, string flag)
        {
            if (spec == "scripted")
            {
                return null;
            }

            if (!spec.StartsWith(OnnxPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(flag + " must be scripted or onnx:<path>, got '" + spec + "'");
            }

            string path = spec.Substring(OnnxPrefix.Length);
            if (!OnnxPolicy.TryLoad(path, SchemaV2.Hash, out OnnxPolicy? policy, out string reason) || policy == null)
            {
                throw new ArgumentException(flag + " model not usable: " + reason);
            }

            return policy;
        }
    }
}
