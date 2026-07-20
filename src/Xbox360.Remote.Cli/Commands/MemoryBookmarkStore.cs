using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class MemoryBookmarkStoreData {
    public int SchemaVersion { get; set; } = MemoryBookmarkStore.CurrentSchemaVersion;

    public List<MemoryBookmarkRecord> Bookmarks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed class MemoryBookmarkRecord {
    public string Name { get; set; } = string.Empty;

    public string? Module { get; set; }

    public uint? Rva { get; set; }

    public uint? LiveAddress { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed record MemoryBookmarkLoadResult(MemoryBookmarkStoreData Data, string? Issue);

internal static class MemoryBookmarkStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public const int CurrentSchemaVersion = 1;

    public static string StorePath => Path.Combine(CliPaths.ConfigDirectory, "memory-bookmarks.json");

    public static MemoryBookmarkLoadResult LoadRaw() {
        if (!File.Exists(StorePath))
            return new MemoryBookmarkLoadResult(new MemoryBookmarkStoreData(), null);

        try {
            MemoryBookmarkStoreData? data = JsonSerializer.Deserialize<MemoryBookmarkStoreData>(File.ReadAllText(StorePath), JsonOptions);
            return new MemoryBookmarkLoadResult(Normalize(data ?? new MemoryBookmarkStoreData()), null);
        }
        catch (JsonException ex) {
            return new MemoryBookmarkLoadResult(
                new MemoryBookmarkStoreData(),
                $"Stored memory-bookmarks.json is malformed: {ex.Message}");
        }
    }

    public static MemoryBookmarkStoreData Load() {
        return LoadRaw().Data;
    }

    public static void Save(MemoryBookmarkStoreData data) {
        MemoryBookmarkStoreData normalized = Normalize(data);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        AtomicFileWriter.WriteAllText(StorePath, Serialize(normalized));
    }

    public static string Serialize(MemoryBookmarkStoreData data) {
        return JsonSerializer.Serialize(Normalize(data), JsonOptions);
    }

    public static bool TryValidateName(string? name, out string normalizedName, out string error) {
        normalizedName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name)) {
            error = "Bookmark name is required.";
            return false;
        }

        string candidate = name.Trim();
        if (candidate.Any(char.IsControl)) {
            error = "Bookmark name cannot contain control characters.";
            return false;
        }

        normalizedName = candidate;
        return true;
    }

    public static bool TryAddBookmark(MemoryBookmarkStoreData data, MemoryBookmarkRecord bookmark, out string error) {
        error = string.Empty;

        MemoryBookmarkStoreData normalized = Normalize(data);
        if (normalized.Bookmarks.Any(existing =>
            string.Equals(existing.Name, bookmark.Name, StringComparison.OrdinalIgnoreCase))) {
            error = "Bookmark name already exists.";
            return false;
        }

        normalized.Bookmarks.Add(bookmark);
        data.SchemaVersion = normalized.SchemaVersion;
        data.Bookmarks = normalized.Bookmarks;
        data.ExtraData = normalized.ExtraData;
        return true;
    }

    public static bool TryRemoveBookmark(MemoryBookmarkStoreData data, string name, out MemoryBookmarkRecord? removedBookmark, out string error) {
        removedBookmark = null;
        error = string.Empty;

        MemoryBookmarkStoreData normalized = Normalize(data);
        if (!TryValidateName(name, out string trimmed, out error))
            return false;

        int index = normalized.Bookmarks.FindIndex(bookmark =>
            string.Equals(bookmark.Name, trimmed, StringComparison.Ordinal));
        if (index < 0) {
            error = "Bookmark not found.";
            return false;
        }

        removedBookmark = normalized.Bookmarks[index];
        normalized.Bookmarks.RemoveAt(index);
        data.SchemaVersion = normalized.SchemaVersion;
        data.Bookmarks = normalized.Bookmarks;
        data.ExtraData = normalized.ExtraData;
        return true;
    }

    public static bool TryFindBookmark(MemoryBookmarkStoreData data, string name, out MemoryBookmarkRecord? bookmark, out string error) {
        bookmark = null;
        error = string.Empty;

        MemoryBookmarkStoreData normalized = Normalize(data);
        if (!TryValidateName(name, out string trimmed, out error))
            return false;

        bookmark = normalized.Bookmarks.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, trimmed, StringComparison.Ordinal));
        if (bookmark == null) {
            error = "Bookmark not found.";
            return false;
        }

        return true;
    }

    public static bool TryRenameBookmark(
        MemoryBookmarkStoreData data,
        string name,
        string newName,
        out MemoryBookmarkRecord? renamedBookmark,
        out string error) {
        renamedBookmark = null;
        error = string.Empty;

        MemoryBookmarkStoreData normalized = Normalize(data);
        if (!TryValidateName(name, out string trimmedName, out error))
            return false;
        if (!TryValidateName(newName, out string trimmedNewName, out error))
            return false;

        int index = normalized.Bookmarks.FindIndex(bookmark =>
            string.Equals(bookmark.Name, trimmedName, StringComparison.Ordinal));
        if (index < 0) {
            error = "Bookmark not found.";
            return false;
        }

        for (int bookmarkIndex = 0; bookmarkIndex < normalized.Bookmarks.Count; bookmarkIndex++) {
            if (bookmarkIndex == index)
                continue;

            if (string.Equals(normalized.Bookmarks[bookmarkIndex].Name, trimmedNewName, StringComparison.OrdinalIgnoreCase)) {
                error = "Bookmark name already exists.";
                return false;
            }
        }

        renamedBookmark = normalized.Bookmarks[index];
        renamedBookmark.Name = trimmedNewName;
        renamedBookmark.UpdatedUtc = DateTimeOffset.UtcNow;
        data.SchemaVersion = normalized.SchemaVersion;
        data.Bookmarks = normalized.Bookmarks;
        data.ExtraData = normalized.ExtraData;
        return true;
    }

    public static MemoryBookmarkRecord CreateBookmark(
        string name,
        string? module,
        uint? rva,
        uint? liveAddress,
        string? note,
        DateTimeOffset timestampUtc) {
        MemoryBookmarkRecord bookmark = new() {
            Name = name,
            Module = module,
            Rva = rva,
            LiveAddress = liveAddress,
            Type = DetermineType(module, rva, liveAddress),
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            CreatedUtc = timestampUtc,
            UpdatedUtc = timestampUtc
        };

        return bookmark;
    }

    public static string DetermineType(string? module, uint? rva, uint? liveAddress) {
        bool hasModule = !string.IsNullOrWhiteSpace(module);
        bool hasRva = rva.HasValue;
        bool hasLiveAddress = liveAddress.HasValue;

        if (hasLiveAddress && hasModule && hasRva)
            return "mixed";

        if (hasLiveAddress && hasModule)
            return "mixed";

        if (hasModule && hasRva)
            return "module-rva";

        if (hasLiveAddress)
            return "live";

        if (hasModule)
            return "module";

        return "unknown";
    }

    public static string FormatHex(uint? value) {
        return value.HasValue ? $"0x{value.Value:X8}" : "n/a";
    }

    public static string FormatHex(uint value) {
        return $"0x{value:X8}";
    }

    private static MemoryBookmarkStoreData Normalize(MemoryBookmarkStoreData data) {
        data.SchemaVersion = data.SchemaVersion <= 0 ? CurrentSchemaVersion : data.SchemaVersion;
        data.Bookmarks ??= [];

        foreach (MemoryBookmarkRecord bookmark in data.Bookmarks) {
            bookmark.Name = bookmark.Name.Trim();
            if (string.IsNullOrWhiteSpace(bookmark.Type))
                bookmark.Type = DetermineType(bookmark.Module, bookmark.Rva, bookmark.LiveAddress);
            bookmark.Type = bookmark.Type.Trim();
            if (string.IsNullOrWhiteSpace(bookmark.Note))
                bookmark.Note = null;
        }

        return data;
    }
}
