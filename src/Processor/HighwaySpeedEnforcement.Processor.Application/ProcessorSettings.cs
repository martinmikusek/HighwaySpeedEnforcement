using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Processor parameters (BR-CFG-01, BR-CFG-03). A plain object: the host binds it from configuration and calls
/// <see cref="Validate"/> before anything is built. Infinite durations are <see cref="Timeout.InfiniteTimeSpan"/>.
/// </summary>
public sealed class ProcessorSettings
{
    public int PartitionCount { get; set; } = 4;

    /// <summary>Bounded channel capacity per partition; a full channel delays the acknowledgement (ADR-0001).</summary>
    public int IngestChannelCapacity { get; set; } = 10_000;

    public TimeSpan CameraBoardWindow { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan GlobalBoardLinger { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>A measurement further ahead of the processor's clock is <see cref="RejectReason.InvalidTimestamp"/> (BR-ING-01).</summary>
    public TimeSpan FutureTimestampTolerance { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Ingest lag above which a warning is logged (BR-OPS-02).</summary>
    public TimeSpan IngestLagWarning { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>When false, heartbeats are counted as ignored and change nothing (BR-TEL-03).</summary>
    public bool HeartbeatsEnabled { get; set; } = true;

    /// <summary>Every violated rule as one message; empty when the settings are valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PartitionCount < 1)
        {
            errors.Add($"{nameof(PartitionCount)} must be at least 1 (was {PartitionCount}).");
        }

        if (IngestChannelCapacity < 1)
        {
            errors.Add($"{nameof(IngestChannelCapacity)} must be at least 1 (was {IngestChannelCapacity}).");
        }

        if (!LivenessPolicy.IsInfinite(CameraBoardWindow) && CameraBoardWindow <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(CameraBoardWindow)} must be greater than zero or Infinite (was {CameraBoardWindow}).");
        }

        if (!LivenessPolicy.IsInfinite(GlobalBoardLinger) && GlobalBoardLinger < TimeSpan.Zero)
        {
            errors.Add($"{nameof(GlobalBoardLinger)} must be zero or more, or Infinite (was {GlobalBoardLinger}).");
        }

        if (LivenessPolicy.IsInfinite(InactivityTimeout) || InactivityTimeout <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(InactivityTimeout)} must be finite and greater than zero (was {InactivityTimeout}).");
        }

        if (FutureTimestampTolerance < TimeSpan.Zero)
        {
            errors.Add($"{nameof(FutureTimestampTolerance)} must be zero or more (was {FutureTimestampTolerance}).");
        }

        if (IngestLagWarning <= TimeSpan.Zero)
        {
            errors.Add($"{nameof(IngestLagWarning)} must be greater than zero (was {IngestLagWarning}).");
        }

        return errors;
    }

    /// <summary>The domain policy for valid settings; throws for invalid ones, so call <see cref="Validate"/> first.</summary>
    public LivenessPolicy ToLivenessPolicy()
    {
        return new LivenessPolicy(CameraBoardWindow, GlobalBoardLinger, InactivityTimeout);
    }
}
