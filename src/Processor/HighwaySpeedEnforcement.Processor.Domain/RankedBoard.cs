namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// A thread-safe leaderboard: at most one entry per plate, expiry against its own watermark (BR-LB-07, BR-LB-08, BR-LB-11),
/// presentation in <see cref="RankOrder"/> (BR-LB-09). Owns its lock; callers never hold two board locks at once.
/// Entries with an infinite lifetime are pruned to the best <see cref="HighwayLayout.BoardSize"/> (BR-LB-10).
/// </summary>
public sealed class RankedBoard
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, BoardEntry> _byPlate = new(StringComparer.Ordinal);
    private readonly SortedSet<BoardEntry> _ranked = new(RankOrder.Instance);
    private readonly SortedSet<BoardEntry> _byExpiry = new(ExpiryOrder.Instance);
    private readonly SortedSet<BoardEntry> _infiniteRanked = new(RankOrder.Instance);
    private long? _watermark;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _byPlate.Count;
            }
        }
    }

    public long? LatestEventTicks
    {
        get
        {
            lock (_lock)
            {
                return _watermark;
            }
        }
    }

    /// <summary>Replaces the plate's entry. An entry that is already expired only removes the plate's old entry (BR-LB-13).</summary>
    public void Upsert(BoardEntry entry)
    {
        lock (_lock)
        {
            UpsertCore(entry);
        }
    }

    public bool Remove(string plate)
    {
        lock (_lock)
        {
            return RemoveCore(plate);
        }
    }

    /// <summary>Raises the watermark (never lowers it, BR-LB-11) and expires every entry whose expiry is not after it.</summary>
    public void AdvanceTo(long watermarkTicks)
    {
        lock (_lock)
        {
            AdvanceCore(watermarkTicks);
        }
    }

    /// <summary>Advances and upserts under one lock acquisition.</summary>
    public void Update(long watermarkTicks, BoardEntry entry)
    {
        lock (_lock)
        {
            AdvanceCore(watermarkTicks);
            UpsertCore(entry);
        }
    }

    /// <summary>The best <paramref name="count"/> live entries with ranks 1..n, and the watermark, read coherently.</summary>
    public BoardView Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        lock (_lock)
        {
            if (_ranked.Count == 0 || count == 0)
            {
                return new BoardView([], _watermark);
            }

            var entries = new RankedEntry[Math.Min(count, _ranked.Count)];
            int rank = 0;

            foreach (var entry in _ranked)
            {
                entries[rank] = new RankedEntry(rank + 1, entry);
                rank++;

                if (rank == entries.Length)
                {
                    break;
                }
            }

            return new BoardView(entries, _watermark);
        }
    }

    private void UpsertCore(BoardEntry entry)
    {
        RemoveCore(entry.Plate);

        if (_watermark is { } watermark && entry.ExpiresAtTicks <= watermark)
        {
            return;
        }

        _byPlate.Add(entry.Plate, entry);
        _ranked.Add(entry);
        _byExpiry.Add(entry);

        if (entry.ExpiresAtTicks == long.MaxValue)
        {
            _infiniteRanked.Add(entry);

            // BR-LB-10: ten better entries that never expire dominate the worst infinite one forever.
            if (_infiniteRanked.Count > HighwayLayout.BoardSize)
            {
                RemoveCore(_infiniteRanked.Max.Plate);
            }
        }
    }

    private bool RemoveCore(string plate)
    {
        if (!_byPlate.Remove(plate, out var existing))
        {
            return false;
        }

        _ranked.Remove(existing);
        _byExpiry.Remove(existing);

        if (existing.ExpiresAtTicks == long.MaxValue)
        {
            _infiniteRanked.Remove(existing);
        }

        return true;
    }

    private void AdvanceCore(long watermarkTicks)
    {
        if (_watermark is { } current && watermarkTicks <= current)
        {
            return;
        }

        _watermark = watermarkTicks;

        while (_byExpiry.Count > 0 && _byExpiry.Min.ExpiresAtTicks <= watermarkTicks)
        {
            RemoveCore(_byExpiry.Min.Plate);
        }
    }

    private sealed class ExpiryOrder : IComparer<BoardEntry>
    {
        public static ExpiryOrder Instance { get; } = new();

        public int Compare(BoardEntry x, BoardEntry y)
        {
            int byExpiry = x.ExpiresAtTicks.CompareTo(y.ExpiresAtTicks);

            return byExpiry != 0 ? byExpiry : string.CompareOrdinal(x.Plate, y.Plate);
        }
    }
}
