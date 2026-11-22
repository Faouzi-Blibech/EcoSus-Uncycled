using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Sim.Core;

namespace Uncycled.Bots.Neural
{
    /// <summary>
    /// One exported network (obs float32 [batch, obs], mask bool [batch, actions] -> logits float32
    /// [batch, actions], see bots/train/export_onnx.py) on the CPU. Thread-safe: one instance per role
    /// serves every bot of that role in the process.
    /// </summary>
    public sealed class OnnxPolicy : IPolicy, IDisposable
    {
        private const string ObsInput = "obs";
        private const string MaskInput = "mask";
        private const string LogitsOutput = "logits";

        private static readonly string[] InputNames = { ObsInput, MaskInput };
        private static readonly string[] OutputNames = { LogitsOutput };

        private readonly InferenceSession _session;
        private readonly int _obsSize;
        private readonly int _actionCount;

        private OnnxPolicy(InferenceSession session, int obsSize, int actionCount)
        {
            _session = session;
            _obsSize = obsSize;
            _actionCount = actionCount;
        }

        /// <summary>
        /// Loads onnxPath (its sidecar JSON, same name with .json, must carry the expected schema hash).
        /// Never throws: any problem gives false and a reason.
        /// </summary>
        public static bool TryLoad(string onnxPath, string expectedSchemaHash, out OnnxPolicy? policy, out string reason)
        {
            policy = null;
            InferenceSession? session = null;
            try
            {
                if (!File.Exists(onnxPath))
                {
                    reason = "model file not found: " + onnxPath;
                    return false;
                }

                string sidecar = Path.ChangeExtension(onnxPath, ".json");
                if (!File.Exists(sidecar))
                {
                    reason = "model metadata not found: " + sidecar;
                    return false;
                }

                Match m = Regex.Match(File.ReadAllText(sidecar), "\"schema_hash\"\\s*:\\s*\"([^\"]*)\"");
                string found = m.Success ? m.Groups[1].Value : "";
                if (!string.Equals(found, expectedSchemaHash, StringComparison.Ordinal))
                {
                    reason = "schema hash mismatch: model has '" + found + "', bots expect '" + expectedSchemaHash + "'";
                    return false;
                }

                using (var options = new SessionOptions())
                {
                    // tiny network, many bots: no thread pool per session
                    options.IntraOpNumThreads = 1;
                    options.InterOpNumThreads = 1;
                    session = new InferenceSession(onnxPath, options);
                }

                int obsSize = LastDimension(session.InputMetadata, ObsInput);
                int actionCount = LastDimension(session.OutputMetadata, LogitsOutput);
                if (obsSize != ObservationBuilderV2.Size || actionCount != PolicyActionsV2.Count)
                {
                    reason = "network shape " + obsSize + " -> " + actionCount + " does not match the bots ("
                        + ObservationBuilderV2.Size + " -> " + PolicyActionsV2.Count + ")";
                    session.Dispose();
                    return false;
                }

                var loaded = new OnnxPolicy(session, obsSize, actionCount);
                var allValid = new bool[actionCount];
                Array.Fill(allValid, true);
                loaded.Act(new float[obsSize], allValid, 0f, new XorShiftRandom(1));
                policy = loaded;
                reason = "";
                return true;
            }
            catch (Exception e)
            {
                session?.Dispose();
                policy = null;
                reason = "cannot load " + onnxPath + ": " + e.Message;
                return false;
            }
        }

        /// <summary>Best valid action (temperature 0) or a sample from softmax(logits / temperature) over the valid ones.</summary>
        public int Act(float[] obs, bool[] mask, float temperature, IRandom rng)
        {
            if (obs.Length != _obsSize || mask.Length != _actionCount)
            {
                throw new ArgumentException("expected obs " + _obsSize + " and mask " + _actionCount + ", got " + obs.Length + " and " + mask.Length);
            }

            using var obsValue = OrtValue.CreateTensorValueFromMemory(obs, new long[] { 1, _obsSize });
            using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, new long[] { 1, _actionCount });
            using var runOptions = new RunOptions();
            using var outputs = _session.Run(runOptions, InputNames, new[] { obsValue, maskValue }, OutputNames);
            ReadOnlySpan<float> logits = outputs[0].GetTensorDataAsSpan<float>();
            if (logits.Length != _actionCount)
            {
                throw new InvalidOperationException("network returned " + logits.Length + " logits, expected " + _actionCount);
            }

            return Pick(logits, mask, temperature, rng);
        }

        public void Dispose()
        {
            _session.Dispose();
        }

        private static int Pick(ReadOnlySpan<float> logits, bool[] mask, float temperature, IRandom rng)
        {
            int best = -1;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                if (float.IsNaN(logits[i]))
                {
                    throw new InvalidOperationException("network returned NaN for action " + i);
                }

                if (best < 0 || logits[i] > logits[best])
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                throw new InvalidOperationException("no valid action in the mask");
            }

            if (temperature <= 0f)
            {
                return best;
            }

            double total = 0;
            var weight = new double[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i])
                {
                    weight[i] = Math.Exp((logits[i] - logits[best]) / temperature);
                    total += weight[i];
                }
            }

            double pick = rng.NextFloat() * total;
            int last = best;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                last = i;
                pick -= weight[i];
                if (pick < 0)
                {
                    return i;
                }
            }

            return last;
        }

        /// <summary>Size of the second axis of a 2-D tensor, or -1 when the tensor is missing or not 2-D.</summary>
        private static int LastDimension(IReadOnlyDictionary<string, NodeMetadata> metadata, string name)
        {
            if (!metadata.TryGetValue(name, out NodeMetadata? node) || node.Dimensions.Length != 2)
            {
                return -1;
            }

            return node.Dimensions[1];
        }
    }
}
