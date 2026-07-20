using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NetworkDoctorCommand : Command<NetworkDoctorCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show saved target endpoints in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        NetworkDoctorSnapshot snapshot = NetworkDoctorSnapshotBuilder.Build(settings.IncludePrivate);
        if (settings.Json) {
            CliOutput.EmitJson(snapshot);
            return snapshot.Issues.Count == 0 ? 0 : 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Network Doctor[/]").RuleStyle("silver"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[green]Value[/]"));
        table.AddRow("[white]Mode[/]", "[grey]offline[/]");
        table.AddRow("[white]Config[/]", $"[cyan]{Markup.Escape(snapshot.ConfigStatus)}[/]");
        table.AddRow("[white]Current profile[/]", $"[cyan]{Markup.Escape(snapshot.CurrentProfileName ?? "none")}[/]");
        table.AddRow("[white]Active target[/]", $"[cyan]{Markup.Escape(snapshot.ActiveTarget)}[/]");
        table.AddRow("[white]Saved target[/]", $"[cyan]{Markup.Escape(snapshot.SavedTarget)}[/]");
        table.AddRow("[white]Saved FTP target[/]", $"[cyan]{Markup.Escape(snapshot.SavedFtpTarget)}[/]");
        table.AddRow("[white]Profile FTP settings[/]", $"[cyan]{Markup.Escape(snapshot.ProfileFtpSettings)}[/]");
        table.AddRow("[white]Intended ports[/]", $"[cyan]{Markup.Escape(snapshot.IntendedPorts)}[/]");
        table.AddRow(
            "[white]Issues[/]",
            snapshot.Issues.Count == 0
                ? "[green]none[/]"
                : $"[yellow]{snapshot.Issues.Count.ToString(CultureInfo.InvariantCulture)}[/]");
        AnsiConsole.Write(table);

        if (snapshot.Recent.Count > 0) {
            AnsiConsole.MarkupLine("[bold deepskyblue1]Recent[/]");
            Table recent = CliOutput.CreateTable();
            recent.AddColumn(new TableColumn("[white]Endpoint[/]"));
            recent.AddColumn(new TableColumn("[white]Profile[/]"));
            recent.AddColumn(new TableColumn("[white]Latency[/]"));
            recent.AddColumn(new TableColumn("[white]Seen[/]"));
            foreach (RecentConsoleHistorySummary entry in snapshot.Recent) {
                recent.AddRow(
                    $"[cyan]{Markup.Escape(entry.Endpoint)}[/]",
                    string.IsNullOrWhiteSpace(entry.ProfileName) ? "[grey]none[/]" : $"[green]{Markup.Escape(entry.ProfileName)}[/]",
                    $"[cyan]{Markup.Escape(entry.LatencyClass)}[/]",
                    $"[grey]{Markup.Escape(entry.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture))}[/]");
            }

            AnsiConsole.Write(recent);
        }

        if (snapshot.Issues.Count == 0) {
            AnsiConsole.MarkupLine("[green]No inconsistencies found.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[yellow]{snapshot.Issues.Count} issue(s) found.[/]");
        foreach (string issue in snapshot.Issues) {
            AnsiConsole.MarkupLine($"[yellow]-[/] {Markup.Escape(issue)}");
        }

        return 1;
    }
}

internal sealed record NetworkDoctorSnapshot(
    string ConfigStatus,
    string? CurrentProfileName,
    string ActiveTarget,
    string SavedTarget,
    string SavedFtpTarget,
    string ProfileFtpSettings,
    string IntendedPorts,
    IReadOnlyList<RecentConsoleHistorySummary> Recent,
    IReadOnlyList<string> Issues);

internal sealed record RecentConsoleHistorySummary(
    string Endpoint,
    string? ProfileName,
    string LatencyClass,
    DateTimeOffset LastSeenUtc,
    int ContactCount);

