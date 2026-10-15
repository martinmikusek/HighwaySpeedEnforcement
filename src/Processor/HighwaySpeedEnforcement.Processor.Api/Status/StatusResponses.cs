using HighwaySpeedEnforcement.Processor.Api.Configuration;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Infrastructure;

namespace HighwaySpeedEnforcement.Processor.Api.Status;

/// <summary>Body of the health endpoints (BR-API-03): the broker state is informative, the pipeline state decides.</summary>
public sealed record HealthResponse(string Status, string Pipeline, string Broker);

/// <summary>Body of <c>GET /api/status</c> (BR-OPS-02): counters, gauges, broker state and the effective settings.</summary>
public sealed record StatusResponse(CountersResponse Counters, IngestResponse Ingest, BrokerResponse Broker, SettingsResponse Settings);

public sealed record CountersResponse(long Received, long Accepted, long Ignored, long RejectedTotal, IReadOnlyDictionary<string, long> Rejected)
{
    public static CountersResponse From(IngestCounters counters)
    {
        return new CountersResponse(
            counters.Received,
            counters.Accepted,
            counters.Ignored,
            counters.RejectedTotal,
            counters.Rejected.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
    }
}

public sealed record IngestResponse(
    bool IsRunning,
    int TrackedVehicles,
    int BufferDepth,
    IReadOnlyList<int> PartitionDepths,
    DateTimeOffset? NewestProcessedEventUtc,
    double? IngestLagSeconds,
    DateTimeOffset? LastReceivedAtUtc,
    double? LatencyAvgSeconds,
    double? LatencyMaxSeconds,
    long LatencySamples)
{
    public static IngestResponse From(IngestStatus status)
    {
        return new IngestResponse(
            status.IsRunning,
            status.TrackedVehicles,
            status.BufferDepth,
            status.PartitionDepths,
            status.NewestProcessedEvent,
            status.IngestLag?.TotalSeconds,
            status.LastReceivedAt,
            status.Latency?.Average.TotalSeconds,
            status.Latency?.Maximum.TotalSeconds,
            status.Latency?.Count ?? 0);
    }
}

public sealed record BrokerResponse(bool Enabled, bool Connected, DateTimeOffset? LastConnectedAtUtc, string Host, int Port, string Topic, string ClientId)
{
    public static BrokerResponse From(MqttSourceOptions options, MqttMeasurementSource? source)
    {
        return new BrokerResponse(options.Enabled, source?.IsConnected ?? false, source?.LastConnectedAt, options.Host, options.Port, options.Topic, options.ClientId);
    }

    public string State => !Enabled ? "Disabled" : Connected ? "Connected" : "Disconnected";
}

public sealed record SettingsResponse(
    int PartitionCount,
    int IngestChannelCapacity,
    string CameraBoardWindow,
    string GlobalBoardLinger,
    string InactivityTimeout,
    string FutureTimestampTolerance,
    string IngestLagWarning,
    bool HeartbeatsEnabled)
{
    public static SettingsResponse From(ProcessorSettings settings)
    {
        return new SettingsResponse(
            settings.PartitionCount,
            settings.IngestChannelCapacity,
            ProcessorConfiguration.FormatDuration(settings.CameraBoardWindow),
            ProcessorConfiguration.FormatDuration(settings.GlobalBoardLinger),
            ProcessorConfiguration.FormatDuration(settings.InactivityTimeout),
            ProcessorConfiguration.FormatDuration(settings.FutureTimestampTolerance),
            ProcessorConfiguration.FormatDuration(settings.IngestLagWarning),
            settings.HeartbeatsEnabled);
    }
}
