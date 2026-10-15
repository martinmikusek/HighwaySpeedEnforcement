using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class LivenessPolicyTests
{
    private static readonly TimeSpan Infinite = Timeout.InfiniteTimeSpan;

    [Fact]
    [Trait("BR", "BR-LB-07")]
    [Trait("BR", "BR-LB-08")]
    [Trait("BR", "BR-ING-06")]
    public void Expiry_is_timestamp_plus_the_matching_duration()
    {
        var policy = new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
        long timestamp = TimeSpan.FromSeconds(100).Ticks;

        Assert.Equal(TimeSpan.FromSeconds(102).Ticks, policy.CameraExpiry(timestamp));
        Assert.Equal(TimeSpan.FromSeconds(103).Ticks, policy.CompletedExpiry(timestamp));
        Assert.Equal(TimeSpan.FromSeconds(105).Ticks, policy.InFlightExpiry(timestamp));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Infinite_durations_give_long_MaxValue()
    {
        var policy = new LivenessPolicy(Infinite, Infinite, TimeSpan.FromSeconds(5));

        Assert.Equal(long.MaxValue, policy.CameraExpiry(123));
        Assert.Equal(long.MaxValue, policy.CompletedExpiry(123));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Addition_saturates_instead_of_overflowing()
    {
        var policy = new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        Assert.Equal(long.MaxValue, policy.CameraExpiry(long.MaxValue - 1));
        Assert.Equal(long.MaxValue, policy.InFlightExpiry(long.MaxValue));
    }

    [Fact]
    [Trait("AC", "AC-14")]
    [Trait("BR", "BR-LB-08")]
    public void Linger_zero_expires_a_completed_entry_at_its_own_timestamp()
    {
        var policy = new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.FromSeconds(5));

        Assert.Equal(777, policy.CompletedExpiry(777));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [Trait("BR", "BR-CFG-01")]
    public void Window_must_be_greater_than_zero(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LivenessPolicy(TimeSpan.FromSeconds(seconds), TimeSpan.Zero, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Linger_must_not_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Timeout_must_be_finite_and_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.Zero, Infinite));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LivenessPolicy(TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.Zero));
    }
}
