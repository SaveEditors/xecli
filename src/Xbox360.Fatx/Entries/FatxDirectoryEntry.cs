namespace Xbox360.Fatx.Entries;

public sealed class FatxDirectoryEntry : FatxEntry
{
    public uint? FirstCluster { get; init; }

    public int ChildCount { get; init; }

    public IReadOnlyList<FatxEntry> Children { get; init; } = Array.Empty<FatxEntry>();
}
