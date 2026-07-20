using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record TargetProfileRecord {
    public string Name { get; init; } = string.Empty;

    public string Ip { get; init; } = string.Empty;

    public int Port { get; init; }

    public int? FtpPort { get; init; }

    public string? FtpUser { get; init; }

    public string? FtpPassword { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}

internal sealed class TargetProfileStoreData {
    public List<TargetProfileRecord> Profiles { get; set; } = [];

    public string? CurrentProfileName { get; set; }
}

internal sealed record TargetProfileValidationIssue(string Name, string Message);

internal sealed class TargetProfileValidationReport {
    public List<TargetProfileRecord> Profiles { get; init; } = [];

    public List<TargetProfileValidationIssue> Issues { get; init; } = [];

    public string? CurrentProfileName { get; init; }

    public bool IsValid => Issues.Count == 0;
}

internal sealed record TargetProfileLoadResult(TargetProfileStoreData Data, TargetProfileValidationIssue? Issue);

internal sealed record TargetProfileHealthSnapshot(
    bool ConfigPresent,
    bool ConfigParsed,
    bool StorePresent,
    bool StoreReadable,
    int ProfileCount,
    string? DefaultTarget,
    string? CurrentProfileName,
    IReadOnlyList<string> Issues);

internal static class TargetProfileStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    internal static Func<string, string> ReadAllText { get; set; } = File.ReadAllText;

    public static string StorePath => Path.Combine(CliPaths.ConfigDirectory, "target-profiles.json");

    public static TargetProfileLoadResult LoadRaw() {
        if (!File.Exists(StorePath))
            return new TargetProfileLoadResult(new TargetProfileStoreData(), null);

        try {
            TargetProfileStoreData? data = JsonSerializer.Deserialize<TargetProfileStoreData>(ReadAllText(StorePath), JsonOptions);
            return new TargetProfileLoadResult(data ?? new TargetProfileStoreData(), null);
        }
        catch (JsonException ex) {
            return new TargetProfileLoadResult(
                new TargetProfileStoreData(),
                new TargetProfileValidationIssue(string.Empty, $"Stored target profile data is malformed: {ex.Message}"));
        }
        catch (IOException) {
            return new TargetProfileLoadResult(
                new TargetProfileStoreData(),
                new TargetProfileValidationIssue(string.Empty, "Stored target profile data could not be read."));
        }
        catch (UnauthorizedAccessException) {
            return new TargetProfileLoadResult(
                new TargetProfileStoreData(),
                new TargetProfileValidationIssue(string.Empty, "Stored target profile data could not be read."));
        }
    }

    public static TargetProfileHealthSnapshot GetHealthSnapshot(CliConfig? config, bool configPresent, bool configParsed, bool includePrivate) {
        TargetProfileLoadResult load = LoadRaw();
        TargetProfileValidationReport report = ValidateProfiles(load.Data);
        if (load.Issue != null)
            report.Issues.Insert(0, load.Issue);

        List<string> issues = [];
        foreach (TargetProfileValidationIssue issue in report.Issues) {
            string label = string.IsNullOrWhiteSpace(issue.Name) ? "profile" : issue.Name;
            issues.Add(CommandLogRedactor.RedactFreeText($"{label}: {issue.Message}"));
        }

        string? defaultTarget = null;
        if (config != null && !string.IsNullOrWhiteSpace(config.DefaultIp)) {
            int port = config.DefaultPort ?? 730;
            if (TryValidateTarget(config.DefaultIp, port, out string ip, out int normalizedPort, out string error)) {
                defaultTarget = includePrivate
                    ? FormatEndpoint(ip, normalizedPort)
                    : $"redacted:{normalizedPort}";
            }
            else {
                issues.Add(CommandLogRedactor.RedactFreeText($"Default target is invalid: {error}"));
            }
        }

        return new TargetProfileHealthSnapshot(
            configPresent,
            configParsed,
            File.Exists(StorePath),
            load.Issue == null,
            report.Profiles.Count,
            defaultTarget,
            report.CurrentProfileName,
            issues);
    }

    public static TargetProfileStoreData Load() {
        return Normalize(LoadRaw().Data);
    }

    public static void Save(TargetProfileStoreData data) {
        TargetProfileStoreData normalized = Normalize(data);
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
            error = "Target host or IP is required.";
            return false;
        }

        string candidateIp = NormalizeTargetHost(ip);
        if (Uri.CheckHostName(candidateIp) == UriHostNameType.Unknown) {
            error = "Target host or IP is invalid.";
            return false;
        }

        int candidatePort = port ?? 730;
        if (candidatePort < 1 || candidatePort > 65535) {
            error = "Target port must be between 1 and 65535.";
            return false;
        }

        normalizedIp = candidateIp;
        normalizedPort = candidatePort;
        return true;
    }

    public static bool TryValidateFtpPort(int? port, out int? normalizedPort, out string error) {
        normalizedPort = null;
        error = string.Empty;

        if (!port.HasValue)
            return true;

        if (port is < 1 or > 65535) {
            error = "FTP port must be between 1 and 65535.";
            return false;
        }

        normalizedPort = port;
        return true;
    }

    public static bool TryParseTargetEndpoint(string? value, out string normalizedIp, out int normalizedPort) {
        normalizedIp = string.Empty;
        normalizedPort = 730;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string candidate = value.Trim();
        string host = candidate;
        int port = 730;

        if (candidate.StartsWith("[", StringComparison.Ordinal)) {
            int closeIndex = candidate.IndexOf(']');
            if (closeIndex <= 1)
                return false;

            host = candidate.Substring(1, closeIndex - 1);
            if (closeIndex + 1 < candidate.Length) {
                if (candidate[closeIndex + 1] != ':' ||
                    !int.TryParse(candidate.AsSpan(closeIndex + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) {
                    return false;
                }
            }
        }
        else {
            int colonIndex = candidate.LastIndexOf(':');
            if (colonIndex > 0 && colonIndex < candidate.Length - 1 && candidate.IndexOf(':') == colonIndex) {
                if (int.TryParse(candidate.AsSpan(colonIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)) {
                    host = candidate[..colonIndex];
                    port = parsedPort;
                }
            }
        }

        host = NormalizeTargetHost(host);
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            return false;

        if (port < 1 || port > 65535)
            return false;

        normalizedIp = host;
        normalizedPort = port;
        return true;
    }

    public static TargetProfileRecord? FindProfile(TargetProfileStoreData data, string name) {
        string normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        return Normalize(data).Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    public static TargetProfileRecord? FindProfileByEndpoint(TargetProfileStoreData data, string ip, int port) {
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        string normalizedIp = NormalizeTargetHost(ip);
        return Normalize(data).Profiles.FirstOrDefault(profile =>
            string.Equals(NormalizeTargetHost(profile.Ip), normalizedIp, StringComparison.OrdinalIgnoreCase) &&
            profile.Port == port);
    }

    public static bool UpsertProfile(TargetProfileStoreData data, TargetProfileRecord profile) {
        TargetProfileStoreData normalized = Normalize(data);
        int existingIndex = normalized.Profiles.FindIndex(existing =>
            string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0) {
            normalized.Profiles[existingIndex] = profile;
        }
        else {
            normalized.Profiles.Add(profile);
        }

        data.Profiles = normalized.Profiles;
        data.CurrentProfileName = normalized.CurrentProfileName;
        return existingIndex >= 0;
    }

    public static bool TryRemoveProfile(TargetProfileStoreData data, string name, CliConfig? config, bool force, out TargetProfileRecord? removedProfile, out bool clearedDefaultTarget, out string error) {
        removedProfile = null;
        clearedDefaultTarget = false;
        error = string.Empty;

        string normalizedName = NormalizeName(name);
        if (string.IsNullOrWhiteSpace(normalizedName)) {
            error = "Profile name is required.";
            return false;
        }

        TargetProfileStoreData normalized = Normalize(data);
        removedProfile = normalized.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (removedProfile == null) {
            error = "Profile not found.";
            return false;
        }

        TargetProfileRecord? currentProfile = config == null ? null : ResolveCurrentProfile(normalized, config);
        bool isSelectedProfile = currentProfile != null &&
            string.Equals(currentProfile.Name, removedProfile.Name, StringComparison.OrdinalIgnoreCase);
        if (!isSelectedProfile &&
            !string.IsNullOrWhiteSpace(normalized.CurrentProfileName) &&
            string.Equals(normalized.CurrentProfileName, removedProfile.Name, StringComparison.OrdinalIgnoreCase)) {
            isSelectedProfile = true;
        }

        if (!force && isSelectedProfile) {
            removedProfile = null;
            error = "Refusing to remove the current target profile. Use --force to remove it.";
            return false;
        }

        int removed = normalized.Profiles.RemoveAll(profile =>
            string.Equals(profile.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) {
            removedProfile = null;
            error = "Profile not found.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(normalized.CurrentProfileName) &&
            string.Equals(normalized.CurrentProfileName, normalizedName, StringComparison.OrdinalIgnoreCase)) {
            normalized.CurrentProfileName = null;
        }

        string removedIp = removedProfile.Ip;
        int removedPort = removedProfile.Port;
        if (config != null &&
            string.Equals(NormalizeTargetHost(config.DefaultIp), NormalizeTargetHost(removedIp), StringComparison.OrdinalIgnoreCase) &&
            (config.DefaultPort ?? 730) == removedPort) {
            bool remainingProfileUsesEndpoint = normalized.Profiles.Any(profile =>
                string.Equals(NormalizeTargetHost(profile.Ip), NormalizeTargetHost(removedIp), StringComparison.OrdinalIgnoreCase) &&
                profile.Port == removedPort);
            if (!remainingProfileUsesEndpoint) {
                config.DefaultIp = null;
                config.DefaultPort = null;
                clearedDefaultTarget = true;
            }
        }

        data.Profiles = normalized.Profiles;
        data.CurrentProfileName = normalized.CurrentProfileName;
        return true;
    }

    public static bool RemoveProfile(TargetProfileStoreData data, string name) {
        TargetProfileStoreData normalized = Normalize(data);
        int removed = normalized.Profiles.RemoveAll(profile =>
            string.Equals(profile.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;

        if (!string.IsNullOrWhiteSpace(normalized.CurrentProfileName) &&
            string.Equals(normalized.CurrentProfileName, name.Trim(), StringComparison.OrdinalIgnoreCase)) {
            normalized.CurrentProfileName = null;
        }

        data.Profiles = normalized.Profiles;
        data.CurrentProfileName = normalized.CurrentProfileName;
        return true;
    }

    public static TargetProfileValidationReport ValidateProfiles(TargetProfileStoreData? data) {
        TargetProfileStoreData source = data ?? new TargetProfileStoreData();
        Dictionary<string, TargetProfileRecord> profiles = new(StringComparer.OrdinalIgnoreCase);
        List<TargetProfileValidationIssue> issues = [];

        foreach (TargetProfileRecord? profile in source.Profiles ?? []) {
            if (profile == null) {
                issues.Add(new TargetProfileValidationIssue(string.Empty, "Stored profile entry is missing."));
                continue;
            }

            if (!TryValidateName(profile.Name, out string normalizedName, out string nameError)) {
                issues.Add(new TargetProfileValidationIssue(profile.Name ?? string.Empty, nameError));
                continue;
            }

            int? storedPort = profile.Port > 0 ? profile.Port : null;
            if (!TryValidateTarget(profile.Ip, storedPort, out string normalizedIp, out int normalizedPort, out string targetError)) {
                issues.Add(new TargetProfileValidationIssue(normalizedName, targetError));
                continue;
            }

            if (profile.Port <= 0) {
                issues.Add(new TargetProfileValidationIssue(normalizedName, "Stored profile port was missing or invalid; defaulted to 730."));
            }

            int? normalizedFtpPort = null;
            if (profile.FtpPort.HasValue) {
                if (!TryValidateFtpPort(profile.FtpPort, out normalizedFtpPort, out string ftpPortError)) {
                    issues.Add(new TargetProfileValidationIssue(normalizedName, ftpPortError));
                }
            }

            if (profiles.ContainsKey(normalizedName)) {
                issues.Add(new TargetProfileValidationIssue(normalizedName, "Duplicate profile name was ignored."));
                continue;
            }

            profiles[normalizedName] = profile with {
                Name = normalizedName,
                Ip = normalizedIp,
                Port = normalizedPort,
                FtpPort = normalizedFtpPort,
                FtpUser = NormalizeOptional(profile.FtpUser),
                FtpPassword = NormalizeOptional(profile.FtpPassword)
            };
        }

        string? currentName = null;
        if (!string.IsNullOrWhiteSpace(source.CurrentProfileName)) {
            string normalizedCurrent = NormalizeName(source.CurrentProfileName);
            if (!string.IsNullOrWhiteSpace(normalizedCurrent) &&
                profiles.TryGetValue(normalizedCurrent, out TargetProfileRecord? currentProfile)) {
                currentName = currentProfile.Name;
            }
            else {
                issues.Add(new TargetProfileValidationIssue(source.CurrentProfileName.Trim(), "Current profile selection points to a missing profile."));
            }
        }

        return new TargetProfileValidationReport {
            Profiles = profiles.Values
                .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Issues = issues,
            CurrentProfileName = currentName
        };
    }

    public static TargetProfileRecord? ResolveCurrentProfile(TargetProfileStoreData data, CliConfig config) {
        TargetProfileStoreData normalized = Normalize(data);
        if (!string.IsNullOrWhiteSpace(normalized.CurrentProfileName)) {
            TargetProfileRecord? current = normalized.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, normalized.CurrentProfileName, StringComparison.OrdinalIgnoreCase));
            if (current != null)
                return current;
        }

        string? configuredIp = NormalizeTargetHost(config.DefaultIp);
        if (string.IsNullOrWhiteSpace(configuredIp))
            return null;

        int defaultPort = config.DefaultPort ?? 730;
        TargetProfileRecord? matched = normalized.Profiles.FirstOrDefault(profile =>
            string.Equals(NormalizeTargetHost(profile.Ip), configuredIp, StringComparison.OrdinalIgnoreCase) &&
            profile.Port == defaultPort);
        if (matched != null)
            return matched;

        return new TargetProfileRecord {
            Name = string.Empty,
            Ip = configuredIp,
            Port = defaultPort,
            CreatedUtc = DateTimeOffset.MinValue,
            UpdatedUtc = DateTimeOffset.MinValue
        };
    }

    public static bool TryResolveProfile(TargetProfileStoreData data, CliConfig config, string? profileName, out TargetProfileRecord? profile, out string error) {
        profile = null;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(profileName)) {
            if (!TryValidateName(profileName, out string normalizedName, out error))
                return false;

            profile = FindProfile(data, normalizedName);
            if (profile == null) {
                error = "Profile not found.";
                return false;
            }

            return true;
        }

        profile = ResolveCurrentProfile(data, config);
        return true;
    }

    public static bool TrySetCurrentProfile(TargetProfileStoreData data, string name, out TargetProfileRecord? profile) {
        TargetProfileStoreData normalized = Normalize(data);
        profile = normalized.Profiles.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
            return false;

        data.Profiles = normalized.Profiles;
        data.CurrentProfileName = profile.Name;
        return true;
    }

    public static string FormatEndpoint(TargetProfileRecord profile, bool includePrivate) {
        return includePrivate
            ? FormatEndpoint(profile.Ip, profile.Port)
            : $"redacted:{profile.Port}";
    }

    public static string FormatEndpoint(string ip, int port) {
        if (string.IsNullOrWhiteSpace(ip))
            return $"redacted:{port}";

        return ip.Contains(':') && !ip.StartsWith("[", StringComparison.Ordinal)
            ? $"[{ip}]:{port}"
            : $"{ip}:{port}";
    }

    public static object ToJsonObject(TargetProfileRecord profile, bool includePrivate, bool current = false) {
        return new {
            profile.Name,
            Ip = includePrivate ? profile.Ip : "redacted",
            profile.Port,
            Current = current
        };
    }

    private static TargetProfileStoreData Normalize(TargetProfileStoreData? data) {
        TargetProfileValidationReport report = ValidateProfiles(data);

        return new TargetProfileStoreData {
            Profiles = report.Profiles,
            CurrentProfileName = report.CurrentProfileName
        };
    }

    private static string NormalizeName(string? name) {
        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    private static string NormalizeTargetHost(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string candidate = value.Trim();
        if (candidate.Length > 1 && candidate.StartsWith("[", StringComparison.Ordinal) && candidate.EndsWith("]", StringComparison.Ordinal))
            candidate = candidate[1..^1];

        return candidate;
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void VerifyRoundTrip(string json) {
        TargetProfileStoreData? roundTripped = JsonSerializer.Deserialize<TargetProfileStoreData>(json, JsonOptions);
        if (roundTripped == null)
            throw new JsonException("Serialized target profile data could not be round-tripped.");

        string roundTrippedJson = JsonSerializer.Serialize(roundTripped, JsonOptions);
        JsonNode? originalNode = JsonNode.Parse(json);
        JsonNode? roundTrippedNode = JsonNode.Parse(roundTrippedJson);
        if (originalNode == null || roundTrippedNode == null || !JsonNode.DeepEquals(originalNode, roundTrippedNode))
            throw new JsonException("Serialized target profile data did not round-trip structurally.");
    }
}
