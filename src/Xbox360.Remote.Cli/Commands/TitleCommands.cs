using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TitleLookupCommand : AsyncCommand<TitleLookupCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[TITLEID]")]
        [Description("Title ID (hex, with or without 0x). Omit it or pass 'active' to resolve the current title.")]
        public string TitleId { get; init; } = string.Empty;

        [CommandArgument(1, "[MEDIAID]")]
        [Description("Optional media ID (hex).")]
        public string? MediaId { get; init; }

        [CommandOption("--active")]
        [Description("Resolve the currently active title from the connected console.")]
        public bool Active { get; init; }

    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        uint? mediaId = null;
        if (!string.IsNullOrWhiteSpace(settings.MediaId)) {
            if (!TryParseHex(settings.MediaId, out uint parsedMediaId)) {
                AnsiConsole.MarkupLine("[red]Invalid Media ID.[/]");
                return 1;
            }

            mediaId = parsedMediaId;
        }

        uint titleId;
        string source;
        string? runningXex = null;

        if (settings.Active ||
            string.IsNullOrWhiteSpace(settings.TitleId) ||
            string.Equals(settings.TitleId, "active", StringComparison.OrdinalIgnoreCase)) {
            (titleId, runningXex) = await ResolveActiveTitleAsync(settings);
            source = "active";
        }
        else {
            if (!TryParseHex(settings.TitleId, out titleId)) {
                AnsiConsole.MarkupLine("[red]Invalid Title ID.[/]");
                return 1;
            }

            source = "manual";
        }

        IReadOnlyList<TitleIdEntry> entries = TitleIdDatabase.Instance.FindAll(titleId);
        if (mediaId.HasValue)
            entries = entries.Where(e => e.MediaId == mediaId.Value).ToList();

        string? fallbackTitle = ProfileHelpers.TryGetTitleFallbackName(titleId, runningXex, null);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Source = source,
                TitleId = $"0x{titleId:X8}",
                MediaId = mediaId.HasValue ? $"0x{mediaId.Value:X8}" : null,
                RunningXex = runningXex,
                Matches = entries,
                FallbackTitle = fallbackTitle
            });
            return entries.Count == 0 && string.IsNullOrWhiteSpace(fallbackTitle) ? 1 : 0;
        }

        if (entries.Count == 0) {
            if (!string.IsNullOrWhiteSpace(fallbackTitle)) {
                Panel fallbackPanel = new Panel($"[bold green]{Markup.Escape(fallbackTitle)}[/]\n[grey]Title ID[/] [cyan]0x{titleId:X8}[/]")
                    .Header("[bold deepskyblue1]Active Title[/]")
                    .BorderColor(Color.Grey);
                AnsiConsole.Write(fallbackPanel);
                if (!string.IsNullOrWhiteSpace(runningXex))
                    AnsiConsole.MarkupLine($"[grey]Running XEX:[/] [green]{Markup.Escape(runningXex)}[/]");
                AnsiConsole.MarkupLine("[yellow]No bundled database entry matched this title yet.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
            return 1;
        }

        TitleIdEntry primary = entries[0];
        string displayName = source == "active" && !string.IsNullOrWhiteSpace(fallbackTitle)
            ? fallbackTitle!
            : primary.Name;
        string header = source == "active" ? "Active Title" : "Title Lookup";
        string mediaText = primary.MediaId.HasValue ? $"0x{primary.MediaId.Value:X8}" : "unknown";
        Panel summary = new Panel(
                $"[bold green]{Markup.Escape(displayName)}[/]\n" +
                $"[grey]Title ID[/] [cyan]0x{primary.TitleId:X8}[/]  [grey]Media ID[/] [cyan]{Markup.Escape(mediaText)}[/]")
            .Header($"[bold deepskyblue1]{header}[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(summary);

        if (!string.IsNullOrWhiteSpace(runningXex))
            AnsiConsole.MarkupLine($"[grey]Running XEX:[/] [green]{Markup.Escape(runningXex)}[/]");
        if (!string.Equals(displayName, primary.Name, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[grey]Database entry:[/] [mediumpurple3]{Markup.Escape(primary.Name)}[/]");

        Table table = CliOutput.CreateTable();
        table.Border(TableBorder.Rounded);
        table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Title ID[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Media ID[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Region[/]"));
        table.AddColumn(new TableColumn("[bold mediumpurple3]Type[/]"));
        table.AddColumn(new TableColumn("[bold grey]Serial[/]"));
        table.AddColumn(new TableColumn("[bold grey]Wave[/]"));

        foreach (TitleIdEntry entry in entries) {
            table.AddRow(
                $"[springgreen3_1]{Markup.Escape(entry.Name)}[/]",
                $"[deepskyblue1]0x{entry.TitleId:X8}[/]",
                entry.MediaId.HasValue ? $"[deepskyblue1]0x{entry.MediaId.Value:X8}[/]" : "[grey]unknown[/]",
                string.IsNullOrWhiteSpace(entry.Region) ? "[grey]unknown[/]" : $"[gold1]{Markup.Escape(entry.Region)}[/]",
                string.IsNullOrWhiteSpace(entry.Type) ? "[grey]unknown[/]" : $"[mediumpurple3]{Markup.Escape(entry.Type)}[/]",
                string.IsNullOrWhiteSpace(entry.Serial) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Serial)}[/]",
                string.IsNullOrWhiteSpace(entry.Wave) ? "[grey]unknown[/]" : $"[grey]{Markup.Escape(entry.Wave)}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<(uint TitleId, string? RunningXex)> ResolveActiveTitleAsync(Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
            Host = ip,
            Port = port,
            TimeoutMs = timeout
        }, CancellationToken.None);

        string? runningXex = null;
        try {
            runningXex = await client.GetRunningXexPathAsync(null, CancellationToken.None);
        }
        catch {
            // ignored
        }

        Jrpc2Client jrpc = new Jrpc2Client(client);
        uint titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
        return (titleId, runningXex);
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
