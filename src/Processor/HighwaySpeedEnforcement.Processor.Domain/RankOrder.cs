namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>Rank order of a leaderboard: score descending, then plate ascending, ordinal and case-sensitive (BR-LB-05).</summary>
public sealed class RankOrder : IComparer<BoardEntry>
{
    public static RankOrder Instance { get; } = new();

    private RankOrder()
    {
    }

    public int Compare(BoardEntry x, BoardEntry y)
    {
        int byScore = y.Score.CompareTo(x.Score);

        return byScore != 0 ? byScore : string.CompareOrdinal(x.Plate, y.Plate);
    }
}
