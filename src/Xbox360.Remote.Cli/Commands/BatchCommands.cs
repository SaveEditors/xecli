using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public sealed class BatchRunCommand : AsyncCommand<BatchRunCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<FILE>")]
        [LocalizedDescription("File containing one CLI command per line.")]
        public string File { get; init; } = string.Empty;

        [CommandOption("--dry-run")]
        [LocalizedDescription("Validate and print the batch without running any commands.")]
        public bool DryRun { get; init; }

        [CommandOption("--allow-live")]
        [LocalizedDescription("Allow live or destructive commands instead of blocking them.")]
        public bool AllowLive { get; init; }

        [CommandOption("--transcript-out")]
        [LocalizedDescription("Write a structured redacted transcript artifact to the given file.")]
        public string? TranscriptOut { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.File)) {
            AnsiConsole.MarkupLine("[red]Provide a batch file with `rgh script run <file>`.[/]");
            return 1;
        }

        string batchPath;
        try {
            batchPath = Path.GetFullPath(settings.File);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            OperationFeedback.WriteFailure("Batch run failed", CommandLogRedactor.RedactFreeText(ex.Message));
            return 1;
        }

        if (!File.Exists(batchPath)) {
            OperationFeedback.WriteFailure(
                "Batch run failed",
                $"Batch file not found: {Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.File) ?? settings.File)}");
            return 1;
        }

        BatchRunExecutor executor = new BatchRunExecutor(
            batchPath,
            Path.GetDirectoryName(batchPath) ?? Environment.CurrentDirectory,
            settings.DryRun,
            settings.AllowLive,
            settings.TranscriptOut);

        BatchRunResult result = await executor.RunAsync();
        return result.ExitCode;
    }
}

public sealed class BatchReplayCommand : AsyncCommand<BatchReplayCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<TRANSCRIPT>")]
        [LocalizedDescription("Structured batch transcript artifact to review offline.")]
        public string Transcript { get; init; } = string.Empty;

        [CommandOption("--dry-run")]
        [LocalizedDescription("Review the transcript offline without executing child commands.")]
        public bool DryRun { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Transcript)) {
            AnsiConsole.MarkupLine("[red]Provide a transcript file with `rgh script replay <transcript> --dry-run`.[/]");
            return Task.FromResult(1);
        }

        if (!settings.DryRun) {
            AnsiConsole.MarkupLine("[red]`script replay` only supports `--dry-run`.[/]");
            return Task.FromResult(1);
        }

        string transcriptPath;
        try {
            transcriptPath = Path.GetFullPath(settings.Transcript);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            OperationFeedback.WriteFailure("Transcript replay failed", CommandLogRedactor.RedactFreeText(ex.Message));
            return Task.FromResult(1);
        }

        BatchTranscriptDocument? transcript;
        if (!BatchTranscriptStore.TryRead(transcriptPath, out transcript, out string readError)) {
            OperationFeedback.WriteFailure("Transcript replay failed", readError);
            return Task.FromResult(1);
        }

        ArgumentNullException.ThrowIfNull(transcript);
        BatchTranscriptRenderer.WriteReplayHeader(transcriptPath, transcript);

        int ok = 0;
        int failed = 0;
        int blocked = 0;

        try {
            foreach (BatchTranscriptEntry entry in transcript.Entries) {
                string classification = BatchTranscriptRenderer.GetReplayClassification(entry.Status);
                switch (classification) {
                    case "ok":
                        ok++;
                        break;
                    case "failed":
                        failed++;
                        break;
                    case "blocked":
                        blocked++;
                        break;
                }

                BatchTranscriptRenderer.WriteReplayEntry(entry, classification);
            }
        }
        catch (InvalidOperationException ex) {
            OperationFeedback.WriteFailure("Transcript replay failed", CommandLogRedactor.RedactFreeText(ex.Message));
            return Task.FromResult(1);
        }

        if (transcript.Entries.Count == 0)
            AnsiConsole.MarkupLine("[grey]No transcript entries found.[/]");

        AnsiConsole.MarkupLine(
            $"[grey]Summary:[/] [springgreen3_1]{ok} ok[/], [yellow]{failed} failed[/], [gold1]{blocked} blocked[/], [white]{transcript.Entries.Count} reviewed[/]");
        return Task.FromResult(0);
    }
}

internal sealed record BatchRunResult(int ExitCode, int ExecutedCount, int SucceededCount, int FailedCount, int BlockedCount);

internal sealed record BatchTranscriptDocument {
    public string Schema { get; init; } = "xecli.batch-transcript.v1";
    public DateTimeOffset CreatedUtc { get; init; }
    public string Mode { get; init; } = string.Empty;
    public bool AllowLive { get; init; }
    public IReadOnlyList<BatchTranscriptEntry> Entries { get; init; } = [];
}

