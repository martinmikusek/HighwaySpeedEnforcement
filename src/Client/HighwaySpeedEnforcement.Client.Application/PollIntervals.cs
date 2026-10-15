namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>The periods the user can choose from.</summary>
public static class PollIntervals
{
    public static IReadOnlyList<TimeSpan> Standard { get; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
    ];

    /// <summary>The standard periods plus the configured one when it is not among them, ascending.</summary>
    public static IReadOnlyList<TimeSpan> OptionsFor(TimeSpan configured)
    {
        return Standard.Append(configured).Distinct().Order().ToArray();
    }
}
