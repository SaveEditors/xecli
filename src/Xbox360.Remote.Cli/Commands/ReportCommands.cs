using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ReportCommand : AsyncCommand<ReportCommand.Settings> {
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true
    };

    public sealed class Settings : ConnectionSettings {
        [CommandOption("--report-profile <NAME>")]
        [LocalizedDescription("Apply a saved report profile by name.")]
        public string? ReportProfileName { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write the report to a file instead of stdout.")]
        public string? Out { get; init; }

        [CommandOption("--format <FORMAT>")]
        [LocalizedDescription("Report format: md, html, csv, or json. Default: md.")]
        public string? Format { get; init; }

        [CommandOption("--include-modules")]
        [LocalizedDescription("Include loaded module inventory.")]
        public bool IncludeModules { get; init; }

        [CommandOption("--include-threads")]
        [LocalizedDescription("Include thread inventory.")]
        public bool IncludeThreads { get; init; }

        [CommandOption("--include-memory")]
        [LocalizedDescription("Include XBDM memory region map.")]
        public bool IncludeMemory { get; init; }

        [CommandOption("--include-private")]
        [LocalizedDescription("Keep console and network identifiers visible instead of redacting them.")]
        public bool IncludePrivate { get; init; }

        [CommandOption("--since <DATE>")]
        [LocalizedDescription("Include only modules at or after the given UTC date/time.")]
        public string? Since { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Limit the number of modules after filtering.")]
        public int? Limit { get; init; }

        [CommandOption("--status <VALUE>")]
        [LocalizedDescription("Only include optional report sections when the console execution state matches.")]
        public string? Status { get; init; }

        [CommandOption("--diff")]
        [LocalizedDescription("Suppress timestamps and volatile duration fields for stable diff output.")]
        public bool Diff { get; init; }

        [CommandOption("--left <FILE>")]
        [LocalizedDescription("Compare this saved report file against --right.")]
        public string? Left { get; init; }

        [CommandOption("--right <FILE>")]
        [LocalizedDescription("Compare this saved report file against --left.")]
        public string? Right { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!TryResolveProfile(settings, out ResolvedReportSettings resolved, out string? profileError)) {
            return WriteFailure(settings.Json, "Report profile validation failed", profileError ?? "Invalid report profile.", "REPORT_PROFILE_INVALID");
        }

        if (!TryResolveFormat(resolved, out string format)) {
            return WriteFailure(settings.Json, "Report format validation failed", "--format must be md, html, csv, or json.", "REPORT_FORMAT_INVALID");
        }

        bool hasDiffInputs = !string.IsNullOrWhiteSpace(resolved.Left) || !string.IsNullOrWhiteSpace(resolved.Right);
        if (hasDiffInputs) {
            if (!TryResolveDiffInputs(resolved, out string leftPath, out string rightPath, out string? diffError)) {
                return WriteFailure(settings.Json, "Report diff validation failed", diffError ?? "Invalid report diff input.", "REPORT_DIFF_INPUT_INVALID");
            }

            if (!TryResolveDiffFormat(resolved, out string diffFormat, out string? diffFormatError)) {
                return WriteFailure(settings.Json, "Report diff validation failed", diffFormatError ?? "Invalid report diff format.", "REPORT_DIFF_FORMAT_INVALID");
            }

            ReportDiffSnapshot diffSnapshot;
            try {
                diffSnapshot = await BuildOfflineDiffAsync(leftPath, rightPath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException or JsonException) {
                string detail = ex is JsonException
                    ? "One of the saved report files could not be parsed."
                    : SummarizeDiffInputError(ex, leftPath);
                return WriteFailure(settings.Json, "Report diff failed", detail, "REPORT_DIFF_READ_FAILED");
            }

            string diffContent = diffFormat == "json"
                ? JsonSerializer.Serialize(diffSnapshot, JsonOptions)
                : RenderDiffMarkdown(diffSnapshot);

            if (!string.IsNullOrWhiteSpace(resolved.Out)) {
                try {
                    string fullPath = Path.GetFullPath(resolved.Out);
                    string? directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);

                    await File.WriteAllTextAsync(fullPath, diffContent, new UTF8Encoding(false), CancellationToken.None);
                    if (settings.Json)
                        WriteFileReceipt("report-diff", diffFormat, resolved.Out, diffContent);
                    else
                        AnsiConsole.MarkupLine($"[green]Report diff written:[/] {Markup.Escape(XexInfoCommand.GetDisplayFileName(resolved.Out) ?? string.Empty)}");
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
                    string leafName = XexInfoCommand.GetDisplayFileName(resolved.Out);
                    string detail = string.IsNullOrWhiteSpace(leafName)
                        ? "Failed to write report diff output. Check the output path, parent directory, and permissions."
                        : $"Failed to write report diff output file '{leafName}'. Check the output path, parent directory, and permissions.";
                    return WriteFailure(settings.Json, "Report diff write failed", detail, "REPORT_DIFF_WRITE_FAILED");
                }
            }
            else {
                Console.WriteLine(diffContent);
            }

            return 0;
        }

        if (!TryResolveFilters(resolved, out DateTimeOffset? sinceUtc, out int? limit, out string? statusFilter, out string? filterError)) {
            return WriteFailure(settings.Json, "Report filter validation failed", filterError ?? "Invalid report filter.", "REPORT_FILTER_INVALID");
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(resolved.Connection, CancellationToken.None);
        ReportSnapshot snapshot = new ReportSnapshot {
            Target = new ReportTarget(ip, port),
            GeneratedUtc = resolved.Diff ? null : DateTime.UtcNow
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        int result = await CliHelpers.WithClientOnceAsync((ip, port, timeout), resolved.Connection, async client => {
            await CollectCoreAsync(client, snapshot, timeout);

            bool statusMatches = string.IsNullOrWhiteSpace(statusFilter) || StatusMatches(snapshot.Console?.ExecutionState, statusFilter);

            if (resolved.IncludeModules)
                snapshot.Modules = statusMatches
                    ? await CollectModulesAsync(client, snapshot.Warnings, timeout, resolved.Diff, sinceUtc, limit)
                    : Array.Empty<ReportModule>();

            if (resolved.IncludeThreads)
                snapshot.Threads = statusMatches
                    ? await CollectThreadsAsync(client, snapshot.Warnings, timeout)
                    : Array.Empty<ReportThread>();

            if (resolved.IncludeMemory)
                snapshot.MemoryRegions = statusMatches
                    ? await CollectMemoryRegionsAsync(client, snapshot.Warnings, timeout)
                    : Array.Empty<ReportMemoryRegion>();

            return 0;
        }, CancellationToken.None);
        stopwatch.Stop();

        snapshot.Summary = BuildSummary(snapshot);

        if (!resolved.Diff)
            snapshot.CollectionMs = stopwatch.ElapsedMilliseconds;

        if (!resolved.IncludePrivate)
            RedactPrivateValues(snapshot);

        bool redactPrivateValues = !resolved.IncludePrivate;
        string content = format switch {
            "html" => RenderHtml(snapshot, redactPrivateValues),
            "csv" => RenderCsv(snapshot, redactPrivateValues),
            "json" => JsonSerializer.Serialize(snapshot, JsonOptions),
            _ => RenderMarkdown(snapshot, redactPrivateValues)
        };

        if (!string.IsNullOrWhiteSpace(resolved.Out)) {
            try {
                string fullPath = Path.GetFullPath(resolved.Out);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                await File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false), CancellationToken.None);
                if (settings.Json)
                    WriteFileReceipt("report", format, resolved.Out, content);
                else
                    AnsiConsole.MarkupLine($"[green]Report written:[/] {Markup.Escape(XexInfoCommand.GetDisplayFileName(resolved.Out) ?? string.Empty)}");
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
                string leafName = XexInfoCommand.GetDisplayFileName(resolved.Out);
                string detail = string.IsNullOrWhiteSpace(leafName)
                    ? "Failed to write report output. Check the output path, parent directory, and permissions."
                    : $"Failed to write report output file '{leafName}'. Check the output path, parent directory, and permissions.";
                return WriteFailure(settings.Json, "Report write failed", detail, "REPORT_WRITE_FAILED");
            }
        }
        else {
            Console.WriteLine(content);
        }

        return result;
    }

    private static int WriteFailure(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                new[] { "Correct the report options or output path and retry." }));
        }
        else {
            OperationFeedback.WriteFailure(title, message);
        }

        return 1;
    }

    private static void WriteFileReceipt(string operation, string format, string path, string content) {
        CliOutput.EmitJson(new {
            Operation = operation,
            Status = "written",
            Format = format,
            Output = XexInfoCommand.GetDisplayFileName(path),
            Bytes = Encoding.UTF8.GetByteCount(content)
        });
    }

    private static bool TryResolveProfile(Settings settings, out ResolvedReportSettings resolved, out string? error) {
        resolved = new ResolvedReportSettings(
            new ConnectionSettings {
                Ip = settings.Ip,
                Profile = settings.Profile,
                Port = settings.Port,
                TimeoutMs = settings.TimeoutMs,
                Json = settings.Json
            },
            settings.Out,
            settings.Format,
            settings.IncludeModules,
            settings.IncludeThreads,
            settings.IncludeMemory,
            settings.IncludePrivate,
            settings.Since,
            settings.Limit,
            settings.Status,
            settings.Diff,
            settings.Left,
            settings.Right);
        error = null;

        if (string.IsNullOrWhiteSpace(settings.ReportProfileName))
            return true;

        ReportProfileStoreData store = ReportProfileStore.Load();
        ReportProfileRecord? profile = ReportProfileStore.FindProfile(store, settings.ReportProfileName);
        if (profile == null) {
            error = $"Report profile not found: {settings.ReportProfileName.Trim()}";
            return false;
        }

        resolved = resolved with {
            Connection = new ConnectionSettings {
                Ip = settings.Ip ?? profile.Ip,
                Profile = settings.Profile ?? profile.TargetProfileName,
                Port = settings.Port ?? profile.Port,
                TimeoutMs = settings.TimeoutMs ?? profile.TimeoutMs,
                Json = settings.Json
            },
            Format = string.IsNullOrWhiteSpace(settings.Format) ? profile.Format : settings.Format,
            IncludeModules = settings.IncludeModules || profile.IncludeModules,
            IncludeThreads = settings.IncludeThreads || profile.IncludeThreads,
            IncludeMemory = settings.IncludeMemory || profile.IncludeMemory,
            IncludePrivate = settings.IncludePrivate || profile.IncludePrivate,
            Since = string.IsNullOrWhiteSpace(settings.Since) ? profile.Since : settings.Since,
            Limit = settings.Limit ?? profile.Limit,
            Status = string.IsNullOrWhiteSpace(settings.Status) ? profile.Status : settings.Status,
            Diff = settings.Diff || profile.Diff
        };
        return true;
    }

    private static bool TryResolveFormat(ResolvedReportSettings settings, out string format) {
        format = settings.Connection.Json ? "json" : (settings.Format ?? "md").Trim().ToLowerInvariant();
        if (format == "markdown")
            format = "md";
        return format is "md" or "html" or "csv" or "json";
    }

    private static bool TryResolveDiffFormat(ResolvedReportSettings settings, out string format, out string? error) {
        format = settings.Connection.Json ? "json" : (settings.Format ?? "md").Trim().ToLowerInvariant();
        if (format == "markdown")
            format = "md";

        if (format is "md" or "json") {
            error = null;
            return true;
        }

        error = "--format must be md or json when comparing saved report files.";
        return false;
    }

    private static bool TryResolveDiffInputs(ResolvedReportSettings settings, out string leftPath, out string rightPath, out string? error) {
        leftPath = string.Empty;
        rightPath = string.Empty;
        error = null;

        bool hasLeft = !string.IsNullOrWhiteSpace(settings.Left);
        bool hasRight = !string.IsNullOrWhiteSpace(settings.Right);
        if (!hasLeft || !hasRight) {
            error = "Provide --left <FILE> and --right <FILE> to compare saved report files.";
            return false;
        }

        leftPath = settings.Left!;
        rightPath = settings.Right!;
        return true;
    }

    private static bool TryResolveFilters(
        ResolvedReportSettings settings,
        out DateTimeOffset? sinceUtc,
        out int? limit,
        out string? statusFilter,
        out string? error) {
        sinceUtc = null;
        limit = null;
        statusFilter = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(settings.Since)) {
            if (!DateTimeOffset.TryParse(
                    settings.Since,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset parsedSince)) {
                error = "--since must be a valid UTC date or date-time.";
                return false;
            }

            sinceUtc = parsedSince.ToUniversalTime();
        }

        if (settings.Limit.HasValue) {
            if (settings.Limit.Value < 0) {
                error = "--limit must be zero or greater.";
                return false;
            }

            limit = settings.Limit.Value;
        }

        if (!string.IsNullOrWhiteSpace(settings.Status)) {
            if (!TryNormalizeExecutionState(settings.Status, out string normalizedStatus)) {
                error = "--status must be one of: start, running, stop, stopped, break, reboot, or reboot_title.";
                return false;
            }

            statusFilter = normalizedStatus;
        }

        return true;
    }

    private static async Task CollectCoreAsync(XbdmClient client, ReportSnapshot snapshot, int timeoutMs) {
        snapshot.Console = await TryCollectAsync(
            snapshot.Warnings,
            "console info",
            timeoutMs,
            token => client.GetConsoleInfoAsync(token));

        snapshot.DmVersion = await TryCollectAsync(
            snapshot.Warnings,
            "DM version",
            timeoutMs,
            token => client.GetDmVersionAsync(token));

        snapshot.RunningXex = await TryCollectAsync(
            snapshot.Warnings,
            "running XEX path",
            timeoutMs,
            token => client.GetRunningXexPathAsync(null, token));
    }

    private static async Task<IReadOnlyList<ReportModule>> CollectModulesAsync(
        XbdmClient client,
        List<string> warnings,
        int timeoutMs,
        bool diff,
        DateTimeOffset? sinceUtc,
        int? limit) {
        IReadOnlyList<XbdmModuleInfo>? modules = await TryCollectAsync(
            warnings,
            "modules",
            Math.Max(timeoutMs, 8000),
            token => client.GetModulesAsync(includeSections: false, token));

        if (modules == null)
            return Array.Empty<ReportModule>();

        IEnumerable<XbdmModuleInfo> filtered = modules
            .OrderBy(module => module.BaseAddress)
            .Where(module => !sinceUtc.HasValue || (module.Timestamp.HasValue && module.Timestamp.Value.ToUniversalTime() >= sinceUtc.Value.UtcDateTime));

        if (limit.HasValue)
            filtered = filtered.Take(limit.Value);

        return filtered
            .Select(module => new ReportModule(
                module.Name,
                Hex(module.BaseAddress),
                Hex(module.ModuleSize),
                Hex(module.OriginalModuleSize),
                Hex(module.EntryPoint),
                diff ? null : module.Timestamp))
            .ToArray();
    }

    private static bool StatusMatches(string? actualState, string expectedNormalizedState) {
        return TryNormalizeExecutionState(actualState, out string normalizedActualState) &&
               string.Equals(normalizedActualState, expectedNormalizedState, StringComparison.Ordinal);
    }

    private static bool TryNormalizeExecutionState(string? value, out string normalizedState) {
        normalizedState = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        normalizedState = value.Trim().ToLowerInvariant() switch {
            "start" or "running" => "running",
            "stop" or "stopped" or "break" => "stopped",
            "reboot_title" or "reboot" => "reboot",
            _ => string.Empty
        };

        return normalizedState.Length > 0;
    }

    private static async Task<IReadOnlyList<ReportThread>> CollectThreadsAsync(
        XbdmClient client,
        List<string> warnings,
        int timeoutMs) {
        IReadOnlyList<XbdmThreadInfo>? threads = await TryCollectAsync(
            warnings,
            "threads",
            Math.Max(timeoutMs, 8000),
            token => client.GetThreadsAsync(includeNames: false, token));

        if (threads == null)
            return Array.Empty<ReportThread>();

        return threads
            .OrderBy(thread => thread.Id)
            .Select(thread => new ReportThread(
                Hex(thread.Id),
                thread.SuspendCount,
                thread.Priority,
                thread.CurrentProcessor,
                HexZeroAsNull(thread.StartAddress),
                Hex(thread.BaseAddress),
                Hex(thread.StackLimit),
                Hex(thread.TlsBaseAddress),
                HexZeroAsNull(thread.NameAddress)))
            .ToArray();
    }

    private static async Task<IReadOnlyList<ReportMemoryRegion>> CollectMemoryRegionsAsync(
        XbdmClient client,
        List<string> warnings,
        int timeoutMs) {
        IReadOnlyList<XbdmMemoryRegion>? regions = await TryCollectAsync(
            warnings,
            "memory regions",
            Math.Max(timeoutMs, 8000),
            token => client.GetMemoryRegionsAsync(token));

        if (regions == null)
            return Array.Empty<ReportMemoryRegion>();

        return regions
            .OrderBy(region => region.BaseAddress)
            .Select(region => new ReportMemoryRegion(
                Hex(region.BaseAddress),
                Hex(region.Size),
                Hex(MemoryMapHelpers.ComputeEnd(region.BaseAddress, region.Size)),
                Hex(region.Protect),
                Hex(region.Phys),
                FormatMemoryBand(region.BaseAddress)))
            .ToArray();
    }

    internal static string FormatMemoryBand(uint baseAddress) {
        return MemoryMapHelpers.ClassifyBand(baseAddress).Name;
    }

    private static async Task<T?> TryCollectAsync<T>(
        List<string> warnings,
        string label,
        int timeoutMs,
        Func<CancellationToken, Task<T>> action) {
        using CancellationTokenSource cts = new CancellationTokenSource(timeoutMs);
        try {
            return await action(cts.Token);
        }
        catch (Exception ex) {
            warnings.Add($"{label}: {SummarizeException(ex)}");
            return default;
        }
    }

    private static string RenderMarkdown(ReportSnapshot report, bool redactPrivateValues) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# XeCLI Console Report");
        sb.AppendLine();
        if (report.GeneratedUtc.HasValue)
            sb.AppendLine($"Generated UTC: {report.GeneratedUtc.Value:O}");
        if (report.CollectionMs.HasValue)
            sb.AppendLine($"Collection: {report.CollectionMs.Value.ToString(CultureInfo.InvariantCulture)} ms");
        sb.AppendLine($"Target: {EscapeMarkdown(report.Target.Ip)}:{report.Target.Port.ToString(CultureInfo.InvariantCulture)}");
        if (redactPrivateValues) {
            sb.AppendLine();
            sb.AppendLine("_Private identifiers are redacted by default. Add `--include-private` to show them in local output._");
        }
        sb.AppendLine();

        AppendMarkdownSummary(sb, report.Summary);

        sb.AppendLine("## Console");
        AppendMarkdownTable(
            sb,
            new[] { "Field", "Value" },
            new[] {
                new[] { "Console ID", ValueOrUnknown(report.Console?.ConsoleId) },
                new[] { "Debug Name", ValueOrUnknown(report.Console?.DebugName) },
                new[] { "Execution State", ValueOrUnknown(report.Console?.ExecutionState) },
                new[] { "Title IP", ValueOrUnknown(report.Console?.TitleIp) },
                new[] { "Process ID", report.Console?.ProcessId.HasValue == true ? Hex(report.Console.ProcessId.Value) : "unknown" },
                new[] { "DM Version", ValueOrUnknown(report.DmVersion) },
                new[] { "Running XEX", ValueOrUnknown(report.RunningXex) }
            });
        sb.AppendLine();

        if (report.Modules != null) {
            sb.AppendLine("## Modules");
            AppendMarkdownTable(
                sb,
                report.Modules.Any(module => module.TimestampUtc.HasValue)
                    ? new[] { "Name", "Base", "Size", "Original Size", "Entry", "Timestamp UTC" }
                    : new[] { "Name", "Base", "Size", "Original Size", "Entry" },
                report.Modules.Select(module => module.TimestampUtc.HasValue
                    ? new[] {
                        module.Name,
                        module.Base,
                        module.Size,
                        module.OriginalSize,
                        ValueOrUnknown(module.EntryPoint),
                        module.TimestampUtc.Value.ToString("O", CultureInfo.InvariantCulture)
                    }
                    : new[] {
                        module.Name,
                        module.Base,
                        module.Size,
                        module.OriginalSize,
                        ValueOrUnknown(module.EntryPoint)
                    }));
            sb.AppendLine();
        }

        if (report.Threads != null) {
            sb.AppendLine("## Threads");
            AppendMarkdownTable(
                sb,
                new[] { "ID", "Suspend", "Priority", "CPU", "Start", "Stack Base", "Stack Limit", "TLS Base", "Name Ptr" },
                report.Threads.Select(thread => new[] {
                    thread.Id,
                    thread.SuspendCount.ToString(CultureInfo.InvariantCulture),
                    thread.Priority.ToString(CultureInfo.InvariantCulture),
                    thread.CurrentProcessor.ToString(CultureInfo.InvariantCulture),
                    ValueOrUnknown(thread.StartAddress),
                    thread.StackBase,
                    thread.StackLimit,
                    thread.TlsBase,
                    ValueOrUnknown(thread.NameAddress)
                }));
            sb.AppendLine();
        }

        if (report.MemoryRegions != null) {
            sb.AppendLine("## Memory Regions");
            AppendMarkdownTable(
                sb,
                new[] { "Base", "End", "Size", "Protect", "Phys", "Band" },
                report.MemoryRegions.Select(region => new[] {
                    region.Base,
                    region.End,
                    region.Size,
                    region.Protect,
                    region.Phys,
                    region.Band
                }));
            sb.AppendLine();
        }

        if (report.Warnings.Count > 0) {
            sb.AppendLine("## Warnings");
            foreach (string warning in report.Warnings)
                sb.AppendLine($"- {EscapeMarkdown(warning)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    internal static void RedactPrivateValues(ReportSnapshot report) {
        report.Target = report.Target with { Ip = "redacted" };
        if (report.Console != null) {
            report.Console = report.Console with {
                ConsoleId = RedactedValue(report.Console.ConsoleId),
                DebugName = RedactedValue(report.Console.DebugName),
                TitleIp = RedactedValue(report.Console.TitleIp)
            };
        }

        report.RunningXex = RedactRunningXex(report.RunningXex);
    }

    internal static string RedactedValue(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : "redacted";
    }

    internal static string? RedactRunningXex(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string candidate = path.Trim();
        string fileName = Path.GetFileName(candidate);
        if (!string.IsNullOrWhiteSpace(fileName))
            return fileName;

        int slash = candidate.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash + 1 < candidate.Length)
            return candidate.Substring(slash + 1);

        return "redacted";
    }

    private static string RenderHtml(ReportSnapshot report, bool redactPrivateValues) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>XeCLI Console Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;background:#101418;color:#e8eef5}h1,h2{color:#65d6ff}table{border-collapse:collapse;width:100%;margin:12px 0 28px}th,td{border:1px solid #33404c;padding:7px 9px;text-align:left}th{background:#18232e}code{color:#9af7c6}.warn{color:#ffd166}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<h1>XeCLI Console Report</h1>");
        if (report.GeneratedUtc.HasValue)
            sb.AppendLine($"<p><strong>Generated UTC:</strong> {H(report.GeneratedUtc.Value.ToString("O", CultureInfo.InvariantCulture))}</p>");
        if (report.CollectionMs.HasValue)
            sb.AppendLine($"<p><strong>Collection:</strong> {report.CollectionMs.Value.ToString(CultureInfo.InvariantCulture)} ms</p>");
        sb.AppendLine($"<p><strong>Target:</strong> {H(report.Target.Ip)}:{report.Target.Port.ToString(CultureInfo.InvariantCulture)}</p>");
        if (redactPrivateValues)
            sb.AppendLine("<p><em>Private identifiers are redacted by default. Add <code>--include-private</code> to show them in local output.</em></p>");

        AppendHtmlSummary(sb, report.Summary);

        AppendHtmlTable(
            sb,
            "Console",
            new[] { "Field", "Value" },
            new[] {
                new[] { "Console ID", ValueOrUnknown(report.Console?.ConsoleId) },
                new[] { "Debug Name", ValueOrUnknown(report.Console?.DebugName) },
                new[] { "Execution State", ValueOrUnknown(report.Console?.ExecutionState) },
                new[] { "Title IP", ValueOrUnknown(report.Console?.TitleIp) },
                new[] { "Process ID", report.Console?.ProcessId.HasValue == true ? Hex(report.Console.ProcessId.Value) : "unknown" },
                new[] { "DM Version", ValueOrUnknown(report.DmVersion) },
                new[] { "Running XEX", ValueOrUnknown(report.RunningXex) }
            });

        if (report.Modules != null) {
            bool hasTimestamp = report.Modules.Any(module => module.TimestampUtc.HasValue);
            AppendHtmlTable(
                sb,
                "Modules",
                hasTimestamp
                    ? new[] { "Name", "Base", "Size", "Original Size", "Entry", "Timestamp UTC" }
                    : new[] { "Name", "Base", "Size", "Original Size", "Entry" },
                report.Modules.Select(module => hasTimestamp
                    ? new[] {
                        module.Name,
                        module.Base,
                        module.Size,
                        module.OriginalSize,
                        ValueOrUnknown(module.EntryPoint),
                        module.TimestampUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown"
                    }
                    : new[] {
                        module.Name,
                        module.Base,
                        module.Size,
                        module.OriginalSize,
                        ValueOrUnknown(module.EntryPoint)
                    }));
        }

        if (report.Threads != null) {
            AppendHtmlTable(
                sb,
                "Threads",
                new[] { "ID", "Suspend", "Priority", "CPU", "Start", "Stack Base", "Stack Limit", "TLS Base", "Name Ptr" },
                report.Threads.Select(thread => new[] {
                    thread.Id,
                    thread.SuspendCount.ToString(CultureInfo.InvariantCulture),
                    thread.Priority.ToString(CultureInfo.InvariantCulture),
                    thread.CurrentProcessor.ToString(CultureInfo.InvariantCulture),
                    ValueOrUnknown(thread.StartAddress),
                    thread.StackBase,
                    thread.StackLimit,
                    thread.TlsBase,
                    ValueOrUnknown(thread.NameAddress)
                }));
        }

        if (report.MemoryRegions != null) {
            AppendHtmlTable(
                sb,
                "Memory Regions",
                new[] { "Base", "End", "Size", "Protect", "Phys", "Band" },
                report.MemoryRegions.Select(region => new[] {
                    region.Base,
                    region.End,
                    region.Size,
                    region.Protect,
                    region.Phys,
                    region.Band
                }));
        }

        if (report.Warnings.Count > 0) {
            sb.AppendLine("<h2>Warnings</h2>");
            sb.AppendLine("<ul>");
            foreach (string warning in report.Warnings)
                sb.AppendLine($"<li class=\"warn\">{H(warning)}</li>");
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string RenderCsv(ReportSnapshot report, bool redactPrivateValues) {
        StringBuilder sb = new StringBuilder();
        sb.Append("Section,Index,Field,Value").Append('\n');

        if (report.GeneratedUtc.HasValue)
            AppendCsvRow(sb, "Meta", string.Empty, "GeneratedUtc", report.GeneratedUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        if (report.CollectionMs.HasValue)
            AppendCsvRow(sb, "Meta", string.Empty, "CollectionMs", report.CollectionMs.Value.ToString(CultureInfo.InvariantCulture));

        AppendCsvRow(sb, "Target", string.Empty, "Ip", report.Target.Ip);
        AppendCsvRow(sb, "Target", string.Empty, "Port", report.Target.Port.ToString(CultureInfo.InvariantCulture));

        if (redactPrivateValues) {
            AppendCsvRow(
                sb,
                "Meta",
                string.Empty,
                "RedactionNote",
                "Private identifiers are redacted by default. Add --include-private to show them in local output.");
        }

        AppendCsvRow(sb, "Summary", string.Empty, "Modules", report.Summary.ModuleCount.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "ModuleBytes", report.Summary.ModuleTotalSizeBytes.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "OriginalModuleBytes", report.Summary.ModuleTotalOriginalSizeBytes.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "Threads", report.Summary.ThreadCount.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "MemoryRegions", report.Summary.MemoryRegionCount.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "MemoryRegionBytes", report.Summary.MemoryRegionTotalBytes.ToString(CultureInfo.InvariantCulture));
        AppendCsvRow(sb, "Summary", string.Empty, "Warnings", report.Summary.WarningCount.ToString(CultureInfo.InvariantCulture));

        AppendCsvRow(sb, "Console", string.Empty, "ConsoleId", ValueOrUnknown(report.Console?.ConsoleId));
        AppendCsvRow(sb, "Console", string.Empty, "DebugName", ValueOrUnknown(report.Console?.DebugName));
        AppendCsvRow(sb, "Console", string.Empty, "ExecutionState", ValueOrUnknown(report.Console?.ExecutionState));
        AppendCsvRow(sb, "Console", string.Empty, "TitleIp", ValueOrUnknown(report.Console?.TitleIp));
        AppendCsvRow(sb, "Console", string.Empty, "ProcessId", report.Console?.ProcessId.HasValue == true ? Hex(report.Console.ProcessId.Value) : "unknown");
        AppendCsvRow(sb, "Console", string.Empty, "DmVersion", ValueOrUnknown(report.DmVersion));
        AppendCsvRow(sb, "Console", string.Empty, "RunningXex", ValueOrUnknown(report.RunningXex));

        if (report.Modules != null) {
            for (int i = 0; i < report.Modules.Count; i++) {
                ReportModule module = report.Modules[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                AppendCsvRow(sb, "Modules", index, "Name", module.Name);
                AppendCsvRow(sb, "Modules", index, "Base", module.Base);
                AppendCsvRow(sb, "Modules", index, "Size", module.Size);
                AppendCsvRow(sb, "Modules", index, "OriginalSize", module.OriginalSize);
                AppendCsvRow(sb, "Modules", index, "Entry", ValueOrUnknown(module.EntryPoint));
                if (module.TimestampUtc.HasValue)
                    AppendCsvRow(sb, "Modules", index, "TimestampUtc", module.TimestampUtc.Value.ToString("O", CultureInfo.InvariantCulture));
            }
        }

        if (report.Threads != null) {
            for (int i = 0; i < report.Threads.Count; i++) {
                ReportThread thread = report.Threads[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                AppendCsvRow(sb, "Threads", index, "Id", thread.Id);
                AppendCsvRow(sb, "Threads", index, "SuspendCount", thread.SuspendCount.ToString(CultureInfo.InvariantCulture));
                AppendCsvRow(sb, "Threads", index, "Priority", thread.Priority.ToString(CultureInfo.InvariantCulture));
                AppendCsvRow(sb, "Threads", index, "CurrentProcessor", thread.CurrentProcessor.ToString(CultureInfo.InvariantCulture));
                AppendCsvRow(sb, "Threads", index, "StartAddress", ValueOrUnknown(thread.StartAddress));
                AppendCsvRow(sb, "Threads", index, "StackBase", thread.StackBase);
                AppendCsvRow(sb, "Threads", index, "StackLimit", thread.StackLimit);
                AppendCsvRow(sb, "Threads", index, "TlsBase", thread.TlsBase);
                AppendCsvRow(sb, "Threads", index, "NameAddress", ValueOrUnknown(thread.NameAddress));
            }
        }

        if (report.MemoryRegions != null) {
            for (int i = 0; i < report.MemoryRegions.Count; i++) {
                ReportMemoryRegion region = report.MemoryRegions[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                AppendCsvRow(sb, "MemoryRegions", index, "Base", region.Base);
                AppendCsvRow(sb, "MemoryRegions", index, "End", region.End);
                AppendCsvRow(sb, "MemoryRegions", index, "Size", region.Size);
                AppendCsvRow(sb, "MemoryRegions", index, "Protect", region.Protect);
                AppendCsvRow(sb, "MemoryRegions", index, "Phys", region.Phys);
                AppendCsvRow(sb, "MemoryRegions", index, "Band", region.Band);
            }
        }

        if (report.Warnings.Count > 0) {
            for (int i = 0; i < report.Warnings.Count; i++)
                AppendCsvRow(sb, "Warnings", (i + 1).ToString(CultureInfo.InvariantCulture), "Message", report.Warnings[i]);
        }

        return sb.ToString();
    }

    private static string RenderDiffMarkdown(ReportDiffSnapshot diff) {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# XeCLI Report Diff");
        sb.AppendLine();
        sb.AppendLine($"Left: {EscapeMarkdown(diff.Left.Name)}");
        sb.AppendLine($"Right: {EscapeMarkdown(diff.Right.Name)}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        AppendMarkdownTable(
            sb,
            new[] { "Metric", "Value" },
            new[] {
                new[] { "Left rows", diff.Summary.LeftRowCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Right rows", diff.Summary.RightRowCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Shared rows", diff.Summary.SharedRowCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Changed rows", diff.Summary.ChangedRowCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Added rows", diff.Summary.AddedRowCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Removed rows", diff.Summary.RemovedRowCount.ToString(CultureInfo.InvariantCulture) }
            });
        sb.AppendLine();
        sb.AppendLine("## Differences");
        if (diff.Rows.Count == 0) {
            sb.AppendLine("_No row differences found._");
        }
        else {
            AppendMarkdownTable(
                sb,
                new[] { "Section", "Index", "Field", "Left", "Right", "Status" },
                diff.Rows.Select(row => new[] {
                    row.Section,
                    row.Index,
                    row.Field,
                    row.LeftValue ?? string.Empty,
                    row.RightValue ?? string.Empty,
                    row.Status
                }));
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendCsvRow(StringBuilder sb, string section, string index, string field, string value) {
        sb.Append(Csv(section)).Append(',');
        sb.Append(Csv(index)).Append(',');
        sb.Append(Csv(field)).Append(',');
        sb.Append(Csv(value)).Append('\n');
    }

    private static string Csv(string value) {
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendMarkdownTable(StringBuilder sb, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) {
        sb.Append('|');
        foreach (string header in headers)
            sb.Append(' ').Append(EscapeMarkdown(header)).Append(" |");
        sb.AppendLine();
        sb.Append('|');
        foreach (string _ in headers)
            sb.Append(" --- |");
        sb.AppendLine();
        foreach (IReadOnlyList<string> row in rows) {
            sb.Append('|');
            for (int i = 0; i < headers.Count; i++) {
                string value = i < row.Count ? row[i] : string.Empty;
                sb.Append(' ').Append(EscapeMarkdown(value)).Append(" |");
            }
            sb.AppendLine();
        }
    }

    private static void AppendMarkdownSummary(StringBuilder sb, ReportSummary summary) {
        sb.AppendLine("## Summary");
        AppendMarkdownTable(
            sb,
            new[] { "Metric", "Value" },
            new[] {
                new[] { "Modules", summary.ModuleCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Module bytes", summary.ModuleTotalSizeBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Original module bytes", summary.ModuleTotalOriginalSizeBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Threads", summary.ThreadCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Memory regions", summary.MemoryRegionCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Memory region bytes", summary.MemoryRegionTotalBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Warnings", summary.WarningCount.ToString(CultureInfo.InvariantCulture) }
            });
        sb.AppendLine();
    }

    private static void AppendHtmlTable(StringBuilder sb, string title, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) {
        sb.AppendLine($"<h2>{H(title)}</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr>");
        foreach (string header in headers)
            sb.AppendLine($"<th>{H(header)}</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (IReadOnlyList<string> row in rows) {
            sb.AppendLine("<tr>");
            for (int i = 0; i < headers.Count; i++) {
                string value = i < row.Count ? row[i] : string.Empty;
                sb.AppendLine($"<td>{H(value)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");
    }

    private static void AppendHtmlSummary(StringBuilder sb, ReportSummary summary) {
        AppendHtmlTable(
            sb,
            "Summary",
            new[] { "Metric", "Value" },
            new[] {
                new[] { "Modules", summary.ModuleCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Module bytes", summary.ModuleTotalSizeBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Original module bytes", summary.ModuleTotalOriginalSizeBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Threads", summary.ThreadCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Memory regions", summary.MemoryRegionCount.ToString(CultureInfo.InvariantCulture) },
                new[] { "Memory region bytes", summary.MemoryRegionTotalBytes.ToString(CultureInfo.InvariantCulture) },
                new[] { "Warnings", summary.WarningCount.ToString(CultureInfo.InvariantCulture) }
            });
    }

    private static ReportSummary BuildSummary(ReportSnapshot report) {
        return new ReportSummary {
            ModuleCount = report.Modules?.Count ?? 0,
            ModuleTotalSizeBytes = SumHexValues(report.Modules?.Select(module => module.Size)),
            ModuleTotalOriginalSizeBytes = SumHexValues(report.Modules?.Select(module => module.OriginalSize)),
            ThreadCount = report.Threads?.Count ?? 0,
            MemoryRegionCount = report.MemoryRegions?.Count ?? 0,
            MemoryRegionTotalBytes = SumHexValues(report.MemoryRegions?.Select(region => region.Size)),
            WarningCount = report.Warnings.Count
        };
    }

    private static ulong SumHexValues(IEnumerable<string>? values) {
        if (values == null)
            return 0;

        ulong total = 0;
        foreach (string value in values) {
            if (TryParseHexValue(value, out ulong parsed))
                total += parsed;
        }

        return total;
    }

    private static bool TryParseHexValue(string value, out ulong parsed) {
        string candidate = value.Trim();
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            candidate = candidate[2..];

        return ulong.TryParse(candidate, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed);
    }

    private static string Hex(uint value) {
        return $"0x{value:X8}";
    }

    private static string? Hex(uint? value) {
        return value.HasValue ? Hex(value.Value) : null;
    }

    private static string? HexZeroAsNull(uint value) {
        return value == 0 ? null : Hex(value);
    }

    private static string ValueOrUnknown(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    private static string EscapeMarkdown(string value) {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string H(string value) {
        return WebUtility.HtmlEncode(value);
    }

    private static string SummarizeException(Exception ex) {
        return ex is OperationCanceledException
            ? "timed out"
            : string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }

    private static async Task<ReportDiffSnapshot> BuildOfflineDiffAsync(string leftPath, string rightPath) {
        ReportFile left = await LoadReportFileAsync(leftPath);
        ReportFile right = await LoadReportFileAsync(rightPath);

        Dictionary<string, ReportRow> leftRows = left.Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
        Dictionary<string, ReportRow> rightRows = right.Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

        List<ReportDiffRow> rows = new List<ReportDiffRow>();
        int shared = 0;
        int changed = 0;
        int added = 0;
        int removed = 0;

        foreach (ReportRow row in left.Rows) {
            if (!rightRows.TryGetValue(row.Key, out ReportRow? rightRow)) {
                removed++;
                rows.Add(new ReportDiffRow(row.Section, row.Index, row.Field, row.Value, null, "removed"));
                continue;
            }

            shared++;
            if (!string.Equals(row.Value, rightRow.Value, StringComparison.Ordinal)) {
                changed++;
                rows.Add(new ReportDiffRow(row.Section, row.Index, row.Field, row.Value, rightRow.Value, "changed"));
            }
        }

        foreach (ReportRow row in right.Rows) {
            if (leftRows.ContainsKey(row.Key))
                continue;

            added++;
            rows.Add(new ReportDiffRow(row.Section, row.Index, row.Field, null, row.Value, "added"));
        }

        return new ReportDiffSnapshot(
            new ReportDiffSource(left.Name, left.Format, left.Rows.Count),
            new ReportDiffSource(right.Name, right.Format, right.Rows.Count),
            new ReportDiffSummary(left.Rows.Count, right.Rows.Count, shared, changed, added, removed),
            rows);
    }

    private static async Task<ReportFile> LoadReportFileAsync(string path) {
        string format = GetReportInputFormat(path);
        return format switch {
            "json" => await LoadReportJsonAsync(path),
            "csv" => await LoadReportCsvAsync(path),
            "md" => await LoadReportMarkdownAsync(path),
            _ => throw new NotSupportedException($"Unsupported report file format: {Path.GetFileName(path)}")
        };
    }

    private static async Task<ReportFile> LoadReportJsonAsync(string path) {
        await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ReportSnapshot? report = await JsonSerializer.DeserializeAsync<ReportSnapshot>(stream, JsonOptions, CancellationToken.None);
        if (report == null)
            throw new JsonException("The report JSON was empty.");

        return new ReportFile(Path.GetFileName(path), "json", FlattenReportRows(report));
    }

    private static async Task<ReportFile> LoadReportCsvAsync(string path) {
        List<ReportRow> rows = new List<ReportRow>();
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? header = await reader.ReadLineAsync();
        if (!string.Equals(header, "Section,Index,Field,Value", StringComparison.Ordinal)) {
            throw new JsonException("The report CSV header was not recognized.");
        }

        string? line;
        while ((line = await reader.ReadLineAsync()) != null) {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] fields = ParseCsvLine(line);
            if (fields.Length < 4)
                continue;

            rows.Add(new ReportRow(fields[0], fields[1], fields[2], fields[3]));
        }

        return new ReportFile(Path.GetFileName(path), "csv", rows);
    }

    private static async Task<ReportFile> LoadReportMarkdownAsync(string path) {
        string[] lines = await File.ReadAllLinesAsync(path, CancellationToken.None);
        List<ReportRow> rows = new List<ReportRow>();
        int index = 0;

        while (index < lines.Length) {
            string line = lines[index].TrimEnd();
            if (line.StartsWith("Generated UTC:", StringComparison.Ordinal)) {
                rows.Add(new ReportRow("Meta", string.Empty, "GeneratedUtc", line["Generated UTC:".Length..].Trim()));
            }
            else if (line.StartsWith("Collection:", StringComparison.Ordinal)) {
                string value = line["Collection:".Length..].Trim();
                if (value.EndsWith(" ms", StringComparison.Ordinal))
                    value = value[..^3].TrimEnd();
                rows.Add(new ReportRow("Meta", string.Empty, "CollectionMs", value));
            }
            else if (line.StartsWith("Target:", StringComparison.Ordinal)) {
                string value = line["Target:".Length..].Trim();
                int colon = value.LastIndexOf(':');
                if (colon > 0 && colon + 1 < value.Length) {
                    rows.Add(new ReportRow("Target", string.Empty, "Ip", value[..colon]));
                    rows.Add(new ReportRow("Target", string.Empty, "Port", value[(colon + 1)..]));
                }
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal)) {
                string section = line[3..].Trim();
                index++;
                if (section.Equals("Warnings", StringComparison.OrdinalIgnoreCase)) {
                    int warningIndex = 1;
                    while (index < lines.Length) {
                        string warningLine = lines[index].Trim();
                        if (!warningLine.StartsWith("- ", StringComparison.Ordinal))
                            break;

                        rows.Add(new ReportRow("Warnings", warningIndex.ToString(CultureInfo.InvariantCulture), "Message", warningLine[2..].Trim()));
                        warningIndex++;
                        index++;
                    }

                    continue;
                }

                if (index + 1 < lines.Length && lines[index].TrimStart().StartsWith("|", StringComparison.Ordinal) && lines[index + 1].TrimStart().StartsWith("|", StringComparison.Ordinal)) {
                    index += 2;
                    int rowIndex = 1;
                    while (index < lines.Length) {
                        string tableLine = lines[index].Trim();
                        if (tableLine.Length == 0 || !tableLine.StartsWith("|", StringComparison.Ordinal))
                            break;

                        string[] columns = ParseMarkdownTableRow(tableLine);
                        if (columns.Length > 0 && columns[0].Equals("Metric", StringComparison.OrdinalIgnoreCase)) {
                            index++;
                            continue;
                        }

                        AddMarkdownRows(rows, section, columns, rowIndex.ToString(CultureInfo.InvariantCulture));
                        rowIndex++;
                        index++;
                    }

                    continue;
                }
            }

            index++;
        }

        return new ReportFile(Path.GetFileName(path), "md", rows);
    }

    private static IReadOnlyList<ReportRow> FlattenReportRows(ReportSnapshot report) {
        List<ReportRow> rows = new List<ReportRow>();
        if (report.GeneratedUtc.HasValue)
            rows.Add(new ReportRow("Meta", string.Empty, "GeneratedUtc", report.GeneratedUtc.Value.ToString("O", CultureInfo.InvariantCulture)));
        if (report.CollectionMs.HasValue)
            rows.Add(new ReportRow("Meta", string.Empty, "CollectionMs", report.CollectionMs.Value.ToString(CultureInfo.InvariantCulture)));

        rows.Add(new ReportRow("Target", string.Empty, "Ip", report.Target.Ip));
        rows.Add(new ReportRow("Target", string.Empty, "Port", report.Target.Port.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "Modules", report.Summary.ModuleCount.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "ModuleBytes", report.Summary.ModuleTotalSizeBytes.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "OriginalModuleBytes", report.Summary.ModuleTotalOriginalSizeBytes.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "Threads", report.Summary.ThreadCount.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "MemoryRegions", report.Summary.MemoryRegionCount.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "MemoryRegionBytes", report.Summary.MemoryRegionTotalBytes.ToString(CultureInfo.InvariantCulture)));
        rows.Add(new ReportRow("Summary", string.Empty, "Warnings", report.Summary.WarningCount.ToString(CultureInfo.InvariantCulture)));

        rows.Add(new ReportRow("Console", string.Empty, "ConsoleId", ValueOrUnknown(report.Console?.ConsoleId)));
        rows.Add(new ReportRow("Console", string.Empty, "DebugName", ValueOrUnknown(report.Console?.DebugName)));
        rows.Add(new ReportRow("Console", string.Empty, "ExecutionState", ValueOrUnknown(report.Console?.ExecutionState)));
        rows.Add(new ReportRow("Console", string.Empty, "TitleIp", ValueOrUnknown(report.Console?.TitleIp)));
        rows.Add(new ReportRow("Console", string.Empty, "ProcessId", report.Console?.ProcessId.HasValue == true ? Hex(report.Console.ProcessId.Value) : "unknown"));
        rows.Add(new ReportRow("Console", string.Empty, "DmVersion", ValueOrUnknown(report.DmVersion)));
        rows.Add(new ReportRow("Console", string.Empty, "RunningXex", ValueOrUnknown(report.RunningXex)));

        if (report.Modules != null) {
            for (int i = 0; i < report.Modules.Count; i++) {
                ReportModule module = report.Modules[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                rows.Add(new ReportRow("Modules", index, "Name", module.Name));
                rows.Add(new ReportRow("Modules", index, "Base", module.Base));
                rows.Add(new ReportRow("Modules", index, "Size", module.Size));
                rows.Add(new ReportRow("Modules", index, "OriginalSize", module.OriginalSize));
                rows.Add(new ReportRow("Modules", index, "Entry", ValueOrUnknown(module.EntryPoint)));
                if (module.TimestampUtc.HasValue)
                    rows.Add(new ReportRow("Modules", index, "TimestampUtc", module.TimestampUtc.Value.ToString("O", CultureInfo.InvariantCulture)));
            }
        }

        if (report.Threads != null) {
            for (int i = 0; i < report.Threads.Count; i++) {
                ReportThread thread = report.Threads[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                rows.Add(new ReportRow("Threads", index, "Id", thread.Id));
                rows.Add(new ReportRow("Threads", index, "SuspendCount", thread.SuspendCount.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new ReportRow("Threads", index, "Priority", thread.Priority.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new ReportRow("Threads", index, "CurrentProcessor", thread.CurrentProcessor.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new ReportRow("Threads", index, "StartAddress", ValueOrUnknown(thread.StartAddress)));
                rows.Add(new ReportRow("Threads", index, "StackBase", thread.StackBase));
                rows.Add(new ReportRow("Threads", index, "StackLimit", thread.StackLimit));
                rows.Add(new ReportRow("Threads", index, "TlsBase", thread.TlsBase));
                rows.Add(new ReportRow("Threads", index, "NameAddress", ValueOrUnknown(thread.NameAddress)));
            }
        }

        if (report.MemoryRegions != null) {
            for (int i = 0; i < report.MemoryRegions.Count; i++) {
                ReportMemoryRegion region = report.MemoryRegions[i];
                string index = (i + 1).ToString(CultureInfo.InvariantCulture);
                rows.Add(new ReportRow("MemoryRegions", index, "Base", region.Base));
                rows.Add(new ReportRow("MemoryRegions", index, "End", region.End));
                rows.Add(new ReportRow("MemoryRegions", index, "Size", region.Size));
                rows.Add(new ReportRow("MemoryRegions", index, "Protect", region.Protect));
                rows.Add(new ReportRow("MemoryRegions", index, "Phys", region.Phys));
                rows.Add(new ReportRow("MemoryRegions", index, "Band", region.Band));
            }
        }

        if (report.Warnings.Count > 0) {
            for (int i = 0; i < report.Warnings.Count; i++)
                rows.Add(new ReportRow("Warnings", (i + 1).ToString(CultureInfo.InvariantCulture), "Message", report.Warnings[i]));
        }

        return rows;
    }

    private static void AddMarkdownRows(List<ReportRow> rows, string section, string[] columns, string rowIndex) {
        if (section.Equals("Summary", StringComparison.OrdinalIgnoreCase)) {
            AddSummaryMarkdownRows(rows, columns);
            return;
        }

        if (section.Equals("Console", StringComparison.OrdinalIgnoreCase)) {
            AddConsoleMarkdownRows(rows, columns);
            return;
        }

        if (section.Equals("Modules", StringComparison.OrdinalIgnoreCase)) {
            AddModuleMarkdownRows(rows, columns, rowIndex);
            return;
        }

        if (section.Equals("Threads", StringComparison.OrdinalIgnoreCase)) {
            AddThreadMarkdownRows(rows, columns, rowIndex);
            return;
        }

        if (section.Equals("Memory Regions", StringComparison.OrdinalIgnoreCase)) {
            AddMemoryMarkdownRows(rows, columns, rowIndex);
        }
    }

    private static void AddSummaryMarkdownRows(List<ReportRow> rows, string[] columns) {
        if (columns.Length < 2)
            return;

        rows.Add(new ReportRow("Summary", string.Empty, columns[0] switch {
            "Modules" => "Modules",
            "Module bytes" => "ModuleBytes",
            "Original module bytes" => "OriginalModuleBytes",
            "Threads" => "Threads",
            "Memory regions" => "MemoryRegions",
            "Memory region bytes" => "MemoryRegionBytes",
            "Warnings" => "Warnings",
            _ => columns[0]
        }, columns[1]));
    }

    private static void AddConsoleMarkdownRows(List<ReportRow> rows, string[] columns) {
        if (columns.Length < 2)
            return;

        rows.Add(new ReportRow("Console", string.Empty, columns[0] switch {
            "Console ID" => "ConsoleId",
            "Debug Name" => "DebugName",
            "Execution State" => "ExecutionState",
            "Title IP" => "TitleIp",
            "Process ID" => "ProcessId",
            "DM Version" => "DmVersion",
            "Running XEX" => "RunningXex",
            _ => columns[0]
        }, columns[1]));
    }

    private static void AddModuleMarkdownRows(List<ReportRow> rows, string[] columns, string index) {
        if (columns.Length < 5)
            return;

        rows.Add(new ReportRow("Modules", index, "Name", columns[0]));
        rows.Add(new ReportRow("Modules", index, "Base", columns[1]));
        rows.Add(new ReportRow("Modules", index, "Size", columns[2]));
        rows.Add(new ReportRow("Modules", index, "OriginalSize", columns[3]));
        rows.Add(new ReportRow("Modules", index, "Entry", columns[4]));
        if (columns.Length > 5 && !string.IsNullOrWhiteSpace(columns[5]))
            rows.Add(new ReportRow("Modules", index, "TimestampUtc", columns[5]));
    }

    private static void AddThreadMarkdownRows(List<ReportRow> rows, string[] columns, string index) {
        if (columns.Length < 9)
            return;

        rows.Add(new ReportRow("Threads", index, "Id", columns[0]));
        rows.Add(new ReportRow("Threads", index, "SuspendCount", columns[1]));
        rows.Add(new ReportRow("Threads", index, "Priority", columns[2]));
        rows.Add(new ReportRow("Threads", index, "CurrentProcessor", columns[3]));
        rows.Add(new ReportRow("Threads", index, "StartAddress", columns[4]));
        rows.Add(new ReportRow("Threads", index, "StackBase", columns[5]));
        rows.Add(new ReportRow("Threads", index, "StackLimit", columns[6]));
        rows.Add(new ReportRow("Threads", index, "TlsBase", columns[7]));
        rows.Add(new ReportRow("Threads", index, "NameAddress", columns[8]));
    }

    private static void AddMemoryMarkdownRows(List<ReportRow> rows, string[] columns, string index) {
        if (columns.Length < 6)
            return;

        rows.Add(new ReportRow("MemoryRegions", index, "Base", columns[0]));
        rows.Add(new ReportRow("MemoryRegions", index, "End", columns[1]));
        rows.Add(new ReportRow("MemoryRegions", index, "Size", columns[2]));
        rows.Add(new ReportRow("MemoryRegions", index, "Protect", columns[3]));
        rows.Add(new ReportRow("MemoryRegions", index, "Phys", columns[4]));
        rows.Add(new ReportRow("MemoryRegions", index, "Band", columns[5]));
    }

    private static string[] ParseMarkdownTableRow(string line) {
        List<string> cells = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool escape = false;

        for (int i = 0; i < line.Length; i++) {
            char ch = line[i];
            if (escape) {
                cell.Append(ch);
                escape = false;
            }
            else if (ch == '\\') {
                escape = true;
            }
            else if (ch == '|') {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else {
                cell.Append(ch);
            }
        }

        cells.Add(cell.ToString().Trim());
        if (cells.Count >= 2 && string.IsNullOrWhiteSpace(cells[0]))
            cells.RemoveAt(0);
        if (cells.Count >= 2 && string.IsNullOrWhiteSpace(cells[^1]))
            cells.RemoveAt(cells.Count - 1);

        return cells.ToArray();
    }

    private static string[] ParseCsvLine(string line) {
        List<string> fields = new List<string>();
        StringBuilder field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++) {
            char ch = line[i];
            if (inQuotes) {
                if (ch == '"') {
                    if (i + 1 < line.Length && line[i + 1] == '"') {
                        field.Append('"');
                        i++;
                    }
                    else {
                        inQuotes = false;
                    }
                }
                else {
                    field.Append(ch);
                }
            }
            else if (ch == '"') {
                inQuotes = true;
            }
            else if (ch == ',') {
                fields.Add(field.ToString());
                field.Clear();
            }
            else {
                field.Append(ch);
            }
        }

        fields.Add(field.ToString());
        return fields.ToArray();
    }

    private static string GetReportInputFormat(string path) {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch {
            ".json" => "json",
            ".csv" => "csv",
            ".md" or ".markdown" => "md",
            _ => throw new NotSupportedException($"Unsupported report file format: {Path.GetFileName(path)}")
        };
    }

    private static string SummarizeDiffInputError(Exception ex, string leftPath) {
        if (ex is FileNotFoundException fileNotFound)
            return $"File not found: {Path.GetFileName(fileNotFound.FileName ?? leftPath)}";

        if (ex is DirectoryNotFoundException)
            return $"File not found: {Path.GetFileName(leftPath)}";

        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }

    internal sealed record ReportSnapshot {
        public required ReportTarget Target { get; set; }
        public DateTime? GeneratedUtc { get; set; }
        public long? CollectionMs { get; set; }
        public ReportSummary Summary { get; set; } = new ReportSummary();
        public XbdmConsoleInfo? Console { get; set; }
        public string? DmVersion { get; set; }
        public string? RunningXex { get; set; }
        public IReadOnlyList<ReportModule>? Modules { get; set; }
        public IReadOnlyList<ReportThread>? Threads { get; set; }
        public IReadOnlyList<ReportMemoryRegion>? MemoryRegions { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    internal sealed record ReportSummary {
        public int ModuleCount { get; set; }
        public ulong ModuleTotalSizeBytes { get; set; }
        public ulong ModuleTotalOriginalSizeBytes { get; set; }
        public int ThreadCount { get; set; }
        public int MemoryRegionCount { get; set; }
        public ulong MemoryRegionTotalBytes { get; set; }
        public int WarningCount { get; set; }
    }

    internal sealed record ReportTarget(string Ip, int Port);

    private sealed record ResolvedReportSettings(
        ConnectionSettings Connection,
        string? Out,
        string? Format,
        bool IncludeModules,
        bool IncludeThreads,
        bool IncludeMemory,
        bool IncludePrivate,
        string? Since,
        int? Limit,
        string? Status,
        bool Diff,
        string? Left,
        string? Right);

    internal sealed record ReportDiffSnapshot(
        ReportDiffSource Left,
        ReportDiffSource Right,
        ReportDiffSummary Summary,
        IReadOnlyList<ReportDiffRow> Rows);

    internal sealed record ReportDiffSource(string Name, string Format, int RowCount);

    internal sealed record ReportDiffSummary(
        int LeftRowCount,
        int RightRowCount,
        int SharedRowCount,
        int ChangedRowCount,
        int AddedRowCount,
        int RemovedRowCount);

    internal sealed record ReportDiffRow(
        string Section,
        string Index,
        string Field,
        string? LeftValue,
        string? RightValue,
        string Status);

    internal sealed record ReportFile(string Name, string Format, IReadOnlyList<ReportRow> Rows);

    internal sealed record ReportRow(string Section, string Index, string Field, string Value) {
        public string Key => string.Join('\u001f', Section, Index, Field);
    }

    internal sealed record ReportModule(
        string Name,
        string Base,
        string Size,
        string OriginalSize,
        string? EntryPoint,
        DateTime? TimestampUtc);

    internal sealed record ReportThread(
        string Id,
        uint SuspendCount,
        uint Priority,
        uint CurrentProcessor,
        string? StartAddress,
        string StackBase,
        string StackLimit,
        string TlsBase,
        string? NameAddress);

    internal sealed record ReportMemoryRegion(
        string Base,
        string Size,
        string End,
        string Protect,
        string Phys,
        string Band);
}
