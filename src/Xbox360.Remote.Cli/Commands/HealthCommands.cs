using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public sealed class HealthCommand : Command<HealthCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Show saved target endpoints in full, including the port.")]
        public bool IncludePrivate { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        HealthReport report = HealthReportBuilder.Build(settings.IncludePrivate);
        if (settings.Json) {
            CliOutput.EmitJson(report);
            return report.IsHealthy ? 0 : 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]XeCLI Health[/]").RuleStyle("silver"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Check[/]"));
        table.AddColumn(new TableColumn("[green]Status[/]"));
        table.AddColumn(new TableColumn("[cyan]Detail[/]"));

        foreach (HealthCheckResult check in report.Checks) {
            string color = check.Status switch {
                "ok" => "green",
                "warn" => "yellow",
                _ => "red"
            };
            table.AddRow(
                $"[white]{Markup.Escape(check.Name)}[/]",
                $"[{color}]{Markup.Escape(check.Status)}[/]",
                Markup.Escape(check.Detail));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Support:[/] run [cyan]{Markup.Escape(report.SupportCommand)}[/] (or [cyan]rgh diagnostics bundle[/]) to collect a redacted offline bundle.");
        return report.IsHealthy ? 0 : 1;
    }
}

internal sealed record HealthReport(
    bool IsHealthy,
    bool IncludePrivate,
    IReadOnlyList<HealthCheckResult> Checks,
    TargetProfileHealthSnapshot Target,
    CommandLogHealthSnapshot CommandLog,
    string SupportCommand);

internal sealed record HealthCheckResult(
    string Section,
    string Name,
    string Status,
    string Severity,
    string Code,
    string Message,
    string Detail,
    string? Remediation = null);

internal static class HealthReportBuilder {
    internal static Func<string, IEnumerable<string>> ReadCommandLogLines { get; set; } = File.ReadLines;

    public static HealthReport Build(bool includePrivate) {
        List<HealthCheckResult> checks = [];
        string? configParseIssue = null;

        CliConfig? config = null;
        bool configPresent = File.Exists(CliPaths.ConfigPath);
        bool configParsed = false;
        if (!configPresent) {
            checks.Add(BuildCheck(
                section: "configuration",
                name: "Config",
                status: "warn",
                code: "config-missing",
                message: "Config file is not present yet.",
                detail: "Config file is not present yet.",
                remediation: "Run a command that saves configuration, or create config.json before running rgh health again."));
        }
        else {
            try {
                config = CliConfig.Load();
                configParsed = true;
                checks.Add(BuildCheck(
                    section: "configuration",
                    name: "Config",
                    status: "ok",
                    code: "config-present",
                    message: "Config file is present and parses.",
                    detail: "Config file is present and parses."));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                configParseIssue = "Config parse failed. Fix or remove config.json, then run rgh health again.";
                checks.Add(BuildCheck(
                    section: "configuration",
                    name: "Config",
                    status: "fail",
                    code: "config-parse-failed",
                    message: "Config file is present but could not be parsed.",
                    detail: "Config file is present but could not be parsed.",
                    remediation: "Fix or remove config.json, then run rgh health again."));
            }
        }

        TargetProfileHealthSnapshot target = TargetProfileStore.GetHealthSnapshot(config, configPresent, configParsed, includePrivate);
        if (!string.IsNullOrWhiteSpace(configParseIssue)) {
            target = target with {
                Issues = target.Issues.Concat([CommandLogRedactor.RedactFreeText(configParseIssue)]).ToList()
            };
        }
        checks.Add(BuildTargetCheck(target));

        CommandLogHealthSnapshot commandLog = CommandLogStore.GetHealthSnapshot(ReadCommandLogLines);
        checks.Add(BuildCommandLogCheck(commandLog));

        bool healthy = checks.All(check => !string.Equals(check.Status, "fail", StringComparison.OrdinalIgnoreCase));
        return new HealthReport(
            healthy,
            includePrivate,
            checks,
            target,
            commandLog,
            "rgh support bundle");
    }

    private static HealthCheckResult BuildTargetCheck(TargetProfileHealthSnapshot target) {
        if (target.Issues.Count > 0)
            return BuildCheck(
                section: "target",
                name: "Target profile",
                status: "fail",
                code: "target-invalid",
                message: string.Join("; ", target.Issues),
                detail: string.Join("; ", target.Issues),
                remediation: "Fix the saved target profile or remove the bad profile data, then run rgh health again.");

        if (string.IsNullOrWhiteSpace(target.DefaultTarget) && string.IsNullOrWhiteSpace(target.CurrentProfileName))
            return BuildCheck(
                section: "target",
                name: "Target profile",
                status: "warn",
                code: "target-not-saved",
                message: "No default target or current profile is saved.",
                detail: "No default target or current profile is saved.",
                remediation: "Save a target or profile if you expect rgh to reconnect to the same console later.");

        string detail = string.IsNullOrWhiteSpace(target.CurrentProfileName)
            ? $"Default target {target.DefaultTarget}; profiles {target.ProfileCount.ToString(CultureInfo.InvariantCulture)}."
            : $"Current profile {target.CurrentProfileName}; profiles {target.ProfileCount.ToString(CultureInfo.InvariantCulture)}.";
        return BuildCheck(
            section: "target",
            name: "Target profile",
            status: "ok",
            code: "target-ready",
            message: detail,
            detail: detail);
    }

    private static HealthCheckResult BuildCommandLogCheck(CommandLogHealthSnapshot commandLog) {
        if (!commandLog.Present)
            return BuildCheck(
                section: "logging",
                name: "Command log",
                status: "warn",
                code: "command-log-missing",
                message: "Command log is not present yet.",
                detail: "Command log is not present yet.",
                remediation: "Run any command once to create the local command log cache.");

        if (!commandLog.Readable)
            return BuildCheck(
                section: "logging",
                name: "Command log",
                status: "fail",
                code: "command-log-unreadable",
                message: "Command log could not be read.",
                detail: "Command log could not be read.",
                remediation: "Check file permissions or delete the damaged command log cache, then run rgh health again.");

        string detail = $"{commandLog.Entries.ToString(CultureInfo.InvariantCulture)} entr{(commandLog.Entries == 1 ? "y" : "ies")}; {commandLog.MalformedLines.ToString(CultureInfo.InvariantCulture)} malformed line(s).";
        return commandLog.MalformedLines == 0
            ? BuildCheck(
                section: "logging",
                name: "Command log",
                status: "ok",
                code: "command-log-ready",
                message: detail,
                detail: detail)
            : BuildCheck(
                section: "logging",
                name: "Command log",
                status: "warn",
                code: "command-log-partial",
                message: detail,
                detail: detail,
                remediation: "Delete or rotate the malformed entries if the cache keeps growing corrupted.");
    }

    private static HealthCheckResult BuildCheck(
        string section,
        string name,
        string status,
        string code,
        string message,
        string detail,
        string? remediation = null) {
        return new HealthCheckResult(
            section,
            name,
            status,
            ToSeverity(status),
            code,
            message,
            detail,
            remediation);
    }

    private static string ToSeverity(string status) {
        return status.ToLowerInvariant() switch {
            "ok" => "info",
            "warn" => "warning",
            "fail" => "error",
            _ => status
        };
    }
}
