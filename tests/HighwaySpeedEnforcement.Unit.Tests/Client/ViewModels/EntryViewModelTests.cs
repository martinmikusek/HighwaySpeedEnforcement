using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.ViewModels;

public sealed class EntryViewModelTests
{
    /// <summary>A fixed zone two hours ahead of UTC, so the tests do not depend on the machine's zone.</summary>
    public static readonly TimeZoneInfo PlusTwo = TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");

    private static readonly DisplayFormatter Formatter = new(PlusTwo);
    private static readonly DateTimeOffset MeasuredAt = new(2026, 10, 14, 14, 3, 9, 398, TimeSpan.Zero);

    [Theory]
    [InlineData(152.4, "152.4")]
    [InlineData(149.75, "149.8")]
    [InlineData(149.74, "149.7")]
    [InlineData(0.0, "0.0")]
    [InlineData(0.04, "0.0")]
    [InlineData(0.06, "0.1")]
    [InlineData(999.96, "1000.0")]
    [InlineData(12345.678, "12345.7")]
    [Trait("BR", "BR-LB-05")]
    public void Speed_is_rounded_to_one_decimal_in_the_invariant_culture(double kmh, string expected)
    {
        Assert.Equal(expected, Formatter.Speed(kmh));
    }

    [Fact]
    public void Time_is_shown_in_the_given_zone_with_tenths_of_a_second()
    {
        Assert.Equal("16:03:09.3", Formatter.Time(MeasuredAt));
        Assert.Equal("16:03:09", Formatter.TimeOfDay(MeasuredAt));
    }

    [Fact]
    public void Age_is_shown_in_seconds_with_one_decimal()
    {
        Assert.Equal("12.4 s", Formatter.Age(TimeSpan.FromSeconds(12.4)));
        Assert.Equal("0.0 s", Formatter.Age(TimeSpan.Zero));
    }

    [Fact]
    [Trait("BR", "BR-UI-01")]
    public void Camera_entry_shows_rank_plate_speed_and_time_without_a_detail()
    {
        var row = EntryViewModel.From(new CameraEntry(2, "2AB 9876", 149.8f, MeasuredAt), Formatter);

        Assert.Equal(2, row.Rank);
        Assert.Equal("2AB 9876", row.NumberPlate);
        Assert.Equal("149.8", row.ScoreText);
        Assert.Equal("", row.DetailText);
        Assert.Equal("16:03:09.3", row.TimeText);
    }

    [Fact]
    [Trait("BR", "BR-UI-01")]
    public void Global_entry_shows_the_average_and_the_measurement_count()
    {
        var row = EntryViewModel.From(new GlobalEntry(1, "4S7 1234", 144.35, 6, MeasuredAt), Formatter);

        Assert.Equal(1, row.Rank);
        Assert.Equal("4S7 1234", row.NumberPlate);
        Assert.Equal("144.4", row.ScoreText);
        Assert.Equal("6", row.DetailText);
        Assert.Equal("16:03:09.3", row.TimeText);
    }
}
