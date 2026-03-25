using Xbox360.Fatx.Entries;

namespace Xbox360.Fatx.Layout;

public sealed class FatxDirectoryEntryRecord
{
    public required string Name { get; init; }

    public required uint FirstCluster { get; init; }

    public required long Size { get; init; }

    public required FatxEntryAttributes Attributes { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public DateTimeOffset? ModifiedAtUtc { get; init; }

    public DateTimeOffset? AccessedAtUtc { get; init; }
}
