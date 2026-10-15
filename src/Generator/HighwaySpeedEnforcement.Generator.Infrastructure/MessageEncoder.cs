using System.Text.Json;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.Generator.Infrastructure;

/// <summary>
/// Encodes the generator's own records into the wire form owned by the processor (<c>docs/contracts/telemetry-messages.md</c>),
/// through the contract's source-generated context only.
/// </summary>
public static class MessageEncoder
{
    public static byte[] EncodeMeasurement(Measurement measurement)
    {
        var message = new MeasurementMessage(
            TelemetryContract.MeasurementType,
            measurement.NumberPlate,
            measurement.TimestampTicks,
            measurement.CurrentSpeed,
            measurement.CameraId);

        return JsonSerializer.SerializeToUtf8Bytes(message, TelemetryJsonContext.Default.MeasurementMessage);
    }

    public static byte[] EncodeHeartbeat(int cameraId, long timestampTicks)
    {
        var message = new HeartbeatMessage(TelemetryContract.HeartbeatType, cameraId, timestampTicks);

        return JsonSerializer.SerializeToUtf8Bytes(message, TelemetryJsonContext.Default.HeartbeatMessage);
    }
}
