using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class RankedBoardTests
{
    private static readonly long Second = TimeSpan.FromSeconds(1).Ticks;

    [Fact]
    [Trait("AC", "AC-22")]
    public void Top_of_an_empty_board_has_no_entries_and_no_watermark()
    {
        var board = new RankedBoard();

        var view = board.Top(10);

        Assert.Empty(view.Entries);
        Assert.Null(view.LatestEventTicks);
        Assert.Equal(0, board.Count);
    }

    [Fact]
    [Trait("AC", "AC-11")]
    [Trait("BR", "BR-LB-02")]
    [Trait("BR", "BR-LB-03")]
    public void Upsert_replaces_the_plate_entry_even_with_a_lower_score()
    {
        var board = new RankedBoard();
        board.Upsert(Entry("B", 135, 1));
        board.Upsert(Entry("A", 140, 1));

        board.Upsert(Entry("A", 130, 2));

        Assert.Equal(["B", "A"], Plates(board));
        Assert.Equal(130, board.Top(10).Entries[1].Entry.Score);
        Assert.Equal(2, board.Count);
    }

    [Fact]
    [Trait("AC", "AC-14")]
    [Trait("BR", "BR-LB-08")]
    [Trait("BR", "BR-LB-13")]
    public void Upsert_of_an_already_expired_entry_removes_the_old_entry_and_inserts_nothing()
    {
        var board = new RankedBoard();
        board.AdvanceTo(5 * Second);
        board.Upsert(Entry("A", 140, 5, expiresAtSeconds: 9));

        board.Upsert(Entry("A", 150, 6, expiresAtSeconds: 5));

        Assert.Empty(board.Top(10).Entries);
    }

    [Fact]
    [Trait("AC", "AC-17")]
    [Trait("BR", "BR-LB-07")]
    public void AdvanceTo_expires_entries_whose_expiry_is_not_after_the_watermark()
    {
        var board = new RankedBoard();
        board.Upsert(Entry("A", 150, 10, expiresAtSeconds: 12));
        board.Upsert(Entry("B", 140, 11, expiresAtSeconds: 13));

        board.AdvanceTo(12 * Second);

        Assert.Equal(["B"], Plates(board));
        Assert.Equal(12 * Second, board.LatestEventTicks);
    }

    [Fact]
    [Trait("AC", "AC-21")]
    [Trait("BR", "BR-LB-11")]
    public void AdvanceTo_never_lowers_the_watermark()
    {
        var board = new RankedBoard();
        board.AdvanceTo(12 * Second);

        board.AdvanceTo(8 * Second);
        board.Upsert(Entry("Late", 150, 8, expiresAtSeconds: 10));

        Assert.Equal(12 * Second, board.Top(10).LatestEventTicks);
        Assert.Empty(board.Top(10).Entries);
    }

    [Fact]
    [Trait("BR", "BR-LB-06")]
    public void Remove_takes_the_plate_off_the_board()
    {
        var board = new RankedBoard();
        board.Upsert(Entry("A", 150, 1));

        Assert.True(board.Remove("A"));
        Assert.False(board.Remove("A"));
        Assert.Empty(board.Top(10).Entries);
    }

    [Fact]
    [Trait("AC", "AC-16")]
    [Trait("BR", "BR-LB-09")]
    public void Top_returns_ranks_one_to_n_in_rank_order_with_the_watermark()
    {
        var board = new RankedBoard();

        for (int i = 0; i < 25; i++)
        {
            board.Update(i * Second, Entry($"P{i:00}", 100 + (i % 7), i));
        }

        var view = board.Top(10);

        Assert.Equal(Enumerable.Range(1, 10), view.Entries.Select(e => e.Rank));
        Assert.Equal(["P06", "P13", "P20", "P05", "P12", "P19", "P04", "P11", "P18", "P03"], view.Entries.Select(e => e.Entry.Plate));
        Assert.Equal(24 * Second, view.LatestEventTicks);
    }

    [Fact]
    [Trait("AC", "AC-12")]
    [Trait("AC", "AC-17")]
    [Trait("BR", "BR-LB-09")]
    public void Expiry_promotes_the_next_best_live_candidate()
    {
        var board = new RankedBoard();

        for (int i = 0; i < 10; i++)
        {
            board.Upsert(Entry($"Fast{i}", 150, 1, expiresAtSeconds: 3));
        }

        board.Upsert(Entry("X", 120, 2, expiresAtSeconds: 4));
        Assert.DoesNotContain("X", Plates(board));

        board.AdvanceTo(3 * Second);

        Assert.Equal(["X"], Plates(board));
    }

    [Fact]
    [Trait("AC", "AC-12")]
    [Trait("BR", "BR-LB-09")]
    public void Falling_score_promotes_the_next_best_live_candidate()
    {
        var board = new RankedBoard();

        for (int i = 0; i < 10; i++)
        {
            board.Upsert(Entry($"Fast{i}", 150, 1));
        }

        board.Upsert(Entry("X", 120, 2));
        board.Upsert(Entry("Fast3", 110, 3));

        Assert.Contains("X", Plates(board));
        Assert.DoesNotContain("Fast3", Plates(board));
    }

    [Fact]
    [Trait("AC", "AC-14")]
    [Trait("AC", "AC-20")]
    [Trait("BR", "BR-LB-10")]
    public void Infinite_entries_are_pruned_to_the_best_ten_without_changing_top()
    {
        var board = new RankedBoard();
        var expected = new RankedBoard();

        for (int i = 0; i < 100; i++)
        {
            board.Upsert(Entry($"P{i:000}", (i * 37) % 101, i, expiresAtSeconds: null));
        }

        var best = Enumerable.Range(0, 100).Select(i => Entry($"P{i:000}", (i * 37) % 101, i, expiresAtSeconds: null)).Order(RankOrder.Instance).Take(10);

        foreach (var entry in best)
        {
            expected.Upsert(entry);
        }

        Assert.Equal(10, board.Count);
        Assert.Equal(expected.Top(10).Entries, board.Top(10).Entries);
    }

    [Fact]
    [Trait("BR", "BR-LB-10")]
    public void Pruning_counts_only_infinite_entries()
    {
        var board = new RankedBoard();

        for (int i = 0; i < 15; i++)
        {
            board.Upsert(Entry($"Finite{i:00}", 200, 1, expiresAtSeconds: 100));
        }

        for (int i = 0; i < 12; i++)
        {
            board.Upsert(Entry($"Inf{i:00}", 100 + i, 1, expiresAtSeconds: null));
        }

        Assert.Equal(25, board.Count);
        Assert.DoesNotContain(Plates(board, 25), plate => plate is "Inf00" or "Inf01");
    }

    [Fact]
    [Trait("BR", "BR-API-02")]
    public void Top_is_a_detached_copy()
    {
        var board = new RankedBoard();
        board.Upsert(Entry("A", 150, 1));

        var view = board.Top(10);
        board.Upsert(Entry("B", 160, 2));
        board.Remove("A");

        Assert.Equal(["A"], view.Entries.Select(e => e.Entry.Plate));
    }

    private static IEnumerable<string> Plates(RankedBoard board, int count = 10)
    {
        return board.Top(count).Entries.Select(e => e.Entry.Plate);
    }

    internal static BoardEntry Entry(string plate, double score, int timestampSeconds, int? expiresAtSeconds = 1_000_000)
    {
        return new BoardEntry(
            plate,
            score,
            timestampSeconds * Second,
            expiresAtSeconds is { } seconds ? seconds * Second : long.MaxValue,
            MeasurementCount: 0);
    }
}
