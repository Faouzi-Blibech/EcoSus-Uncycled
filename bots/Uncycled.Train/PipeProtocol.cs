using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Uncycled.Train
{
    /// <summary>The first message the host sends: what a frame looks like and how many agents there are.</summary>
    public sealed class Handshake
    {
        public Handshake(int obsSize, int actionCount, int agentCount, string schemaHash)
        {
            ObsSize = obsSize;
            ActionCount = actionCount;
            AgentCount = agentCount;
            SchemaHash = schemaHash;
        }

        public int ObsSize { get; }

        public int ActionCount { get; }

        public int AgentCount { get; }

        public string SchemaHash { get; }
    }

    /// <summary>One agent's decision frame, decoded (tests and tools; the host writes frames in place).</summary>
    public sealed class Frame
    {
        public Frame(float[] obs, bool[] mask, float reward, bool done)
        {
            Obs = obs;
            Mask = mask;
            Reward = reward;
            Done = done;
        }

        public float[] Obs { get; }

        public bool[] Mask { get; }

        public float Reward { get; }

        public bool Done { get; }
    }

    /// <summary>
    /// Binary pipe format, little-endian. Host to trainer: handshake, then one frame per agent per
    /// decision. Trainer to host: int32[agentCount] actions per decision.
    /// <code>
    /// handshake: "UCT1" | int32 obsSize | int32 actionCount | int32 agentCount | 64 ASCII bytes schema hash
    /// frame:     float32[obsSize] obs | uint8[actionCount] mask | float32 reward | uint8 done
    /// actions:   int32[agentCount]
    /// </code>
    /// </summary>
    public static class PipeProtocol
    {
        public const string Magic = "UCT1";
        public const int HashLength = 64;

        public static int FrameSize(int obsSize, int actionCount) => obsSize * 4 + actionCount + 4 + 1;

        public static void WriteHandshake(Stream s, int obsSize, int actionCount, int agentCount, string schemaHash)
        {
            if (schemaHash == null || schemaHash.Length != HashLength)
            {
                throw new ArgumentException("schema hash must be " + HashLength + " ASCII characters", nameof(schemaHash));
            }

            var buf = new byte[4 + 12 + HashLength];
            Encoding.ASCII.GetBytes(Magic, 0, 4, buf, 0);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4), obsSize);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8), actionCount);
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12), agentCount);
            Encoding.ASCII.GetBytes(schemaHash, 0, HashLength, buf, 16);
            s.Write(buf, 0, buf.Length);
            s.Flush();
        }

        public static Handshake ReadHandshake(Stream s)
        {
            var buf = new byte[4 + 12 + HashLength];
            if (!ReadExactly(s, buf))
            {
                throw new EndOfStreamException("handshake truncated");
            }

            if (Encoding.ASCII.GetString(buf, 0, 4) != Magic)
            {
                throw new InvalidDataException("bad magic");
            }

            return new Handshake(
                BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(4)),
                BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(8)),
                BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(12)),
                Encoding.ASCII.GetString(buf, 16, HashLength));
        }

        /// <summary>Writes one frame into dest (at least FrameSize bytes).</summary>
        public static void WriteFrame(Span<byte> dest, ReadOnlySpan<float> obs, ReadOnlySpan<bool> mask, float reward, bool done)
        {
            if (dest.Length < FrameSize(obs.Length, mask.Length))
            {
                throw new ArgumentException("frame buffer too small", nameof(dest));
            }

            for (int i = 0; i < obs.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(i * 4), obs[i]);
            }

            int at = obs.Length * 4;
            for (int i = 0; i < mask.Length; i++)
            {
                dest[at + i] = mask[i] ? (byte)1 : (byte)0;
            }

            at += mask.Length;
            BinaryPrimitives.WriteSingleLittleEndian(dest.Slice(at), reward);
            dest[at + 4] = done ? (byte)1 : (byte)0;
        }

        public static Frame ReadFrame(ReadOnlySpan<byte> src, int obsSize, int actionCount)
        {
            var obs = new float[obsSize];
            for (int i = 0; i < obsSize; i++)
            {
                obs[i] = BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i * 4));
            }

            var mask = new bool[actionCount];
            int at = obsSize * 4;
            for (int i = 0; i < actionCount; i++)
            {
                mask[i] = src[at + i] != 0;
            }

            at += actionCount;
            return new Frame(obs, mask, BinaryPrimitives.ReadSingleLittleEndian(src.Slice(at)), src[at + 4] != 0);
        }

        public static void WriteActions(Stream s, int[] actions)
        {
            var buf = new byte[actions.Length * 4];
            for (int i = 0; i < actions.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(i * 4), actions[i]);
            }

            s.Write(buf, 0, buf.Length);
        }

        /// <summary>Reads actions.Length int32 values; false on a clean end of stream before the first byte.</summary>
        public static bool ReadActions(Stream s, int[] actions)
        {
            var buf = new byte[actions.Length * 4];
            if (!ReadExactly(s, buf))
            {
                return false;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                actions[i] = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(i * 4));
            }

            return true;
        }

        private static bool ReadExactly(Stream s, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = s.Read(buf, got, buf.Length - got);
                if (n <= 0)
                {
                    if (got == 0)
                    {
                        return false;
                    }

                    throw new EndOfStreamException("stream ended inside a message");
                }

                got += n;
            }

            return true;
        }
    }
}
