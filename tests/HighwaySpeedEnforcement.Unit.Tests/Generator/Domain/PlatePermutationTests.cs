using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed class PlatePermutationTests
{
    private const ulong Seed = 20261014;

    [Theory]
    [InlineData(1, 0)]
    [InlineData(7, 3)]
    [InlineData(999, 998)]
    [InlineData(13, 500)]
    public void Next_small_size_is_a_bijection(long a, long b)
    {
        const long Size = 1_000;
        var permutation = new PlatePermutation(Size, new PlateState(a, b, 0));
        var seen = new bool[Size];

        for (long i = 0; i < Size; i++)
        {
            long index = permutation.Next();

            Assert.InRange(index, 0, Size - 1);
            Assert.False(seen[index], $"Index {index} repeats at draw {i} (A={a}, B={b}).");
            seen[index] = true;
        }

        Assert.All(seen, covered => Assert.True(covered));
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Next_real_size_first_million_outputs_are_distinct()
    {
        var permutation = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed));
        var seen = new HashSet<long>();

        for (int i = 0; i < 1_000_000; i++)
        {
            long index = permutation.Next();

            Assert.InRange(index, 0, PlateFormat.Size - 1);
            Assert.True(seen.Add(index), $"Index {index} repeats at draw {i} (seed {Seed}).");
        }
    }

    [Fact]
    public void FromSeed_same_seed_gives_same_state_and_different_seeds_differ()
    {
        var first = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed));
        var second = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed));
        var other = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed + 1));

        Assert.Equal(first.State, second.State);
        Assert.NotEqual(first.State, other.State);
        Assert.Equal(0, first.State.NextIndex);
    }

    [Theory]
    [InlineData(PlateFormat.Size)]
    [InlineData(2 * 3 * 5 * 7 * 11 * 13 * 17)]
    [InlineData(1L << 20)]
    [InlineData(2)]
    public void FromSeed_always_yields_a_coprime_multiplier(long size)
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var state = PlatePermutation.FromSeed(size, new DeterministicRandom(seed)).State;

            Assert.True(PlatePermutation.IsValidState(size, state), $"State {state} is invalid for size {size} (seed {seed}).");
            Assert.Equal(1, Gcd(state.A, size));
        }
    }

    [Theory]
    [InlineData(2, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(-3, 0, 0)]
    [InlineData(1_000, 0, 0)]
    [InlineData(3, -1, 0)]
    [InlineData(3, 1_000, 0)]
    [InlineData(3, 0, -1)]
    [InlineData(3, 0, 1_000)]
    public void Constructor_rejects_an_invalid_state(long a, long b, long nextIndex)
    {
        Assert.Throws<ArgumentException>(() => new PlatePermutation(1_000, new PlateState(a, b, nextIndex)));
    }

    [Fact]
    public void Constructor_rejects_a_size_that_is_not_supported()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlatePermutation(1, new PlateState(1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlatePermutation((long)int.MaxValue + 1, new PlateState(1, 0, 0)));
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Next_after_size_draws_wraps_and_repeats_the_sequence()
    {
        const long Size = 1_000;
        var permutation = new PlatePermutation(Size, new PlateState(7, 3, 0));
        var firstRound = Enumerable.Range(0, (int)Size).Select(_ => permutation.Next()).ToArray();

        Assert.Equal(0, permutation.State.NextIndex);
        Assert.Equal(1, permutation.WrapCount);

        var secondRound = Enumerable.Range(0, (int)Size).Select(_ => permutation.Next()).ToArray();

        Assert.Equal(firstRound, secondRound);
        Assert.Equal(2, permutation.WrapCount);
    }

    [Fact]
    [Trait("AC", "AC-03")]
    [Trait("BR", "BR-SIM-03")]
    public void Resuming_from_state_continues_the_original_sequence()
    {
        var original = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed));
        var firstRun = Enumerable.Range(0, 5_000).Select(_ => original.Next()).ToArray();

        var resumed = new PlatePermutation(PlateFormat.Size, original.State);
        var secondRun = Enumerable.Range(0, 5_000).Select(_ => resumed.Next()).ToArray();
        var continuation = Enumerable.Range(0, 5_000).Select(_ => original.Next()).ToArray();

        Assert.Equal(continuation, secondRun);
        Assert.Empty(firstRun.Intersect(secondRun));
        Assert.Equal(original.State, resumed.State);
    }

    [Fact]
    public void Next_consecutive_plates_spread_over_all_regions()
    {
        const int Draws = 14_000;
        var permutation = PlatePermutation.FromSeed(PlateFormat.Size, new DeterministicRandom(Seed));
        var counts = new Dictionary<char, int>();

        for (int i = 0; i < Draws; i++)
        {
            char region = PlateFormat.Decode(permutation.Next())[1];
            counts[region] = counts.GetValueOrDefault(region) + 1;
        }

        const double Mean = Draws / 14.0;
        Assert.Equal(14, counts.Count);
        Assert.All(counts, pair => Assert.True(
            Math.Abs(pair.Value - Mean) <= 0.30 * Mean,
            $"Region {pair.Key} has {pair.Value} plates, more than 30 % away from {Mean} (seed {Seed})."));
    }

    private static long Gcd(long x, long y)
    {
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return x;
    }
}
