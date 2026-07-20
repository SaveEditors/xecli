namespace Xbox360.Fatx.Entries;

public abstract class FatxEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required FatxEntryAttributes Attributes { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public DateTimeOffset? ModifiedAtUtc { get; init; }

    public bool IsDirectory => (Attributes & FatxEntryAttributes.Directory) != 0;
}
