using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.Layout;

namespace Xbox360.Fatx.Formatting;

public static class FatxFormatter
{
    private const int SectorSize = 0x200;
    private const int HeaderSize = 0x1000;

    public static async Task<FatxVolumeHeader> FormatAsync(
        Stream partitionStream,
        FatxFormatOptions? options = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partitionStream);
        if (!partitionStream.CanWrite)
            throw new FatxException("The target FATX partition stream is not writable.");
        if (!partitionStream.CanSeek)
            throw new FatxException("The target FATX partition stream must be seekable.");

        options ??= new FatxFormatOptions();
        long partitionLength = partitionStream.Length;
        if (partitionLength < 0x200000)
            throw new FatxException("The selected partition is too small to format as FATX.");

        uint sectorsPerCluster = options.SectorsPerCluster ?? ChooseSectorsPerCluster(partitionLength);
        if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0)
            throw new FatxException("FATX sectors-per-cluster must be a power of two.");

        uint clusterSize = checked(sectorsPerCluster * SectorSize);
        uint totalClusters = checked((uint)((partitionLength / clusterSize) + 1));
        int chainEntrySize = totalClusters < 0xFFF0 ? 2 : 4;
        long chainMapLength = (((long)chainEntrySize * totalClusters) + 0xFFF) & ~0xFFFL;
        long dataRegionOffset = HeaderSize + chainMapLength;
        if (dataRegionOffset + clusterSize > partitionLength)
            throw new FatxException("The selected partition is too small for the requested FATX layout.");

        if (options.FullZero)
        {
            await ZeroRegionAsync(partitionStream, 0, partitionLength, progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            long quickLength = Math.Min(partitionLength, dataRegionOffset + clusterSize);
            await ZeroRegionAsync(partitionStream, 0, quickLength, progress, cancellationToken).ConfigureAwait(false);
        }

        uint volumeId = options.VolumeId ?? GetRandomVolumeId();
        byte[] header = BuildHeader(options, volumeId, sectorsPerCluster);
        partitionStream.Position = 0;
        await partitionStream.WriteAsync(header, cancellationToken).ConfigureAwait(false);

        byte[] chainBuffer = new byte[Math.Min(0x10000, (int)Math.Min(chainMapLength, 0x10000))];
        long chainOffset = HeaderSize;
        while (chainOffset < HeaderSize + chainMapLength)
        {
            int toWrite = (int)Math.Min(chainBuffer.Length, (HeaderSize + chainMapLength) - chainOffset);
            partitionStream.Position = chainOffset;
            await partitionStream.WriteAsync(chainBuffer.AsMemory(0, toWrite), cancellationToken).ConfigureAwait(false);
            chainOffset += toWrite;
        }

        byte[] terminalEntry = chainEntrySize == 2
            ? new byte[] { 0xFF, 0xFF }
            : new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

        partitionStream.Position = HeaderSize;
        await partitionStream.WriteAsync(terminalEntry, cancellationToken).ConfigureAwait(false);
        partitionStream.Position = HeaderSize + chainEntrySize;
        await partitionStream.WriteAsync(terminalEntry, cancellationToken).ConfigureAwait(false);

        partitionStream.Position = dataRegionOffset;
        await partitionStream.WriteAsync(new byte[clusterSize], cancellationToken).ConfigureAwait(false);
        await partitionStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        partitionStream.Position = 0;
        return await FatxVolumeHeader.ReadAsync(partitionStream, cancellationToken).ConfigureAwait(false);
    }

    public static uint ChooseSectorsPerCluster(long partitionLength)
    {
        if (partitionLength >= 0x100000000L)
            return 64;
        if (partitionLength >= 0x40000000L)
            return 32;
        return 16;
    }

    private static byte[] BuildHeader(FatxFormatOptions options, uint volumeId, uint sectorsPerCluster)
    {
        byte[] header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes(string.IsNullOrWhiteSpace(options.Magic) ? "FATX" : options.Magic.ToUpperInvariant()).AsSpan(0, 4).CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), volumeId);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), sectorsPerCluster);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x0C, 4), 1);

        string label = string.IsNullOrWhiteSpace(options.Label) ? "FATX Volume" : options.Label.Trim();
        byte[] labelBytes = Encoding.Unicode.GetBytes(label);
        int labelLength = Math.Min(labelBytes.Length, 0x40);
        labelBytes.AsSpan(0, labelLength).CopyTo(header.AsSpan(0x10, labelLength));
        return header;
    }

    private static uint GetRandomVolumeId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static async Task ZeroRegionAsync(Stream stream, long offset, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[0x10000];
        long written = 0;
        while (written < length)
        {
            int toWrite = (int)Math.Min(buffer.Length, length - written);
            stream.Position = offset + written;
            await stream.WriteAsync(buffer.AsMemory(0, toWrite), cancellationToken).ConfigureAwait(false);
            written += toWrite;
            progress?.Report(written);
        }
    }
}
