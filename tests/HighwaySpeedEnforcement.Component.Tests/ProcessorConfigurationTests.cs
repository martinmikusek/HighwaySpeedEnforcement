using HighwaySpeedEnforcement.Processor.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace HighwaySpeedEnforcement.Component.Tests;

/// <summary>Step 14: binding of the <c>Processor</c> and <c>Mqtt</c> sections (design section 6, BR-CFG-01, BR-CFG-03).</summary>
public sealed class ProcessorConfigurationTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    [Trait("BR", "BR-CFG-03")]
    public void Empty_configuration_gives_the_defaults()
    {
        var settings = ProcessorConfiguration.Load(Configuration());

        Assert.Empty(settings.Errors);
        Assert.Equal(4, settings.Processor.PartitionCount);
        Assert.Equal(10_000, settings.Processor.IngestChannelCapacity);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.Processor.CameraBoardWindow);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.Processor.GlobalBoardLinger);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.Processor.InactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.Processor.FutureTimestampTolerance);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.Processor.IngestLagWarning);
        Assert.True(settings.Processor.HeartbeatsEnabled);
        Assert.True(settings.Mqtt.Enabled);
        Assert.Equal("127.0.0.1", settings.Mqtt.Host);
        Assert.Equal(1883, settings.Mqtt.Port);
        Assert.Equal("highway/telemetry/v1", settings.Mqtt.Topic);
        Assert.Equal("speed-processor", settings.Mqtt.ClientId);
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    [Trait("BR", "BR-CFG-03")]
    public void Overrides_are_bound_from_both_sections()
    {
        var settings = ProcessorConfiguration.Load(Configuration(
            ("Processor:PartitionCount", "2"),
            ("Processor:IngestChannelCapacity", "500"),
            ("Processor:CameraBoardWindow", "00:00:01"),
            ("Processor:GlobalBoardLinger", "00:00:00"),
            ("Processor:InactivityTimeout", "00:01:30"),
            ("Processor:FutureTimestampTolerance", "00:00:10"),
            ("Processor:IngestLagWarning", "00:00:02.500"),
            ("Processor:HeartbeatsEnabled", "false"),
            ("Mqtt:Enabled", "false"),
            ("Mqtt:Host", "broker"),
            ("Mqtt:Port", "11883"),
            ("Mqtt:Topic", "highway/telemetry/v2"),
            ("Mqtt:ClientId", "processor-2"),
            ("Mqtt:KeepAlive", "00:00:10")));

        Assert.Empty(settings.Errors);
        Assert.Equal(2, settings.Processor.PartitionCount);
        Assert.Equal(500, settings.Processor.IngestChannelCapacity);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.Processor.CameraBoardWindow);
        Assert.Equal(TimeSpan.Zero, settings.Processor.GlobalBoardLinger);
        Assert.Equal(TimeSpan.FromSeconds(90), settings.Processor.InactivityTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.Processor.FutureTimestampTolerance);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), settings.Processor.IngestLagWarning);
        Assert.False(settings.Processor.HeartbeatsEnabled);
        Assert.False(settings.Mqtt.Enabled);
        Assert.Equal("broker", settings.Mqtt.Host);
        Assert.Equal(11883, settings.Mqtt.Port);
        Assert.Equal("highway/telemetry/v2", settings.Mqtt.Topic);
        Assert.Equal("processor-2", settings.Mqtt.ClientId);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.Mqtt.KeepAlive);
    }

    [Theory]
    [InlineData("Infinite")]
    [InlineData("infinite")]
    [InlineData(" INFINITE ")]
    [Trait("BR", "BR-CFG-01")]
    public void Infinite_window_and_linger_are_bound_as_the_infinite_timespan(string keyword)
    {
        var settings = ProcessorConfiguration.Load(Configuration(
            ("Processor:CameraBoardWindow", keyword),
            ("Processor:GlobalBoardLinger", keyword)));

        Assert.Empty(settings.Errors);
        Assert.Equal(Timeout.InfiniteTimeSpan, settings.Processor.CameraBoardWindow);
        Assert.Equal(Timeout.InfiniteTimeSpan, settings.Processor.GlobalBoardLinger);
        Assert.Equal("Infinite", ProcessorConfiguration.FormatDuration(settings.Processor.CameraBoardWindow));
        Assert.Equal("00:00:05", ProcessorConfiguration.FormatDuration(settings.Processor.InactivityTimeout));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Every_problem_is_reported_at_once()
    {
        var settings = ProcessorConfiguration.Load(Configuration(
            ("Processor:PartitionCount", "zero"),
            ("Processor:IngestChannelCapacity", "0"),
            ("Processor:CameraBoardWindow", "2 seconds"),
            ("Processor:GlobalBoardLinger", "-00:00:01"),
            ("Processor:InactivityTimeout", "Infinite"),
            ("Processor:HeartbeatsEnabled", "maybe"),
            ("Mqtt:Port", "70000"),
            ("Mqtt:Topic", " ")));

        Assert.Contains(settings.Errors, error => error.Contains("Processor:PartitionCount", StringComparison.Ordinal) && error.Contains("'zero'", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("IngestChannelCapacity", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("Processor:CameraBoardWindow", StringComparison.Ordinal) && error.Contains("Infinite", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("GlobalBoardLinger", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("InactivityTimeout", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("Processor:HeartbeatsEnabled", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("Mqtt: Port", StringComparison.Ordinal));
        Assert.Contains(settings.Errors, error => error.Contains("Mqtt: Topic", StringComparison.Ordinal));
        Assert.Equal(8, settings.Errors.Count);
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Host_does_not_start_with_invalid_settings()
    {
        using var factory = new ProcessorFactory { Settings = { ["Processor:PartitionCount"] = "0" } };

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("PartitionCount", exception.Message, StringComparison.Ordinal);
    }
}
