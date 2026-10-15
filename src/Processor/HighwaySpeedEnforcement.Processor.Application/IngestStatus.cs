namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Read-only view of the pipeline for health and status (BR-OPS-02). <paramref name="IngestLag"/> is wall-clock time
/// minus the newest processed event time (it grows between the one-second bursts of the generator); <paramref name="Latency"/> is
/// the time from the event time of a message to its processing, averaged and maximized over the last ten seconds (null while nothing
/// was processed). Both are meaningful only when the generator's and the processor's clocks agree.
/// </summary>
public sealed record IngestStatus(
    bool IsRunning,
    int TrackedVehicles,
    IReadOnlyList<int> PartitionDepths,
    DateTimeOffset? NewestProcessedEvent,
    TimeSpan? IngestLag,
    DateTimeOffset? LastReceivedAt,
    LatencyStats? Latency = null)
{
    public int BufferDepth => PartitionDepths.Sum();
}
