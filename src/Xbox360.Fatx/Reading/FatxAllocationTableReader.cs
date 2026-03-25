using System.Buffers.Binary;
using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.IO;

namespace Xbox360.Fatx.Reading;

public sealed class FatxAllocationTableReader
{
    private readonly FatxVolume _volume;

    public FatxAllocationTableReader(FatxVolume volume)
    {
        _volume = volume;
    }

    public async Task<uint?> ReadNextClusterAsync(uint cluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (cluster == 0)
        {
            return null;
        }

        int entrySize = _volume.ChainEntrySizeBytes;
        long offset = _volume.ChainMapOffset + ((long)cluster * entrySize);
        byte[] entryBytes = await _volume.ReadBytesAtAsync(offset, entrySize, cancellationToken).ConfigureAwait(false);
        if (entryBytes.Length < entrySize)
        {
            throw new FatxCorruptVolumeException($"The FATX chain map ended unexpectedly while reading cluster {cluster}.");
        }

        uint next = entrySize == 2
            ? BinaryPrimitives.ReadUInt16BigEndian(entryBytes)
            : BinaryPrimitives.ReadUInt32BigEndian(entryBytes);

        if (IsTerminalValue(next, entrySize))
        {
            return null;
        }

        if (next >= _volume.Header.TotalClusters)
        {
            throw new FatxCorruptVolumeException($"The FATX chain map pointed cluster {cluster} at an invalid next cluster {next}.");
        }

        return next;
    }

    private static bool IsTerminalValue(uint value, int entrySize)
    {
        if (value == 0)
        {
            return true;
        }

        return entrySize == 2
            ? value is 0xFFF0 or 0xFFF7 or 0xFFF8 or 0xFFFF
            : value is 0xFFFFFFF0 or 0xFFFFFFF7 or 0xFFFFFFF8 or 0xFFFFFFFF;
    }
}
