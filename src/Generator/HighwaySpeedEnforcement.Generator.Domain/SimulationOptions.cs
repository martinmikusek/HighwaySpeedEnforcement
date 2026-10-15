namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>Parameters of the state machine (design sections 2, 5 and 6).</summary>
public sealed record SimulationOptions
{
    public int VehiclesPerTick { get; init; } = 100;

    /// <summary>After this tick no new vehicles spawn; null means spawn forever.</summary>
    public long? SpawnUntilTick { get; init; }

    /// <summary>While true no new vehicles spawn (the operator's pause, design section 11); the ones on the road finish.</summary>
    public bool TrafficPaused { get; init; }

    /// <summary>Cameras whose measurements are not emitted (AC-34); vehicles still pass them.</summary>
    public IReadOnlyCollection<int> DisabledCameras { get; init; } = [];

    /// <summary>When true, an enabled camera that emitted nothing in a tick sends a heartbeat (BR-TEL-03).</summary>
    public bool HeartbeatsEnabled { get; init; }

    public SpeedOptions Speed { get; init; } = new();
}
