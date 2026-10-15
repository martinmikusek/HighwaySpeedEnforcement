namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// Seeded SplitMix64 generator. The generator has its own PRNG because <see cref="Random"/> does not promise the same
/// sequence for a seed across runtime versions, and reproducible runs are a requirement (design section 3).
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong Increment = 0x9E3779B97F4A7C15;
    private const ulong Multiplier1 = 0xBF58476D1CE4E5B9;
    private const ulong Multiplier2 = 0x94D049BB133111EB;

    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        _state += Increment;
        ulong z = _state;
        z = (z ^ (z >> 30)) * Multiplier1;
        z = (z ^ (z >> 27)) * Multiplier2;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1) from the top 53 bits, so every value is exactly representable.</summary>
    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>Uniform in [0, <paramref name="maxExclusive"/>) by rejection sampling, so no value is favored by the modulo.</summary>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        ulong range = (ulong)maxExclusive;
        ulong threshold = (0UL - range) % range;
        ulong draw;

        do
        {
            draw = NextUInt64();
        }
        while (draw < threshold);

        return (int)(draw % range);
    }

    /// <summary>Standard normal deviate (Box-Muller).</summary>
    public double NextGaussian()
    {
        // 1 - NextDouble() lies in (0, 1], so the logarithm is finite.
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public double NextGaussian(double mean, double stdDev)
    {
        return mean + (stdDev * NextGaussian());
    }
}
