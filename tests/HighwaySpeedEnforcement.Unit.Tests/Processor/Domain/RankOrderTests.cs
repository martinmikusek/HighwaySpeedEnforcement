using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class RankOrderTests
{
    private static readonly RankOrder Order = RankOrder.Instance;

    [Fact]
    [Trait("BR", "BR-LB-05")]
    public void Compare_higher_score_ranks_first()
    {
        var faster = Entry("B", 150);
        var slower = Entry("A", 140);

        Assert.True(Order.Compare(faster, slower) < 0);
        Assert.True(Order.Compare(slower, faster) > 0);
    }

    [Fact]
    [Trait("AC", "AC-15")]
    [Trait("BR", "BR-LB-05")]
    public void Compare_equal_scores_rank_plate_ascending()
    {
        Assert.True(Order.Compare(Entry("AA9", 140), Entry("AB1", 140)) < 0);
    }

    [Fact]
    [Trait("BR", "BR-LB-05")]
    public void Compare_equal_scores_is_ordinal_and_case_sensitive()
    {
        var sorted = new[] { Entry("aa9", 140), Entry("AB1", 140), Entry("AA9", 140) }.Order(Order).Select(e => e.Plate);

        Assert.Equal(["AA9", "AB1", "aa9"], sorted);
    }

    [Fact]
    [Trait("BR", "BR-LB-05")]
    public void Compare_same_plate_and_score_is_zero()
    {
        Assert.Equal(0, Order.Compare(Entry("X", 1), Entry("X", 1)));
    }

    [Fact]
    [Trait("BR", "BR-LB-05")]
    public void Compare_uses_unrounded_scores()
    {
        Assert.True(Order.Compare(Entry("Z", 140.0001), Entry("A", 140.0)) < 0);
    }

    [Fact]
    [Trait("BR", "BR-LB-05")]
    public void Compare_is_a_consistent_total_order()
    {
        var random = new Random(15);
        var entries = Enumerable.Range(0, 60)
            .Select(_ => Entry(new string((char)('A' + random.Next(3)), random.Next(1, 3)), random.Next(1, 4)))
            .ToArray();

        foreach (var x in entries)
        {
            foreach (var y in entries)
            {
                Assert.Equal(Math.Sign(Order.Compare(x, y)), -Math.Sign(Order.Compare(y, x)));

                foreach (var z in entries)
                {
                    if (Order.Compare(x, y) <= 0 && Order.Compare(y, z) <= 0)
                    {
                        Assert.True(Order.Compare(x, z) <= 0, $"not transitive: {x.Plate}/{x.Score}, {y.Plate}/{y.Score}, {z.Plate}/{z.Score}");
                    }
                }
            }
        }
    }

    private static BoardEntry Entry(string plate, double score)
    {
        return new BoardEntry(plate, score, TimestampTicks: 0, ExpiresAtTicks: long.MaxValue, MeasurementCount: 0);
    }
}
