using System.Text.Json;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.Unit.Tests.Contracts;

/// <summary>Contract tests of <c>docs/contracts/telemetry-messages.md</c> against its approved example files.</summary>
public sealed class TelemetryMessageContractTests
{
    private const long ExampleTicks = 639275833894000000;

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void Measurement_example_deserializes_into_the_wire_type()
    {
        var json = ContractExamples.Read("measurement.json");

        var message = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage);

        Assert.Equal(new MeasurementMessage(TelemetryContract.MeasurementType, "1AB 2345", ExampleTicks, 131.2f, 5), message);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Heartbeat_example_deserializes_into_the_wire_type()
    {
        var json = ContractExamples.Read("heartbeat.json");

        var message = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.HeartbeatMessage);

        Assert.Equal(new HeartbeatMessage(TelemetryContract.HeartbeatType, 5, ExampleTicks), message);
    }

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void Measurement_serializes_to_exactly_the_example_text()
    {
        var expected = ContractExamples.Read("measurement.json").Trim();
        var message = new MeasurementMessage(TelemetryContract.MeasurementType, "1AB 2345", ExampleTicks, 131.2f, 5);

        var json = JsonSerializer.Serialize(message, TelemetryJsonContext.Default.MeasurementMessage);

        Assert.Equal(expected, json);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Heartbeat_serializes_to_exactly_the_example_text()
    {
        var expected = ContractExamples.Read("heartbeat.json").Trim();
        var message = new HeartbeatMessage(TelemetryContract.HeartbeatType, 5, ExampleTicks);

        var json = JsonSerializer.Serialize(message, TelemetryJsonContext.Default.HeartbeatMessage);

        Assert.Equal(expected, json);
    }

    [Theory]
    [InlineData(131.2f, "131.2")]
    [InlineData(0f, "0")]
    [InlineData(147f, "147")]
    [InlineData(0.1f, "0.1")]
    public void Speed_is_written_as_the_shortest_round_tripping_text(float speed, string expectedText)
    {
        var message = new MeasurementMessage(TelemetryContract.MeasurementType, "X", ExampleTicks, speed, 1);

        var json = JsonSerializer.Serialize(message, TelemetryJsonContext.Default.MeasurementMessage);
        var roundTripped = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage);

        Assert.Contains($"\"CurrentSpeed\":{expectedText},", json, StringComparison.Ordinal);
        Assert.Equal(speed, roundTripped!.CurrentSpeed);
    }

    [Theory]
    [InlineData("""{"NumberPlate":"1AB 2345","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"1AB 2345","CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"1AB 2345","TimestampTicks":1,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"1AB 2345","TimestampTicks":1,"CurrentSpeed":131.2}""")]
    [Trait("BR", "BR-ING-01")]
    public void Measurement_with_a_missing_property_fails_to_deserialize(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage));
    }

    [Theory]
    [InlineData("""{"Type":"Heartbeat","TimestampTicks":1}""")]
    [InlineData("""{"Type":"Heartbeat","CameraId":5}""")]
    [InlineData("""{"CameraId":5,"TimestampTicks":1}""")]
    [Trait("BR", "BR-ING-01")]
    public void Heartbeat_with_a_missing_property_fails_to_deserialize(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.HeartbeatMessage));
    }

    [Theory]
    [InlineData("""{"Type":"Measurement","NumberPlate":42,"TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":"1","CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1.5,"CurrentSpeed":131.2,"CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":"fast","CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":"NaN","CameraId":5}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":"5"}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5.5}""")]
    [InlineData("""{"Type":true,"NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5}""")]
    [Trait("BR", "BR-ING-01")]
    public void Measurement_with_a_mistyped_property_fails_to_deserialize(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage));
    }

    [Fact]
    [Trait("BR", "BR-ING-01")]
    public void Null_plate_is_representable_on_the_wire_and_left_to_validation()
    {
        const string json = """{"Type":"Measurement","NumberPlate":null,"TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5}""";

        var message = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage);

        Assert.NotNull(message);
        Assert.Null(message.NumberPlate);
    }

    [Fact]
    public void Unknown_additional_properties_are_ignored()
    {
        const string json = """{"Type":"Measurement","Lane":2,"NumberPlate":"1AB 2345","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5,"Extra":{"a":[1,2]}}""";

        var message = JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage);

        Assert.Equal(new MeasurementMessage(TelemetryContract.MeasurementType, "1AB 2345", 1, 131.2f, 5), message);
    }

    [Fact]
    public void Property_names_are_case_sensitive()
    {
        const string json = """{"type":"Measurement","numberPlate":"1AB 2345","timestampTicks":1,"currentSpeed":131.2,"cameraId":5}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TelemetryJsonContext.Default.MeasurementMessage));
    }

    [Fact]
    public void Topic_and_type_constants_match_the_contract()
    {
        Assert.Equal("highway/telemetry/v1", TelemetryContract.Topic);
        Assert.Equal("Measurement", TelemetryContract.MeasurementType);
        Assert.Equal("Heartbeat", TelemetryContract.HeartbeatType);
        Assert.Equal(1024, TelemetryContract.MaxPayloadBytes);
        Assert.True(ContractExamples.Read("measurement.json").Trim().Length <= TelemetryContract.MaxPayloadBytes);
    }
}
