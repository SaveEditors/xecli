using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Exceptions;

namespace Xbox360.Fatx.Writing;

public sealed class FatxMutationService
{
    private readonly FatxVolume _volume;
    private readonly FatxAllocationTableEditor _allocationTable;
    private readonly FatxDirectoryEditor _directoryEditor;

    public FatxMutationService(FatxVolume volume)
    {
        if (!volume.BaseStream.CanWrite)
        {
            throw new FatxException("This FATX volume was opened read-only.");
        }

        _volume = volume;
        _allocationTable = new FatxAllocationTableEditor(volume);
        _directoryEditor = new FatxDirectoryEditor(volume, _allocationTable);
    }

    public async Task PutFileAsync(string hostPath, string fatxPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException("Host file not found.", hostPath);
        }

        string normalizedPath = FatxDirectoryEditor.NormalizePath(fatxPath);
        (string parentPath, string name) = FatxDirectoryEditor.SplitParentAndName(normalizedPath);
        FatxDirectoryContext parent = await _directoryEditor.OpenDirectoryAsync(parentPath, cancellationToken).ConfigureAwait(false);
        FatxDirectorySlot? existing = _directoryEditor.FindEntry(parent, name);
        if (existing?.Entry is FatxDirectoryEntry)
        {
            throw new FatxException($"FATX path '{normalizedPath}' already exists as a directory.");
        }

        if (existing is not null && !overwrite)
        {
            throw new FatxException($"FATX path '{normalizedPath}' already exists. Use --overwrite to replace it.");
        }

        if (existing?.Entry is FatxFileEntry existingFile && existingFile.FirstCluster.HasValue)
        {
            await _allocationTable.FreeChainAsync(existingFile.FirstCluster.Value, cancellationToken).ConfigureAwait(false);
        }

