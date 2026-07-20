using System.Globalization;
using System.Text.Json;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record ReportProfileRecord {
    public string Name { get; init; } = string.Empty;

    public string? Ip { get; init; }

    public string? TargetProfileName { get; init; }

    public int? Port { get; init; }

    public int? TimeoutMs { get; init; }

    public string? Format { get; init; }

    public bool IncludeModules { get; init; }

    public bool IncludeThreads { get; init; }

    public bool IncludeMemory { get; init; }

    public bool IncludePrivate { get; init; }

    public string? Since { get; init; }

    public int? Limit { get; init; }

    public string? Status { get; init; }

    public bool Diff { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}

internal sealed class ReportProfileStoreData {
    public List<ReportProfileRecord> Profiles { get; set; } = [];
}

internal sealed record ReportProfileValidationIssue(string Name, string Message);

internal sealed class ReportProfileValidationReport {
    public List<ReportProfileRecord> Profiles { get; init; } = [];

    public List<ReportProfileValidationIssue> Issues { get; init; } = [];

    public bool IsValid => Issues.Count == 0;
}

internal sealed record ReportProfileLoadResult(ReportProfileStoreData Data, ReportProfileValidationIssue? Issue);

internal static class ReportProfileStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    internal static Func<string, string> ReadAllText { get; set; } = File.ReadAllText;

    public static string StorePath => Path.Combine(CliPaths.ConfigDirectory, "report-profiles.json");

    public static ReportProfileLoadResult LoadRaw() {
        if (!File.Exists(StorePath))
            return new ReportProfileLoadResult(new ReportProfileStoreData(), null);

        try {
            ReportProfileStoreData? data = JsonSerializer.Deserialize<ReportProfileStoreData>(ReadAllText(StorePath), JsonOptions);
            return new ReportProfileLoadResult(data ?? new ReportProfileStoreData(), null);
        }
        catch (JsonException ex) {
            return new ReportProfileLoadResult(
                new ReportProfileStoreData(),
                new ReportProfileValidationIssue(string.Empty, $"Stored report profile data is malformed: {ex.Message}"));
        }
        catch (IOException) {
            return new ReportProfileLoadResult(
                new ReportProfileStoreData(),
                new ReportProfileValidationIssue(string.Empty, "Stored report profile data could not be read."));
        }
        catch (UnauthorizedAccessException) {
            return new ReportProfileLoadResult(
                new ReportProfileStoreData(),
                new ReportProfileValidationIssue(string.Empty, "Stored report profile data could not be read."));
        }
    }

    public static ReportProfileStoreData Load() {
        return Normalize(LoadRaw().Data);
    }

    public static void Save(ReportProfileStoreData data) {
        ReportProfileStoreData normalized = Normalize(data);
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        VerifyRoundTrip(json);
        AtomicFileWriter.WriteAllText(StorePath, json);
    }

    public static bool TryValidateName(string? name, out string normalizedName, out string error) {
        normalizedName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(name)) {
            error = "Profile name is required.";
            return false;
        }

        string candidate = name.Trim();
        if (candidate.Any(char.IsControl)) {
            error = "Profile name cannot contain control characters.";
            return false;
        }

        normalizedName = candidate;
        return true;
    }

    public static bool TryValidateTarget(string? ip, int? port, out string normalizedIp, out int normalizedPort, out string error) {
        normalizedIp = string.Empty;
        normalizedPort = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(ip)) {
            if (port.HasValue) {
                error = "Target port requires a target host or IP.";
                return false;
            }

            return true;
        }

        if (!TargetProfileStore.TryValidateTarget(ip, port, out normalizedIp, out normalizedPort, out error))
            return false;

        return true;
    }

    public static bool TryValidateFormat(string? format, out string? normalizedFormat, out string error) {
        normalizedFormat = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(format))
            return true;

        normalizedFormat = format.Trim().ToLowerInvariant();
        if (normalizedFormat == "markdown")
            normalizedFormat = "md";

        if (normalizedFormat is "md" or "html" or "csv" or "json")
            return true;

        error = "Report format must be md, html, csv, or json.";
        normalizedFormat = null;
        return false;
    }

    public static bool TryValidateSince(string? since, out string? normalizedSince, out string error) {
        normalizedSince = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(since))
            return true;

        if (!DateTimeOffset.TryParse(
                since,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsedSince)) {
            error = "Report since value must be a valid UTC date or date-time.";
            return false;
        }

        normalizedSince = parsedSince.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return true;
    }

    public static bool TryValidateLimit(int? limit, out int? normalizedLimit, out string error) {
        normalizedLimit = limit;
        error = string.Empty;

        if (!limit.HasValue)
            return true;

        if (limit.Value < 0) {
            error = "Report limit must be zero or greater.";
            normalizedLimit = null;
            return false;
        }

        return true;
    }

    public static bool TryValidateStatus(string? status, out string? normalizedStatus, out string error) {
        normalizedStatus = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(status))
            return true;

        if (!ReportStatusHelper.TryNormalize(status, out normalizedStatus)) {
            error = "Report status must be one of: start, running, stop, stopped, break, reboot, or reboot_title.";
            return false;
        }

        return true;
    }

    public static ReportProfileRecord? FindProfile(ReportProfileStoreData data, string name) {
        string normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        return Normalize(data).Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool UpsertProfile(ReportProfileStoreData data, ReportProfileRecord profile) {
        ReportProfileStoreData normalized = Normalize(data);
        int existingIndex = normalized.Profiles.FindIndex(existing =>
            string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0) {
            normalized.Profiles[existingIndex] = profile;
        }
        else {
            normalized.Profiles.Add(profile);
        }

        data.Profiles = normalized.Profiles;
        return existingIndex >= 0;
    }

    public static bool RemoveProfile(ReportProfileStoreData data, string name) {
        ReportProfileStoreData normalized = Normalize(data);
        int removed = normalized.Profiles.RemoveAll(profile =>
            string.Equals(profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        data.Profiles = normalized.Profiles;
        return true;
    }

    public static object ToJsonObject(ReportProfileRecord profile, bool includePrivate) {
        return new {
            profile.Name,
            Connection = new {
                TargetProfile = profile.TargetProfileName,
                Ip = includePrivate ? profile.Ip : (string.IsNullOrWhiteSpace(profile.Ip) ? null : "redacted"),
                profile.Port,
                profile.TimeoutMs
            },
            profile.Format,
            profile.IncludeModules,
            profile.IncludeThreads,
            profile.IncludeMemory,
            profile.IncludePrivate,
            profile.Since,
            profile.Limit,
            profile.Status,
            profile.Diff,
            profile.CreatedUtc,
            profile.UpdatedUtc
        };
    }

    public static string DescribeConnection(ReportProfileRecord profile, bool includePrivate) {
        if (!string.IsNullOrWhiteSpace(profile.TargetProfileName) && string.IsNullOrWhiteSpace(profile.Ip))
            return $"target-profile {profile.TargetProfileName}";

        if (string.IsNullOrWhiteSpace(profile.Ip))
            return "saved default target";

        string endpoint = includePrivate
            ? TargetProfileStore.FormatEndpoint(profile.Ip, profile.Port ?? 730)
            : $"redacted:{(profile.Port ?? 730).ToString(CultureInfo.InvariantCulture)}";

        if (!string.IsNullOrWhiteSpace(profile.TargetProfileName))
            return $"{endpoint} via target-profile {profile.TargetProfileName}";

        return endpoint;
    }

    public static string DescribeFlags(ReportProfileRecord profile) {
        List<string> flags = [];
        if (profile.IncludeModules)
            flags.Add("modules");
        if (profile.IncludeThreads)
            flags.Add("threads");
        if (profile.IncludeMemory)
            flags.Add("memory");
        if (profile.IncludePrivate)
            flags.Add("private");
        if (profile.Diff)
            flags.Add("diff");
        return flags.Count == 0 ? "none" : string.Join(", ", flags);
    }

    public static string DescribeFilters(ReportProfileRecord profile) {
        List<string> filters = [];
        if (!string.IsNullOrWhiteSpace(profile.Since))
            filters.Add($"since={profile.Since}");
        if (profile.Limit.HasValue)
            filters.Add($"limit={profile.Limit.Value.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(profile.Status))
            filters.Add($"status={profile.Status}");
        return filters.Count == 0 ? "none" : string.Join(", ", filters);
    }

    private static ReportProfileStoreData Normalize(ReportProfileStoreData? data) {
        ReportProfileStoreData source = data ?? new ReportProfileStoreData();
        Dictionary<string, ReportProfileRecord> profiles = new(StringComparer.OrdinalIgnoreCase);

        foreach (ReportProfileRecord? profile in source.Profiles ?? []) {
            if (profile == null)
                continue;

            if (!TryValidateName(profile.Name, out string normalizedName, out _))
                continue;

            if (!TryValidateTarget(profile.Ip, profile.Port, out string normalizedIp, out int normalizedPort, out _))
                continue;

            if (!TryValidateFormat(profile.Format, out string? normalizedFormat, out _))
                normalizedFormat = null;

            if (!TryValidateSince(profile.Since, out string? normalizedSince, out _))
                normalizedSince = null;

            if (!TryValidateLimit(profile.Limit, out int? normalizedLimit, out _))
                normalizedLimit = null;

            if (!TryValidateStatus(profile.Status, out string? normalizedStatus, out _))
                normalizedStatus = null;

            profiles[normalizedName] = profile with {
                Name = normalizedName,
                Ip = string.IsNullOrWhiteSpace(profile.Ip) ? null : normalizedIp,
                Port = string.IsNullOrWhiteSpace(profile.Ip) ? null : normalizedPort,
                TargetProfileName = NormalizeOptional(profile.TargetProfileName),
                TimeoutMs = profile.TimeoutMs.HasValue && profile.TimeoutMs.Value > 0 ? profile.TimeoutMs : null,
                Format = normalizedFormat,
                Since = normalizedSince,
                Limit = normalizedLimit,
                Status = normalizedStatus
            };
        }

        return new ReportProfileStoreData {
            Profiles = profiles.Values
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void VerifyRoundTrip(string json) {
        ReportProfileStoreData? roundTripped = JsonSerializer.Deserialize<ReportProfileStoreData>(json, JsonOptions);
        if (roundTripped == null)
            throw new JsonException("Serialized report profile data could not be round-tripped.");

        string roundTrippedJson = JsonSerializer.Serialize(roundTripped, JsonOptions);
        using JsonDocument originalDoc = JsonDocument.Parse(json);
        using JsonDocument roundTrippedDoc = JsonDocument.Parse(roundTrippedJson);
        if (!JsonElement.DeepEquals(originalDoc.RootElement, roundTrippedDoc.RootElement))
            throw new JsonException("Serialized report profile data did not round-trip structurally.");
    }
}

internal static class ReportStatusHelper {
    public static bool TryNormalize(string? value, out string normalizedState) {
        normalizedState = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        normalizedState = value.Trim().ToLowerInvariant() switch {
            "start" or "running" => "running",
            "stop" or "stopped" or "break" => "stopped",
            "reboot_title" or "reboot" => "reboot",
            _ => string.Empty
        };

        return normalizedState.Length > 0;
    }
}
