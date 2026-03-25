namespace Xbox360.Remote.Cli.God;

internal static class GodConstants {
    public const ulong BlockSize = 0x1000;
    public const ulong BlocksPerPart = 0xA1C4;
    public const ulong BlocksPerSubpart = 0xCC;
    public const uint SubpartsPerPart = 0xCB;
    public const ulong SubpartSize = BlockSize * BlocksPerSubpart;
    public const ulong PartSizeBlocks = 0xA290;
}
