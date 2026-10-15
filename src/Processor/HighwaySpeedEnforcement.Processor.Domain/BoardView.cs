namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>One presented row of a leaderboard with its 1-based rank.</summary>
public readonly record struct RankedEntry(int Rank, BoardEntry Entry);

/// <summary>
/// Immutable result of <see cref="RankedBoard.Top"/>: the presented entries in rank order and the board's watermark
/// (<see cref="LatestEventTicks"/> is null while the board never advanced).
/// </summary>
public sealed record BoardView(IReadOnlyList<RankedEntry> Entries, long? LatestEventTicks)
{
    public static BoardView Empty { get; } = new([], null);
}
