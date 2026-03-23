using System.Buffers.Binary;
using Xbox360.Fatx.Exceptions;

namespace Xbox360.Fatx.Writing;

public sealed class FatxAllocationTableEditor
{
    private readonly FatxVolume _volume;

    public FatxAllocationTableEditor(FatxVolume volume)
    {
        _volume = volume;
    }

    public uint TerminalValue => _volume.ChainEntrySizeBytes == 2 ? 0xFFFFu : 0xFFFFFFFFu;

    public async Task<uint> ReadEntryAsync(uint cluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCluster(cluster);

        int entrySize = _volume.ChainEntrySizeBytes;
        long offset = _volume.ChainMapOffset + ((long)cluster * entrySize);
        byte[] bytes = await _volume.ReadBytesAtAsync(offset, entrySize, cancellationToken).ConfigureAwait(false);
        if (bytes.Length < entrySize)
        {
            throw new FatxCorruptVolumeException($"The FATX chain map ended unexpectedly while reading cluster {cluster}.");
        }

        return entrySize == 2
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    public async Task WriteEntryAsync(uint cluster, uint value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCluster(cluster);

        int entrySize = _volume.ChainEntrySizeBytes;
        byte[] bytes = new byte[entrySize];
        if (entrySize == 2)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }

        long offset = _volume.ChainMapOffset + ((long)cluster * entrySize);
        await _volume.WriteBytesAtAsync(offset, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<uint>> GetChainAsync(uint firstCluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (firstCluster == 0)
        {
            return Array.Empty<uint>();
        }

        var clusters = new List<uint>();
        var visited = new HashSet<uint>();
        uint current = firstCluster;
        while (true)
        {
            ValidateCluster(current);
            if (!visited.Add(current))
            {
                throw new FatxCorruptVolumeException($"Detected a loop in the FATX chain map starting at cluster {firstCluster}.");
            }

            clusters.Add(current);
            uint next = await ReadEntryAsync(current, cancellationToken).ConfigureAwait(false);
            if (IsTerminalValue(next))
            {
                break;
            }

            current = next;
        }

        return clusters;
    }

    public async Task<IReadOnlyList<uint>> AllocateChainAsync(int clusterCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (clusterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterCount));
        }

        if (clusterCount == 0)
        {
            return Array.Empty<uint>();
        }

        List<uint> freeClusters = await FindFreeClustersAsync(clusterCount, cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < freeClusters.Count - 1; index++)
        {
            await WriteEntryAsync(freeClusters[index], freeClusters[index + 1], cancellationToken).ConfigureAwait(false);
        }

        await WriteEntryAsync(freeClusters[^1], TerminalValue, cancellationToken).ConfigureAwait(false);
        return freeClusters;
    }

    public async Task<uint> AppendClusterAsync(uint firstCluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<uint> chain = await GetChainAsync(firstCluster, cancellationToken).ConfigureAwait(false);
        if (chain.Count == 0)
        {
            throw new FatxException("Cannot append a cluster to an empty FATX chain.");
        }

        uint newCluster = (await AllocateChainAsync(1, cancellationToken).ConfigureAwait(false))[0];
        await WriteEntryAsync(chain[^1], newCluster, cancellationToken).ConfigureAwait(false);
        return newCluster;
    }

    public async Task FreeChainAsync(uint firstCluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (firstCluster == 0)
        {
            return;
        }

        var visited = new HashSet<uint>();
        uint current = firstCluster;
        while (true)
        {
            ValidateCluster(current);
            if (!visited.Add(current))
            {
                throw new FatxCorruptVolumeException($"Detected a loop while freeing the FATX chain starting at cluster {firstCluster}.");
            }

            uint next = await ReadEntryAsync(current, cancellationToken).ConfigureAwait(false);
            await WriteEntryAsync(current, 0, cancellationToken).ConfigureAwait(false);
            if (IsTerminalValue(next))
            {
                break;
            }

            current = next;
        }
    }

    public bool IsTerminalValue(uint value)
    {
        if (value == 0)
        {
            return true;
        }

        return _volume.ChainEntrySizeBytes == 2
            ? value is 0xFFF0 or 0xFFF7 or 0xFFF8 or 0xFFFF
            : value is 0xFFFFFFF0 or 0xFFFFFFF7 or 0xFFFFFFF8 or 0xFFFFFFFF;
    }

    private async Task<List<uint>> FindFreeClustersAsync(int clusterCount, CancellationToken cancellationToken)
    {
        var freeClusters = new List<uint>(clusterCount);
        for (uint cluster = 1; cluster < _volume.Header.TotalClusters && freeClusters.Count < clusterCount; cluster++)
        {
            uint value = await ReadEntryAsync(cluster, cancellationToken).ConfigureAwait(false);
            if (value == 0)
            {
                freeClusters.Add(cluster);
            }
        }

        if (freeClusters.Count != clusterCount)
        {
            throw new FatxException($"Not enough free FATX clusters were found to allocate {clusterCount} cluster(s).");
        }

        return freeClusters;
    }

    private void ValidateCluster(uint cluster)
    {
        if (cluster == 0 || cluster >= _volume.Header.TotalClusters)
        {
            throw new FatxException($"Cluster {cluster} is outside the readable FATX cluster range.");
        }
    }
}
