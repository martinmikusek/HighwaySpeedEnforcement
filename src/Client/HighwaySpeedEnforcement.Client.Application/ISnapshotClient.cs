namespace HighwaySpeedEnforcement.Client.Application;

/// <summary>Port: obtains one traffic snapshot from the processor (BR-API-01). Implemented by Infrastructure.</summary>
public interface ISnapshotClient
{
    /// <summary>Returns a valid snapshot or throws <see cref="SnapshotUnavailableException"/>; caller cancellation propagates as <see cref="OperationCanceledException"/>.</summary>
    Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
