namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>
/// Inbound port of the processor, implemented by the pipeline. An input adapter (MQTT, a test publisher) pushes decoded
/// messages into it in arrival order, one at a time.
/// </summary>
public interface IIngestSink
{
    /// <summary>
    /// Validates, stamps and queues the message. Completes once the message is in a partition buffer or was rejected; a
    /// full buffer makes it wait, which delays the adapter's acknowledgement (backpressure, ADR-0001). Not thread-safe:
    /// callers submit sequentially.
    /// </summary>
    ValueTask SubmitAsync(IncomingMessage message, CancellationToken cancellationToken);

    /// <summary>Counts and logs a payload the adapter could not decode (<see cref="RejectReason.Malformed"/>, <see cref="RejectReason.UnknownType"/>).</summary>
    void ReportUndecodable(RejectReason reason);
}
