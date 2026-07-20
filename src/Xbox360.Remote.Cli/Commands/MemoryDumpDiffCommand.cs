using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class MemoryDumpDiffCommand : Command<MemoryDumpDiffCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--left <FILE>")]
        [LocalizedDescription("Left memory dump file to compare.")]
        public string? Left { get; init; }

        [CommandOption("--right <FILE>")]
        [LocalizedDescription("Right memory dump file to compare.")]
        public string? Right { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Left) || string.IsNullOrWhiteSpace(settings.Right))
            return WriteError(settings.Json, "Memory dump compare failed", "Provide --left <FILE> and --right <FILE>.", "MEM_DUMP_DIFF_INVALID_ARGS", "Save two binary memory dumps and try again.");

        if (!MemorySessionStore.TryResolveCaptureReference(settings.Left, out MemorySessionPathResolution leftResolution, out string leftRefIssue))
            return WriteError(settings.Json, "Memory dump compare failed", leftRefIssue, "MEM_DUMP_DIFF_INPUT_INVALID", "Verify the left file exists, or use @<session>:<label> for a saved session capture.");

        if (!MemorySessionStore.TryResolveCaptureReference(settings.Right, out MemorySessionPathResolution rightResolution, out string rightRefIssue))
            return WriteError(settings.Json, "Memory dump compare failed", rightRefIssue, "MEM_DUMP_DIFF_INPUT_INVALID", "Verify the right file exists, or use @<session>:<label> for a saved session capture.");

        if (!MemoryDumpDiffHelpers.TryLoadDump(leftResolution.FullPath, out MemoryDumpDiffHelpers.MemoryDumpFile left, out string leftIssue))
            return WriteError(settings.Json, "Memory dump compare failed", leftIssue, "MEM_DUMP_DIFF_INPUT_INVALID", "Verify the left file exists and is a saved memory dump.");

        if (!MemoryDumpDiffHelpers.TryLoadDump(rightResolution.FullPath, out MemoryDumpDiffHelpers.MemoryDumpFile right, out string rightIssue))
            return WriteError(settings.Json, "Memory dump compare failed", rightIssue, "MEM_DUMP_DIFF_INPUT_INVALID", "Verify the right file exists and is a saved memory dump.");

        left = left with { Name = leftResolution.DisplayPath };
        right = right with { Name = rightResolution.DisplayPath };
        MemoryDumpDiffHelpers.MemoryDumpDiffResult diff = MemoryDumpDiffHelpers.Compare(left, right);
        if (settings.Json) {
            CliOutput.EmitJson(MemoryDumpDiffHelpers.ToJsonPayload(diff));
            return 0;
        }

        RenderHuman(diff);
        return 0;
    }

    private static void RenderHuman(MemoryDumpDiffHelpers.MemoryDumpDiffResult diff) {
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Dump Compare[/]").RuleStyle("grey"));

        Table summary = CliOutput.CreateTable();
        summary.AddColumn("[grey]Field[/]");
        summary.AddColumn("[grey]Left[/]");
        summary.AddColumn("[grey]Right[/]");
        summary.AddColumn("[grey]Notes[/]");
        summary.AddRow("File", Markup.Escape(diff.Left.Name), Markup.Escape(diff.Right.Name), diff.Identical ? "identical" : "compared");
        summary.AddRow("Size", FormatSize(diff.Left.Size), FormatSize(diff.Right.Size), diff.SizeMismatch ? "size mismatch" : "same size");
        summary.AddRow("Differing bytes", diff.DifferingBytes.ToString(CultureInfo.InvariantCulture), diff.DifferingBytes.ToString(CultureInfo.InvariantCulture), diff.DifferingBytes == 0 ? "none" : "byte-level compare");

        if (diff.FirstDifference is null) {
            summary.AddRow("First difference", "[grey]-[/]", "[grey]-[/]", "none");
        }
        else {
            summary.AddRow("First difference", $"[cyan]{diff.FirstDifference.OffsetText}[/]", $"[cyan]{diff.FirstDifference.OffsetText}[/]", "offset");
            summary.AddRow("Byte at offset", FormatByteOrEof(diff.FirstDifference.LeftByte), FormatByteOrEof(diff.FirstDifference.RightByte), "left vs right");
        }

        AnsiConsole.Write(summary);
    }

    private static string FormatSize(long size) {
        return $"{size.ToString(CultureInfo.InvariantCulture)} bytes";
    }

    private static string FormatByteOrEof(byte? value) {
        return value.HasValue ? $"[cyan]{FormatByte(value.Value)}[/]" : "[grey]EOF[/]";
    }

    private static string FormatByte(byte value) {
        return $"0x{value:X2}";
    }

    private static int WriteError(bool json, string title, string message, string code, string nextStep) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] { nextStep }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }
}

