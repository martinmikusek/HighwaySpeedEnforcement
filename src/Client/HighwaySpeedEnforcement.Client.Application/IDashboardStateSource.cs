namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>What the presentation subscribes to: the current <see cref="DashboardState"/> and a change event raised on any thread.</summary>
public interface IDashboardStateSource
{
    event EventHandler<DashboardState>? StateChanged;

    DashboardState State { get; }
}
