using System.Text.Json;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class TrainerStoreData {
    public int? SchemaVersion { get; set; }

    public int? Version { get; set; }

    public List<TrainerEntryRecord> Entries { get; set; } = [];
}

internal sealed class TrainerEntryRecord {
    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? Type { get; set; }

    public string? Value { get; set; }

    public bool? LittleEndian { get; set; }

    public bool? Enabled { get; set; }

    public string? Note { get; set; }
}

internal static class TrainerStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryLoad(string path, out TrainerStoreData data, out string error) {
        data = new TrainerStoreData();
        error = string.Empty;

        try {
            string json = File.ReadAllText(path);
            TrainerStoreData? loaded = JsonSerializer.Deserialize<TrainerStoreData>(json, JsonOptions);
            if (loaded == null) {
                error = "Trainer file is empty or malformed.";
                return false;
            }

            if (loaded.SchemaVersion.HasValue && loaded.Version.HasValue && loaded.SchemaVersion.Value != loaded.Version.Value) {
                error = "Trainer file SchemaVersion and Version disagree.";
                return false;
            }

            int version = loaded.SchemaVersion ?? loaded.Version ?? 1;
            if (version != 1) {
                error = $"Unsupported trainer schema version {version}. Expected 1.";
                return false;
            }

            loaded.SchemaVersion = version;
            loaded.Version = version;
            loaded.Entries ??= [];
            data = loaded;
            return true;
        }
        catch (JsonException ex) {
            error = $"Failed to parse trainer file: {ex.Message}";
            return false;
        }
        catch (IOException ex) {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex) {
            error = ex.Message;
            return false;
        }
    }
}
