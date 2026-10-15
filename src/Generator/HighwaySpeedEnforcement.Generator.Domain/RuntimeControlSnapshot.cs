namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// Detached, immutable view of <see cref="RuntimeControl"/> at one instant (design section 11). <paramref name="DisabledCameras"/>
/// is sorted ascending; equality is by value, so the runner can tell whether anything changed between two ticks.
/// </summary>
public sealed record RuntimeControlSnapshot(bool TrafficPaused, int VehiclesPerTick, bool HeartbeatsEnabled, IReadOnlyList<int> DisabledCameras)
{
    public bool Equals(RuntimeControlSnapshot? other)
    {
        return other is not null &&
            TrafficPaused == other.TrafficPaused &&
            VehiclesPerTick == other.VehiclesPerTick &&
            HeartbeatsEnabled == other.HeartbeatsEnabled &&
            DisabledCameras.SequenceEqual(other.DisabledCameras);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TrafficPaused);
        hash.Add(VehiclesPerTick);
        hash.Add(HeartbeatsEnabled);

        foreach (int camera in DisabledCameras)
        {
            hash.Add(camera);
        }

        return hash.ToHashCode();
    }

    /// <summary>The options of one tick: the fixed <paramref name="baseline"/> (spawn limit, speed model) with this snapshot applied.</summary>
    public SimulationOptions ToSimulationOptions(SimulationOptions baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        return baseline with
        {
            TrafficPaused = TrafficPaused,
            VehiclesPerTick = VehiclesPerTick,
            HeartbeatsEnabled = HeartbeatsEnabled,
            DisabledCameras = DisabledCameras,
        };
    }
}
