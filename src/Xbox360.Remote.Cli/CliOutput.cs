using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Xbox360.Remote;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli;

internal static class CliOutput {
    private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

    internal readonly record struct TransferProgressUpdate(long Value, string? Status = null);
    internal readonly record struct TransferBatchItem(string Label, long Size);

    public static void EmitJson(object value) {
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        AnsiConsole.Console.Profile.Out.Writer.WriteLine(json);
    }

    public static void EmitJsonError(CliErrorEnvelope error) {
        EmitJson(new { Error = CliErrorReporter.PrepareForJsonOutput(error) });
    }

    public static async Task RunWithProgressAsync(string title, uint? totalBytes, Func<IProgress<long>, Task> operation) {
        await RunWithProgressAsync(title, totalBytes.HasValue ? totalBytes.Value : null, async progress => {
            Progress<long> bridged = new Progress<long>(value => progress.Report(new TransferProgressUpdate(value)));
            await operation(bridged);
        });
    }

    public static async Task RunWithProgressAsync(string title, long? totalBytes, Func<IProgress<TransferProgressUpdate>, Task> operation) {
        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(new ProgressColumn[] {
                new TaskDescriptionColumn(),
                new ProgressBarColumn {
                    CompletedStyle = new Style(Color.SpringGreen3_1, decoration: Decoration.Bold),
                    FinishedStyle = new Style(Color.SpringGreen3_1, decoration: Decoration.Bold),
                    RemainingStyle = new Style(Color.Grey35)
                },
                new PercentageColumn(),
                new DownloadedColumn(),
                new TransferSpeedColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            })
            .StartAsync(async ctx => {
                ProgressTask task = ctx.AddTask(FormatTransferDescription(title, "starting"), maxValue: totalBytes ?? 1);
                if (!totalBytes.HasValue) {
                    task.IsIndeterminate = true;
                }

                Progress<TransferProgressUpdate> progress = new Progress<TransferProgressUpdate>(update => {
                    if (totalBytes.HasValue) {
                        task.Value = Math.Min(update.Value, totalBytes.Value);
                    }

                    if (!string.IsNullOrWhiteSpace(update.Status)) {
                        task.Description = FormatTransferDescription(title, update.Status);
                    }
                });

                await operation(progress);
                task.Description = FormatTransferDescription(title, "done");
                task.Value = task.MaxValue;
            });
    }

    public static async Task RunBatchProgressAsync(string title, IReadOnlyList<TransferBatchItem> items, Func<TransferBatchScope, Task> operation) {
        long totalBytes = items.Sum(item => Math.Max(0, item.Size));
        await RunWithProgressAsync(title, totalBytes > 0 ? totalBytes : null, async progress => {
            TransferBatchScope scope = new TransferBatchScope(progress, items.Count, totalBytes);
            await operation(scope);
            scope.Finish();
        });
    }

    private static string FormatTransferDescription(string title, string? status) {
        string escapedTitle = Markup.Escape(title);
        if (string.IsNullOrWhiteSpace(status))
            return $"[bold springgreen3_1]{escapedTitle}[/]";
        return $"[bold springgreen3_1]{escapedTitle}[/] [silver]{Markup.Escape(status)}[/]";
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

internal sealed class TransferBatchScope {
    private readonly IProgress<CliOutput.TransferProgressUpdate> _progress;
    private readonly int _totalFiles;
    private readonly long _totalBytes;
    private long _completedBytes;
    private long _currentFileSize;
    private string _currentLabel = string.Empty;

    public TransferBatchScope(IProgress<CliOutput.TransferProgressUpdate> progress, int totalFiles, long totalBytes) {
        _progress = progress;
        _totalFiles = Math.Max(0, totalFiles);
        _totalBytes = Math.Max(0, totalBytes);
    }

    public int CompletedFiles { get; private set; }

    public void StartFile(string label, long fileSize) {
        _currentLabel = label;
        _currentFileSize = Math.Max(0, fileSize);
        ReportFileProgress(0, $"file {CompletedFiles + 1}/{Math.Max(1, _totalFiles)}");
    }

    public void ReportFileProgress(long fileBytes, string? status = null) {
        long bounded = Math.Max(0, Math.Min(fileBytes, _currentFileSize));
        long totalProgress = _completedBytes + bounded;
        string suffix = string.IsNullOrWhiteSpace(status)
            ? $"file {CompletedFiles + 1}/{Math.Max(1, _totalFiles)}"
            : status;
        _progress.Report(new CliOutput.TransferProgressUpdate(totalProgress, $"{suffix} | {Path.GetFileName(_currentLabel)}"));
    }

    public void CompleteFile() {
        _completedBytes = Math.Min(_completedBytes + _currentFileSize, _totalBytes > 0 ? _totalBytes : _completedBytes + _currentFileSize);
        CompletedFiles++;
        _progress.Report(new CliOutput.TransferProgressUpdate(_completedBytes, $"completed {CompletedFiles}/{Math.Max(1, _totalFiles)} | {Path.GetFileName(_currentLabel)}"));
        _currentFileSize = 0;
        _currentLabel = string.Empty;
    }

    public void Finish() {
        _progress.Report(new CliOutput.TransferProgressUpdate(_totalBytes, $"completed {CompletedFiles}/{Math.Max(1, _totalFiles)}"));
    }
}
