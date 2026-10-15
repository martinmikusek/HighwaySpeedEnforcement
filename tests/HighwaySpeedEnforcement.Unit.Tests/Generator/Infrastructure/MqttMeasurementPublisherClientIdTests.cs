using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Infrastructure;

public sealed class MqttMeasurementPublisherClientIdTests
{
    [Theory]
    [InlineData(1, "speed-camera-1-ab12cd")]
    [InlineData(10, "speed-camera-10-ab12cd")]
    public void ClientIdFor_per_camera_names_the_camera_and_the_run(int camera, string expected)
    {
        Assert.Equal(expected, MqttMeasurementPublisher.ClientIdFor(PublisherMode.PerCamera, camera, "ab12cd"));
    }

    [Fact]
    public void ClientIdFor_single_connection_uses_the_contract_prefix_and_ignores_the_camera()
    {
        string id = MqttMeasurementPublisher.ClientIdFor(PublisherMode.SingleConnection, 7, "ab12cd");

        Assert.Equal("speed-generator-ab12cd", id);
        Assert.StartsWith(TelemetryContract.GeneratorClientIdPrefix, id, StringComparison.Ordinal);
        Assert.Equal(id, MqttMeasurementPublisher.ClientIdFor(PublisherMode.SingleConnection, 3, "ab12cd"));
    }

    [Fact]
    public void ClientIdFor_rejects_a_blank_run_id()
    {
        Assert.Throws<ArgumentException>(() => MqttMeasurementPublisher.ClientIdFor(PublisherMode.PerCamera, 1, " "));
    }
}
