using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

public sealed class PlateHashTests
{
    [Fact]
    [Trait("BR", "BR-ING-03")]
    public void Compute_is_stable_across_calls_and_instances()
    {
        Assert.Equal(PlateHash.Compute("1AB2345"), PlateHash.Compute(new string("1AB2345".ToCharArray())));
    }

    /// <summary>Reference values computed independently (FNV-1a 32 over UTF-16LE bytes); they fix the algorithm.</summary>
    [Theory]
    [InlineData("", 2166136261u)]
    [InlineData("A", 1792377636u)]
    [InlineData("1AB2345", 1645153911u)]
    [InlineData("ABC-123", 1976677088u)]
    [InlineData("abc-123", 3422673216u)]
    [Trait("BR", "BR-ING-03")]
    public void Compute_matches_the_reference_values(string plate, uint expected)
    {
        Assert.Equal(expected, PlateHash.Compute(plate));
    }

    [Fact]
    [Trait("BR", "BR-ING-03")]
    public void Partition_spreads_generated_plates_evenly_over_four_partitions()
    {
        const int plates = 10_000;
        const int partitions = 4;
        var random = new Random(6);
        var counts = new int[partitions];

        for (int i = 0; i < plates; i++)
        {
            string plate = $"{random.Next(1, 10)}{(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))} {random.Next(1000, 10000)}";
            counts[PlateHash.Partition(plate, partitions)]++;
        }

        const int expected = plates / partitions;

        Assert.All(counts, count => Assert.InRange(count, expected * 9 / 10, expected * 11 / 10));
    }
}
