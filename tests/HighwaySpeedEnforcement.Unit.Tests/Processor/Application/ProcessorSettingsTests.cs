using HighwaySpeedEnforcement.Processor.Application;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

public sealed class ProcessorSettingsTests
{
    [Fact]
    [Trait("BR", "BR-CFG-01")]
    [Trait("BR", "BR-CFG-03")]
    public void Defaults_are_valid_and_match_the_design()
    {
        var settings = new ProcessorSettings();

        Assert.Empty(settings.Validate());
        Assert.Equal(4, settings.PartitionCount);
        Assert.Equal(10_000, settings.IngestChannelCapacity);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.CameraBoardWindow);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.GlobalBoardLinger);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.InactivityTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.FutureTimestampTolerance);
        Assert.True(settings.HeartbeatsEnabled);
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    public void Infinite_window_and_linger_are_valid()
    {
        var settings = new ProcessorSettings { CameraBoardWindow = Timeout.InfiniteTimeSpan, GlobalBoardLinger = Timeout.InfiniteTimeSpan };

        Assert.Empty(settings.Validate());
        Assert.Equal(long.MaxValue, settings.ToLivenessPolicy().CameraExpiry(1));
    }

    [Fact]
    [Trait("BR", "BR-CFG-01")]
    [Trait("BR", "BR-CFG-03")]
    public void Validate_names_every_violated_rule()
    {
        var settings = new ProcessorSettings
        {
            PartitionCount = 0,
            IngestChannelCapacity = 0,
            CameraBoardWindow = TimeSpan.Zero,
            GlobalBoardLinger = TimeSpan.FromSeconds(-1),
            InactivityTimeout = Timeout.InfiniteTimeSpan,
            FutureTimestampTolerance = TimeSpan.FromSeconds(-1),
            IngestLagWarning = TimeSpan.Zero,
        };

        var errors = settings.Validate();

        Assert.Equal(7, errors.Count);
        Assert.All(
            new[] { "PartitionCount", "IngestChannelCapacity", "CameraBoardWindow", "GlobalBoardLinger", "InactivityTimeout", "FutureTimestampTolerance", "IngestLagWarning" },
            name => Assert.Contains(errors, error => error.StartsWith(name, StringComparison.Ordinal)));
    }
}
