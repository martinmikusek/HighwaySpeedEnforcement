namespace HighwaySpeedEnforcement.Generator.Domain;

/// <summary>
/// The simplified Czech-like plate space of design section 4 (AMB-08): series digit, region letter, third character, a space and
/// a four-digit number. An index in [0, <see cref="Size"/>) is a mixed-radix number, least significant part (the number) first.
/// </summary>
public static class PlateFormat
{
    public const string SeriesDigits = "123456789";
    public const string RegionLetters = "ASCPKULHEJBMZT";
    public const string ThirdCharacters = "0123456789ABCDEFHIJKLMNPRSTUVXYZ";
    public const int NumberRadix = 10_000;

    public const int SeriesRadix = 9;
    public const int RegionRadix = 14;
    public const int ThirdRadix = 32;

    public const long Size = SeriesRadix * (long)RegionRadix * ThirdRadix * NumberRadix;

    /// <summary>Length of every decoded plate (for example "1AB 2345").</summary>
    public const int PlateLength = 8;

    public static string Decode(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Size);

        long number = index % NumberRadix;
        long rest = index / NumberRadix;
        char third = ThirdCharacters[(int)(rest % ThirdRadix)];
        rest /= ThirdRadix;
        char region = RegionLetters[(int)(rest % RegionRadix)];
        rest /= RegionRadix;
        char series = SeriesDigits[(int)rest];

        return string.Create(PlateLength, (series, region, third, number), static (span, parts) =>
        {
            span[0] = parts.series;
            span[1] = parts.region;
            span[2] = parts.third;
            span[3] = ' ';
            parts.number.TryFormat(span[4..], out _, "D4");
        });
    }
}
