using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Sim.Core;
using Sim.Uncycled;

namespace Uncycled.Bots.Neural
{
    /// <summary>Picks the brain of a bot seat: the role's network when it loads, the scripted brain otherwise.</summary>
    public static class NeuralBrainFactory
    {
        // One session per model file for the whole process, shared by every bot of that role.
        private static readonly ConcurrentDictionary<string, OnnxPolicy> Loaded = new ConcurrentDictionary<string, OnnxPolicy>();
        // A model that failed to load is not opened again for every bot: the failure is kept for the process, like a loaded session.
        private static readonly ConcurrentDictionary<string, bool> Reported = new ConcurrentDictionary<string, bool>();
        private static readonly ConcurrentDictionary<string, bool> Failed = new ConcurrentDictionary<string, bool>();

        /// <summary>The model file of a role inside modelsDir: citizen.onnx or polluter.onnx.</summary>
        public static string ModelPath(Role role, string modelsDir)
        {
            return Path.Combine(modelsDir, role == Role.Polluter ? "polluter.onnx" : "citizen.onnx");
        }

        /// <summary>
        /// Neural when s.UsesNetwork(role) (brain "neural", or the role's own "neural_polluter" / "neural_citizen") and the role's model loads with the v2 schema
        /// hash; scripted otherwise (switch off, file missing, corrupt, other schema). Never throws for a bad model.
        /// </summary>
        public static IBotBrain For(Role role, BotsSettings s, string modelsDir, IRandom rng, IReadOnlyList<TaskTypeDef>? taskTypes = null, Action<string>? log = null)
        {
            IBotBrain scripted = BrainFactory.Scripted(role, s, rng, taskTypes);
            if (!s.UsesNetwork(role))
            {
                string? unknown = s.UnknownBrainMessage();
                if (unknown != null && Reported.TryAdd("brain:" + s.Brain, true))
                {
                    (log ?? (message => Console.Error.WriteLine(message)))(unknown);
                }

                return scripted;
            }

            string path = ModelPath(role, modelsDir);
            string key = Path.GetFullPath(path);
            if (Failed.ContainsKey(key))
            {
                return scripted;
            }

            if (!Loaded.TryGetValue(key, out OnnxPolicy? policy))
            {
                if (!OnnxPolicy.TryLoad(path, SchemaV2.Hash, out policy, out string reason) || policy == null)
                {
                    Failed[key] = true;
                    if (Reported.TryAdd(key, true))
                    {
                        (log ?? (message => Console.Error.WriteLine(message)))("neural " + role + " model not used, scripted brain plays: " + reason);
                    }

                    return scripted;
                }

                // two rooms can build the same session at once: keep the first, free the other
                OnnxPolicy built = policy;
                policy = Loaded.GetOrAdd(key, built);
                if (!ReferenceEquals(policy, built))
                {
                    built.Dispose();
                }
            }

            return new NeuralBrain(policy, scripted, s, rng, taskTypes, log);
        }
    }
}
