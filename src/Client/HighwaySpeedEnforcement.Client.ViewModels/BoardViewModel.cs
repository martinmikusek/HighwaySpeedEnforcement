using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HighwaySpeedEnforcement.Client.Application;

namespace HighwaySpeedEnforcement.Client.ViewModels;

/// <summary>
/// One displayed leaderboard: its rows, the watermark text and the liveness marking (BR-UI-04). Updated only on the UI
/// thread, by <see cref="MainViewModel"/> inside the dispatcher.
/// </summary>
public sealed partial class BoardViewModel : ObservableObject
{
    private readonly DisplayFormatter _formatter;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive))]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    private Liveness _liveness = Liveness.NoData;

    [ObservableProperty]
    private string _latestEventText = "Latest event: none";

    [ObservableProperty]
    private string _ageText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _emptyText = "no data yet";

    public BoardViewModel(string title, DisplayFormatter formatter)
    {
        _title = title;
        _formatter = formatter;
    }

    public ObservableCollection<EntryViewModel> Entries { get; } = [];

    public bool IsLive => Liveness == Liveness.Live;

    /// <summary>A board that is not live (frozen, late or without data) is shown dimmed; the age text stays readable.</summary>
    public bool IsDimmed => Liveness != Liveness.Live;

    /// <summary>True when the empty text is shown instead of rows.</summary>
    public bool IsEmpty => EmptyText.Length > 0;

    public void Update(CameraBoard board, BoardStatus status)
    {
        ArgumentNullException.ThrowIfNull(board);

        Update(board.Entries.Select(entry => EntryViewModel.From(entry, _formatter)).ToArray(), board.LatestEvent, status);
    }

    public void Update(GlobalBoard board, BoardStatus status)
    {
        ArgumentNullException.ThrowIfNull(board);

        Update(board.Entries.Select(entry => EntryViewModel.From(entry, _formatter)).ToArray(), board.LatestEvent, status);
    }

    private void Update(IReadOnlyList<EntryViewModel> rows, DateTimeOffset? latestEvent, BoardStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        Entries.Clear();

        foreach (var row in rows)
        {
            Entries.Add(row);
        }

        Liveness = status.Liveness;
        LatestEventText = latestEvent is { } instant ? $"Latest event: {_formatter.Time(instant)}" : "Latest event: none";
        AgeText = status.Liveness switch
        {
            Liveness.Live => "live",
            Liveness.NotLive => $"not live, latest event {_formatter.Age(status.Lag ?? TimeSpan.Zero)} old",
            _ => "",
        };
        EmptyText = (status.Liveness, rows.Count) switch
        {
            (Liveness.NoData, _) => "no data yet",
            (_, 0) => "no live entries",
            _ => "",
        };
    }
}
