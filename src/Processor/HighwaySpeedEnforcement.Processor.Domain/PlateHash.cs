namespace HighwaySpeedEnforcement.Processor.Domain;

/// <summary>
/// Stable, culture-independent hash of a plate for partitioning: 32-bit FNV-1a over the UTF-16 code units, low byte first.
/// <see cref="string.GetHashCode()"/> is randomized per process and must not be used for routing.
/// </summary>
public static class PlateHash
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Compute(string plate)
    {
        uint hash = OffsetBasis;

        foreach (char c in plate)
        {
            hash = (hash ^ (byte)c) * Prime;
            hash = (hash ^ (byte)(c >> 8)) * Prime;
        }

        return hash;
    }

    public static int Partition(string plate, int partitionCount)
    {
        return (int)(Compute(plate) % (uint)partitionCount);
    }
}
