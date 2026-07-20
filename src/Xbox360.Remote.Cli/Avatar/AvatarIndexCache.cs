using System.Text.Json;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarIndexCache {
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
        WriteIndented = false
    };

    public static string GetDefaultCachePath() {
        return GetCachePath(null);
    }

    public static string GetCachePath(string? explicitCachePath) {
        if (string.IsNullOrWhiteSpace(explicitCachePath))
            return Path.Combine(CliPaths.ConfigDirectory, "avatar-index.v5.json");

        string fullPath = Path.GetFullPath(explicitCachePath);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
            return Path.Combine(fullPath, "avatar-index.v5.json");

        return fullPath;
    }

    public static AvatarLibraryIndex? TryLoad(string cachePath, AvatarCollectionFingerprint expectedFingerprint) {
        try {
            if (!File.Exists(cachePath))
                return null;

            string json = File.ReadAllText(cachePath);
            AvatarLibraryIndex? index = JsonSerializer.Deserialize<AvatarLibraryIndex>(json, SerializerOptions);
            if (index == null)
                return null;

            return Equals(index.Fingerprint, expectedFingerprint) ? index : null;
        }
        catch {
            return null;
        }
    }

    public static AvatarCacheStatus DescribeStatus(string cachePath, AvatarCollectionFingerprint expectedFingerprint) {
        string expectedSignature = FormatFingerprint(expectedFingerprint);
        if (!File.Exists(cachePath)) {
            return new AvatarCacheStatus(
                cachePath,
                "missing",
                "No local avatar cache file was found.",
                expectedSignature,
                null,
                null,
                null,
                null);
        }

        try {
            string json = File.ReadAllText(cachePath);
            AvatarLibraryIndex? index = JsonSerializer.Deserialize<AvatarLibraryIndex>(json, SerializerOptions);
            if (index == null) {
                return new AvatarCacheStatus(
                    cachePath,
                    "corrupt",
                    "The cache file could not be parsed.",
                    expectedSignature,
                    null,
                    null,
                    null,
                    null);
            }

            string cachedSignature = FormatFingerprint(index.Fingerprint);
            if (!Equals(index.Fingerprint, expectedFingerprint)) {
                return new AvatarCacheStatus(
                    cachePath,
                    "stale",
                    "The cache fingerprint does not match the current library.",
                    expectedSignature,
                    cachedSignature,
                    index.GeneratedUtc,
                    index.Items.Count,
                    index.Items.Sum(item => item.SizeBytes));
            }

            return new AvatarCacheStatus(
                cachePath,
                "ready",
                "The cache matches the current library.",
                expectedSignature,
                cachedSignature,
                index.GeneratedUtc,
                index.Items.Count,
                index.Items.Sum(item => item.SizeBytes));
        }
        catch {
            return new AvatarCacheStatus(
                cachePath,
                "corrupt",
                "The cache file could not be read.",
                expectedSignature,
                null,
                null,
                null,
                null);
        }
    }

    public static void Save(string cachePath, AvatarLibraryIndex index) {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string json = JsonSerializer.Serialize(index, SerializerOptions);
        AtomicFileWriter.WriteAllText(cachePath, json);
    }

    private static string FormatFingerprint(AvatarCollectionFingerprint fingerprint) {
        return $"root={fingerprint.RootPath};files={fingerprint.FileCount};bytes={fingerprint.TotalBytes};ticks={fingerprint.LatestWriteTimeUtcTicks}";
    }
}
