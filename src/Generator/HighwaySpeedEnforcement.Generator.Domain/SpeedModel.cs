namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// Speed of a vehicle at each camera (design section 4, BR-SIM-05): a desired speed per vehicle and, per camera, a step that
/// pulls toward it plus noise. Results are rounded to 0.1 km/h, so equal speeds between vehicles occur naturally (BR-LB-05).
/// </summary>
public sealed class SpeedModel
{
    private readonly SpeedOptions _options;

    public SpeedModel(SpeedOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The vehicle's desired speed: Normal(baseline - 5, DesiredStdDev) clamped to [60, 200].</summary>
    public double DrawDesired(DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        double desired = random.NextGaussian(_options.BaselineKmh + SpeedOptions.DesiredOffsetKmh, _options.DesiredStdDev);

        return Math.Clamp(desired, SpeedOptions.MinDesiredKmh, SpeedOptions.MaxDesiredKmh);
    }

    /// <summary>v_k = v_(k-1) + 0.3 * (v* - v_(k-1)) + N(0, StepStdDev), at least 0, rounded to 0.1.</summary>
    public float Next(float previous, double desired, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);

        double next = previous + (SpeedOptions.PullFactor * (desired - previous)) + random.NextGaussian(0.0, _options.StepStdDev);
        double rounded = Math.Round(Math.Max(0.0, next) / SpeedOptions.ResolutionKmh) * SpeedOptions.ResolutionKmh;

        return (float)rounded;
    }
}
