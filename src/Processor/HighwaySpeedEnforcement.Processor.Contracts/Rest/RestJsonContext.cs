using System.Text.Json.Serialization;

namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>
/// Source-generated JSON context for the REST API: camelCase names, nulls written (a board that never received data has
/// <c>latestEventUtc: null</c>), times as ISO 8601 UTC text, unknown properties ignored so additive changes stay compatible.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(UtcDateTimeOffsetConverter)],
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(TrafficSnapshotDto))]
public sealed partial class RestJsonContext : JsonSerializerContext
{
}