        byte[] payload = await File.ReadAllBytesAsync(hostPath, cancellationToken).ConfigureAwait(false);
        int clusterSize = _volume.ClusterSizeBytes;
        int clusterCount = payload.Length == 0 ? 0 : (int)Math.Ceiling(payload.Length / (double)clusterSize);
        IReadOnlyList<uint> clusters = await _allocationTable.AllocateChainAsync(clusterCount, cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < clusters.Count; index++)
        {
            int offset = index * clusterSize;
            int count = Math.Min(clusterSize, payload.Length - offset);
            await _volume.WriteClusterAsync(clusters[index], payload.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }

        long recordOffset = existing?.RecordOffset ?? await _directoryEditor.GetWritableRecordOffsetAsync(parent, cancellationToken).ConfigureAwait(false);
        await _directoryEditor.WriteEntryAsync(
            recordOffset,
            name,
            FatxEntryAttributes.Archive,
            clusters.Count == 0 ? 0u : clusters[0],
            payload.LongLength,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateDirectoryAsync(string fatxPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = FatxDirectoryEditor.NormalizePath(fatxPath);
        (string parentPath, string name) = FatxDirectoryEditor.SplitParentAndName(normalizedPath);
        FatxDirectoryContext parent = await _directoryEditor.OpenDirectoryAsync(parentPath, cancellationToken).ConfigureAwait(false);
        if (_directoryEditor.FindEntry(parent, name) is not null)
        {
            throw new FatxException($"FATX path '{normalizedPath}' already exists.");
        }

        uint cluster = (await _allocationTable.AllocateChainAsync(1, cancellationToken).ConfigureAwait(false))[0];
        await _volume.WriteClusterAsync(cluster, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        long recordOffset = await _directoryEditor.GetWritableRecordOffsetAsync(parent, cancellationToken).ConfigureAwait(false);
        await _directoryEditor.WriteEntryAsync(recordOffset, name, FatxEntryAttributes.Directory, cluster, 0, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string fatxPath, bool recursive = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = FatxDirectoryEditor.NormalizePath(fatxPath);
        (string parentPath, string name) = FatxDirectoryEditor.SplitParentAndName(normalizedPath);
        FatxDirectoryContext parent = await _directoryEditor.OpenDirectoryAsync(parentPath, cancellationToken).ConfigureAwait(false);
        FatxDirectorySlot slot = _directoryEditor.FindEntry(parent, name)
            ?? throw new FatxPathNotFoundException(normalizedPath);
        if (slot.Entry is null)
        {
            throw new FatxPathNotFoundException(normalizedPath);
        }

        if (slot.Entry is FatxDirectoryEntry directoryEntry)
        {
            if (directoryEntry.FirstCluster.HasValue)
            {
                FatxDirectoryContext directory = await _directoryEditor.OpenDirectoryAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
                List<FatxDirectorySlot> children = directory.Slots.Where(candidate => !candidate.IsFree && candidate.Entry is not null).ToList();
                if (children.Count > 0 && !recursive)
                {
                    throw new FatxException($"FATX directory '{normalizedPath}' is not empty. Use --recursive to remove it.");
                }

                foreach (FatxDirectorySlot child in children)
                {
                    await DeleteAsync(child.Entry!.FullPath, true, cancellationToken).ConfigureAwait(false);
                }

                await _allocationTable.FreeChainAsync(directoryEntry.FirstCluster.Value, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (slot.Entry is FatxFileEntry fileEntry && fileEntry.FirstCluster.HasValue)
        {
            await _allocationTable.FreeChainAsync(fileEntry.FirstCluster.Value, cancellationToken).ConfigureAwait(false);
        }

        await _directoryEditor.MarkDeletedAsync(slot.RecordOffset, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedSource = FatxDirectoryEditor.NormalizePath(sourcePath);
        string normalizedDestination = FatxDirectoryEditor.NormalizePath(destinationPath);
        (string sourceParentPath, string sourceName) = FatxDirectoryEditor.SplitParentAndName(normalizedSource);
        (string destinationParentPath, string destinationName) = FatxDirectoryEditor.SplitParentAndName(normalizedDestination);

        FatxDirectoryContext sourceParent = await _directoryEditor.OpenDirectoryAsync(sourceParentPath, cancellationToken).ConfigureAwait(false);
        FatxDirectorySlot sourceSlot = _directoryEditor.FindEntry(sourceParent, sourceName)
            ?? throw new FatxPathNotFoundException(normalizedSource);
        if (sourceSlot.Entry is null)
        {
            throw new FatxPathNotFoundException(normalizedSource);
        }

        FatxDirectoryContext destinationParent = await _directoryEditor.OpenDirectoryAsync(destinationParentPath, cancellationToken).ConfigureAwait(false);
        FatxDirectorySlot? existingDestination = _directoryEditor.FindEntry(destinationParent, destinationName);
        if (existingDestination is not null)
        {
            if (!overwrite)
            {
                throw new FatxException($"FATX path '{normalizedDestination}' already exists. Use --overwrite to replace it.");
            }

            if (existingDestination.Entry is FatxDirectoryEntry)
            {
                throw new FatxException("Overwriting an existing FATX directory during move is not supported.");
            }

            await DeleteAsync(normalizedDestination, false, cancellationToken).ConfigureAwait(false);
            destinationParent = await _directoryEditor.OpenDirectoryAsync(destinationParentPath, cancellationToken).ConfigureAwait(false);
        }

        uint firstCluster = sourceSlot.Entry switch
        {
            FatxDirectoryEntry directory => directory.FirstCluster ?? 0,
            FatxFileEntry file => file.FirstCluster ?? 0,
            _ => 0
        };

        FatxEntryAttributes attributes = sourceSlot.Entry.Attributes;
        long size = sourceSlot.Entry is FatxFileEntry fileEntry ? fileEntry.Size : 0;

        if (string.Equals(sourceParentPath, destinationParentPath, StringComparison.OrdinalIgnoreCase))
        {
            await _directoryEditor.WriteEntryAsync(sourceSlot.RecordOffset, destinationName, attributes, firstCluster, size, cancellationToken).ConfigureAwait(false);
            return;
        }

        long destinationOffset = await _directoryEditor.GetWritableRecordOffsetAsync(destinationParent, cancellationToken).ConfigureAwait(false);
        await _directoryEditor.WriteEntryAsync(destinationOffset, destinationName, attributes, firstCluster, size, cancellationToken).ConfigureAwait(false);
        await _directoryEditor.MarkDeletedAsync(sourceSlot.RecordOffset, cancellationToken).ConfigureAwait(false);
    }
}
