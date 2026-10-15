using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed class DeterministicRandomTests
{
    private const ulong Seed = 20261014;

    // Reference values computed independently (PowerShell, BigInteger arithmetic modulo 2^64) before pinning;
    // the first output for seed 0 is the published SplitMix64 reference value.
    [Theory]
    [InlineData(0UL, new[] { 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL })]
    [InlineData(42UL, new[] { 0xBDD732262FEB6E95UL, 0x28EFE333B266F103UL, 0x47526757130F9F52UL })]
    [InlineData(1234567890123UL, new[] { 0x9A253C9557CD08D8UL, 0x13A4D3C1AB4B6B6AUL, 0x6FDA11718B5667F7UL })]
    [InlineData(ulong.MaxValue, new[] { 0xE4D971771B652C20UL, 0xE99FF867DBF682C9UL })]
    public void NextUInt64_fixed_seed_matches_reference_values(ulong seed, ulong[] expected)
    {
        var random = new DeterministicRandom(seed);

        var actual = expected.Select(_ => random.NextUInt64()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NextUInt64_same_seed_gives_same_sequence_and_different_seeds_differ()
    {
        var first = new DeterministicRandom(Seed);
        var second = new DeterministicRandom(Seed);
        var other = new DeterministicRandom(Seed + 1);

        var firstValues = Enumerable.Range(0, 1_000).Select(_ => first.NextUInt64()).ToArray();
        var secondValues = Enumerable.Range(0, 1_000).Select(_ => second.NextUInt64()).ToArray();
        var otherValues = Enumerable.Range(0, 1_000).Select(_ => other.NextUInt64()).ToArray();

        Assert.Equal(firstValues, secondValues);
        Assert.NotEqual(firstValues, otherValues);
    }

    [Fact]
    public void NextDouble_stays_in_unit_interval()
    {
        var random = new DeterministicRandom(Seed);

        for (int i = 0; i < 100_000; i++)
        {
            double value = random.NextDouble();

            Assert.True(value >= 0.0 && value < 1.0, $"Value {value} outside [0, 1) at draw {i}, seed {Seed}.");
        }
    }

    [Fact]
    public void NextInt_stays_in_range_and_is_roughly_uniform()
    {
        const int Buckets = 10;
        const int Draws = 100_000;
        var random = new DeterministicRandom(Seed);
        var counts = new int[Buckets];

        for (int i = 0; i < Draws; i++)
        {
            int value = random.NextInt(Buckets);

            Assert.InRange(value, 0, Buckets - 1);
            counts[value]++;
        }

        const double Expected = Draws / (double)Buckets;
        Assert.All(counts, count => Assert.True(
            Math.Abs(count - Expected) <= 0.10 * Expected,
            $"Bucket count {count} deviates more than 10 % from {Expected} (seed {Seed}): {string.Join(", ", counts)}"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void NextInt_never_reaches_the_exclusive_bound(int maxExclusive)
    {
        var random = new DeterministicRandom(Seed);

        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(random.NextInt(maxExclusive), 0, maxExclusive - 1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NextInt_rejects_a_bound_that_is_not_positive(int maxExclusive)
    {
        var random = new DeterministicRandom(Seed);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(maxExclusive));
    }

    [Fact]
    public void NextGaussian_has_unit_mean_and_deviation_and_is_always_finite()
    {
        const int Draws = 100_000;
        var random = new DeterministicRandom(Seed);
        double sum = 0;
        double sumOfSquares = 0;

        for (int i = 0; i < Draws; i++)
        {
            double value = random.NextGaussian();

            Assert.True(double.IsFinite(value), $"Draw {i} is not finite (seed {Seed}).");
            sum += value;
            sumOfSquares += value * value;
        }

        double mean = sum / Draws;
        double stdDev = Math.Sqrt((sumOfSquares / Draws) - (mean * mean));

        Assert.True(Math.Abs(mean) <= 0.02, $"Mean {mean} is not within ±0.02 of 0 (seed {Seed}).");
        Assert.True(Math.Abs(stdDev - 1.0) <= 0.02, $"Standard deviation {stdDev} is not within ±0.02 of 1 (seed {Seed}).");
    }

    [Fact]
    public void NextGaussian_with_parameters_scales_and_shifts_the_standard_deviate()
    {
        var standard = new DeterministicRandom(Seed);
        var scaled = new DeterministicRandom(Seed);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(125.0 + (15.0 * standard.NextGaussian()), scaled.NextGaussian(125.0, 15.0), 12);
        }
    }
}
