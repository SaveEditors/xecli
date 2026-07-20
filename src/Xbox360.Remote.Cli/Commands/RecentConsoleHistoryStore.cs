using System.Globalization;
using System.Text.Json;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class RecentConsoleHistoryStoreData {
    public List<RecentConsoleHistoryRecord> Entries { get; set; } = [];
}

internal sealed record RecentConsoleHistoryRecord {
    public string Ip { get; set; } = string.Empty;

    public int Port { get; set; }

    public long? LatencyMs { get; set; }

    public string LatencyClass { get; set; } = "unknown";

    public DateTimeOffset LastSeenUtc { get; set; }

    public int ContactCount { get; set; }
}

internal sealed record RecentConsoleHistoryLoadResult(RecentConsoleHistoryStoreData Data, string? Issue);

internal static class RecentConsoleHistoryStore {
    private const int MaxEntries = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public static string StorePath => Path.Combine(CliPaths.CachePath, "recent-console-history.json");

    public static RecentConsoleHistoryLoadResult LoadRaw() {
        if (!File.Exists(StorePath))
            return new RecentConsoleHistoryLoadResult(new RecentConsoleHistoryStoreData(), null);

        try {
            RecentConsoleHistoryStoreData? data = JsonSerializer.Deserialize<RecentConsoleHistoryStoreData>(File.ReadAllText(StorePath), JsonOptions);
            return new RecentConsoleHistoryLoadResult(Normalize(data ?? new RecentConsoleHistoryStoreData()), null);
        }
        catch (JsonException ex) {
            return new RecentConsoleHistoryLoadResult(
                new RecentConsoleHistoryStoreData(),
                $"Stored recent console history is malformed: {ex.Message}");
        }
        catch (IOException) {
            return new RecentConsoleHistoryLoadResult(
                new RecentConsoleHistoryStoreData(),
                "Stored recent console history could not be read.");
        }
        catch (UnauthorizedAccessException) {
            return new RecentConsoleHistoryLoadResult(
                new RecentConsoleHistoryStoreData(),
                "Stored recent console history could not be read.");
        }
    }

    public static RecentConsoleHistoryStoreData Load() {
        return LoadRaw().Data;
    }

    public static IReadOnlyList<RecentConsoleHistoryRecord> ReadRecent(int limit) {
        RecentConsoleHistoryStoreData data = Load();
        if (limit <= 0)
            return [];

        return data.Entries.Take(limit).ToList();
    }

    public static void RecordContact(string ip, int port, DateTimeOffset seenUtc, long? latencyMs, string? latencyClass = null) {
        if (!TargetProfileStore.TryValidateTarget(ip, port, out string normalizedIp, out int normalizedPort, out _))
            return;

        try {
            RecentConsoleHistoryLoadResult load = LoadRaw();
            RecentConsoleHistoryStoreData data = load.Data;
            string normalizedClass = NormalizeLatencyClass(latencyClass, latencyMs);
            int existingIndex = data.Entries.FindIndex(entry =>
                string.Equals(NormalizeTargetHost(entry.Ip), normalizedIp, StringComparison.OrdinalIgnoreCase) &&
                entry.Port == normalizedPort);

            RecentConsoleHistoryRecord updated = existingIndex >= 0
                ? data.Entries[existingIndex] with {
                    Ip = normalizedIp,
                    Port = normalizedPort,
                    LatencyMs = latencyMs ?? data.Entries[existingIndex].LatencyMs,
                    LatencyClass = normalizedClass,
                    LastSeenUtc = seenUtc,
                    ContactCount = Math.Max(1, data.Entries[existingIndex].ContactCount + 1)
                }
                : new RecentConsoleHistoryRecord {
                    Ip = normalizedIp,
                    Port = normalizedPort,
                    LatencyMs = latencyMs,
                    LatencyClass = normalizedClass,
                    LastSeenUtc = seenUtc,
                    ContactCount = 1
                };

            if (existingIndex >= 0)
                data.Entries.RemoveAt(existingIndex);

            data.Entries.Insert(0, updated);
            Save(data);
        }
        catch {
            // Best-effort cache only.
        }
    }

    public static string DescribeLatencyClass(RecentConsoleHistoryRecord entry) {
        if (!string.IsNullOrWhiteSpace(entry.LatencyClass))
            return entry.LatencyClass.Trim().ToLowerInvariant();

        return NormalizeLatencyClass(null, entry.LatencyMs);
    }

    public static string FormatEndpoint(RecentConsoleHistoryRecord entry, bool includePrivate) {
        if (includePrivate)
            return TargetProfileStore.FormatEndpoint(entry.Ip, entry.Port);

        return $"redacted:{entry.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    public static void Save(RecentConsoleHistoryStoreData data) {
        RecentConsoleHistoryStoreData normalized = Normalize(data);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        AtomicFileWriter.WriteAllText(StorePath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    private static RecentConsoleHistoryStoreData Normalize(RecentConsoleHistoryStoreData? data) {
        data ??= new RecentConsoleHistoryStoreData();
        data.Entries ??= [];

        Dictionary<string, RecentConsoleHistoryRecord> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (RecentConsoleHistoryRecord? entry in data.Entries) {
            if (entry == null)
                continue;

            if (!TargetProfileStore.TryValidateTarget(entry.Ip, entry.Port, out string normalizedIp, out int normalizedPort, out _))
                continue;

            string key = BuildKey(normalizedIp, normalizedPort);
            RecentConsoleHistoryRecord normalized = entry with {
                Ip = normalizedIp,
                Port = normalizedPort,
                LatencyMs = entry.LatencyMs is < 0 ? null : entry.LatencyMs,
                LatencyClass = NormalizeLatencyClass(entry.LatencyClass, entry.LatencyMs),
                LastSeenUtc = entry.LastSeenUtc == default ? DateTimeOffset.MinValue : entry.LastSeenUtc,
                ContactCount = entry.ContactCount > 0 ? entry.ContactCount : 1
            };

            if (!entries.TryGetValue(key, out RecentConsoleHistoryRecord? existing) || normalized.LastSeenUtc > existing.LastSeenUtc)
                entries[key] = normalized;
        }

        data.Entries = entries.Values
            .OrderByDescending(entry => entry.LastSeenUtc)
            .ThenBy(entry => entry.Ip, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Port)
            .Take(MaxEntries)
            .ToList();
        return data;
    }

    private static string BuildKey(string ip, int port) {
        return $"{NormalizeTargetHost(ip).ToLowerInvariant()}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string NormalizeTargetHost(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string candidate = value.Trim();
        if (candidate.Length > 1 && candidate.StartsWith("[", StringComparison.Ordinal) && candidate.EndsWith("]", StringComparison.Ordinal))
            candidate = candidate[1..^1];

        return candidate;
    }

    private static string NormalizeLatencyClass(string? latencyClass, long? latencyMs) {
        string candidate = (latencyClass ?? string.Empty).Trim().ToLowerInvariant();
        if (candidate is "fast" or "normal" or "slow" or "timeout" or "unknown")
            return candidate;

        if (latencyMs == null)
            return "unknown";

        if (latencyMs.Value < 100)
            return "fast";

        if (latencyMs.Value < 500)
            return "normal";

        return "slow";
    }
}
