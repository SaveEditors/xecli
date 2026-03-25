using System.Buffers.Binary;
using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Writing;

namespace Xbox360.Fatx.Repair;

public sealed class FatxRepairService
{
    private readonly FatxVolume _volume;
    private readonly FatxAllocationTableEditor _editor;

    public FatxRepairService(FatxVolume volume)
    {
        _volume = volume;
        _editor = new FatxAllocationTableEditor(volume);
    }

    public async Task<FatxRepairReport> AnalyzeAsync(bool applyRepairs = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        uint[] entries = await ReadChainEntriesAsync(cancellationToken).ConfigureAwait(false);
        HashSet<uint> referenced = new() { _volume.Header.RootDirectoryCluster };
        List<string> issues = new();

        await CollectReferencedChainsAsync("/", entries, referenced, issues, cancellationToken).ConfigureAwait(false);

        HashSet<uint> allocated = new();
        Dictionary<uint, int> predecessors = new();
        List<uint> invalidUnreferenced = new();
        for (uint cluster = 1; cluster < entries.Length; cluster++)
        {
            uint next = entries[cluster];
            if (next == 0)
                continue;

            allocated.Add(cluster);
            if (_editor.IsTerminalValue(next))
                continue;

            if (next >= entries.Length)
            {
                if (!referenced.Contains(cluster))
                {
                    invalidUnreferenced.Add(cluster);
                }

                issues.Add($"Cluster {cluster} points outside the FATX range ({next}).");
                continue;
            }

            predecessors[next] = predecessors.TryGetValue(next, out int count) ? count + 1 : 1;
        }

        HashSet<uint> orphaned = allocated.Where(cluster => !referenced.Contains(cluster)).ToHashSet();
        int reclaimedChains = 0;
        int reclaimedClusters = 0;

        if (applyRepairs && _volume.BaseStream.CanWrite)
        {
            foreach (uint invalidCluster in invalidUnreferenced)
            {
                await _editor.WriteEntryAsync(invalidCluster, 0, cancellationToken).ConfigureAwait(false);
                orphaned.Remove(invalidCluster);
                reclaimedClusters++;
            }

            List<uint> heads = orphaned
                .Where(cluster => !predecessors.ContainsKey(cluster) || predecessors[cluster] == 0)
                .OrderBy(cluster => cluster)
                .ToList();
            foreach (uint head in heads)
            {
                IReadOnlyList<uint> chain = await SafeReadChainAsync(head, entries, cancellationToken).ConfigureAwait(false);
                foreach (uint cluster in chain)
                {
                    orphaned.Remove(cluster);
                    reclaimedClusters++;
                }

                if (chain.Count > 0)
                {
                    await _editor.FreeChainAsync(head, cancellationToken).ConfigureAwait(false);
                    reclaimedChains++;
                }
            }

            if (orphaned.Count > 0)
            {
                foreach (uint cluster in orphaned.OrderBy(cluster => cluster))
                {
                    await _editor.WriteEntryAsync(cluster, 0, cancellationToken).ConfigureAwait(false);
                    reclaimedClusters++;
                }
            }
        }

        return new FatxRepairReport(
            _volume.Partition.Name,
            allocated.Count,
            referenced.Count,
            orphaned.Count,
            invalidUnreferenced.Count,
            applyRepairs,
            reclaimedChains,
            reclaimedClusters,
            issues);
    }

    private async Task CollectReferencedChainsAsync(
        string path,
        uint[] entries,
        HashSet<uint> referenced,
        List<string> issues,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FatxEntry> children;
        try
        {
            children = await _volume.ListDirectoryAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            issues.Add($"Directory read failed for {path}: {ex.Message}");
            return;
        }

        foreach (FatxEntry child in children)
        {
            uint? firstCluster = child switch
            {
                FatxDirectoryEntry directory => directory.FirstCluster,
                FatxFileEntry file => file.FirstCluster,
                _ => null
            };

            if (firstCluster.HasValue && firstCluster.Value > 0)
            {
                MarkChain(child.FullPath, firstCluster.Value, entries, referenced, issues);
            }

            if (child is FatxDirectoryEntry)
            {
                await CollectReferencedChainsAsync(child.FullPath, entries, referenced, issues, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void MarkChain(string path, uint firstCluster, uint[] entries, HashSet<uint> referenced, List<string> issues)
    {
        HashSet<uint> visited = new();
        uint current = firstCluster;
        while (current > 0)
        {
            if (current >= entries.Length)
            {
                issues.Add($"Path {path} references cluster {current}, which is outside the FATX range.");
                return;
            }

            referenced.Add(current);
            if (!visited.Add(current))
            {
                issues.Add($"Path {path} has a loop in its cluster chain starting at {firstCluster}.");
                return;
            }

            uint next = entries[current];
            if (next == 0 || _editor.IsTerminalValue(next))
                return;

            if (next >= entries.Length)
            {
                issues.Add($"Path {path} points cluster {current} to invalid cluster {next}.");
                return;
            }

            current = next;
        }
    }

    private async Task<IReadOnlyList<uint>> SafeReadChainAsync(uint firstCluster, uint[] entries, CancellationToken cancellationToken)
    {
        List<uint> chain = new();
        HashSet<uint> visited = new();
        uint current = firstCluster;
        while (current > 0 && current < entries.Length && visited.Add(current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chain.Add(current);
            uint next = entries[current];
            if (next == 0 || _editor.IsTerminalValue(next))
                break;
            if (next >= entries.Length)
                break;
            current = next;
        }

        await Task.CompletedTask;
        return chain;
    }

    private async Task<uint[]> ReadChainEntriesAsync(CancellationToken cancellationToken)
    {
        int entrySize = _volume.ChainEntrySizeBytes;
        long byteLength = (long)_volume.Header.TotalClusters * entrySize;
        if (byteLength > int.MaxValue)
            throw new InvalidOperationException("The FATX chain map is too large for the current repair service implementation.");

        byte[] bytes = await _volume.ReadBytesAtAsync(_volume.ChainMapOffset, (int)byteLength, cancellationToken).ConfigureAwait(false);
        uint[] entries = new uint[_volume.Header.TotalClusters];
        for (uint cluster = 0; cluster < entries.Length; cluster++)
        {
            int offset = checked((int)cluster * entrySize);
            entries[cluster] = entrySize == 2
                ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, entrySize))
                : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, entrySize));
        }

        return entries;
    }
}

public sealed record FatxRepairReport(
    string Partition,
    int AllocatedClusters,
    int ReferencedClusters,
    int OrphanedClusters,
    int InvalidUnreferencedClusters,
    bool RepairsApplied,
    int ReclaimedChains,
    int ReclaimedClusters,
    IReadOnlyList<string> Issues);
