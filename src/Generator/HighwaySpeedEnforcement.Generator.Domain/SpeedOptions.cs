namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>Parameters of the speed model (design section 4, BR-SIM-05). The constants are the design's fixed values.</summary>
public sealed record SpeedOptions
{
    /// <summary>Weight of the pull toward the vehicle's desired speed at each camera.</summary>
    public const double PullFactor = 0.3;

    /// <summary>The mean desired speed lies this far below the baseline.</summary>
    public const double DesiredOffsetKmh = -5.0;

    public const double MinDesiredKmh = 60.0;

    public const double MaxDesiredKmh = 200.0;

    /// <summary>Resolution of a measuring device; speeds are rounded to it.</summary>
    public const double ResolutionKmh = 0.1;

    public double BaselineKmh { get; init; } = 130.0;

    public double DesiredStdDev { get; init; } = 15.0;

    public double StepStdDev { get; init; } = 3.0;
}
