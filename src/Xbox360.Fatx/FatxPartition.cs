namespace Xbox360.Fatx;

public sealed class FatxPartition
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public required FatxPartitionKind Kind { get; init; }

    public required long Offset { get; init; }

    public required long Length { get; init; }

    public bool CanMount => Length > 0;
}
