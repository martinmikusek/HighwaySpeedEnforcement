namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>Output port, implemented in Infrastructure: delivers a tick's messages to the broker (design section 5).</summary>
public interface IMeasurementPublisher : IAsyncDisposable
{
    /// <summary>Completes when every message of the tick is acknowledged; throws <see cref="OperationCanceledException"/> when cancelled while waiting.</summary>
    Task PublishTickAsync(TickOutput tick, CancellationToken cancellationToken);
}
