namespace Xbox360.Fatx.Entries;

[Flags]
public enum FatxEntryAttributes : byte
{
    None = 0,
    ReadOnly = 1 << 0,
    Hidden = 1 << 1,
    System = 1 << 2,
    Directory = 1 << 4,
    Archive = 1 << 5,
}
