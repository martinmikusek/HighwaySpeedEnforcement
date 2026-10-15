using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class WatermarkClockTests
{
    [Fact]
    [Trait("BR", "BR-LB-11")]
    public void New_clock_has_no_watermarks()
    {
        var clock = new WatermarkClock();

        Assert.Null(clock.GlobalTicks);
        Assert.Null(clock.CameraTicks(1));
        Assert.Null(clock.CameraTicks(10));
    }

    [Fact]
    [Trait("BR", "BR-LB-11")]
    public void Observe_raises_the_camera_and_the_global_maximum_and_returns_them()
    {
        var clock = new WatermarkClock();

        var first = clock.Observe(3, 100);
        var second = clock.Observe(7, 200);

        Assert.Equal(new Stamp(100, 100), first);
        Assert.Equal(new Stamp(200, 200), second);
        Assert.Equal(100, clock.CameraTicks(3));
        Assert.Equal(200, clock.CameraTicks(7));
        Assert.Equal(200, clock.GlobalTicks);
        Assert.Null(clock.CameraTicks(5));
    }

    [Fact]
    [Trait("AC", "AC-21")]
    [Trait("BR", "BR-LB-11")]
    public void Older_timestamp_leaves_the_maxima_unchanged()
    {
        var clock = new WatermarkClock();
        clock.Observe(3, 200);

        var stamp = clock.Observe(3, 150);

        Assert.Equal(new Stamp(200, 200), stamp);
        Assert.Equal(200, clock.CameraTicks(3));
        Assert.Equal(200, clock.GlobalTicks);
    }

    [Fact]
    [Trait("BR", "BR-LB-11")]
    public void Stamp_carries_the_camera_maximum_of_its_own_camera_only()
    {
        var clock = new WatermarkClock();
        clock.Observe(1, 500);

        var stamp = clock.Observe(2, 300);

        Assert.Equal(new Stamp(GlobalTicks: 500, CameraTicks: 300), stamp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Observe_rejects_an_invalid_camera(int cameraId)
    {
        var clock = new WatermarkClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Observe(cameraId, 1));
    }
}