public sealed class NetworkRecentCommand : Command<NetworkRecentCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum entries to show (default: 5).")]
        public int? Limit { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show console endpoints in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Limit.HasValue && settings.Limit.Value < 0) {
            AnsiConsole.MarkupLine("[red]--limit must be zero or greater.[/]");
            return 1;
        }

        int limit = settings.Limit ?? 5;
        RecentConsoleHistorySnapshot snapshot = RecentConsoleHistorySnapshotBuilder.Build(limit, settings.IncludePrivate);
        if (settings.Json) {
            CliOutput.EmitJson(snapshot);
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Network Recent[/]").RuleStyle("silver"));
        if (snapshot.Entries.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No recent console history found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Endpoint[/]"));
        table.AddColumn(new TableColumn("[white]Profile[/]"));
        table.AddColumn(new TableColumn("[white]Latency[/]"));
        table.AddColumn(new TableColumn("[white]Seen[/]"));
        table.AddColumn(new TableColumn("[white]Contacts[/]"));

        foreach (RecentConsoleHistoryEntry entry in snapshot.Entries) {
            table.AddRow(
                $"[cyan]{Markup.Escape(entry.Endpoint)}[/]",
                string.IsNullOrWhiteSpace(entry.ProfileName) ? "[grey]none[/]" : $"[green]{Markup.Escape(entry.ProfileName)}[/]",
                $"[cyan]{Markup.Escape(entry.LatencyClass)}[/]",
                $"[grey]{Markup.Escape(entry.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture))}[/]",
                $"[white]{entry.ContactCount.ToString(CultureInfo.InvariantCulture)}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

internal static class NetworkDoctorSnapshotBuilder {
    public static NetworkDoctorSnapshot Build(bool includePrivate) {
        bool configPresent = File.Exists(CliPaths.ConfigPath);
        bool configParsed = false;
        CliConfig? config = null;
        string? configIssue = null;
        if (configPresent) {
            try {
                config = CliConfig.Load();
                configParsed = true;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                configIssue = "Config file is present but could not be parsed.";
            }
        }

        TargetProfileLoadResult load = TargetProfileStore.LoadRaw();
        TargetProfileValidationReport report = TargetProfileStore.ValidateProfiles(load.Data);
        if (load.Issue != null)
            report.Issues.Insert(0, load.Issue);

        TargetProfileHealthSnapshot health = TargetProfileStore.GetHealthSnapshot(config, configPresent, configParsed, includePrivate);

        TargetProfileRecord? activeProfile = configParsed && config != null
            ? TargetProfileStore.ResolveCurrentProfile(load.Data, config)
            : TargetProfileStore.ResolveCurrentProfile(load.Data, new CliConfig());

        string activeTarget = DescribeActiveTarget(activeProfile, config, includePrivate);
        string savedTarget = DescribeSavedTarget(config, includePrivate);
        string savedFtpTarget = DescribeSavedFtpTarget(config, includePrivate);
        string profileFtpSettings = DescribeProfileFtpSettings(activeProfile);
        string intendedPorts = DescribeIntendedPorts(activeProfile, config);
        IReadOnlyList<RecentConsoleHistorySummary> recent = BuildRecentSummary(load.Data, includePrivate);

        List<string> issues = [];
        if (configIssue != null)
            issues.Add(configIssue);
        issues.AddRange(health.Issues);
        issues.AddRange(BuildConsistencyWarnings(activeProfile, config, includePrivate));

        return new NetworkDoctorSnapshot(
            DescribeConfigStatus(configPresent, configParsed, configIssue != null),
            activeProfile?.Name,
            activeTarget,
            savedTarget,
            savedFtpTarget,
            profileFtpSettings,
            intendedPorts,
            recent,
            issues);
    }

    private static string DescribeConfigStatus(bool configPresent, bool configParsed, bool configFailed) {
        if (!configPresent)
            return "missing";

        if (configFailed)
            return "present but could not be parsed";

        return configParsed ? "present and parses" : "present";
    }

    private static string DescribeActiveTarget(TargetProfileRecord? activeProfile, CliConfig? config, bool includePrivate) {
        if (activeProfile == null)
            return "not set";

        string endpoint = includePrivate
            ? TargetProfileStore.FormatEndpoint(activeProfile, includePrivate: true)
            : TargetProfileStore.FormatEndpoint(activeProfile, includePrivate: false);

        if (!string.IsNullOrWhiteSpace(activeProfile.Name))
            return $"profile {activeProfile.Name} -> {endpoint}";

        if (config != null && !string.IsNullOrWhiteSpace(config.DefaultIp))
            return $"saved default target -> {endpoint}";

        return $"manual default target -> {endpoint}";
    }

    private static string DescribeSavedTarget(CliConfig? config, bool includePrivate) {
        if (config == null || string.IsNullOrWhiteSpace(config.DefaultIp))
            return "not set";

        if (!TargetProfileStore.TryValidateTarget(config.DefaultIp, config.DefaultPort, out string ip, out int port, out _))
            return "invalid saved target";

        return includePrivate
            ? $"saved default target -> {TargetProfileStore.FormatEndpoint(ip, port)}"
            : $"saved default target -> redacted:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string DescribeSavedFtpTarget(CliConfig? config, bool includePrivate) {
        if (config == null || string.IsNullOrWhiteSpace(config.DefaultIp))
            return "not set";

        int port = FtpEndpointHelpers.GetConfiguredFtpPort(config);
        string endpoint = includePrivate
            ? TargetProfileStore.FormatEndpoint(config.DefaultIp, port)
            : $"redacted:{port.ToString(CultureInfo.InvariantCulture)}";
        string user = string.IsNullOrWhiteSpace(config.DefaultFtpUser) ? "xboxftp" : config.DefaultFtpUser;
        string passwordState = string.IsNullOrWhiteSpace(config.DefaultFtpPassword) ? "none" : "set";
        return $"saved FTP target -> {endpoint} user={user} pass={passwordState}";
    }

    private static string DescribeProfileFtpSettings(TargetProfileRecord? profile) {
        if (profile == null)
            return "none";

        string port = profile.FtpPort.HasValue
            ? profile.FtpPort.Value.ToString(CultureInfo.InvariantCulture)
            : "default 21";
        string user = string.IsNullOrWhiteSpace(profile.FtpUser) ? "xboxftp" : profile.FtpUser!;
        string passwordState = string.IsNullOrWhiteSpace(profile.FtpPassword) ? "none" : "set";
        return $"profile {profile.Name} -> port={port} user={user} pass={passwordState}";
    }

    private static string DescribeIntendedPorts(TargetProfileRecord? activeProfile, CliConfig? config) {
        int xbdmPort = activeProfile?.Port ?? config?.DefaultPort ?? 730;
        int jrpcPort = xbdmPort;
        int ftpPort = FtpEndpointHelpers.GetEffectiveFtpPort(activeProfile?.FtpPort ?? config?.DefaultFtpPort);
        string ftpSource = activeProfile?.FtpPort.HasValue == true
            ? $"profile {activeProfile.Name}"
            : config?.DefaultFtpPort.HasValue == true
                ? "saved config"
                : "default 21";

        return $"XBDM/JRPC {xbdmPort.ToString(CultureInfo.InvariantCulture)}; FTP {ftpPort.ToString(CultureInfo.InvariantCulture)} ({ftpSource})";
    }

    private static IReadOnlyList<string> BuildConsistencyWarnings(TargetProfileRecord? activeProfile, CliConfig? config, bool includePrivate) {
        List<string> warnings = [];
        if (activeProfile == null || config == null)
            return warnings;

        if (!string.IsNullOrWhiteSpace(activeProfile.Name) &&
            !string.IsNullOrWhiteSpace(config.DefaultIp) &&
            TargetProfileStore.TryValidateTarget(config.DefaultIp, config.DefaultPort, out string savedIp, out int savedPort, out _) &&
            (!string.Equals(savedIp, activeProfile.Ip, StringComparison.OrdinalIgnoreCase) || savedPort != activeProfile.Port)) {
            string savedEndpoint = includePrivate
                ? TargetProfileStore.FormatEndpoint(savedIp, savedPort)
                : $"redacted:{savedPort.ToString(CultureInfo.InvariantCulture)}";
            string activeEndpoint = includePrivate
                ? TargetProfileStore.FormatEndpoint(activeProfile.Ip, activeProfile.Port)
                : $"redacted:{activeProfile.Port.ToString(CultureInfo.InvariantCulture)}";
            warnings.Add($"Saved default target {savedEndpoint} differs from active profile {activeProfile.Name} ({activeEndpoint}).");
        }

        if (activeProfile.FtpPort.HasValue && config.DefaultFtpPort.HasValue && activeProfile.FtpPort.Value != config.DefaultFtpPort.Value) {
            warnings.Add($"Saved FTP port {config.DefaultFtpPort.Value.ToString(CultureInfo.InvariantCulture)} differs from profile FTP port {activeProfile.FtpPort.Value.ToString(CultureInfo.InvariantCulture)}.");
        }
        else if (activeProfile.FtpPort.HasValue && !config.DefaultFtpPort.HasValue) {
            warnings.Add($"Saved FTP port is missing; profile FTP port {activeProfile.FtpPort.Value.ToString(CultureInfo.InvariantCulture)} will be used.");
        }
        else if (!activeProfile.FtpPort.HasValue && config.DefaultFtpPort.HasValue) {
            warnings.Add($"Profile FTP port is missing; saved FTP port {config.DefaultFtpPort.Value.ToString(CultureInfo.InvariantCulture)} will be used.");
        }

        if (!string.IsNullOrWhiteSpace(activeProfile.FtpUser) && !string.IsNullOrWhiteSpace(config.DefaultFtpUser) &&
            !string.Equals(activeProfile.FtpUser, config.DefaultFtpUser, StringComparison.OrdinalIgnoreCase)) {
            warnings.Add($"Saved FTP user {config.DefaultFtpUser} differs from profile FTP user {activeProfile.FtpUser}.");
        }
        else if (!string.IsNullOrWhiteSpace(activeProfile.FtpUser) && string.IsNullOrWhiteSpace(config.DefaultFtpUser)) {
            warnings.Add($"Saved FTP user is missing; profile FTP user {activeProfile.FtpUser} will be used.");
        }
        else if (string.IsNullOrWhiteSpace(activeProfile.FtpUser) && !string.IsNullOrWhiteSpace(config.DefaultFtpUser)) {
            warnings.Add($"Profile FTP user is missing; saved FTP user {config.DefaultFtpUser} will be used.");
        }

        return warnings;
    }

    private static IReadOnlyList<RecentConsoleHistorySummary> BuildRecentSummary(TargetProfileStoreData store, bool includePrivate) {
        RecentConsoleHistoryStoreData history = RecentConsoleHistoryStore.Load();
        return history.Entries
            .Take(3)
            .Select(entry => new RecentConsoleHistorySummary(
                Endpoint: RecentConsoleHistoryStore.FormatEndpoint(entry, includePrivate),
                ProfileName: TargetProfileStore.FindProfileByEndpoint(store, entry.Ip, entry.Port)?.Name,
                LatencyClass: RecentConsoleHistoryStore.DescribeLatencyClass(entry),
                LastSeenUtc: entry.LastSeenUtc,
                ContactCount: entry.ContactCount))
            .ToList();
    }
}

internal sealed record RecentConsoleHistoryEntry(
    string Endpoint,
    string? ProfileName,
    string LatencyClass,
    DateTimeOffset LastSeenUtc,
    int ContactCount);

internal sealed record RecentConsoleHistorySnapshot(
    int Limit,
    int Total,
    IReadOnlyList<RecentConsoleHistoryEntry> Entries);

internal static class RecentConsoleHistorySnapshotBuilder {
    public static RecentConsoleHistorySnapshot Build(int limit, bool includePrivate) {
        RecentConsoleHistoryStoreData history = RecentConsoleHistoryStore.Load();
        IReadOnlyList<RecentConsoleHistoryRecord> records = history.Entries.Take(Math.Max(limit, 0)).ToList();
        TargetProfileStoreData profiles = TargetProfileStore.Load();
        return new RecentConsoleHistorySnapshot(
            limit,
            history.Entries.Count,
            records.Select(entry => new RecentConsoleHistoryEntry(
                Endpoint: RecentConsoleHistoryStore.FormatEndpoint(entry, includePrivate),
                ProfileName: TargetProfileStore.FindProfileByEndpoint(profiles, entry.Ip, entry.Port)?.Name,
                LatencyClass: RecentConsoleHistoryStore.DescribeLatencyClass(entry),
                LastSeenUtc: entry.LastSeenUtc,
                ContactCount: entry.ContactCount)).ToList());
    }
}
