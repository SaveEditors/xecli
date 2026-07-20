using System.ComponentModel;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexStringsCommand : AsyncCommand<XexStringsCommand.Settings> {
    internal const long MaxScanBytes = 256L * 1024 * 1024;
    internal static Func<string?, string?, bool, Task<string?>>? ResolveInputOverride { get; set; }

    public sealed class Settings : CommandSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Input file to scan.")]
        public string? Input { get; init; }

        [CommandOption("--in <FILE>")]
        [LocalizedDescription("Legacy alias for --file.")]
        public string? LegacyInput { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [LocalizedDescription("Fetch the XEX via FTP before scanning (e.g. /Hdd1/Aurora/Aurora.xex).")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [LocalizedDescription("Use the running title XEX (resolved via XBDM + FTP).")]
        public bool Running { get; init; }

        [CommandOption("--min-length <N>")]
        [LocalizedDescription("Minimum string length (default: 4).")]
        public int? MinLength { get; init; }

        [CommandOption("--min <N>")]
        [LocalizedDescription("Legacy alias for --min-length.")]
        public int? LegacyMinLength { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum strings to return (default: 200).")]
        public int? MaxCount { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Legacy alias for --limit.")]
        public int? LegacyMaxCount { get; init; }

        [CommandOption("--unicode")]
        [LocalizedDescription("Include UTF-16 (LE/BE) strings.")]
        public bool Unicode { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write output to a text file.")]
        public string? Output { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!TryValidateSourceSelectors(settings, out string? sourceError)) {
            WriteFailure(
                settings.Json,
                "XEX strings source validation failed",
                sourceError!,
                "XEX_STRINGS_SOURCE_INVALID");
            return 1;
        }

        string? inputPath = await ResolveInputAsync(
            settings.Input ?? settings.LegacyInput,
            settings.FtpPath,
            settings.Running,
            settings.Json);
        if (string.IsNullOrWhiteSpace(inputPath))
            return 1;

        if (!TryValidateFileSize(inputPath, out string? sizeError)) {
            WriteFailure(
                settings.Json,
                "XEX strings input validation failed",
                sizeError!,
                "XEX_STRINGS_INPUT_INVALID");
            return 1;
        }

        int minLength = Math.Max(1, settings.MinLength ?? settings.LegacyMinLength ?? 4);
        int maxCount = Math.Max(1, settings.MaxCount ?? settings.LegacyMaxCount ?? 200);

        byte[] data;
        try {
            data = await File.ReadAllBytesAsync(inputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            WriteFailure(
                settings.Json,
                "XEX strings read failed",
                XexInfoCommand.BuildReadFailureMessage(inputPath),
                "XEX_STRINGS_READ_FAILED");
            return 1;
        }

        XexStringScanResult result = XexStringScanner.Scan(data, minLength, maxCount, settings.Unicode);
        IReadOnlyList<StringHit> hits = result.Hits;

        if (!string.IsNullOrWhiteSpace(settings.Output)) {
            if (!TryWriteOutput(settings.Output, hits, out string? writeError)) {
                WriteFailure(
                    settings.Json,
                    "XEX strings output failed",
                    writeError!,
                    "XEX_STRINGS_WRITE_FAILED");
                return 1;
            }

            if (!settings.Json)
                AnsiConsole.MarkupLine($"[green]Wrote[/] {Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.Output))}");
        }

        if (settings.Json) {
            CliOutput.EmitJson(BuildJsonPayload(inputPath, minLength, maxCount, settings.Unicode, hits, settings.Output));
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

    private static async Task<string?> ResolveInputAsync(string? input, string? ftpPath, bool running, bool json) {
        if (ResolveInputOverride != null) {
            return await ResolveInputOverride(input, ftpPath, running);
        }

        if (!string.IsNullOrWhiteSpace(input)) {
            if (!File.Exists(input)) {
                WriteFailure(
                    json,
                    "XEX strings input not found",
                    "Input file not found.",
                    "XEX_STRINGS_INPUT_NOT_FOUND");
                return null;
            }
            return input;
        }

        if (!string.IsNullOrWhiteSpace(ftpPath)) {
            return await GhidraInputHelpers.DownloadViaFtpAsync(ftpPath, quiet: json);
        }

        if (running) {
            string? path = await GhidraInputHelpers.ResolveRunningFtpPathAsync(quiet: json);
            if (string.IsNullOrWhiteSpace(path)) {
                WriteFailure(
                    json,
                    "XEX strings running-title resolution failed",
                    "Unable to resolve the running XEX to an FTP path.",
                    "XEX_STRINGS_RUNNING_RESOLUTION_FAILED");
                return null;
            }
            return await GhidraInputHelpers.DownloadViaFtpAsync(path, quiet: json);
        }

        WriteFailure(
            json,
            "XEX strings source validation failed",
            "Provide --file, --ftp-path, or --running.",
            "XEX_STRINGS_SOURCE_INVALID");
        return null;
    }

    private static void WriteFailure(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                new[] { "Check the XEX input selector and output path, then retry." }));
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }

    internal static bool TryValidateSourceSelectors(Settings settings, out string? error) {
        bool fileSelected = !string.IsNullOrWhiteSpace(settings.Input) || !string.IsNullOrWhiteSpace(settings.LegacyInput);
        bool ftpSelected = !string.IsNullOrWhiteSpace(settings.FtpPath);
        bool runningSelected = settings.Running;

        int sourceCount = (fileSelected ? 1 : 0) + (ftpSelected ? 1 : 0) + (runningSelected ? 1 : 0);
        if (sourceCount == 0) {
            error = "Provide --file, --ftp-path, or --running.";
            return false;
        }

        if (sourceCount > 1) {
            error = "Only one of --file/--in, --ftp-path, or --running may be specified.";
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryValidateFileSize(string path, out string? error) {
        try {
            FileInfo info = new FileInfo(path);
            return TryValidateFileSize(info.Exists, info.Exists ? info.Length : 0, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or PathTooLongException) {
            error = "Failed to inspect XEX input file.";
            return false;
        }
    }

    internal static bool TryValidateFileSize(bool exists, long length, out string? error) {
        if (!exists) {
            error = "Input file not found.";
            return false;
        }

        if (length > MaxScanBytes) {
            error = $"Input file is too large to scan safely ({length:N0} bytes).";
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryWriteOutput(string outputPath, IReadOnlyList<StringHit> hits, out string? error) {
        try {
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            using StreamWriter writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            foreach (StringHit hit in hits) {
                writer.WriteLine($"{hit.Offset:X8}\t{hit.Kind}\t{hit.Text}");
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string leafName = XexInfoCommand.GetDisplayFileName(outputPath);
            error = string.IsNullOrWhiteSpace(leafName)
                ? "Failed to write output. Check the output path, parent directory, and permissions."
                : $"Failed to write output file '{leafName}'. Check the output path, parent directory, and permissions.";
            return false;
        }
    }

    internal readonly record struct StringHit(int Offset, string Kind, string Text, int Length);

    internal sealed record XexStringScanResult(IReadOnlyList<StringHit> Hits);

    internal sealed record XexStringsJsonEnvelope(
        string File,
        string? Output,
        int MinLength,
        int Limit,
        bool Unicode,
        int Count,
        IReadOnlyList<XexStringsJsonEntry> Strings);

    internal sealed record XexStringsJsonEntry(
        string Offset,
        string Kind,
        int Length,
        string Text);

    internal static XexStringsJsonEnvelope BuildJsonPayload(
        string inputPath,
        int minLength,
        int limit,
        bool unicode,
        IReadOnlyList<StringHit> hits,
        string? outputPath = null) {
        return new XexStringsJsonEnvelope(
            XexInfoCommand.GetDisplayFileName(inputPath),
            string.IsNullOrWhiteSpace(outputPath) ? null : XexInfoCommand.GetDisplayFileName(outputPath),
            minLength,
            limit,
            unicode,
            hits.Count,
            hits.Select(h => new XexStringsJsonEntry(
                $"0x{h.Offset:X8}",
                h.Kind,
                h.Length,
                h.Text)).ToArray());
    }

    internal static class XexStringScanner {
        public static XexStringScanResult Scan(byte[] data, int minLength, int limit, bool includeUnicode) {
            List<StringHit> hits = new List<StringHit>();
            hits.AddRange(ExtractAscii(data, minLength));
            if (includeUnicode) {
                hits.AddRange(ExtractUtf16(data, minLength, littleEndian: true));
                hits.AddRange(ExtractUtf16(data, minLength, littleEndian: false));
            }

            IReadOnlyList<StringHit> ordered = hits
                .OrderBy(h => h.Offset)
                .ThenBy(h => h.Kind, StringComparer.Ordinal)
                .ThenBy(h => h.Text, StringComparer.Ordinal)
                .Take(Math.Max(1, limit))
                .ToArray();

            return new XexStringScanResult(ordered);
        }

        private static List<StringHit> ExtractAscii(byte[] data, int minLength) {
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
                        hits.Add(new StringHit(start, "ascii", Encoding.ASCII.GetString(data, start, length), length));
                    }
                    start = -1;
                }
            }

            if (start != -1) {
                int length = data.Length - start;
                if (length >= minLength) {
                    hits.Add(new StringHit(start, "ascii", Encoding.ASCII.GetString(data, start, length), length));
                }
            }

            return hits;
        }

        private static List<StringHit> ExtractUtf16(byte[] data, int minLength, bool littleEndian) {
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
                        hits.Add(new StringHit(start, littleEndian ? "utf16le" : "utf16be", text, lengthChars));
                    }
                    start = -1;
                }
            }

            if (start != -1) {
                int lengthBytes = data.Length - start;
                int lengthChars = lengthBytes / 2;
                if (lengthChars >= minLength) {
                    string text = DecodeUtf16(data, start, lengthBytes, littleEndian);
                    hits.Add(new StringHit(start, littleEndian ? "utf16le" : "utf16be", text, lengthChars));
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
    }
}

