using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LogListCommand : Command<LogListCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum entries to show (default: all, 0 = no entries).")]
        public int? Limit { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }

        [CommandOption("--csv")]
        [LocalizedDescription("Output CSV to stdout.")]
        public bool Csv { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Json && settings.Csv) {
            AnsiConsole.MarkupLine("[red]Use either --json or --csv, not both.[/]");
            return 1;
        }

        if (settings.Limit.HasValue && settings.Limit.Value < 0) {
            AnsiConsole.MarkupLine("[red]--limit must be zero or greater.[/]");
            return 1;
        }

        IReadOnlyList<CommandLogEntry> entries = CommandLogStore.ReadAll();
        int limit = settings.Limit.HasValue ? settings.Limit.Value : entries.Count;
        IReadOnlyList<CommandLogEntry> newestFirstEntries = entries.Reverse().Take(limit).ToArray();

        if (settings.Json) {
            CliOutput.EmitJson(newestFirstEntries);
            return 0;
        }

        if (settings.Csv) {
            Console.Write(LogExportCommand.RenderCsv(newestFirstEntries));
            return 0;
        }

        if (entries.Count == 0) {
            AnsiConsole.MarkupLine("[grey]No command log entries found.[/]");
            return 0;
        }

        Table table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[grey]Time[/]");
        table.AddColumn("[grey]Status[/]");
        table.AddColumn("[grey]Command[/]");
        table.AddColumn("[grey]Arguments[/]");
        table.AddColumn("[grey]Exit[/]");
        table.AddColumn("[grey]Ms[/]");

        foreach (CommandLogEntry entry in newestFirstEntries) {
            string time = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            string status = entry.Succeeded ? "[green]ok[/]" : "[red]failed[/]";
            string command = Markup.Escape(entry.CommandName ?? string.Empty);
            string arguments = Truncate(Markup.Escape(entry.CommandLine ?? string.Empty), 120);
            string exitCode = entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "[grey]n/a[/]";
            string duration = entry.DurationMs.ToString(CultureInfo.InvariantCulture);

            table.AddRow(time, status, command, arguments, exitCode, duration);
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static string Truncate(string value, int maxLength) {
        if (value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 1)] + "…";
    }
}

public sealed class LogExportCommand : Command<LogExportCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--format <FORMAT>")]
        [LocalizedDescription("Export format. Only csv is supported.")]
        public string? Format { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output file path.")]
        public string? Output { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!string.Equals(settings.Format, "csv", StringComparison.OrdinalIgnoreCase)) {
            AnsiConsole.MarkupLine("[red]Unsupported export format.[/] Use [cyan]--format csv[/].");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]Missing output file.[/] Use [cyan]--out <FILE>[/].");
            return 1;
        }

        IReadOnlyList<CommandLogEntry> entries = CommandLogStore.ReadAll();
        try {
            string fullPath = Path.GetFullPath(settings.Output);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, RenderCsv(entries), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string leafName = XexInfoCommand.GetDisplayFileName(settings.Output);
            string detail = string.IsNullOrWhiteSpace(leafName)
                ? "Failed to write command log export. Check the output path, parent directory, and permissions."
                : $"Failed to write command log export file '{leafName}'. Check the output path, parent directory, and permissions.";
            OperationFeedback.WriteFailure("Log export failed", detail);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Exported[/] [cyan]{entries.Count}[/] [green]log entr{(entries.Count == 1 ? "y" : "ies")} to[/] [cyan]{Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.Output) ?? string.Empty)}[/]");
        return 0;
    }

    internal static string RenderCsv(IReadOnlyList<CommandLogEntry> entries) {
        StringBuilder sb = new StringBuilder();
        sb.Append("TimestampUtc,CommandName,CommandLine,Succeeded,ExitCode,DurationMs,Error").Append('\n');
        foreach (CommandLogEntry entry in entries) {
            sb.Append(Csv(entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(Csv(entry.CommandName ?? string.Empty)).Append(',');
            sb.Append(Csv(entry.CommandLine ?? string.Empty)).Append(',');
            sb.Append(Csv(entry.Succeeded ? "true" : "false")).Append(',');
            sb.Append(Csv(entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)).Append(',');
            sb.Append(Csv(entry.DurationMs.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append(Csv(entry.Error ?? string.Empty)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Csv(string value) {
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
