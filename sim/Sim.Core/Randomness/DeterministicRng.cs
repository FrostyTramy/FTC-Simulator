namespace Sim.Core.Randomness;

/// <summary>
/// Seeded pseudo-random generator, identical on every machine: xoshiro256** (Blackman &amp; Vigna 2018)
/// seeded through SplitMix64. Never use System.Random or time-based seeds in the simulation.
/// </summary>
public sealed class DeterministicRng
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRng(ulong seed)
    {
        var sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    public ulong NextULong()
    {
        var result = RotL(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0, 1) using the top 53 bits.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform double in [min, max).</summary>
    public double NextDouble(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform integer in [0, bound) without modulo bias (Lemire's method).</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bound));
        }
        var b = (ulong)bound;
        var threshold = (0UL - b) % b;
        while (true)
        {
            var r = NextULong();
            var hi = Math.BigMul(r, b, out var lo);
            if (lo >= threshold)
            {
                return (int)hi;
            }
        }
    }

    /// <summary>Standard normal sample (Box-Muller, one value per call, deterministic).</summary>
    public double NextGaussian()
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
