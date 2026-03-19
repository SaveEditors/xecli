using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal static class CliOutput {
    private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

    public static void EmitJson(object value) {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        AnsiConsole.WriteLine(json);
    }

    public static async Task RunWithProgressAsync(string title, uint? totalBytes, Func<IProgress<long>, Task> operation) {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[] {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask(title, maxValue: totalBytes ?? 1);
                Progress<long> progress = new Progress<long>(value => {
                    task.Value = totalBytes.HasValue ? Math.Min(value, totalBytes.Value) : value;
                });
                await operation(progress);
                task.Value = totalBytes ?? task.MaxValue;
            });
    }

    public static void RenderDiscovery(IReadOnlyList<DiscoveredConsole> consoles, bool json) {
        if (json) {
            EmitJson(consoles.Select(c => new {
                Ip = c.Ip.ToString(),
                c.Port,
                c.DebugName,
                c.ConsoleId,
                c.Source
            }));
            return;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Detected Consoles[/]").RuleStyle("silver"));
        Table table = CreateTable();
        table.AddColumn(new TableColumn("[white]#[/]").Centered());
        table.AddColumn(new TableColumn("[cyan1]IP[/]"));
        table.AddColumn(new TableColumn("[deepskyblue1]Port[/]"));
        table.AddColumn(new TableColumn("[springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[gold1]Console ID/Serial[/]"));
        table.AddColumn(new TableColumn("[white]Source[/]"));
        int index = 1;
        foreach (DiscoveredConsole console in consoles) {
            string name = console.DebugName != null ? Markup.Escape(console.DebugName) : "unknown";
            string consoleId = console.ConsoleId != null ? Markup.Escape(console.ConsoleId) : "unknown";
            table.AddRow(
                $"[white]{index}[/]",
                $"[cyan1]{console.Ip}[/]",
                $"[deepskyblue1]{console.Port}[/]",
                console.DebugName != null ? $"[springgreen3_1]{name}[/]" : "[grey70]unknown[/]",
                console.ConsoleId != null ? $"[gold1]{consoleId}[/]" : "[grey70]unknown[/]",
                $"[silver]{console.Source}[/]");
            index++;
        }

        AnsiConsole.Write(table);
    }

    public static string? PromptForConsole(IReadOnlyList<DiscoveredConsole> consoles) {
        if (consoles.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No consoles discovered. Enter IP manually:[/]");
            return AnsiConsole.Ask<string>("IP:");
        }

        SelectionPrompt<DiscoveredConsole> prompt = new SelectionPrompt<DiscoveredConsole>()
            .Title("Select a console")
            .PageSize(10)
            .HighlightStyle(new Style(Color.DeepSkyBlue1, decoration: Decoration.Bold))
            .UseConverter(c => $"{c.Ip} | {c.DebugName ?? "unknown"} | {c.ConsoleId ?? "unknown"} | {c.Source}");
        prompt.AddChoices(consoles);
        DiscoveredConsole selection = AnsiConsole.Prompt(prompt);
        return selection.Ip.ToString();
    }

    public static void RenderHexDump(uint baseAddress, byte[] data, int width = 16) {
        char[] asciiBuffer = new char[width];
        for (int offset = 0; offset < data.Length; offset += width) {
            int lineCount = Math.Min(width, data.Length - offset);
            for (int i = 0; i < lineCount; i++) {
                byte b = data[offset + i];
                asciiBuffer[i] = b >= 32 && b <= 126 ? (char) b : '.';
            }

            StringBuilder bytesHex = new StringBuilder();
            for (int i = 0; i < width; i++) {
                if (i < lineCount) {
                    bytesHex.Append(data[offset + i].ToString("X2")).Append(' ');
                }
                else {
                    bytesHex.Append("   ");
                }
            }

            string asciiText = Markup.Escape(new string(asciiBuffer, 0, lineCount));
            AnsiConsole.MarkupLine($"[white]0x{baseAddress + (uint) offset:X8}[/] [deepskyblue1]{bytesHex}[/] [springgreen3_1]{asciiText}[/]");
        }
    }

    public static Table CreateTable() {
        return new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Silver)
            .Expand();
    }

    public static string FormatTimestamp(DateTime? value) {
        if (!value.HasValue)
            return "[grey]unknown[/]";

        DateTime dt = value.Value;
        DateTime utc = dt.Kind switch {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime()
        };

        DateTime eastern = TimeZoneInfo.ConvertTimeFromUtc(utc, EasternTimeZone);
        string formatted = eastern.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"[green]{Markup.Escape(formatted)}[/]";
    }

    private static TimeZoneInfo GetEasternTimeZone() {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
        catch {
            return TimeZoneInfo.Local;
        }
    }
}
