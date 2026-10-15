namespace HighwaySpeedEnforcement.Processor.Application;

/// <summary>Outbound port for the ingest counters (BR-OPS-02). Implementations never use the plate as a tag.</summary>
public interface IIngestMetrics
{
    /// <summary>Every message handed to the sink, before validation.</summary>
    void Received();

    /// <summary>A measurement that changed state.</summary>
    void Accepted();

    void Rejected(RejectReason reason);

    /// <summary>A supported but disabled message type, currently heartbeats when <see cref="ProcessorSettings.HeartbeatsEnabled"/> is false.</summary>
    void Ignored();

    /// <summary>The time from the event time of an applied message to its processing (the wall clock of the processor minus the timestamp).</summary>
    void Latency(TimeSpan latency);
}
