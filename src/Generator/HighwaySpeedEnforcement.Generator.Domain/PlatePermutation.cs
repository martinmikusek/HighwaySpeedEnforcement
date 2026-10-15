namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// Affine permutation p(i) = (A * i + B) mod size with gcd(A, size) = 1, a bijection of [0, size): the first <c>size</c>
/// draws are distinct without a set of used indices (design section 4, BR-SIM-03). Sizes up to <see cref="int.MaxValue"/>
/// are supported, the product A * i then fits in a <see cref="long"/>.
/// </summary>
public sealed class PlatePermutation
{
    private const long MinSize = 2;
    private const long MaxSize = int.MaxValue;

    private readonly long _size;
    private readonly long _a;
    private readonly long _b;
    private long _nextIndex;

    public PlatePermutation(long size, PlateState state)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);

        if (!IsValidState(size, state))
        {
            throw new ArgumentException($"State {state} is not valid for size {size}: A must be coprime to size, B and NextIndex within [0, size).", nameof(state));
        }

        // Proves for this size that no draw can overflow: the largest product is A * (size - 1).
        _ = checked((size - 1) * (size - 1) + size);

        _size = size;
        _a = state.A;
        _b = state.B;
        _nextIndex = state.NextIndex;
    }

    public long Size => _size;

    /// <summary>How many times the index wrapped to 0; plates repeat after the first wrap (documented limit).</summary>
    public long WrapCount { get; private set; }

    public PlateState State => new(_a, _b, _nextIndex);

    public static bool IsValidState(long size, PlateState state)
    {
        return state.A >= 1 && state.A < size && GreatestCommonDivisor(state.A, size) == 1
            && state.B >= 0 && state.B < size
            && state.NextIndex >= 0 && state.NextIndex < size;
    }

    /// <summary>Draws A in [1, size) coprime to size and B in [0, size) from the seeded source; the index starts at 0.</summary>
    public static PlatePermutation FromSeed(long size, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, MinSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxSize);

        long a;
        do
        {
            a = 1 + NextIndexBelow(random, size - 1);
        }
        while (GreatestCommonDivisor(a, size) != 1);

        long b = NextIndexBelow(random, size);

        return new PlatePermutation(size, new PlateState(a, b, 0));
    }

    /// <summary>Returns the permuted index of the current counter and advances the counter.</summary>
    public long Next()
    {
        long result = ((_a * _nextIndex) + _b) % _size;

        _nextIndex++;
        if (_nextIndex == _size)
        {
            _nextIndex = 0;
            WrapCount++;
        }

        return result;
    }

    private static long NextIndexBelow(DeterministicRandom random, long maxExclusive)
    {
        return random.NextInt((int)maxExclusive);
    }

    private static long GreatestCommonDivisor(long x, long y)
    {
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return x;
    }
}
