using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;
using HighwaySpeedEnforcement.Unit.Tests.Contracts;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Infrastructure;

/// <summary>Contract level: the generator's encoding equals the approved example files of <c>docs/contracts/examples</c>.</summary>
public sealed class MessageEncoderTests
{
    private const long ExampleTicks = 639275833894000000;

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void EncodeMeasurement_equals_the_example_document()
    {
        var bytes = MessageEncoder.EncodeMeasurement(new Measurement("1AB 2345", ExampleTicks, 131.2f, 5));

        var expected = JsonNode.Parse(ContractExamples.Read("measurement.json"));
        var actual = JsonNode.Parse(bytes);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected}, got {actual}");
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void EncodeHeartbeat_equals_the_example_document()
    {
        var bytes = MessageEncoder.EncodeHeartbeat(5, ExampleTicks);

        var expected = JsonNode.Parse(ContractExamples.Read("heartbeat.json"));
        var actual = JsonNode.Parse(bytes);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected}, got {actual}");
    }

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void EncodeMeasurement_writes_the_documented_property_names_in_order_with_types()
    {
        using var document = JsonDocument.Parse(MessageEncoder.EncodeMeasurement(new Measurement("1AB 2345", ExampleTicks, 131.2f, 5)));
        var properties = document.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.ValueKind)).ToArray();

        Assert.Equal(
            [
                ("Type", JsonValueKind.String),
                ("NumberPlate", JsonValueKind.String),
                ("TimestampTicks", JsonValueKind.Number),
                ("CurrentSpeed", JsonValueKind.Number),
                ("CameraId", JsonValueKind.Number),
            ],
            properties);
        Assert.Equal(TelemetryContract.MeasurementType, document.RootElement.GetProperty("Type").GetString());
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void EncodeHeartbeat_writes_the_documented_property_names_in_order_with_types()
    {
        using var document = JsonDocument.Parse(MessageEncoder.EncodeHeartbeat(5, ExampleTicks));
        var properties = document.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.ValueKind)).ToArray();

        Assert.Equal(
            [
                ("Type", JsonValueKind.String),
                ("CameraId", JsonValueKind.Number),
                ("TimestampTicks", JsonValueKind.Number),
            ],
            properties);
        Assert.Equal(TelemetryContract.HeartbeatType, document.RootElement.GetProperty("Type").GetString());
    }

    [Theory]
    [InlineData(131.2f, "131.2")]
    [InlineData(99.9f, "99.9")]
    [InlineData(0f, "0")]
    [InlineData(147f, "147")]
    public void EncodeMeasurement_writes_the_speed_as_the_shortest_round_trip_text(float speed, string expectedText)
    {
        string json = Encoding.UTF8.GetString(MessageEncoder.EncodeMeasurement(new Measurement("X", ExampleTicks, speed, 1)));

        Assert.Contains($"\"CurrentSpeed\":{expectedText},", json, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void Encoded_payloads_of_generated_measurements_never_exceed_the_contract_limit()
    {
        var random = new DeterministicRandom(20261014);
        var permutation = PlatePermutation.FromSeed(PlateFormat.Size, random);
        var simulation = new Simulation(new SimulationOptions { HeartbeatsEnabled = true, SpawnUntilTick = 2 }, permutation, random);
        int longestPlate = 0;

        for (int tick = 1; tick <= 14; tick++)
        {
            var output = simulation.Tick(DateTime.MaxValue.Ticks - tick);

            foreach (var measurement in output.Measurements)
            {
                longestPlate = Math.Max(longestPlate, measurement.NumberPlate.Length);
                Assert.True(MessageEncoder.EncodeMeasurement(measurement).Length <= TelemetryContract.MaxPayloadBytes);
            }

            foreach (int camera in output.HeartbeatCameras)
            {
                Assert.True(MessageEncoder.EncodeHeartbeat(camera, output.TimestampTicks).Length <= TelemetryContract.MaxPayloadBytes);
            }
        }

        Assert.Equal(PlateFormat.PlateLength, longestPlate);
        Assert.True(PlateFormat.Decode(PlateFormat.Size - 1).Length <= 16, "The longest plate must fit the contract's 16-character limit.");
    }

    [Fact]
    public void Encoded_measurement_round_trips_through_the_contract_context()
    {
        var measurement = new Measurement("9TZ 9999", ExampleTicks, 200.1f, 10);

        var decoded = JsonSerializer.Deserialize(MessageEncoder.EncodeMeasurement(measurement), TelemetryJsonContext.Default.MeasurementMessage);

        Assert.Equal(new MeasurementMessage(TelemetryContract.MeasurementType, "9TZ 9999", ExampleTicks, 200.1f, 10), decoded);
    }
}
