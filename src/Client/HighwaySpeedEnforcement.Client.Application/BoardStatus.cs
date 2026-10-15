namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>Liveness of one board and its lag (<c>GeneratedAt − LatestEvent</c> of the same snapshot); the lag is null without data.</summary>
public sealed record BoardStatus(Liveness Liveness, TimeSpan? Lag)
{
    public static BoardStatus NoData { get; } = new(Liveness.NoData, null);
}
