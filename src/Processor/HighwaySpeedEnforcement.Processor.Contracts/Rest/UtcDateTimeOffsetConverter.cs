using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HighwaySpeedEnforcement.Processor.Contracts.Rest;

/// <summary>
/// Writes every time as ISO 8601 UTC with seven fractional digits and a <c>Z</c> suffix (<c>2026-10-14T14:03:10.0000000Z</c>),
/// as the contract and its example require; the default converter would write <c>+00:00</c> and trim the fraction.
/// Reads any ISO 8601 text and normalizes it to UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDateTimeOffset().ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
    }
}
