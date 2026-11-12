namespace Aelid.World
{
    /// <summary>
    /// Small deterministic random stream (SplitMix64). Region generation seeds one
    /// stream per grid block or per parent slot from integer hashes, so the same
    /// inputs give the same world on every machine, and editing one part of a
    /// region does not reshuffle the rest.
    /// </summary>
    public struct RegionRandom
    {
        const ulong Golden = 0x9E3779B97F4A7C15UL;

        ulong m_State;

        public RegionRandom(ulong seed) => m_State = seed;

        public static RegionRandom For(int a, int b, int c, int d, int e = 0) => new(Hash(a, b, c, d, e));

        public static ulong Hash(int a, int b, int c, int d, int e = 0)
        {
            ulong h = Golden;
            h = Mix(h + (uint)a);
            h = Mix(h + (uint)b);
            h = Mix(h + (uint)c);
            h = Mix(h + (uint)d);
            h = Mix(h + (uint)e);
            return h;
        }

        /// <summary>Stable 32-bit FNV-1a string hash (string.GetHashCode is not stable across runtimes).</summary>
        public static int Hash(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (text != null)
                    foreach (char ch in text)
                        h = (h ^ ch) * 16777619u;
                return (int)h;
            }
        }

        public ulong NextULong()
        {
            m_State += Golden;
            return Mix(m_State);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Next() => (NextULong() >> 40) * (1f / (1 << 24));

        public float Range(float min, float max) => min + (max - min) * Next();

        /// <summary>Uniform float in [-amplitude, amplitude).</summary>
        public float Signed(float amplitude) => (Next() * 2f - 1f) * amplitude;

        public int Index(int count) => count <= 1 ? 0 : (int)((NextULong() >> 33) % (ulong)count);

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
