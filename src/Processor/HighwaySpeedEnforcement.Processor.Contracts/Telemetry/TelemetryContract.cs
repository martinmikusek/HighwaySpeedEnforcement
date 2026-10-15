namespace HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

/// <summary>Constants of the telemetry contract (<c>docs/contracts/telemetry-messages.md</c>).</summary>
public static class TelemetryContract
{
    /// <summary>The one MQTT topic for all cameras and both message types.</summary>
    public const string Topic = "highway/telemetry/v1";

    /// <summary>Value of <c>Type</c> on a <see cref="MeasurementMessage"/>.</summary>
    public const string MeasurementType = "Measurement";

    /// <summary>Value of <c>Type</c> on a <see cref="HeartbeatMessage"/>.</summary>
    public const string HeartbeatType = "Heartbeat";

    /// <summary>Name of the property that selects the message type.</summary>
    public const string TypePropertyName = "Type";

    /// <summary>A payload longer than this is malformed.</summary>
    public const int MaxPayloadBytes = 1024;

    /// <summary>Client id of the processor's persistent MQTT session.</summary>
    public const string ProcessorClientId = "speed-processor";

    /// <summary>Prefix of the generator's client id; a run id follows.</summary>
    public const string GeneratorClientIdPrefix = "speed-generator-";
}
