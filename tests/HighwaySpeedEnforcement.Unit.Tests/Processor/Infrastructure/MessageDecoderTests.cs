using System.Text;
using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Infrastructure;
using HighwaySpeedEnforcement.Unit.Tests.Contracts;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Infrastructure;

public sealed class MessageDecoderTests
{
    private const long ExampleTicks = 639275833894000000;

    private static MessageDecoder Decoder(bool heartbeatsEnabled = true)
    {
        return new MessageDecoder(new ProcessorSettings { HeartbeatsEnabled = heartbeatsEnabled });
    }

    private static DecodeResult Decode(string payload, bool heartbeatsEnabled = true)
    {
        return Decoder(heartbeatsEnabled).Decode(Encoding.UTF8.GetBytes(payload));
    }

    [Fact]
    [Trait("BR", "BR-TEL-01")]
    public void Measurement_example_decodes_into_an_incoming_measurement()
    {
        var result = Decode(ContractExamples.Read("measurement.json"));

        Assert.Equal(DecodeOutcome.Decoded, result.Outcome);
        Assert.Equal(new IncomingMeasurement("1AB 2345", ExampleTicks, 131.2f, 5), result.Message);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Heartbeat_example_decodes_into_an_incoming_heartbeat()
    {
        var result = Decode(ContractExamples.Read("heartbeat.json"));

        Assert.Equal(DecodeOutcome.Decoded, result.Outcome);
        Assert.Equal(new IncomingHeartbeat(5, ExampleTicks), result.Message);
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Heartbeat_is_ignored_when_heartbeats_are_disabled()
    {
        var result = Decode(ContractExamples.Read("heartbeat.json"), heartbeatsEnabled: false);

        Assert.Equal(DecodeOutcome.Ignored, result.Outcome);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"Measurement\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1} trailing""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}{}""")]
    [Trait("BR", "BR-ING-01")]
    public void Payload_that_is_not_one_json_object_is_malformed(string payload)
    {
        var result = Decode(payload);

        Assert.Equal(DecodeOutcome.Malformed, result.Outcome);
        Assert.Equal(RejectReason.Malformed, result.RejectReason);
    }

    [Theory]
    [InlineData("""{"NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":null,"CameraId":1,"TimestampTicks":1}""")]
    [InlineData("""{"Type":5,"CameraId":1,"TimestampTicks":1}""")]
    [InlineData("""{"Type":{"x":1},"CameraId":1,"TimestampTicks":1}""")]
    [InlineData("""{"type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [Trait("BR", "BR-ING-01")]
    public void Missing_or_mistyped_type_is_malformed(string payload)
    {
        Assert.Equal(DecodeOutcome.Malformed, Decode(payload).Outcome);
    }

    [Theory]
    [InlineData("""{"Type":"Measurement","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1}""")]
    [InlineData("""{"Type":"Heartbeat","TimestampTicks":1}""")]
    [InlineData("""{"Type":"Heartbeat","CameraId":1}""")]
    [Trait("BR", "BR-ING-01")]
    public void Missing_required_property_is_malformed(string payload)
    {
        Assert.Equal(DecodeOutcome.Malformed, Decode(payload).Outcome);
    }

    [Theory]
    [InlineData("""{"Type":"Measurement","NumberPlate":7,"TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":"1","CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1.5,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":99999999999999999999,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":"NaN","CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":"Infinity","CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":true,"CameraId":1}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":"1"}""")]
    [InlineData("""{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1.0}""")]
    [InlineData("""{"Type":"Heartbeat","CameraId":"1","TimestampTicks":1}""")]
    [Trait("BR", "BR-ING-01")]
    public void Mistyped_property_is_malformed(string payload)
    {
        Assert.Equal(DecodeOutcome.Malformed, Decode(payload).Outcome);
    }

    [Fact]
    [Trait("BR", "BR-ING-01")]
    public void Payload_over_the_size_limit_is_malformed_without_parsing()
    {
        var padding = new string('x', 1_024);
        var payload = $$"""{"Type":"Measurement","Pad":"{{padding}}","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""";
        Assert.True(Encoding.UTF8.GetByteCount(payload) > 1_024);

        Assert.Equal(DecodeOutcome.Malformed, Decode(payload).Outcome);
    }

    [Fact]
    public void Payload_at_the_size_limit_is_decoded()
    {
        const string head = """{"Type":"Measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1,"Pad":" """;
        const string tail = "\"}";
        var payload = head + new string('x', 1_024 - head.Length - tail.Length) + tail;
        Assert.Equal(1_024, Encoding.UTF8.GetByteCount(payload));

        Assert.Equal(DecodeOutcome.Decoded, Decode(payload).Outcome);
    }

    [Theory]
    [InlineData("""{"Type":"measurement","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":"Telemetry","NumberPlate":"X","TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""")]
    [InlineData("""{"Type":""}""")]
    [Trait("BR", "BR-ING-01")]
    public void Unknown_type_is_reported_as_such(string payload)
    {
        var result = Decode(payload);

        Assert.Equal(DecodeOutcome.UnknownType, result.Outcome);
        Assert.Equal(RejectReason.UnknownType, result.RejectReason);
    }

    [Fact]
    public void Type_is_resolved_wherever_it_appears_and_extra_properties_are_ignored()
    {
        const string payload = """{"Lane":2,"Nested":{"Type":"Heartbeat","a":[1,{"b":null}]},"NumberPlate":"1AB 2345","TimestampTicks":1,"CurrentSpeed":131.2,"CameraId":5,"Type":"Measurement","Extra":[true]}""";

        var result = Decode(payload);

        Assert.Equal(DecodeOutcome.Decoded, result.Outcome);
        Assert.Equal(new IncomingMeasurement("1AB 2345", 1, 131.2f, 5), result.Message);
    }

    [Fact]
    [Trait("BR", "BR-ING-01")]
    public void Null_plate_is_decoded_and_left_to_validation()
    {
        const string payload = """{"Type":"Measurement","NumberPlate":null,"TimestampTicks":1,"CurrentSpeed":1,"CameraId":1}""";

        var result = Decode(payload);

        Assert.Equal(DecodeOutcome.Decoded, result.Outcome);
        Assert.Null(((IncomingMeasurement)result.Message!).NumberPlate);
    }

    [Fact]
    public void Semantically_invalid_values_are_decoded_not_rejected_here()
    {
        const string payload = """{"Type":"Measurement","NumberPlate":"","TimestampTicks":-1,"CurrentSpeed":-5,"CameraId":11}""";

        var result = Decode(payload);

        Assert.Equal(DecodeOutcome.Decoded, result.Outcome);
        Assert.Equal(new IncomingMeasurement("", -1, -5f, 11), result.Message);
    }

    [Fact]
    public void Reject_reason_is_only_defined_for_rejections()
    {
        Assert.Throws<InvalidOperationException>(() => DecodeResult.Ignored.RejectReason);
        Assert.Throws<InvalidOperationException>(() => DecodeResult.Decoded(new IncomingHeartbeat(1, 1)).RejectReason);
    }
}
