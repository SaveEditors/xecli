namespace Xbox360.Fatx.Formatting;

public sealed class FatxFormatOptions
{
    public string Label { get; init; } = "FATX Volume";

    public string Magic { get; init; } = "FATX";

    public uint? SectorsPerCluster { get; init; }

    public uint? VolumeId { get; init; }

    public bool FullZero { get; init; }
}
