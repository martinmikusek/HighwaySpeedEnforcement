namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// How long entries and tracks stay live (BR-CFG-01). <see cref="Timeout.InfiniteTimeSpan"/> means infinite; an infinite
/// lifetime gives <see cref="long.MaxValue"/> as expiry, and additions saturate instead of overflowing.
/// </summary>
public sealed class LivenessPolicy
{
    public LivenessPolicy(TimeSpan cameraBoardWindow, TimeSpan globalBoardLinger, TimeSpan inactivityTimeout)
    {
        if (!IsInfinite(cameraBoardWindow) && cameraBoardWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cameraBoardWindow), cameraBoardWindow, "CameraBoardWindow must be greater than zero or infinite (BR-CFG-01).");
        }

        if (!IsInfinite(globalBoardLinger) && globalBoardLinger < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(globalBoardLinger), globalBoardLinger, "GlobalBoardLinger must be zero or more, or infinite (BR-CFG-01).");
        }

        if (IsInfinite(inactivityTimeout) || inactivityTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(inactivityTimeout), inactivityTimeout, "InactivityTimeout must be finite and greater than zero (BR-CFG-01).");
        }

        CameraBoardWindow = cameraBoardWindow;
        GlobalBoardLinger = globalBoardLinger;
        InactivityTimeout = inactivityTimeout;
    }

    public TimeSpan CameraBoardWindow { get; }

    public TimeSpan GlobalBoardLinger { get; }

    public TimeSpan InactivityTimeout { get; }

    public static bool IsInfinite(TimeSpan duration)
    {
        return duration == Timeout.InfiniteTimeSpan;
    }

    /// <summary>Expiry of a camera entry measured at <paramref name="timestampTicks"/> (BR-LB-07).</summary>
    public long CameraExpiry(long timestampTicks)
    {
        return Add(timestampTicks, CameraBoardWindow);
    }

    /// <summary>Expiry of an in-flight vehicle's global entry and track after its last measurement (BR-ING-06).</summary>
    public long InFlightExpiry(long lastTimestampTicks)
    {
        return Add(lastTimestampTicks, InactivityTimeout);
    }

    /// <summary>Expiry of a completed vehicle's global entry after its Camera 10 measurement (BR-LB-08).</summary>
    public long CompletedExpiry(long lastTimestampTicks)
    {
        return Add(lastTimestampTicks, GlobalBoardLinger);
    }

    private static long Add(long timestampTicks, TimeSpan duration)
    {
        if (IsInfinite(duration))
        {
            return long.MaxValue;
        }

        long durationTicks = duration.Ticks;

        return timestampTicks > long.MaxValue - durationTicks ? long.MaxValue : timestampTicks + durationTicks;
    }
}
