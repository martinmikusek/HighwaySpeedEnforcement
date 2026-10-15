namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// Computes the watermarks in arrival order from valid measurements (BR-LB-11): per camera and global maxima that never
/// decrease. Sequential, not thread-safe: it is touched only by the single submitting sequence.
/// </summary>
public sealed class WatermarkClock
{
    private const long Never = long.MinValue;

    private readonly long[] _camera = Enumerable.Repeat(Never, HighwayLayout.CameraCount).ToArray();
    private long _global = Never;

    /// <summary>Newest event time seen so far, null before the first observation.</summary>
    public long? GlobalTicks => _global == Never ? null : _global;

    public long? CameraTicks(int cameraId)
    {
        long value = _camera[Index(cameraId)];

        return value == Never ? null : value;
    }

    /// <summary>Raises the camera's and the global maximum and returns the stamp reflecting the state after the observation.</summary>
    public Stamp Observe(int cameraId, long timestampTicks)
    {
        int index = Index(cameraId);

        if (timestampTicks > _camera[index])
        {
            _camera[index] = timestampTicks;
        }

        if (timestampTicks > _global)
        {
            _global = timestampTicks;
        }

        return new Stamp(_global, _camera[index]);
    }

    private static int Index(int cameraId)
    {
        if (!HighwayLayout.IsCamera(cameraId))
        {
            throw new ArgumentOutOfRangeException(nameof(cameraId), cameraId, "Not a camera id.");
        }

        return cameraId - HighwayLayout.FirstCamera;
    }
}
