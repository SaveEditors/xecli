namespace Xbox360.Fatx.Layout;

public sealed class Xbox360PartitionDefinition
{
    public required int Index { get; init; }

    public required string Name { get; init; }

    public required FatxPartitionKind Kind { get; init; }

    public required long Offset { get; init; }

    public required long Length { get; init; }
}
