using System.Text.Json.Serialization;

namespace HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

/// <summary>
/// Source-generated JSON context for the telemetry messages: PascalCase names exactly as declared, every property required
/// (a missing one fails deserialization, which the decoder reports as <c>Malformed</c>), unknown properties ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    RespectRequiredConstructorParameters = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MeasurementMessage))]
[JsonSerializable(typeof(HeartbeatMessage))]
public sealed partial class TelemetryJsonContext : JsonSerializerContext
{
}
