using HighwaySpeedEnforcement.Processor.Application;
using HighwaySpeedEnforcement.Processor.Domain;
using Microsoft.Extensions.Time.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Application;

public sealed class MeasurementValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NowTicks = Now.UtcTicks;

    private readonly MeasurementValidator _validator = new(
        new ProcessorSettings { FutureTimestampTolerance = TimeSpan.FromMinutes(1) },
        new FakeTimeProvider(Now));

    public static TheoryData<string?> InvalidPlates => new(null, "", "   ", new string('A', 17));

    [Theory]
    [MemberData(nameof(InvalidPlates))]
    [Trait("AC", "AC-04")]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_rejects_an_invalid_plate(string? plate)
    {
        Assert.Equal(RejectReason.InvalidPlate, Reject(Valid() with { NumberPlate = plate }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [Trait("AC", "AC-04")]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_rejects_an_invalid_camera(int camera)
    {
        Assert.Equal(RejectReason.InvalidCamera, Reject(Valid() with { CameraId = camera }));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait("AC", "AC-04")]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_rejects_an_invalid_speed(float speed)
    {
        Assert.Equal(RejectReason.InvalidSpeed, Reject(Valid() with { CurrentSpeed = speed }));
    }

    public static TheoryData<long> InvalidTimestamps => new(0, -1, DateTime.MaxValue.Ticks + 1, NowTicks + TimeSpan.FromMinutes(1).Ticks + 1);

    [Theory]
    [MemberData(nameof(InvalidTimestamps))]
    [Trait("AC", "AC-04")]
    [Trait("AC", "AC-31")]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_rejects_an_invalid_timestamp(long ticks)
    {
        Assert.Equal(RejectReason.InvalidTimestamp, Reject(Valid() with { TimestampTicks = ticks }));
    }

    [Fact]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_reports_the_first_violated_rule_in_the_order_plate_camera_speed_timestamp()
    {
        var everythingWrong = new IncomingMeasurement(null, -1, float.NaN, 0);

        Assert.Equal(RejectReason.InvalidPlate, Reject(everythingWrong));
        Assert.Equal(RejectReason.InvalidCamera, Reject(everythingWrong with { NumberPlate = "A" }));
        Assert.Equal(RejectReason.InvalidSpeed, Reject(everythingWrong with { NumberPlate = "A", CameraId = 1 }));
        Assert.Equal(RejectReason.InvalidTimestamp, Reject(everythingWrong with { NumberPlate = "A", CameraId = 1, CurrentSpeed = 0 }));
    }

    [Fact]
    [Trait("BR", "BR-ING-01")]
    public void TryValidate_accepts_boundary_values()
    {
        var boundary = new IncomingMeasurement(new string('A', 16), NowTicks + TimeSpan.FromMinutes(1).Ticks, 0, 10);

        Assert.True(_validator.TryValidate(boundary, out var measurement, out _));
        Assert.Equal(new Measurement(boundary.NumberPlate!, boundary.TimestampTicks, 0, 10), measurement);
        Assert.True(_validator.TryValidate(Valid() with { CameraId = 1, TimestampTicks = 1 }, out _, out _));
    }

    [Fact]
    [Trait("BR", "BR-TEL-03")]
    public void Heartbeat_is_validated_for_camera_and_timestamp_only()
    {
        Assert.True(_validator.TryValidate(new IncomingHeartbeat(5, NowTicks), out _));

        Assert.False(_validator.TryValidate(new IncomingHeartbeat(11, NowTicks), out var cameraReason));
        Assert.Equal(RejectReason.InvalidCamera, cameraReason);

        Assert.False(_validator.TryValidate(new IncomingHeartbeat(5, NowTicks + TimeSpan.FromHours(1).Ticks), out var timestampReason));
        Assert.Equal(RejectReason.InvalidTimestamp, timestampReason);
    }

    private RejectReason Reject(IncomingMeasurement incoming)
    {
        Assert.False(_validator.TryValidate(incoming, out _, out var reason));

        return reason;
    }

    private static IncomingMeasurement Valid()
    {
        return new IncomingMeasurement("1AB 2345", NowTicks - TimeSpan.FromSeconds(1).Ticks, 131.2f, 5);
    }
}
