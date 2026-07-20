using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class TrainerApplyEntryResult {
    public int Index { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public bool LittleEndian { get; init; }

    public bool Enabled { get; init; }

    public string? Note { get; init; }

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? BytesHex { get; set; }
}

internal sealed record TrainerApplyPlan(
    int Index,
    string Name,
    uint Address,
    string Type,
    string Value,
    bool LittleEndian,
    string? Note,
    byte[] Bytes);

internal sealed class TrainerApplyJsonPayload {
    public int SchemaVersion { get; init; } = 1;

    public string File { get; init; } = string.Empty;

    public bool DryRun { get; init; }

    public int TotalEntries { get; init; }

    public int EnabledEntries { get; init; }

    public int Succeeded { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<TrainerApplyEntryResult> Entries { get; init; } = [];
}

public sealed class TrainerApplyCommand : AsyncCommand<TrainerApplyCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Trainer JSON file to apply.")]
        public string? File { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Validate the trainer file and show what would be written without connecting.")]
        public bool DryRun { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.File)) {
            WriteError(settings.Json, "Trainer apply validation failed", "Provide --file <trainer.json>.", "TRAINER_FILE_REQUIRED");
            return 1;
        }

        string filePath;
        try {
            filePath = Path.GetFullPath(settings.File);
            if (!File.Exists(filePath)) {
                WriteError(settings.Json, "Trainer apply file failed", "Trainer file not found.", "TRAINER_FILE_NOT_FOUND");
                return 1;
            }
        }
        catch (Exception ex) when (IsBadTrainerPathException(ex)) {
            WriteError(settings.Json, "Trainer apply file failed", $"Invalid trainer file path: {GetFailureMessage(ex)}", "TRAINER_FILE_INVALID_PATH");
            return 1;
        }

        if (!TrainerStore.TryLoad(filePath, out TrainerStoreData store, out string loadError)) {
            WriteError(settings.Json, "Trainer apply parse failed", loadError, "TRAINER_FILE_INVALID");
            return 1;
        }

        List<TrainerApplyEntryResult> results = [];
        List<(TrainerApplyEntryResult Result, TrainerApplyPlan Plan)> livePlans = [];
        bool hasEnabled = false;

        for (int index = 0; index < store.Entries.Count; index++) {
            TrainerEntryRecord? entry = store.Entries[index];
            TrainerApplyEntryResult result = new() {
                Index = index + 1,
                Name = entry?.Name ?? string.Empty,
                Address = entry?.Address ?? string.Empty,
                Type = entry?.Type ?? string.Empty,
                Value = entry?.Value ?? string.Empty,
                LittleEndian = entry?.LittleEndian ?? false,
                Enabled = entry?.Enabled ?? true,
                Note = entry?.Note
            };

            results.Add(result);

            if (entry == null) {
                result.Status = "fail";
                result.Message = "Entry is null.";
                continue;
            }

            if (!(entry.Enabled ?? true)) {
                result.Status = "skipped";
                result.Message = "disabled";
                continue;
            }

            hasEnabled = true;

            if (!TryBuildPlan(entry, index + 1, out TrainerApplyPlan plan, out string planError)) {
                result.Status = "fail";
                result.Message = planError;
                continue;
            }

            result.Status = "ok";
            result.BytesHex = Convert.ToHexString(plan.Bytes);
            result.Message = settings.DryRun
                ? $"Would write {plan.Bytes.Length} byte(s)."
                : $"Ready to write {plan.Bytes.Length} byte(s).";
            livePlans.Add((result, plan));
        }

        if (!hasEnabled) {
            return WriteSummary(settings, filePath, results, settings.DryRun, "Nothing to apply.");
        }

        if (settings.DryRun) {
            return WriteSummary(settings, filePath, results, dryRun: true, "Dry run complete.");
        }

        if (livePlans.Count == 0) {
            return WriteSummary(settings, filePath, results, dryRun: false, "No enabled entries were valid.");
        }

        bool connectionFailed = false;
        string? connectionError = null;

        try {
            return await CliHelpers.WithClientAsync(settings, async client => {
                using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
                bool stoppedOnLiveFailure = false;
                TrainerApplyEntryResult? failedResult = null;

                foreach ((TrainerApplyEntryResult result, TrainerApplyPlan plan) in livePlans) {
                    if (stoppedOnLiveFailure) {
                        result.Status = "skipped";
                        result.Message = "Not attempted after a previous write failure.";
                        continue;
                    }

                    try {
                        await client.WriteMemoryVerifiedAsync(plan.Address, plan.Bytes, cts.Token);
                        result.Status = "ok";
                        result.Message = $"Wrote and verified 0x{plan.Address:X8} ({plan.Bytes.Length} byte(s)).";
                    }
                    catch (Exception ex) {
                        result.Status = "fail";
                        result.Message = GetFailureMessage(ex);
                        failedResult = result;
                        stoppedOnLiveFailure = true;
                    }
                }

                if (stoppedOnLiveFailure && failedResult != null) {
                    return WriteSummary(settings, filePath, results, dryRun: false, $"Partial apply stopped after entry {failedResult.Index}: {failedResult.Message}");
                }

                return WriteSummary(settings, filePath, results, dryRun: false, "Trainer apply complete.");
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ConnectionFailureModel.IsXbdmConnectionFailure(ex)) {
            connectionFailed = true;
            connectionError = GetFailureMessage(ex);
        }

        if (connectionFailed) {
            foreach ((TrainerApplyEntryResult result, _) in livePlans) {
                if (result.Status == "ok") {
                    result.Status = "fail";
                    result.Message = connectionError ?? "XBDM connection failed.";
                }
            }

            return WriteSummary(settings, filePath, results, dryRun: false, connectionError ?? "XBDM connection failed.");
        }

        return 1;
    }

    private static bool TryBuildPlan(TrainerEntryRecord entry, int index, out TrainerApplyPlan plan, out string error) {
        plan = default!;
        error = string.Empty;

        string name = entry.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) {
            error = $"Entry {index} is missing Name.";
            return false;
        }

        string addressText = entry.Address?.Trim() ?? string.Empty;
        if (!CliHelpers.TryParseUInt32(addressText, out uint address)) {
            error = $"Entry {index} ({name}) has an invalid Address. Use a flat hex or decimal string.";
            return false;
        }

        string type = entry.Type?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type)) {
            error = $"Entry {index} ({name}) is missing Type.";
            return false;
        }

        string value = entry.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            error = $"Entry {index} ({name}) is missing Value.";
            return false;
        }

        try {
            byte[] bytes = MemoryValueCodec.BuildBytes(type, value, entry.LittleEndian ?? false);
            plan = new TrainerApplyPlan(
                index,
                name,
                address,
                MemoryValueCodec.NormalizeType(type),
                value,
                entry.LittleEndian ?? false,
                entry.Note,
                bytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException) {
            error = $"Entry {index} ({name}) has an invalid typed value for --type {type}: {value}.";
            return false;
        }
    }

    private static int WriteSummary(Settings settings, string filePath, IReadOnlyList<TrainerApplyEntryResult> results, bool dryRun, string message) {
        int succeeded = results.Count(result => result.Status == "ok");
        int failed = results.Count(result => result.Status == "fail");
        int skipped = results.Count(result => result.Status == "skipped");
        int enabled = results.Count(result => result.Enabled);

        if (settings.Json) {
            CliOutput.EmitJson(new TrainerApplyJsonPayload {
                File = XexInfoCommand.GetDisplayFileName(filePath) ?? filePath,
                DryRun = dryRun,
                TotalEntries = results.Count,
                EnabledEntries = enabled,
                Succeeded = succeeded,
                Failed = failed,
                Skipped = skipped,
                Message = message,
                Entries = results
            });
        }
        else {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Trainer Apply[/]").RuleStyle("grey"));
            foreach (TrainerApplyEntryResult result in results) {
                string name = string.IsNullOrWhiteSpace(result.Name) ? $"entry {result.Index}" : result.Name;
                string prefix = result.Status switch {
                    "ok" => dryRun ? "[green]Would write[/]" : "[green]Applied[/]",
                    "fail" => "[red]Failed[/]",
                    _ => "[grey]Skipped[/]"
                };

                string details = string.IsNullOrWhiteSpace(result.Message) ? string.Empty : $" [grey]|[/] {Markup.Escape(result.Message)}";
                string address = string.IsNullOrWhiteSpace(result.Address) ? string.Empty : $" [cyan]0x{Markup.Escape(result.Address)}[/]";
                string type = string.IsNullOrWhiteSpace(result.Type) ? string.Empty : $" [white]{Markup.Escape(result.Type)}[/]";
                AnsiConsole.MarkupLine($"{prefix} [white]{Markup.Escape(name)}[/]{address}{type}{details}");
            }

            if (results.Count == 0 || enabled == 0) {
                AnsiConsole.MarkupLine("[yellow]Nothing to apply.[/]");
            }
            else {
                AnsiConsole.MarkupLine(
                    dryRun
                        ? $"[green]Dry run complete.[/] [grey]{Markup.Escape(message)}[/]"
                        : $"[green]Trainer apply complete.[/] [grey]{Markup.Escape(message)}[/]");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static string GetFailureMessage(Exception ex) {
        string message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        return message;
    }

    private static bool IsBadTrainerPathException(Exception ex) {
        return ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException or IOException;
    }

    private static void WriteError(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                [
                    "Check the trainer JSON envelope and entry fields.",
                    "Use a flat hex or decimal Address value.",
                    "Run rgh trainer apply --dry-run to validate without writing."
                ]));
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }
}
