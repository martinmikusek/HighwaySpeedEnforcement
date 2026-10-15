using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>The ingest counters as plain numbers, readable without a metrics stack (BR-OPS-02, <c>GET /api/status</c>).</summary>
public sealed record IngestCounters(long Received, long Accepted, long Ignored, IReadOnlyDictionary<RejectReason, long> Rejected)
{
    public long RejectedTotal => Rejected.Values.Sum();
}
