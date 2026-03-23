using System.Text;
using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Exceptions;

namespace Xbox360.Fatx.Writing;

public sealed class FatxDirectoryEditor
{
    private const int RecordSize = 0x40;
    private readonly FatxVolume _volume;
    private readonly FatxAllocationTableEditor _allocationTable;

    public FatxDirectoryEditor(FatxVolume volume, FatxAllocationTableEditor allocationTable)
    {
        _volume = volume;
        _allocationTable = allocationTable;
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        string normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length > 1
            ? normalized.TrimEnd('/')
            : normalized;
    }

    public static (string ParentPath, string Name) SplitParentAndName(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized == "/")
        {
            throw new FatxException("The FATX root path cannot be modified directly.");
        }

        int separator = normalized.LastIndexOf('/');
        string parentPath = separator <= 0 ? "/" : normalized[..separator];
        string name = normalized[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FatxException("The FATX path does not include a valid entry name.");
        }

        return (parentPath, name);
    }

    public async Task<FatxDirectoryContext> OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = NormalizePath(directoryPath);
        uint firstCluster;

        if (normalized == "/")
        {
            firstCluster = _volume.RootDirectoryCluster;
        }
        else
        {
            FatxEntry entry = await _volume.ResolvePathAsync(normalized, cancellationToken).ConfigureAwait(false);
            if (entry is not FatxDirectoryEntry directoryEntry)
            {
                throw new FatxException($"Path '{directoryPath}' does not reference a FATX directory.");
            }

            firstCluster = directoryEntry.FirstCluster ?? 0;
        }

        IReadOnlyList<uint> chain = firstCluster == 0
            ? Array.Empty<uint>()
            : await _allocationTable.GetChainAsync(firstCluster, cancellationToken).ConfigureAwait(false);

        List<FatxDirectorySlot> slots = new();
        foreach (uint cluster in chain)
        {
            byte[] clusterBytes = await _volume.ReadClusterBytesAsync(cluster, cancellationToken).ConfigureAwait(false);
            long clusterOffset = _volume.GetClusterOffset(cluster);
            for (int index = 0; index + RecordSize <= clusterBytes.Length; index += RecordSize)
            {
                ReadOnlySpan<byte> record = clusterBytes.AsSpan(index, RecordSize);
                slots.Add(ParseSlot(record, clusterOffset + index, normalized));
            }
        }

        return new FatxDirectoryContext(normalized, firstCluster, chain, slots);
    }

    public FatxDirectorySlot? FindEntry(FatxDirectoryContext directory, string name)
        => directory.Slots.FirstOrDefault(slot =>
            !slot.IsFree &&
            slot.Entry is not null &&
            string.Equals(slot.Entry.Name, name, StringComparison.OrdinalIgnoreCase));

    public async Task<long> GetWritableRecordOffsetAsync(FatxDirectoryContext directory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FatxDirectorySlot? freeSlot = directory.Slots.FirstOrDefault(slot => slot.IsFree);
        if (freeSlot is not null)
        {
            return freeSlot.RecordOffset;
        }

        if (directory.FirstCluster == 0)
        {
            throw new FatxException("The selected FATX directory has no writable cluster chain.");
        }

        uint newCluster = await _allocationTable.AppendClusterAsync(directory.FirstCluster, cancellationToken).ConfigureAwait(false);
        await _volume.WriteClusterAsync(newCluster, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        return _volume.GetClusterOffset(newCluster);
    }

    public async Task WriteEntryAsync(long recordOffset, string name, FatxEntryAttributes attributes, uint firstCluster, long size, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateName(name);

        byte[] record = new byte[RecordSize];
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        record[0] = checked((byte)nameBytes.Length);
        record[1] = (byte)attributes;
        nameBytes.CopyTo(record.AsSpan(2, nameBytes.Length));
        Xbox360.Fatx.IO.FatxBinaryReader.WriteUInt32(record.AsSpan(0x2C, 4), firstCluster, Xbox360.Fatx.IO.FatxEndian.Big);
        Xbox360.Fatx.IO.FatxBinaryReader.WriteUInt32(record.AsSpan(0x30, 4), checked((uint)Math.Max(0, size)), Xbox360.Fatx.IO.FatxEndian.Big);
        await _volume.WriteBytesAtAsync(recordOffset, record, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkDeletedAsync(long recordOffset, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] record = new byte[RecordSize];
        record[0] = 0xE5;
        await _volume.WriteBytesAtAsync(recordOffset, record, cancellationToken).ConfigureAwait(false);
    }

    private static FatxDirectorySlot ParseSlot(ReadOnlySpan<byte> recordBytes, long recordOffset, string parentPath)
    {
        int nameLength = recordBytes[0];
        if (nameLength == 0x00 || nameLength == 0xE5 || nameLength == 0xFF || nameLength > 42)
        {
            return new FatxDirectorySlot(recordOffset, parentPath, null, true);
        }

        FatxEntryAttributes attributes = (FatxEntryAttributes)recordBytes[1];
        string name = Encoding.ASCII.GetString(recordBytes.Slice(2, nameLength)).TrimEnd('\0', ' ');
        if (string.IsNullOrWhiteSpace(name))
        {
            return new FatxDirectorySlot(recordOffset, parentPath, null, true);
        }

        uint firstCluster = Xbox360.Fatx.IO.FatxBinaryReader.ReadUInt32(recordBytes.Slice(0x2C, 4), Xbox360.Fatx.IO.FatxEndian.Big);
        long size = Xbox360.Fatx.IO.FatxBinaryReader.ReadUInt32(recordBytes.Slice(0x30, 4), Xbox360.Fatx.IO.FatxEndian.Big);
        string fullPath = parentPath == "/" ? "/" + name : parentPath + "/" + name;

        FatxEntry entry = (attributes & FatxEntryAttributes.Directory) != 0
            ? new FatxDirectoryEntry {
                Name = name,
                FullPath = fullPath,
                Attributes = attributes,
                Size = 0,
                FirstCluster = firstCluster == 0 ? null : firstCluster,
                ChildCount = 0
            }
            : new FatxFileEntry {
                Name = name,
                FullPath = fullPath,
                Attributes = attributes,
                Size = size,
                FirstCluster = firstCluster == 0 ? null : firstCluster
            };

        return new FatxDirectorySlot(recordOffset, parentPath, entry, false);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FatxException("FATX entry names cannot be blank.");
        }

        if (name.Contains('/') || name.Contains('\\'))
        {
            throw new FatxException("FATX entry names cannot contain path separators.");
        }

        if (name.Length > 42)
        {
            throw new FatxException("FATX entry names cannot exceed 42 characters.");
        }

        if (!Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(name)).Equals(name, StringComparison.Ordinal))
        {
            throw new FatxException("FATX entry names must be ASCII.");
        }
    }
}

public sealed record FatxDirectoryContext(
    string Path,
    uint FirstCluster,
    IReadOnlyList<uint> ChainClusters,
    IReadOnlyList<FatxDirectorySlot> Slots);

public sealed record FatxDirectorySlot(
    long RecordOffset,
    string ParentPath,
    FatxEntry? Entry,
    bool IsFree);
