using HighwaySpeedEnforcement.Generator;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using HighwaySpeedEnforcement.Generator.Interactive;
using Microsoft.Extensions.Configuration;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Host;

public sealed class GeneratorSettingsTests
{
    [Fact]
    public void Defaults_match_the_design_and_are_valid()
    {
        var settings = new GeneratorSettings();

        Assert.Empty(settings.Validate());
        Assert.Null(settings.Seed);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HighwaySpeedEnforcement", "generator-state.json"),
            settings.StatePath);
        Assert.Equal(100, settings.VehiclesPerTick);
        Assert.Null(settings.SpawnUntilTick);
        Assert.Null(settings.MaxTicks);
        Assert.Empty(settings.DisabledCameras);
        Assert.False(settings.Heartbeats.Enabled);
        Assert.Equal(130.0, settings.Speed.BaselineKmh);
        Assert.Equal(15.0, settings.Speed.DesiredStdDev);
        Assert.Equal(3.0, settings.Speed.StepStdDev);
        Assert.Equal("127.0.0.1", settings.Mqtt.Host);
        Assert.Equal(1883, settings.Mqtt.Port);
        Assert.Equal("highway/telemetry/v1", settings.Mqtt.Topic);
        Assert.Equal(PublisherMode.PerCamera, settings.Mqtt.PublisherMode);
    }

    [Fact]
    public void Binding_applies_overrides_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Generator:Seed"] = "42",
                ["Generator:StatePath"] = "C:/tmp/state.json",
                ["Generator:VehiclesPerTick"] = "1000",
                ["Generator:SpawnUntilTick"] = "3",
                ["Generator:MaxTicks"] = "15",
                ["Generator:DisabledCameras:0"] = "7",
                ["Generator:DisabledCameras:1"] = "2",
                ["Generator:Heartbeats:Enabled"] = "true",
                ["Generator:Speed:BaselineKmh"] = "110",
                ["Generator:Speed:DesiredStdDev"] = "10",
                ["Generator:Speed:StepStdDev"] = "2",
                ["Generator:Mqtt:Host"] = "10.0.0.5",
                ["Generator:Mqtt:Port"] = "1884",
                ["Generator:Mqtt:Topic"] = "test/topic",
                ["Generator:Mqtt:PublisherMode"] = "SingleConnection",
                ["Generator:Mqtt:RunId"] = "run-1",
            })
            .Build();

        var settings = configuration.GetSection(GeneratorSettings.SectionName).Get<GeneratorSettings>();

        Assert.NotNull(settings);
        Assert.Empty(settings.Validate());
        Assert.Equal(42, settings.Seed);
        Assert.Equal("C:/tmp/state.json", settings.StatePath);
        Assert.Equal(1000, settings.VehiclesPerTick);
        Assert.Equal(3, settings.SpawnUntilTick);
        Assert.Equal(15, settings.MaxTicks);
        Assert.Equal([7, 2], settings.DisabledCameras);
        Assert.True(settings.Heartbeats.Enabled);
        Assert.Equal(110.0, settings.Speed.BaselineKmh);
        Assert.Equal(10.0, settings.Speed.DesiredStdDev);
        Assert.Equal(2.0, settings.Speed.StepStdDev);
        Assert.Equal("10.0.0.5", settings.Mqtt.Host);
        Assert.Equal(1884, settings.Mqtt.Port);
        Assert.Equal("test/topic", settings.Mqtt.Topic);
        Assert.Equal(PublisherMode.SingleConnection, settings.Mqtt.PublisherMode);
        Assert.Equal("run-1", settings.Mqtt.RunId);

        var options = settings.ToSimulationOptions();

        Assert.Equal(1000, options.VehiclesPerTick);
        Assert.Equal(3, options.SpawnUntilTick);
        Assert.Equal([7, 2], options.DisabledCameras);
        Assert.True(options.HeartbeatsEnabled);
        Assert.Equal(110.0, options.Speed.BaselineKmh);
        Assert.Equal(10.0, options.Speed.DesiredStdDev);
        Assert.Equal(2.0, options.Speed.StepStdDev);
    }

    [Theory]
    [InlineData("VehiclesPerTick", "0", "VehiclesPerTick")]
    [InlineData("DisabledCameras:0", "11", "DisabledCameras")]
    [InlineData("DisabledCameras:0", "0", "DisabledCameras")]
    [InlineData("SpawnUntilTick", "0", "SpawnUntilTick")]
    [InlineData("MaxTicks", "-1", "MaxTicks")]
    [InlineData("StatePath", " ", "StatePath")]
    [InlineData("Speed:BaselineKmh", "0", "Speed:BaselineKmh")]
    [InlineData("Speed:DesiredStdDev", "-1", "Speed:DesiredStdDev")]
    [InlineData("Speed:StepStdDev", "0", "Speed:StepStdDev")]
    [InlineData("Mqtt:Port", "0", "Mqtt:Port")]
    [InlineData("Mqtt:Port", "65536", "Mqtt:Port")]
    [InlineData("Mqtt:Host", " ", "Mqtt:Host")]
    [InlineData("Mqtt:Topic", "", "Mqtt:Topic")]
    public void Validate_reports_each_invalid_value_by_name(string key, string value, string expectedPrefix)
    {
        var settings = Bind(new Dictionary<string, string?> { [$"Generator:{key}"] = value });

        var errors = settings.Validate();

        string error = Assert.Single(errors);
        Assert.StartsWith(expectedPrefix, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void Console_interactive_is_a_boolean_that_defaults_to_true(string? configured, bool expected)
    {
        var values = new Dictionary<string, string?>();

        if (configured is not null)
        {
            values["Generator:Console:Interactive"] = configured;
        }

        var settings = configured is null ? new GeneratorSettings() : Bind(values);

        Assert.Empty(settings.Validate());
        Assert.Equal(expected, settings.Console.Interactive);
    }

    [Fact]
    public void Validate_reports_duplicate_disabled_cameras()
    {
        var settings = new GeneratorSettings { DisabledCameras = [7, 7] };

        string error = Assert.Single(settings.Validate());
        Assert.Contains("duplicates", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_reports_several_errors_together()
    {
        var settings = new GeneratorSettings
        {
            VehiclesPerTick = 0,
            DisabledCameras = [0, 3, 3],
            SpawnUntilTick = 0,
            MaxTicks = 0,
            Speed = new GeneratorSettings.SpeedSettings { BaselineKmh = 0, DesiredStdDev = 0, StepStdDev = 0 },
            Mqtt = new MqttPublisherOptions { Host = "", Port = 70_000, Topic = " " },
        };

        var errors = settings.Validate();

        Assert.Equal(11, errors.Count);
        Assert.All(
            new[] { "VehiclesPerTick", "DisabledCameras", "SpawnUntilTick", "MaxTicks", "Speed:BaselineKmh", "Speed:DesiredStdDev", "Speed:StepStdDev", "Mqtt:Host", "Mqtt:Port", "Mqtt:Topic" },
            name => Assert.Contains(errors, error => error.StartsWith(name, StringComparison.Ordinal)));
    }

    [Fact]
    public void RunId_is_generated_when_absent_and_stable_within_one_instance()
    {
        var first = new MqttPublisherOptions();
        var second = new MqttPublisherOptions();

        Assert.Matches("^[0-9a-f]{6}$", first.RunId);
        Assert.Equal(first.RunId, first.RunId);
        Assert.NotEqual(first.RunId, second.RunId);
    }

    private static GeneratorSettings Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return configuration.GetSection(GeneratorSettings.SectionName).Get<GeneratorSettings>()!;
    }
}
