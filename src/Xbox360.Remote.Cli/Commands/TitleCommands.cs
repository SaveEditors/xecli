using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TitleLookupCommand : Command<TitleLookupCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<TITLEID>")]
        [Description("Title ID (hex, with or without 0x).")]
        public string TitleId { get; init; } = string.Empty;

        [CommandArgument(1, "[MEDIAID]")]
        [Description("Optional media ID (hex).")]
        public string? MediaId { get; init; }

        [CommandOption("--json")]
        [Description("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!TryParseHex(settings.TitleId, out uint titleId)) {
            AnsiConsole.MarkupLine("[red]Invalid Title ID.[/]");
            return 1;
        }

        uint? mediaId = null;
        if (!string.IsNullOrWhiteSpace(settings.MediaId)) {
            if (!TryParseHex(settings.MediaId, out uint parsed)) {
                AnsiConsole.MarkupLine("[red]Invalid Media ID.[/]");
                return 1;
            }
            mediaId = parsed;
        }

        IReadOnlyList<TitleIdEntry> entries = TitleIdDatabase.Instance.FindAll(titleId);
        if (mediaId.HasValue) {
            entries = entries.Where(e => e.MediaId == mediaId.Value).ToList();
        }

        if (settings.Json) {
            CliOutput.EmitJson(entries);
            return 0;
        }

        if (entries.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
            return 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Title Lookup[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Name[/]"));
        table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
        table.AddColumn(new TableColumn("[cyan]Media ID[/]"));
        table.AddColumn(new TableColumn("[grey]Region[/]"));
        table.AddColumn(new TableColumn("[grey]Type[/]"));
        table.AddColumn(new TableColumn("[grey]Serial[/]"));
        table.AddColumn(new TableColumn("[grey]Wave[/]"));

        foreach (TitleIdEntry entry in entries) {
            table.AddRow(
                $"[green]{Markup.Escape(entry.Name)}[/]",
                $"[cyan]0x{entry.TitleId:X8}[/]",
                entry.MediaId.HasValue ? $"[cyan]0x{entry.MediaId.Value:X8}[/]" : "[grey]unknown[/]",
                string.IsNullOrWhiteSpace(entry.Region) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Region)}[/]",
                string.IsNullOrWhiteSpace(entry.Type) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Type)}[/]",
                string.IsNullOrWhiteSpace(entry.Serial) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Serial)}[/]",
                string.IsNullOrWhiteSpace(entry.Wave) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Wave)}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static bool TryParseHex(string text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
