using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Exceptions;

namespace Xbox360.Fatx.Reading;

public sealed class FatxPathResolver
{
    private readonly FatxVolume _volume;

    public FatxPathResolver(FatxVolume volume)
    {
        _volume = volume;
    }

    public async Task<FatxEntry> ResolveAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedPath = NormalizePath(path);
        if (normalizedPath == "/")
        {
            return new FatxDirectoryEntry
            {
                Name = "/",
                FullPath = "/",
                Attributes = FatxEntryAttributes.Directory,
                Size = 0,
                FirstCluster = _volume.RootDirectoryCluster,
                ChildCount = 0,
            };
        }

        string[] segments = normalizedPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<FatxEntry> currentEntries = await _volume.DirectoryReader.ReadDirectoryEntriesAsync(_volume.RootDirectoryCluster, "/", cancellationToken).ConfigureAwait(false);

        FatxEntry? current = null;
        string currentPath = "/";
        foreach (string segment in segments)
        {
            current = currentEntries.FirstOrDefault(entry => string.Equals(entry.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                throw new FatxPathNotFoundException(path);
            }

            currentPath = current.FullPath;
            if (!current.IsDirectory)
            {
                break;
            }

            var directory = (FatxDirectoryEntry)current;
            if (!directory.FirstCluster.HasValue || directory.FirstCluster.Value == 0)
            {
                currentEntries = Array.Empty<FatxEntry>();
                continue;
            }

            currentEntries = await _volume.DirectoryReader.ReadDirectoryEntriesAsync(directory.FirstCluster.Value, currentPath, cancellationToken).ConfigureAwait(false);
        }

        return current ?? throw new FatxPathNotFoundException(path);
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
}
