using System.Text.RegularExpressions;
using HighwaySpeedEnforcement.Generator.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Domain;

public sealed partial class PlateFormatTests
{
    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Size_equals_the_product_of_the_radixes()
    {
        Assert.Equal(9, PlateFormat.SeriesDigits.Length);
        Assert.Equal(14, PlateFormat.RegionLetters.Length);
        Assert.Equal(32, PlateFormat.ThirdCharacters.Length);
        Assert.Equal(
            PlateFormat.Size,
            (long)PlateFormat.SeriesDigits.Length * PlateFormat.RegionLetters.Length * PlateFormat.ThirdCharacters.Length * PlateFormat.NumberRadix);
        Assert.Equal(40_320_000, PlateFormat.Size);
    }

    [Theory]
    [InlineData(0, "1A0 0000")]
    [InlineData(1, "1A0 0001")]
    [InlineData(9_999, "1A0 9999")]
    [InlineData(10_000, "1A1 0000")]
    [InlineData(PlateFormat.Size - 1, "9TZ 9999")]
    public void Decode_fixed_index_gives_expected_plate(long index, string expected)
    {
        Assert.Equal(expected, PlateFormat.Decode(index));
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Decode_sampled_indices_match_the_plate_pattern()
    {
        const ulong Seed = 7;
        var random = new DeterministicRandom(Seed);
        var pattern = PlatePattern();

        for (int i = 0; i < 200_000; i++)
        {
            long index = ((long)random.NextInt(int.MaxValue) * int.MaxValue + random.NextInt(int.MaxValue)) % PlateFormat.Size;
            string plate = PlateFormat.Decode(index);

            Assert.True(pattern.IsMatch(plate), $"Plate '{plate}' of index {index} does not match the pattern (seed {Seed}).");
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Decode_first_indices_give_distinct_plates()
    {
        var plates = new HashSet<string>(StringComparer.Ordinal);

        for (long index = 0; index < 100_000; index++)
        {
            Assert.True(plates.Add(PlateFormat.Decode(index)), $"Plate of index {index} repeats an earlier one.");
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PlateFormat.Size)]
    [InlineData(long.MaxValue)]
    public void Decode_index_outside_the_space_throws(long index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlateFormat.Decode(index));
    }

    [GeneratedRegex("^[1-9][ASCPKULHEJBMZT][0-9A-FHIJKLMNPRSTUVXYZ] [0-9]{4}$")]
    private static partial Regex PlatePattern();
}
