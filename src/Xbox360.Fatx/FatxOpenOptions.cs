namespace Xbox360.Fatx;

public sealed class FatxOpenOptions
{
    public bool LeaveOpen { get; init; }

    public bool ReadOnly { get; init; } = true;

    public long? PartitionOffset { get; init; }

    public long? PartitionLength { get; init; }
}
