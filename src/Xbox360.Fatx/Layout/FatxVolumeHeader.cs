using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.IO;

namespace Xbox360.Fatx.Layout;

public sealed class FatxVolumeHeader
{
    public required string Magic { get; init; }

    public required string Label { get; init; }

    public required uint VolumeId { get; init; }

    public required uint SectorsPerCluster { get; init; }

    public required uint RootDirectoryCluster { get; init; }

    public required uint ClusterSize { get; init; }

    public required uint TotalClusters { get; init; }

    public required int ChainEntrySize { get; init; }

    public required long ChainMapOffset { get; init; }

    public required long DataRegionOffset { get; init; }

    public required long DataRegionLength { get; init; }

    public static async Task<FatxVolumeHeader> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        byte[] headerBytes = await FatxBinaryReader.ReadBytesAsync(stream, 0x40, cancellationToken).ConfigureAwait(false);
        if (headerBytes.Length < 0x10)
        {
            throw new FatxCorruptVolumeException("The partition is too small to contain a FATX header.");
        }

        string magic = FatxBinaryReader.ReadAscii(headerBytes.AsSpan(0, 4));
        if (!string.Equals(magic, "FATX", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(magic, "XTAF", StringComparison.OrdinalIgnoreCase))
        {
            throw new FatxUnsupportedLayoutException($"Unsupported FATX magic '{magic}'.");
        }

        uint volumeId = FatxBinaryReader.ReadUInt32(headerBytes.AsSpan(4, 4), FatxEndian.Big);
        uint sectorsPerCluster = FatxBinaryReader.ReadUInt32(headerBytes.AsSpan(8, 4), FatxEndian.Big);
        uint rootDirectoryCluster = FatxBinaryReader.ReadUInt32(headerBytes.AsSpan(0x0C, 4), FatxEndian.Big);
        uint clusterSize = sectorsPerCluster == 0 ? 0u : sectorsPerCluster * 0x200u;
        if (clusterSize == 0)
        {
            throw new FatxCorruptVolumeException("The FATX partition does not define a valid cluster size.");
        }

        const long chainMapOffset = 0x1000L;
        long totalClustersLong = (stream.Length / clusterSize) + 1;
        if (totalClustersLong <= 0 || totalClustersLong > uint.MaxValue)
        {
            throw new FatxCorruptVolumeException("The FATX partition cluster count is invalid.");
        }

        uint totalClusters = (uint)totalClustersLong;
        int chainEntrySize = totalClusters < 0xFFF0 ? 2 : 4;
        long chainMapLength = (((long)chainEntrySize * totalClusters) + 0xFFF) & ~0xFFFL;
        long dataRegionOffset = chainMapOffset + chainMapLength;
        long dataRegionLength = Math.Max(0L, stream.Length - dataRegionOffset);

        return new FatxVolumeHeader
        {
            Magic = magic.ToUpperInvariant(),
            Label = TryReadLabel(headerBytes),
            VolumeId = volumeId,
            SectorsPerCluster = sectorsPerCluster,
            RootDirectoryCluster = rootDirectoryCluster == 0 ? 1u : rootDirectoryCluster,
            ClusterSize = clusterSize,
            TotalClusters = totalClusters,
            ChainEntrySize = chainEntrySize,
            ChainMapOffset = chainMapOffset,
            DataRegionOffset = dataRegionOffset,
            DataRegionLength = dataRegionLength,
        };
    }

    private static string TryReadLabel(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length <= 0x10)
        {
            return "FATX Volume";
        }

        int length = Math.Min(0x40, headerBytes.Length - 0x10);
        if (length <= 0)
        {
            return "FATX Volume";
        }

        try
        {
            string label = System.Text.Encoding.Unicode.GetString(headerBytes.Slice(0x10, length));
            label = label.TrimEnd('\0', ' ');
            return string.IsNullOrWhiteSpace(label) ? "FATX Volume" : label;
        }
        catch
        {
            return "FATX Volume";
        }
    }
}