internal sealed record BatchTranscriptEntry {
    public int LineNumber { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public int? ExitCode { get; init; }
    public string[]? Stdout { get; init; }
    public string[]? Stderr { get; init; }
}

internal sealed class BatchRunExecutor {
    private readonly string batchPath;
    private readonly string workingDirectory;
    private readonly bool dryRun;
    private readonly bool allowLive;
    private readonly string? transcriptOutPath;

    public BatchRunExecutor(string batchPath, string workingDirectory, bool dryRun, bool allowLive, string? transcriptOutPath) {
        this.batchPath = batchPath;
        this.workingDirectory = workingDirectory;
        this.dryRun = dryRun;
        this.allowLive = allowLive;
        this.transcriptOutPath = transcriptOutPath;
    }

    public async Task<BatchRunResult> RunAsync() {
        string[] lines;
        try {
            lines = File.ReadAllLines(batchPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            OperationFeedback.WriteFailure("Batch run failed", CommandLogRedactor.RedactFreeText(ex.Message));
            return new BatchRunResult(1, 0, 0, 1, 0);
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Batch Run[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]File:[/] [white]{Markup.Escape(XexInfoCommand.GetDisplayFileName(batchPath) ?? batchPath)}[/]");
        AnsiConsole.MarkupLine($"[grey]Mode:[/] [white]{(dryRun ? "dry-run" : "run")}[/]");
        AnsiConsole.MarkupLine($"[grey]Guard:[/] [white]{(allowLive ? "allow-live enabled" : "live and destructive commands blocked")}[/]");

        int executed = 0;
        int succeeded = 0;
        int failed = 0;
        int blocked = 0;
        int exitCode = 0;
        List<BatchTranscriptEntry> transcriptEntries = [];

        for (int index = 0; index < lines.Length; index++) {
            string rawLine = lines[index];
            string trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || IsCommentLine(trimmed))
                continue;

            IReadOnlyList<string> tokens = CommandLineTokenizer.StripCliPrefix(CommandLineTokenizer.Split(rawLine));
            if (tokens.Count == 0)
                continue;

            string redactedCommand = CommandLogRedactor.RedactCommandLine(tokens);
            int lineNumber = index + 1;

            if (!BatchCommandSafety.IsAllowed(tokens, allowLive, out string safetyReason)) {
                blocked++;
                WriteTranscriptLine(lineNumber, "blocked", redactedCommand, safetyReason);
                transcriptEntries.Add(BuildTranscriptEntry(lineNumber, "blocked", redactedCommand, safetyReason, null, null, null));
                exitCode = 1;
                break;
            }

            if (dryRun) {
                executed++;
                WriteTranscriptLine(lineNumber, "dry-run", redactedCommand, "not executed");
                transcriptEntries.Add(BuildTranscriptEntry(lineNumber, "dry-run", redactedCommand, "not executed", null, null, null));
                continue;
            }

            executed++;
            BatchCommandExecution execution = await RunChildCommandAsync(tokens);
            if (execution.ExitCode == 0) {
                succeeded++;
                WriteTranscriptLine(lineNumber, "ok", redactedCommand, $"exit {execution.ExitCode}");
                WriteCapturedOutput(execution.Stdout, execution.Stderr);
                transcriptEntries.Add(BuildTranscriptEntry(
                    lineNumber,
                    "ok",
                    redactedCommand,
                    $"exit {execution.ExitCode}",
                    execution.ExitCode,
                    SplitRedactedLines(execution.Stdout),
                    SplitRedactedLines(execution.Stderr)));
                continue;
            }

            failed++;
            WriteTranscriptLine(lineNumber, "failed", redactedCommand, $"exit {execution.ExitCode}");
            WriteCapturedOutput(execution.Stdout, execution.Stderr);
            transcriptEntries.Add(BuildTranscriptEntry(
                lineNumber,
                "failed",
                redactedCommand,
                $"exit {execution.ExitCode}",
                execution.ExitCode,
                SplitRedactedLines(execution.Stdout),
                SplitRedactedLines(execution.Stderr)));
            exitCode = execution.ExitCode;
            break;
        }

        if (executed == 0 && blocked == 0)
            AnsiConsole.MarkupLine("[grey]No executable commands found.[/]");

        if (!TryWriteTranscript(transcriptEntries, out string transcriptError))
            exitCode = exitCode == 0 ? 1 : exitCode;

        AnsiConsole.MarkupLine(
            $"[grey]Summary:[/] [springgreen3_1]{succeeded} ok[/], [yellow]{failed} failed[/], [gold1]{blocked} blocked[/], [white]{executed} executed[/]");
        if (!string.IsNullOrWhiteSpace(transcriptError))
            OperationFeedback.WriteFailure("Transcript export failed", transcriptError);
        return new BatchRunResult(exitCode, executed, succeeded, failed, blocked);
    }

    private bool TryWriteTranscript(IReadOnlyList<BatchTranscriptEntry> entries, out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(transcriptOutPath))
            return true;

        try {
            string transcriptPath = Path.GetFullPath(transcriptOutPath);
            BatchTranscriptDocument document = new() {
                CreatedUtc = DateTimeOffset.UtcNow,
                Mode = dryRun ? "dry-run" : "run",
                AllowLive = allowLive,
                Entries = entries
            };

            BatchTranscriptStore.Write(transcriptPath, document);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            error = CommandLogRedactor.RedactFreeText(ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            error = CommandLogRedactor.RedactFreeText(ex.Message);
            return false;
        }
    }

    private async Task<BatchCommandExecution> RunChildCommandAsync(IReadOnlyList<string> tokens) {
        string? processPath = Environment.ProcessPath;
        string exePath = !string.IsNullOrWhiteSpace(processPath)
            ? processPath
            : Path.Combine(AppContext.BaseDirectory, "rgh.exe");

        ProcessStartInfo psi = new ProcessStartInfo(exePath) {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (string token in tokens)
            psi.ArgumentList.Add(token);

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start batch command process.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new BatchCommandExecution(await stdoutTask, await stderrTask, process.ExitCode);
    }

    private static bool IsCommentLine(string trimmed) {
        return trimmed.StartsWith("#", StringComparison.Ordinal) ||
               trimmed.StartsWith(";", StringComparison.Ordinal) ||
               trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    private static void WriteTranscriptLine(int lineNumber, string status, string command, string detail) {
        Console.WriteLine($"{lineNumber}: [{status}] {command}");
        if (!string.IsNullOrWhiteSpace(detail))
            Console.WriteLine($"    {detail}");
    }

    private static void WriteCapturedOutput(string stdout, string stderr) {
        string redactedStdout = RedactCapturedText(stdout);
        string redactedStderr = RedactCapturedText(stderr);

        if (!string.IsNullOrWhiteSpace(redactedStdout)) {
            Console.WriteLine("    stdout:");
            foreach (string line in SplitLines(redactedStdout))
                Console.WriteLine("      " + line);
        }

        if (!string.IsNullOrWhiteSpace(redactedStderr)) {
            Console.WriteLine("    stderr:");
            foreach (string line in SplitLines(redactedStderr))
                Console.WriteLine("      " + line);
        }
    }

    private static string RedactCapturedText(string text) {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : CommandLogRedactor.RedactFreeText(text.TrimEnd());
    }

    private static string[] SplitRedactedLines(string capturedText) {
        string redacted = RedactCapturedText(capturedText);
        if (string.IsNullOrWhiteSpace(redacted))
            return [];

        List<string> lines = [];
        using StringReader reader = new StringReader(redacted);
        string? line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);

        return lines.ToArray();
    }

    private static BatchTranscriptEntry BuildTranscriptEntry(
        int lineNumber,
        string status,
        string command,
        string? detail,
        int? exitCode,
        string[]? stdout,
        string[]? stderr) {
        return new BatchTranscriptEntry {
            LineNumber = lineNumber,
            Status = status,
            Command = command,
            Detail = string.IsNullOrWhiteSpace(detail) ? null : CommandLogRedactor.RedactFreeText(detail),
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }

    private static IEnumerable<string> SplitLines(string text) {
        using StringReader reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    private sealed record BatchCommandExecution(string Stdout, string Stderr, int ExitCode);
}

internal static class BatchTranscriptStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static void Write(string path, BatchTranscriptDocument document) {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static bool TryRead(string path, out BatchTranscriptDocument? document, out string error) {
        document = null;
        error = string.Empty;

        try {
            string json = File.ReadAllText(path, Encoding.UTF8);
            document = JsonSerializer.Deserialize<BatchTranscriptDocument>(json, JsonOptions);
            if (document == null) {
                error = "Transcript file is malformed.";
                return false;
            }

            if (!string.Equals(document.Schema, "xecli.batch-transcript.v1", StringComparison.OrdinalIgnoreCase) ||
                document.Entries == null) {
                error = "Transcript file is malformed.";
                return false;
            }

            foreach (BatchTranscriptEntry entry in document.Entries) {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Status) || string.IsNullOrWhiteSpace(entry.Command)) {
                    error = "Transcript file is malformed.";
                    document = null;
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) {
            error = CommandLogRedactor.RedactFreeText(ex.Message);
            return false;
        }
    }
}

internal static class BatchTranscriptRenderer {
    public static void WriteReplayHeader(string transcriptPath, BatchTranscriptDocument transcript) {
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Batch Replay[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]File:[/] [white]{Markup.Escape(Path.GetFileName(transcriptPath))}[/]");
        AnsiConsole.MarkupLine($"[grey]Mode:[/] [white]{transcript.Mode}[/]");
        AnsiConsole.MarkupLine($"[grey]Source mode:[/] [white]{(transcript.AllowLive ? "allow-live" : "restricted")}[/]");
    }

    public static void WriteReplayEntry(BatchTranscriptEntry entry, string classification) {
        string detail = string.Empty;
        if (classification == "ok" && entry.Status.Equals("dry-run", StringComparison.OrdinalIgnoreCase))
            detail = "recorded dry-run";
        else if (!string.IsNullOrWhiteSpace(entry.Detail))
            detail = CommandLogRedactor.RedactFreeText(entry.Detail);

        Console.WriteLine($"{entry.LineNumber}: [{classification}] {CommandLogRedactor.RedactFreeText(entry.Command)}");
        if (!string.IsNullOrWhiteSpace(detail))
            Console.WriteLine($"    {detail}");

        WriteLines("stdout", entry.Stdout);
        WriteLines("stderr", entry.Stderr);
    }

    public static string GetReplayClassification(string status) {
        if (status.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("dry-run", StringComparison.OrdinalIgnoreCase))
            return "ok";

        if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return "failed";

        if (status.Equals("blocked", StringComparison.OrdinalIgnoreCase))
            return "blocked";

        throw new InvalidOperationException("Transcript file is malformed.");
    }

    private static void WriteLines(string label, string[]? lines) {
        if (lines == null || lines.Length == 0)
            return;

        Console.WriteLine($"    {label}:");
        foreach (string line in lines)
            Console.WriteLine("      " + CommandLogRedactor.RedactFreeText(line));
    }
}

internal static class BatchCommandSafety {
    public static bool IsAllowed(IReadOnlyList<string> tokens, bool allowLive, out string reason) {
        reason = string.Empty;
        if (tokens.Count == 0) {
            reason = "Empty command line.";
            return false;
        }

        string root = tokens[0];
        if (root.Equals("batch", StringComparison.OrdinalIgnoreCase) || root.Equals("script", StringComparison.OrdinalIgnoreCase)) {
            reason = "Nested batch execution is not supported.";
            return false;
        }

        if (allowLive)
            return true;

        if (IsOfflineSafe(tokens))
            return true;

        reason = "This command is live or destructive. Re-run with --allow-live if you trust the script.";
        return false;
    }

    private static bool IsOfflineSafe(IReadOnlyList<string> tokens) {
        string root = tokens[0];
        if (root.Equals("completion", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("diagnostics", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("health", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("log", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("language", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("target", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("release-notes", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("ida", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("ghidra", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("mem-bookmarks", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("bookmarks", StringComparison.OrdinalIgnoreCase) ||
            root.Equals("memmarks", StringComparison.OrdinalIgnoreCase))
            return true;

        if (root.Equals("release", StringComparison.OrdinalIgnoreCase))
            return tokens.Count > 1 && tokens[1].Equals("check", StringComparison.OrdinalIgnoreCase);

        if (root.Equals("xtaf-cli", StringComparison.OrdinalIgnoreCase))
            return tokens.Count > 1 &&
                   (tokens[1].Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase));

        if (root.Equals("report", StringComparison.OrdinalIgnoreCase))
            return HasReportDiffInputs(tokens);

        if (root.Equals("title", StringComparison.OrdinalIgnoreCase))
            return IsOfflineTitleLookup(tokens);

        return false;
    }

    private static bool HasReportDiffInputs(IReadOnlyList<string> tokens) {
        bool hasLeft = false;
        bool hasRight = false;

        for (int i = 1; i < tokens.Count; i++) {
            string token = tokens[i];
            if (token.Equals("--left", StringComparison.OrdinalIgnoreCase) || token.StartsWith("--left=", StringComparison.OrdinalIgnoreCase))
                hasLeft = true;
            if (token.Equals("--right", StringComparison.OrdinalIgnoreCase) || token.StartsWith("--right=", StringComparison.OrdinalIgnoreCase))
                hasRight = true;
        }

        return hasLeft && hasRight;
    }

    private static bool IsOfflineTitleLookup(IReadOnlyList<string> tokens) {
        if (tokens.Count == 1)
            return false;

        foreach (string token in tokens.Skip(1)) {
            if (token.Equals("--active", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return !string.Equals(tokens[1], "active", StringComparison.OrdinalIgnoreCase);
    }
}
