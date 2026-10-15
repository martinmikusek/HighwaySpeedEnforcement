using System.Globalization;
using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Client.ViewModels;

/// <summary>One row of a board, already formatted. Rows are immutable; a board replaces them in one go.</summary>
public sealed class EntryViewModel
{
    private EntryViewModel(int rank, string numberPlate, string scoreText, string detailText, string timeText)
    {
        Rank = rank;
        NumberPlate = numberPlate;
        ScoreText = scoreText;
        DetailText = detailText;
        TimeText = timeText;
    }

    public int Rank { get; }

    public string NumberPlate { get; }

    /// <summary>Camera board: the measured speed; global board: the cumulative average. Both in km/h to one decimal.</summary>
    public string ScoreText { get; }

    /// <summary>Global board: the number of measurements behind the average; empty on a camera board.</summary>
    public string DetailText { get; }

    /// <summary>Event time of the measurement in local time.</summary>
    public string TimeText { get; }

    public static EntryViewModel From(CameraEntry entry, DisplayFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(formatter);

        return new EntryViewModel(entry.Rank, entry.NumberPlate, formatter.Speed(entry.SpeedKmh), "", formatter.Time(entry.MeasuredAt));
    }

    public static EntryViewModel From(GlobalEntry entry, DisplayFormatter formatter)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(formatter);

        return new EntryViewModel(
            entry.Rank,
            entry.NumberPlate,
            formatter.Speed(entry.AverageSpeedKmh),
            entry.MeasurementCount.ToString(CultureInfo.InvariantCulture),
            formatter.Time(entry.MeasuredAt));
    }
}
