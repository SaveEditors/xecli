using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TitleLookupCommand : AsyncCommand<TitleLookupCommand.Settings> {
    private readonly TitleIdDatabase? titleIdDatabase;

    public TitleLookupCommand() {
    }

    internal TitleLookupCommand(TitleIdDatabase titleIdDatabase) {
        this.titleIdDatabase = titleIdDatabase ?? throw new ArgumentNullException(nameof(titleIdDatabase));
    }

    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[TITLEID]")]
        [LocalizedDescription("Title ID (hex, with or without 0x), or a title name search when not hex. Omit it or pass 'active' to resolve the current title.")]
        public string TitleId { get; init; } = string.Empty;

        [CommandArgument(1, "[MEDIAID]")]
        [LocalizedDescription("Optional media ID (hex). Prefers a matching row when available, but still shows all title-ID matches.")]
        public string? MediaId { get; init; }

        [CommandOption("--active")]
        [LocalizedDescription("Resolve the currently active title from the connected console.")]
        public bool Active { get; init; }

        [CommandOption("--database <PATH>")]
        [LocalizedDescription("Use a user-supplied CSV or TXT Title Database for this lookup. Overrides settings and XECLI_TITLE_DATABASE.")]
        public string? DatabasePath { get; init; }

    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        TitleIdDatabase database = titleIdDatabase ?? ResolveDatabase(settings.DatabasePath);
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
        string? titleIdText = null;
        string? runningXex = null;
        IReadOnlyList<TitleIdEntry> entries;
        TitleIdEntry? primary = null;

        if (settings.Active ||
            string.IsNullOrWhiteSpace(settings.TitleId) ||
            string.Equals(settings.TitleId, "active", StringComparison.OrdinalIgnoreCase)) {
            (titleId, runningXex) = await ResolveActiveTitleAsync(settings);
            source = "active";
            titleIdText = $"0x{titleId:X8}";
            entries = database.FindAll(titleId);
        }
        else {
            if (TryParseHex(settings.TitleId, out titleId)) {
                source = "manual";
                titleIdText = $"0x{titleId:X8}";
                entries = database.FindAll(titleId);
            }
            else {
                source = "search";
                entries = database.FindByName(settings.TitleId);
                if (entries.Count > 0) {
                    primary = mediaId.HasValue
                        ? entries.FirstOrDefault(entry => entry.MediaId == mediaId.Value) ?? entries[0]
                        : entries[0];
                    titleId = primary.TitleId;
                    titleIdText = $"0x{titleId:X8}";
                }
                else {
                    titleId = 0;
                }
            }
        }

        if (source != "search" && mediaId.HasValue)
            database.TryResolve(titleId, mediaId, out primary);
        if (source != "search")
            primary ??= entries.FirstOrDefault();
        primary ??= entries.FirstOrDefault();

        string? fallbackTitle = null;
        if (source == "active") {
            if (entries.Count == 0 && database.TryRememberDiscoveredTitle(titleId, runningXex, out string? discoveredName))
                fallbackTitle = discoveredName;
            fallbackTitle = ProfileHelpers.TryGetTitleFallbackName(titleId, runningXex, fallbackTitle);
        }
        else if (source == "manual" && entries.Count == 0) {
            fallbackTitle = TitleIdDatabase.FormatTitleId(titleId);
        }

        if (settings.Json) {
            if (source == "search") {
                string query = settings.TitleId.Trim();
                CliOutput.EmitJson(new {
                    Source = source,
                    Query = query,
                    TitleId = entries.Count > 0 ? $"0x{titleId:X8}" : null,
                    MediaId = mediaId.HasValue ? $"0x{mediaId.Value:X8}" : null,
                    RunningXex = runningXex,
                    Matches = entries,
                    FallbackTitle = fallbackTitle,
                    Database = database.Status
                });
            }
            else {
                CliOutput.EmitJson(new {
                    Source = source,
                    TitleId = titleIdText,
                    MediaId = mediaId.HasValue ? $"0x{mediaId.Value:X8}" : null,
                    RunningXex = runningXex,
                    Matches = entries,
                    FallbackTitle = fallbackTitle,
                    Database = database.Status
                });
            }
            return source == "search" && entries.Count == 0 ? 1 : 0;
        }

        WriteDatabaseWarnings(database.Warnings);

        if (entries.Count == 0) {
            if (!string.IsNullOrWhiteSpace(fallbackTitle)) {
                string fallbackHeader = source == "active" ? "Active Title" : "Title Lookup";
                Panel fallbackPanel = new Panel($"[bold green]{Markup.Escape(fallbackTitle)}[/]\n[grey]Title ID[/] [cyan]0x{titleId:X8}[/]")
                    .Header($"[bold deepskyblue1]{fallbackHeader}[/]")
                    .BorderColor(Color.Grey);
                AnsiConsole.Write(fallbackPanel);
                if (!string.IsNullOrWhiteSpace(runningXex))
                    AnsiConsole.MarkupLine($"[grey]Running XEX:[/] [green]{Markup.Escape(runningXex)}[/]");
                AnsiConsole.MarkupLine("[yellow]No user Title Database entry matched this ID; raw hexadecimal output remains available.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
            return 1;
        }

        TitleIdEntry displayEntry = primary ?? entries[0];
        string displayName = source == "active" && !string.IsNullOrWhiteSpace(fallbackTitle)
            ? fallbackTitle!
            : displayEntry.Name;
        string header = source == "active" ? "Active Title" : "Title Lookup";
        string mediaText = displayEntry.MediaId.HasValue ? $"0x{displayEntry.MediaId.Value:X8}" : "unknown";
        Panel summary = new Panel(
                $"[bold green]{Markup.Escape(displayName)}[/]\n" +
                $"[grey]Title ID[/] [cyan]0x{displayEntry.TitleId:X8}[/]  [grey]Media ID[/] [cyan]{Markup.Escape(mediaText)}[/]")
            .Header($"[bold deepskyblue1]{header}[/]")
            .BorderColor(Color.Grey);
        AnsiConsole.Write(summary);

        if (!string.IsNullOrWhiteSpace(runningXex))
            AnsiConsole.MarkupLine($"[grey]Running XEX:[/] [green]{Markup.Escape(runningXex)}[/]");
        if (!string.Equals(displayName, displayEntry.Name, StringComparison.OrdinalIgnoreCase))
            AnsiConsole.MarkupLine($"[grey]Database entry:[/] [mediumpurple3]{Markup.Escape(displayEntry.Name)}[/]");

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

    private static TitleIdDatabase ResolveDatabase(string? commandPath) {
        return string.IsNullOrWhiteSpace(commandPath)
            ? TitleIdDatabase.Instance
            : TitleIdDatabase.LoadForCommandPath(commandPath);
    }

    private static void WriteDatabaseWarnings(IReadOnlyList<TitleIdDatabaseWarning> warnings) {
        foreach (TitleIdDatabaseWarning warning in warnings)
            AnsiConsole.MarkupLine($"[yellow]Title Database warning:[/] {Markup.Escape(warning.ToString())}");
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

