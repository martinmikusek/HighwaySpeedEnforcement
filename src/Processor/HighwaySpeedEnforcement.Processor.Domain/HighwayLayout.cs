namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>Fixed layout of the road and the boards (BR-SIM-01, BR-LB-01, BR-LB-02, BR-LB-03).</summary>
public static class HighwayLayout
{
    public const int FirstCamera = 1;

    public const int LastCamera = 10;

    public const int CameraCount = LastCamera - FirstCamera + 1;

    public const int BoardSize = 10;

    /// <summary>Accepted measurements a vehicle needs before it appears on the global board (BR-LB-03).</summary>
    public const int MinimumMeasurementsForGlobalBoard = 3;

    public static bool IsCamera(int cameraId)
    {
        return cameraId is >= FirstCamera and <= LastCamera;
    }
}
