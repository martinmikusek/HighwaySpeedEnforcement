using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.Application;

public sealed class LivenessEvaluatorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 14, 14, 3, 10, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(3);

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("BR", "BR-UI-04")]
    public void Board_without_a_latest_event_has_no_data()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, null, Threshold);

        Assert.Equal(Liveness.NoData, status.Liveness);
        Assert.Null(status.Lag);
    }

    [Fact]
    [Trait("AC", "AC-32")]
    [Trait("BR", "BR-UI-04")]
    public void Lag_below_the_threshold_is_live()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, GeneratedAt.AddSeconds(-2.9), Threshold);

        Assert.Equal(Liveness.Live, status.Liveness);
        Assert.Equal(TimeSpan.FromSeconds(2.9), status.Lag);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("BR", "BR-UI-04")]
    public void Lag_above_the_threshold_is_not_live_and_carries_the_lag()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, GeneratedAt.AddSeconds(-12.4), Threshold);

        Assert.Equal(Liveness.NotLive, status.Liveness);
        Assert.Equal(TimeSpan.FromSeconds(12.4), status.Lag);
    }

    [Fact]
    [Trait("AC", "AC-28")]
    [Trait("BR", "BR-UI-04")]
    public void Lag_exactly_at_the_threshold_is_not_live()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, GeneratedAt - Threshold, Threshold);

        Assert.Equal(Liveness.NotLive, status.Liveness);
    }

    [Fact]
    [Trait("AC", "AC-33")]
    [Trait("BR", "BR-UI-04")]
    public void Event_ahead_of_the_processor_clock_counts_as_zero_lag_and_live()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, GeneratedAt.AddSeconds(5), Threshold);

        Assert.Equal(Liveness.Live, status.Liveness);
        Assert.Equal(TimeSpan.Zero, status.Lag);
    }

    [Fact]
    [Trait("BR", "BR-UI-04")]
    public void Zero_threshold_makes_every_board_with_data_not_live()
    {
        var status = LivenessEvaluator.Evaluate(GeneratedAt, GeneratedAt, TimeSpan.Zero);

        Assert.Equal(Liveness.NotLive, status.Liveness);
        Assert.Equal(TimeSpan.Zero, status.Lag);
    }
}
