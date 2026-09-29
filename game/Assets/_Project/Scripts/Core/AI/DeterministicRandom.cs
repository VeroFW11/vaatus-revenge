namespace VaatusRevenge.Core
{
    // Seeded random numbers (xorshift32). The same seed always gives the same sequence, so enemy choices
    // replay identically in bot playtests and bug reports. Don't use System.Random or UnityEngine.Random in
    // the core: their sequences aren't guaranteed to match across platforms and versions.
    public sealed class DeterministicRandom
    {
        uint state;

        public DeterministicRandom(int seed)
        {
            Reseed(seed);
        }

        public uint State => state;

        public void Reseed(int seed)
        {
            // Scramble the seed (SplitMix-style) so nearby seeds like 1, 2, 3 still start far apart.
            uint z = unchecked((uint)seed + 0x9E3779B9u);
            z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
            z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
            z ^= z >> 16;
            state = z == 0u ? 0x6D2B79F5u : z;   // xorshift must never hold zero
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        // Uniform in [0, 1).
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        public float Range(float min, float max)
        {
            return max <= min ? min : min + (max - min) * NextFloat();
        }

        // Integer in [minInclusive, maxExclusive).
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability)
        {
            return NextFloat() < probability;
        }
    }
}
