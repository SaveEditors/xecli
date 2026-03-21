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
            return Path.Combine(CliPaths.ConfigDirectory, "avatar-index.v4.json");

        string fullPath = Path.GetFullPath(explicitCachePath);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
            return Path.Combine(fullPath, "avatar-index.v4.json");

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

    public static void Save(string cachePath, AvatarLibraryIndex index) {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string json = JsonSerializer.Serialize(index, SerializerOptions);
        File.WriteAllText(cachePath, json);
    }
}
