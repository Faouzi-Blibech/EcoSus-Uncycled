using System;

namespace Sim.Core
{
    /// <summary>The only source of randomness in Sim.</summary>
    public interface IRandom
    {
        /// <summary>Returns an integer in [0, maxExclusive). maxExclusive must be &gt; 0.</summary>
        int NextInt(int maxExclusive);

        /// <summary>Returns a float in [0, 1).</summary>
        float NextFloat();
    }

    /// <summary>Small deterministic xorshift64* generator. Same seed, same sequence.</summary>
    public sealed class XorShiftRandom : IRandom
    {
        private const ulong ZeroSeedReplacement = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 0x2545F4914F6CDD1DUL;

        private ulong _state;

        public XorShiftRandom(ulong seed)
        {
            _state = seed == 0 ? ZeroSeedReplacement : seed;
        }

        private ulong NextULong()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return x * Multiplier;
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            return (int)(NextULong() % (ulong)maxExclusive);
        }

        public float NextFloat()
        {
            // 24 random bits -> [0, 1)
            return (NextULong() >> 40) / 16777216f;
        }
    }

    public static class RandomExtensions
    {
        /// <summary>Fisher-Yates shuffle in place.</summary>
        public static void Shuffle<T>(this IRandom rng, T[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
