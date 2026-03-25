using System.ComponentModel;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexStringsCommand : AsyncCommand<XexStringsCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [Description("Input XEX file.")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [Description("Fetch the XEX via FTP before scanning (e.g. /Hdd1/Aurora/Aurora.xex).")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [Description("Use the running title XEX (resolved via XBDM + FTP).")]
        public bool Running { get; init; }

        [CommandOption("--min <N>")]
        [Description("Minimum string length (default: 4).")]
        public int? MinLength { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum strings to return (default: 200).")]
        public int? MaxCount { get; init; }

        [CommandOption("--unicode")]
        [Description("Include UTF-16 (LE/BE) strings.")]
        public bool Unicode { get; init; }

        [CommandOption("--out <FILE>")]
        [Description("Write output to a text file.")]
        public string? Output { get; init; }

        [CommandOption("--json")]
        [Description("Output JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? inputPath = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
        if (string.IsNullOrWhiteSpace(inputPath))
            return 1;

        int minLength = Math.Max(2, settings.MinLength ?? 4);
        int maxCount = Math.Max(1, settings.MaxCount ?? 200);

        byte[] data = await File.ReadAllBytesAsync(inputPath);
        List<StringHit> hits = new List<StringHit>();
        hits.AddRange(ExtractAscii(data, minLength, maxCount));
        if (settings.Unicode) {
            hits.AddRange(ExtractUtf16(data, minLength, maxCount, littleEndian: true));
            hits.AddRange(ExtractUtf16(data, minLength, maxCount, littleEndian: false));
        }

        hits = hits
            .OrderBy(h => h.Offset)
            .Take(maxCount)
            .ToList();

        if (!string.IsNullOrWhiteSpace(settings.Output)) {
            using StreamWriter writer = new StreamWriter(settings.Output, false, Encoding.UTF8);
            foreach (StringHit hit in hits) {
                writer.WriteLine($"{hit.Offset:X8}\t{hit.Kind}\t{hit.Text}");
            }
            AnsiConsole.MarkupLine($"[green]Wrote[/] {Markup.Escape(settings.Output)}");
        }

        if (settings.Json) {
            CliOutput.EmitJson(hits.Select(h => new { Offset = $"0x{h.Offset:X8}", h.Kind, h.Text }).ToList());
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]XEX Strings[/]").RuleStyle("grey"));
        if (hits.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No strings found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Offset[/]"));
        table.AddColumn(new TableColumn("[grey]Type[/]"));
        table.AddColumn(new TableColumn("[green]Text[/]"));
        foreach (StringHit hit in hits) {
            table.AddRow(
                $"[cyan]0x{hit.Offset:X8}[/]",
                $"[grey]{Markup.Escape(hit.Kind)}[/]",
                $"[green]{Markup.Escape(hit.Text)}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static async Task<string?> ResolveInputAsync(string? input, string? ftpPath, bool running) {
        if (!string.IsNullOrWhiteSpace(input)) {
            if (!File.Exists(input)) {
                AnsiConsole.MarkupLine("[red]Input file not found.[/]");
                return null;
            }
            return input;
        }

        if (!string.IsNullOrWhiteSpace(ftpPath)) {
            return await GhidraInputHelpers.DownloadViaFtpAsync(ftpPath);
        }

        if (running) {
            string? path = await GhidraInputHelpers.ResolveRunningFtpPathAsync();
            if (string.IsNullOrWhiteSpace(path)) {
                AnsiConsole.MarkupLine("[red]Unable to resolve running XEX via FTP path.[/]");
                return null;
            }
            return await GhidraInputHelpers.DownloadViaFtpAsync(path);
        }

        AnsiConsole.MarkupLine("[red]Provide --in, --ftp-path, or --running.[/]");
        return null;
    }

    private static List<StringHit> ExtractAscii(byte[] data, int minLength, int maxCount) {
        List<StringHit> hits = new List<StringHit>();
        int start = -1;
        for (int i = 0; i < data.Length; i++) {
            if (IsPrintable(data[i])) {
                if (start == -1)
                    start = i;
                continue;
            }

            if (start != -1) {
                int length = i - start;
                if (length >= minLength) {
                    hits.Add(new StringHit(start, "ascii", Encoding.ASCII.GetString(data, start, length)));
                    if (hits.Count >= maxCount)
                        return hits;
                }
                start = -1;
            }
        }

        if (start != -1) {
            int length = data.Length - start;
            if (length >= minLength) {
                hits.Add(new StringHit(start, "ascii", Encoding.ASCII.GetString(data, start, length)));
            }
        }
        return hits;
    }

    private static List<StringHit> ExtractUtf16(byte[] data, int minLength, int maxCount, bool littleEndian) {
        List<StringHit> hits = new List<StringHit>();
        int start = -1;
        for (int i = 0; i + 1 < data.Length; i += 2) {
            bool printable = littleEndian
                ? data[i + 1] == 0 && IsPrintable(data[i])
                : data[i] == 0 && IsPrintable(data[i + 1]);

            if (printable) {
                if (start == -1)
                    start = i;
                continue;
            }

            if (start != -1) {
                int lengthBytes = i - start;
                int lengthChars = lengthBytes / 2;
                if (lengthChars >= minLength) {
                    string text = DecodeUtf16(data, start, lengthBytes, littleEndian);
                    hits.Add(new StringHit(start, littleEndian ? "utf16le" : "utf16be", text));
                    if (hits.Count >= maxCount)
                        return hits;
                }
                start = -1;
            }
        }

        if (start != -1) {
            int lengthBytes = data.Length - start;
            int lengthChars = lengthBytes / 2;
            if (lengthChars >= minLength) {
                string text = DecodeUtf16(data, start, lengthBytes, littleEndian);
                hits.Add(new StringHit(start, littleEndian ? "utf16le" : "utf16be", text));
            }
        }
        return hits;
    }

    private static string DecodeUtf16(byte[] data, int offset, int length, bool littleEndian) {
        Encoding encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        return encoding.GetString(data, offset, length).TrimEnd('\0');
    }

    private static bool IsPrintable(byte value) {
        return value >= 32 && value <= 126;
    }

    private readonly record struct StringHit(int Offset, string Kind, string Text);
}