internal static class MemoryDumpDiffHelpers {
    internal static bool TryLoadDump(string path, out MemoryDumpFile dump, out string issue) {
        dump = new MemoryDumpFile(string.Empty, Array.Empty<byte>());
        issue = string.Empty;

        if (string.IsNullOrWhiteSpace(path)) {
            issue = "File path is required.";
            return false;
        }

        string displayName = GetDisplayName(path);
        string fullPath;
        try {
            fullPath = Path.GetFullPath(path);
        }
        catch {
            issue = BuildReadFailureMessage(displayName);
            return false;
        }

        if (!File.Exists(fullPath)) {
            issue = $"File not found: {displayName}";
            return false;
        }

        try {
            dump = new MemoryDumpFile(displayName, File.ReadAllBytes(fullPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            issue = BuildReadFailureMessage(displayName);
            return false;
        }
    }

    internal static MemoryDumpDiffResult Compare(MemoryDumpFile left, MemoryDumpFile right) {
        byte[] leftBytes = left.Bytes;
        byte[] rightBytes = right.Bytes;
        int overlapLength = Math.Min(leftBytes.Length, rightBytes.Length);
        long differingBytes = 0;
        MemoryDifference? firstDifference = null;

        for (int i = 0; i < overlapLength; i++) {
            if (leftBytes[i] == rightBytes[i])
                continue;

            differingBytes++;
            if (firstDifference is null)
                firstDifference = new MemoryDifference(i, leftBytes[i], rightBytes[i]);
        }

        long sizeDelta = Math.Abs((long) leftBytes.Length - rightBytes.Length);
        differingBytes += sizeDelta;

        if (firstDifference is null && sizeDelta > 0) {
            byte? leftByte = leftBytes.Length > overlapLength ? leftBytes[overlapLength] : null;
            byte? rightByte = rightBytes.Length > overlapLength ? rightBytes[overlapLength] : null;
            firstDifference = new MemoryDifference(overlapLength, leftByte, rightByte);
        }

        return new MemoryDumpDiffResult(left, right, differingBytes, firstDifference);
    }

    internal static object ToJsonPayload(MemoryDumpDiffResult diff) {
        return new {
            Left = new {
                Name = diff.Left.Name,
                Size = diff.Left.Size
            },
            Right = new {
                Name = diff.Right.Name,
                Size = diff.Right.Size
            },
            Summary = new {
                Identical = diff.Identical,
                SizeMismatch = diff.SizeMismatch,
                DifferingBytes = diff.DifferingBytes,
                FirstDifference = diff.FirstDifference is null
                    ? null
                    : new {
                        Offset = diff.FirstDifference.Offset,
                        LeftByte = FormatByteOrNull(diff.FirstDifference.LeftByte),
                        RightByte = FormatByteOrNull(diff.FirstDifference.RightByte)
                    }
            }
        };
    }

    private static string? FormatByteOrNull(byte? value) {
        return value.HasValue ? $"0x{value.Value:X2}" : null;
    }

    private static string GetDisplayName(string path) {
        string fileName = Path.GetFileName(path.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? "input" : fileName;
    }

    private static string BuildReadFailureMessage(string displayName) {
        return string.IsNullOrWhiteSpace(displayName)
            ? "Failed to read memory dump."
            : $"Failed to read memory dump '{displayName}'.";
    }

    internal sealed record MemoryDumpFile(string Name, byte[] Bytes) {
        public long Size => Bytes.LongLength;
    }

    internal sealed record MemoryDumpDiffResult(MemoryDumpFile Left, MemoryDumpFile Right, long DifferingBytes, MemoryDifference? FirstDifference) {
        public bool Identical => DifferingBytes == 0;

        public bool SizeMismatch => Left.Size != Right.Size;
    }

    internal sealed record MemoryDifference(long Offset, byte? LeftByte, byte? RightByte) {
        public string OffsetText => $"0x{Offset:X8}";
    }
}
