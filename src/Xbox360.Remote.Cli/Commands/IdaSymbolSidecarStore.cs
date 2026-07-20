using System.Text.Json;
using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class IdaSymbolSidecarFile {
    public int SchemaVersion { get; set; } = IdaSymbolSidecarStore.CurrentSchemaVersion;

    public string Source { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; }

    public List<IdaSymbolSidecarSymbol> Symbols { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed class IdaSymbolSidecarSymbol {
    public string Name { get; set; } = string.Empty;

    public uint Rva { get; set; }

    public string Type { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed record IdaSymbolSidecarLoadResult(IdaSymbolSidecarFile Data, string? Issue);

internal static class IdaSymbolSidecarStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public const int CurrentSchemaVersion = 1;

    public static IdaSymbolSidecarLoadResult LoadRaw(string path) {
        if (!File.Exists(path))
            return new IdaSymbolSidecarLoadResult(new IdaSymbolSidecarFile(), $"Symbol sidecar not found: {path}");

        try {
            IdaSymbolSidecarFile? data = JsonSerializer.Deserialize<IdaSymbolSidecarFile>(File.ReadAllText(path), JsonOptions);
            return new IdaSymbolSidecarLoadResult(Normalize(data ?? new IdaSymbolSidecarFile()), null);
        }
        catch (JsonException ex) {
            return new IdaSymbolSidecarLoadResult(
                new IdaSymbolSidecarFile(),
                $"Stored symbol sidecar is malformed: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            return new IdaSymbolSidecarLoadResult(
                new IdaSymbolSidecarFile(),
                $"Stored symbol sidecar could not be read: {ex.Message}");
        }
    }

    public static bool TryBuildBookmarks(IdaSymbolSidecarFile? data, DateTimeOffset timestampUtc, out List<MemoryBookmarkRecord> bookmarks, out string error) {
        bookmarks = [];
        error = string.Empty;

        if (!TryValidate(data, out IdaSymbolSidecarFile normalized, out error))
            return false;

        bookmarks = normalized.Symbols
            .Select(symbol => new MemoryBookmarkRecord {
                Name = symbol.Name,
                Module = normalized.Module,
                Rva = symbol.Rva,
                LiveAddress = null,
                Type = symbol.Type,
                Note = null,
                CreatedUtc = timestampUtc,
                UpdatedUtc = timestampUtc
            })
            .ToList();
        return true;
    }

    public static bool TryValidate(IdaSymbolSidecarFile? data, out IdaSymbolSidecarFile normalized, out string error) {
        normalized = Normalize(data ?? new IdaSymbolSidecarFile());
        error = string.Empty;

        if (normalized.SchemaVersion != CurrentSchemaVersion) {
            error = $"Unsupported symbol sidecar schema version {normalized.SchemaVersion}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Source)) {
            error = "Symbol sidecar source is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Module)) {
            error = "Symbol sidecar module is missing.";
            return false;
        }

        if (normalized.GeneratedUtc == default) {
            error = "Symbol sidecar generation timestamp is missing.";
            return false;
        }

        if (normalized.Symbols == null) {
            error = "Symbol sidecar symbol list is missing.";
            return false;
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < normalized.Symbols.Count; index++) {
            IdaSymbolSidecarSymbol? symbol = normalized.Symbols[index];
            if (symbol == null) {
                error = $"Symbol entry {index + 1} is missing.";
                return false;
            }

            if (!MemoryBookmarkStore.TryValidateName(symbol.Name, out string normalizedName, out string nameError)) {
                error = $"Symbol entry {index + 1}: {nameError}";
                return false;
            }

            if (!string.Equals(symbol.Type, "function", StringComparison.OrdinalIgnoreCase)) {
                error = $"Symbol entry '{normalizedName}' uses unsupported type '{symbol.Type}'.";
                return false;
            }

            if (!names.Add(normalizedName)) {
                error = $"Duplicate symbol name '{normalizedName}' was found in the sidecar.";
                return false;
            }

            symbol.Name = normalizedName;
            symbol.Type = "function";
        }

        return true;
    }

    private static IdaSymbolSidecarFile Normalize(IdaSymbolSidecarFile data) {
        data.SchemaVersion = data.SchemaVersion <= 0 ? CurrentSchemaVersion : data.SchemaVersion;
        data.Source = NormalizeText(data.Source);
        data.Module = NormalizeText(data.Module);
        data.Symbols ??= [];

        for (int index = 0; index < data.Symbols.Count; index++) {
            IdaSymbolSidecarSymbol? symbol = data.Symbols[index];
            if (symbol == null)
                continue;

            symbol.Name = NormalizeText(symbol.Name);
            symbol.Type = NormalizeText(symbol.Type);
        }

        return data;
    }

    private static string NormalizeText(string? value) {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
