using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.Layout;

namespace Xbox360.Fatx.Reading;

public sealed class FatxDirectoryReader
{
    private readonly FatxVolume _volume;

    public FatxDirectoryReader(FatxVolume volume)
    {
        _volume = volume;
    }

    public async Task<IReadOnlyList<FatxEntry>> ReadDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = NormalizePath(path);
        if (normalizedPath == "/")
        {
            return await ReadDirectoryEntriesAsync(_volume.RootDirectoryCluster, "/", cancellationToken).ConfigureAwait(false);
        }

        FatxEntry entry = await _volume.PathResolver.ResolveAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (entry is not FatxDirectoryEntry directoryEntry)
        {
            throw new FatxException($"Path '{path}' does not reference a FATX directory.");
        }

        if (!directoryEntry.FirstCluster.HasValue || directoryEntry.FirstCluster.Value == 0)
        {
            return Array.Empty<FatxEntry>();
        }

        return await ReadDirectoryEntriesAsync(directoryEntry.FirstCluster.Value, normalizedPath, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<FatxEntry>> ReadDirectoryEntriesAsync(uint firstCluster, string parentPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (firstCluster == 0)
        {
            return Array.Empty<FatxEntry>();
        }

        await using Stream source = await _volume.ClusterChains.OpenChainAsync(firstCluster, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        ReadOnlyMemory<byte> bytes = buffer.ToArray();

        var entries = new List<FatxEntry>();
        for (int offset = 0; offset + 0x40 <= bytes.Length; offset += 0x40)
        {
            FatxEntry? entry = TryParseEntry(bytes.Slice(offset, 0x40).Span, parentPath);
            if (entry is null)
            {
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    private static string NormalizePath(string? path)
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

    private static FatxEntry? TryParseEntry(ReadOnlySpan<byte> recordBytes, string parentPath)
    {
        int nameLength = recordBytes[0];
        if (nameLength == 0x00)
        {
            return null;
        }

        if (nameLength == 0xE5 || nameLength == 0xFF)
        {
            return null;
        }

        if (nameLength > 42)
        {
            return null;
        }

        FatxEntryAttributes attributes = (FatxEntryAttributes)recordBytes[1];
        string name = System.Text.Encoding.ASCII.GetString(recordBytes.Slice(2, nameLength)).TrimEnd('\0', ' ');
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        uint firstCluster = Xbox360.Fatx.IO.FatxBinaryReader.ReadUInt32(recordBytes.Slice(0x2C, 4), Xbox360.Fatx.IO.FatxEndian.Big);
        long size = Xbox360.Fatx.IO.FatxBinaryReader.ReadUInt32(recordBytes.Slice(0x30, 4), Xbox360.Fatx.IO.FatxEndian.Big);

        string fullPath = parentPath == "/"
            ? "/" + name
            : parentPath + "/" + name;

        var record = new FatxDirectoryEntryRecord
        {
            Name = name,
            FirstCluster = firstCluster,
            Size = size,
            Attributes = attributes,
        };

        if ((attributes & FatxEntryAttributes.Directory) != 0)
        {
            return new FatxDirectoryEntry
            {
                Name = record.Name,
                FullPath = fullPath,
                Attributes = record.Attributes,
                Size = 0,
                FirstCluster = record.FirstCluster == 0 ? null : record.FirstCluster,
                ChildCount = 0,
                CreatedAtUtc = record.CreatedAtUtc,
                ModifiedAtUtc = record.ModifiedAtUtc,
            };
        }

        return new FatxFileEntry
        {
            Name = record.Name,
            FullPath = fullPath,
            Attributes = record.Attributes,
            Size = record.Size,
            FirstCluster = record.FirstCluster == 0 ? null : record.FirstCluster,
            CreatedAtUtc = record.CreatedAtUtc,
            ModifiedAtUtc = record.ModifiedAtUtc,
        };
    }
}
