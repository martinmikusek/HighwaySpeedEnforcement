using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Payload validation (BR-ING-01) in the order plate, camera, speed, timestamp; the first violated rule is the reason.
/// The future-timestamp check uses the processor's clock and <see cref="ProcessorSettings.FutureTimestampTolerance"/>.
/// </summary>
public sealed class MeasurementValidator
{
    public const int MaxPlateLength = 16;

    private readonly TimeProvider _timeProvider;
    private readonly long _futureToleranceTicks;

    public MeasurementValidator(ProcessorSettings settings, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _futureToleranceTicks = settings.FutureTimestampTolerance.Ticks;
    }

    public bool TryValidate(IncomingMeasurement incoming, out Measurement measurement, out RejectReason reason)
    {
        measurement = default;

        if (string.IsNullOrWhiteSpace(incoming.NumberPlate) || incoming.NumberPlate.Length > MaxPlateLength)
        {
            reason = RejectReason.InvalidPlate;
            return false;
        }

        if (!HighwayLayout.IsCamera(incoming.CameraId))
        {
            reason = RejectReason.InvalidCamera;
            return false;
        }

        if (!float.IsFinite(incoming.CurrentSpeed) || incoming.CurrentSpeed < 0)
        {
            reason = RejectReason.InvalidSpeed;
            return false;
        }

        if (!IsValidTimestamp(incoming.TimestampTicks))
        {
            reason = RejectReason.InvalidTimestamp;
            return false;
        }

        measurement = new Measurement(incoming.NumberPlate, incoming.TimestampTicks, incoming.CurrentSpeed, incoming.CameraId);
        reason = default;
        return true;
    }

    /// <summary>A heartbeat is validated for camera and timestamp only (BR-TEL-03).</summary>
    public bool TryValidate(IncomingHeartbeat incoming, out RejectReason reason)
    {
        if (!HighwayLayout.IsCamera(incoming.CameraId))
        {
            reason = RejectReason.InvalidCamera;
            return false;
        }

        if (!IsValidTimestamp(incoming.TimestampTicks))
        {
            reason = RejectReason.InvalidTimestamp;
            return false;
        }

        reason = default;
        return true;
    }

    private bool IsValidTimestamp(long ticks)
    {
        if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        long nowTicks = _timeProvider.GetUtcNow().UtcTicks;

        // Watermarks never decrease (BR-LB-11), so one far-future timestamp would expire every board for good.
        return ticks - nowTicks <= _futureToleranceTicks;
    }
}
