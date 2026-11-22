using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Uncycled.Bots.Tests
{
    /// <summary>
    /// Writes a minimal network with the exported contract (obs float [batch, obsSize], mask bool [batch, actions] ->
    /// logits float [batch, actions]) and its sidecar, so loader tests can pick the shape and the schema hash freely.
    /// All logits are zero; it is a loader fixture, not a trained network.
    /// </summary>
    internal static class SyntheticOnnx
    {
        /// <summary>Writes name.onnx and name.json into dir; the sidecar names schemaHash.</summary>
        public static string Write(string dir, string name, int obsSize, int actions, string schemaHash)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".onnx");
            File.WriteAllBytes(path, Model(obsSize, actions));
            File.WriteAllText(Path.Combine(dir, name + ".json"), "{ \"schema_hash\": \"" + schemaHash + "\" }");
            return path;
        }

        // ONNX protobuf field numbers: ModelProto ir_version 1, opset_import 8, graph 7; GraphProto node 1, name 2,
        // initializer 5, input 11, output 12; NodeProto input 1, output 2, name 3, op_type 4; TensorProto dims 1,
        // data_type 2, name 8, raw_data 9; ValueInfoProto name 1, type 2; TypeProto tensor_type 1; Tensor elem_type 1,
        // shape 2; TensorShapeProto dim 1; Dimension dim_value 1, dim_param 2.
        private static byte[] Model(int obsSize, int actions)
        {
            var node = Cat(Str(1, "obs"), Str(1, "weights"), Str(2, "logits"), Str(3, "matmul"), Str(4, "MatMul"));
            var weights = Cat(Int(1, obsSize), Int(1, actions), Int(2, 1), Str(8, "weights"), Bytes(9, new byte[obsSize * actions * 4]));
            var graph = Cat(
                Bytes(1, node),
                Str(2, "synthetic"),
                Bytes(5, weights),
                Bytes(11, ValueInfo("obs", 1, obsSize)),
                Bytes(11, ValueInfo("mask", 9, actions)),
                Bytes(12, ValueInfo("logits", 1, actions)));
            return Cat(Int(1, 8), Bytes(8, Int(2, 13)), Bytes(7, graph));
        }

        private static byte[] ValueInfo(string name, int elemType, int width)
        {
            byte[] batch = Str(2, "batch");
            byte[] size = Int(1, width);
            byte[] shape = Cat(Bytes(1, batch), Bytes(1, size));
            byte[] tensor = Cat(Int(1, elemType), Bytes(2, shape));
            return Cat(Str(1, name), Bytes(2, Bytes(1, tensor)));
        }

        private static byte[] Varint(ulong v)
        {
            var list = new List<byte>();
            while (v >= 0x80)
            {
                list.Add((byte)(v | 0x80));
                v >>= 7;
            }

            list.Add((byte)v);
            return list.ToArray();
        }

        private static byte[] Int(int field, long value) => Cat(Varint((ulong)(field << 3)), Varint((ulong)value));

        private static byte[] Bytes(int field, byte[] payload) => Cat(Varint((ulong)((field << 3) | 2)), Varint((ulong)payload.Length), payload);

        private static byte[] Str(int field, string text) => Bytes(field, Encoding.UTF8.GetBytes(text));

        private static byte[] Cat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] p in parts)
            {
                total += p.Length;
            }

            var all = new byte[total];
            int at = 0;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, all, at, p.Length);
                at += p.Length;
            }

            return all;
        }
    }
}
