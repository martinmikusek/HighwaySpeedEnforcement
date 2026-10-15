namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>BR-UI-04: a board is live when <c>GeneratedAt − LatestEvent</c> is below <c>LiveLagThreshold</c>.</summary>
public static class LivenessEvaluator
{
    public static BoardStatus Evaluate(DateTimeOffset generatedAt, DateTimeOffset? latestEvent, TimeSpan threshold)
    {
        if (latestEvent is null)
        {
            return BoardStatus.NoData;
        }

        var lag = generatedAt - latestEvent.Value;

        // An event ahead of the processor clock (clock skew) is as fresh as it gets.
        if (lag < TimeSpan.Zero)
        {
            lag = TimeSpan.Zero;
        }

        return new BoardStatus(lag < threshold ? Liveness.Live : Liveness.NotLive, lag);
    }
}
