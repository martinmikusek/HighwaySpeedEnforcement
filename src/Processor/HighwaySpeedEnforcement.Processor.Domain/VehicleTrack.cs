namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>Mutable working state of one in-flight vehicle (BR-ING-04). Owned by one <see cref="VehicleTracker"/>.</summary>
public sealed class VehicleTrack
{
    public VehicleTrack(string plate)
    {
        Plate = plate;
    }

    public string Plate { get; }

    public int Count { get; private set; }

    public double SpeedSum { get; private set; }

    public int LastCamera { get; private set; }

    public long LastTimestampTicks { get; private set; }

    /// <summary>Cumulative average: speed sum over count (BR-ING-04).</summary>
    public double Average => SpeedSum / Count;

    public void Add(Measurement measurement)
    {
        Count++;
        SpeedSum += measurement.Speed;
        LastCamera = measurement.CameraId;
        LastTimestampTicks = Math.Max(LastTimestampTicks, measurement.TimestampTicks);
    }
}
