using System.Text.Json;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.Processor.Infrastructure;

/// <summary>
/// Turns a telemetry payload into an <see cref="IncomingMessage"/> by the rules of <c>docs/contracts/telemetry-messages.md</c>:
/// size limit, <c>Type</c> resolution, then the source-generated JSON context of the contracts. Values are not validated
/// here (that is <see cref="MeasurementValidator"/>); only the shape of the payload is.
/// </summary>
public sealed class MessageDecoder
{
    private readonly bool _heartbeatsEnabled;

    public MessageDecoder(ProcessorSettings settings)
    {
        _heartbeatsEnabled = settings.HeartbeatsEnabled;
    }

    public DecodeResult Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length > TelemetryContract.MaxPayloadBytes)
        {
            return DecodeResult.Malformed;
        }

        try
        {
            if (!TryReadType(payload, out var type))
            {
                return DecodeResult.Malformed;
            }

            switch (type)
            {
                case TelemetryContract.MeasurementType:
                    var measurement = JsonSerializer.Deserialize(payload, TelemetryJsonContext.Default.MeasurementMessage)!;
                    return DecodeResult.Decoded(new IncomingMeasurement(
                        measurement.NumberPlate,
                        measurement.TimestampTicks,
                        measurement.CurrentSpeed,
                        measurement.CameraId));

                case TelemetryContract.HeartbeatType:
                    if (!_heartbeatsEnabled)
                    {
                        return DecodeResult.Ignored;
                    }

                    var heartbeat = JsonSerializer.Deserialize(payload, TelemetryJsonContext.Default.HeartbeatMessage)!;
                    return DecodeResult.Decoded(new IncomingHeartbeat(heartbeat.CameraId, heartbeat.TimestampTicks));

                default:
                    return DecodeResult.UnknownType;
            }
        }
        catch (JsonException)
        {
            return DecodeResult.Malformed;
        }
    }

    /// <summary>
    /// Finds the top-level <c>Type</c> string without materializing the document. False when the payload is not a single
    /// JSON object, <c>Type</c> is absent or is not a string; a syntax error surfaces as <see cref="JsonException"/>.
    /// </summary>
    private static bool TryReadType(ReadOnlySpan<byte> payload, out string type)
    {
        type = string.Empty;
        var reader = new Utf8JsonReader(payload);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        var found = false;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals(TelemetryContract.TypePropertyName);
            reader.Read();

            if (isType)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    return false;
                }

                type = reader.GetString()!;
                found = true;
            }
            else
            {
                reader.TrySkip();
            }
        }

        // Reading on validates the rest of the document, so trailing garbage is malformed instead of silently ignored.
        while (reader.Read())
        {
        }

        return found;
    }
}
