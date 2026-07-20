using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

public abstract class BundleCommandSettings : CommandSettings {
        [CommandOption("--out <PATH>")]
        [LocalizedDescription("Output zip file or folder path. Default: xecli-diagnostics-<timestamp>.zip or xecli-support-<timestamp>.zip in the current directory.")]
        public string? Output { get; init; }

        [CommandOption("--folder")]
        [LocalizedDescription("Write an unpacked bundle folder instead of a zip archive.")]
        public bool Folder { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Overwrite an existing zip file or replace an existing bundle folder.")]
        public bool Force { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

public sealed class DiagnosticsBundleCommand : Command<DiagnosticsBundleCommand.Settings> {
    public sealed class Settings : BundleCommandSettings {
        [CommandOption("--support")]
        [LocalizedDescription("Write the support-facing bundle layout and wording.")]
        public bool Support { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        return BundleCommandExecutor.Execute(settings, support: settings.Support, surfaceName: "Diagnostics");
    }
}

public sealed class SupportBundleCommand : Command<SupportBundleCommand.Settings> {
    public sealed class Settings : BundleCommandSettings {
    }

    public override int Execute(CommandContext context, Settings settings) {
        return BundleCommandExecutor.Execute(settings, support: true, surfaceName: "Support");
    }
}

internal static class BundleCommandExecutor {
    public static int Execute(BundleCommandSettings settings, bool support, string surfaceName) {
        try {
            DiagnosticsBundleResult result = DiagnosticsBundleBuilder.Create(new DiagnosticsBundleRequest(
                settings.Output,
                settings.Folder,
                settings.Force,
                support));

            string displayName = XexInfoCommand.GetDisplayFileName(result.OutputPath);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Surface = surfaceName,
                    Output = displayName,
                    result.Kind,
                    result.FileCount,
                    result.CommandLogEntries
                });
            }
            else {
                OperationFeedback.WriteSuccess(
                    $"{surfaceName} bundle created",
                    $"[grey]Output:[/] [cyan]{Markup.Escape(displayName)}[/]\n" +
                    $"[grey]Format:[/] [cyan]{Markup.Escape(result.Kind)}[/]\n" +
                    $"[grey]Files:[/] [cyan]{result.FileCount.ToString(CultureInfo.InvariantCulture)}[/]\n" +
                    $"[grey]Command log entries:[/] [cyan]{result.CommandLogEntries.ToString(CultureInfo.InvariantCulture)}[/]");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
            WriteFailure(settings.Json, surfaceName, CommandLogRedactor.RedactFreeText(ex.Message));
            return 1;
        }
    }

    private static void WriteFailure(bool json, string surfaceName, string message) {
        if (json) {
            CliOutput.EmitJson(new {
                Error = new {
                    Title = $"{surfaceName} bundle",
                    Message = message
                }
            });
            return;
        }

        OperationFeedback.WriteFailure($"{surfaceName} bundle", message);
    }
}

internal sealed record DiagnosticsBundleRequest(
    string? OutputPath,
    bool Folder,
    bool Force,
    bool Support = false,
    DateTimeOffset? GeneratedUtc = null);

internal sealed record DiagnosticsBundleResult(
    string OutputPath,
    string Kind,
    int FileCount,
    int CommandLogEntries);

internal static class DiagnosticsBundleBuilder {
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new() {
        WriteIndented = false
    };

    public static DiagnosticsBundleResult Create(DiagnosticsBundleRequest request) {
        DateTimeOffset generatedUtc = request.GeneratedUtc ?? DateTimeOffset.UtcNow;
        string outputPath = ResolveOutputPath(request.OutputPath, request.Folder, request.Support, generatedUtc);
        string stagingRoot = Path.Combine(CliPaths.CachePath, request.Support ? "support" : "diagnostics", Guid.NewGuid().ToString("N"));
        List<string> files = [];
        List<string> warnings = [];
        string reportFileName = request.Support ? "support-report.md" : "diagnostics-report.md";

        try {
            Directory.CreateDirectory(stagingRoot);

            DiagnosticsReleaseCheckSnapshot releaseCheck = BuildReleaseCheckSnapshot();
            HealthReport healthReport = HealthReportBuilder.Build(includePrivate: false);
            IReadOnlyList<CommandLogEntry> commandLogEntries = ReadRedactedCommandLog();
            string configJson = BuildConfigJson(warnings);
            WriteBundleFile(stagingRoot, reportFileName, BuildReport(request.Support, generatedUtc, commandLogEntries.Count, releaseCheck, warnings), files);
            WriteBundleFile(stagingRoot, "version.json", BuildVersionJson(generatedUtc), files);
            WriteBundleFile(stagingRoot, "health.json", BuildHealthJson(generatedUtc, healthReport), files);
            WriteBundleFile(stagingRoot, "config-redacted.json", configJson, files);
            WriteBundleFile(stagingRoot, "command-log.jsonl", BuildCommandLogJsonLines(commandLogEntries), files);
            WriteBundleFile(stagingRoot, "command-log.csv", LogExportCommand.RenderCsv(commandLogEntries), files);
            WriteBundleFile(stagingRoot, "release-check.json", BuildReleaseCheckJson(generatedUtc, releaseCheck), files);

            IReadOnlyList<string> manifestFiles = files.Concat(["manifest.json"]).ToArray();
            WriteBundleFile(stagingRoot, "manifest.json", BuildManifestJson(request.Support, generatedUtc, request.Folder, reportFileName, manifestFiles, warnings), files);

            if (request.Folder)
                CopyStagedFolder(stagingRoot, outputPath, request.Force);
            else
                WriteZipArchive(stagingRoot, outputPath, request.Force);

            return new DiagnosticsBundleResult(
                outputPath,
                request.Folder ? "folder" : "zip",
                files.Count,
                commandLogEntries.Count);
        }
        finally {
            TryDeleteDirectory(stagingRoot);
        }
    }

    internal static string ResolveOutputPath(string? outputPath, bool folder, bool support, DateTimeOffset generatedUtc) {
        string baseName = support
            ? $"xecli-support-{generatedUtc:yyyyMMdd-HHmmss}"
            : $"xecli-diagnostics-{generatedUtc:yyyyMMdd-HHmmss}";

        if (string.IsNullOrWhiteSpace(outputPath)) {
            return Path.Combine(Environment.CurrentDirectory, folder ? baseName : baseName + ".zip");
        }

        string fullPath = Path.GetFullPath(outputPath);
        if (folder)
            return fullPath;

        if (Directory.Exists(fullPath))
            return Path.Combine(fullPath, baseName + ".zip");

        return string.IsNullOrWhiteSpace(Path.GetExtension(fullPath))
            ? fullPath + ".zip"
            : fullPath;
    }

    private static IReadOnlyList<CommandLogEntry> ReadRedactedCommandLog() {
        return CommandLogStore.ReadAll()
            .Select(entry => entry with {
                CommandName = CommandLogRedactor.RedactFreeText(entry.CommandName),
                CommandLine = RedactStoredCommandLine(entry.CommandLine),
                Error = CommandLogRedactor.RedactFreeText(entry.Error)
            })
            .ToArray();
    }

    private static string RedactStoredCommandLine(string? commandLine) {
        if (string.IsNullOrWhiteSpace(commandLine))
            return string.Empty;

        return CommandLogRedactor.RedactCommandLine(CommandLineTokenizer.Split(commandLine));
    }

    private static string BuildCommandLogJsonLines(IReadOnlyList<CommandLogEntry> entries) {
        StringBuilder sb = new();
        foreach (CommandLogEntry entry in entries)
            sb.AppendLine(JsonSerializer.Serialize(entry, LineJsonOptions));

        return sb.ToString();
    }

    private static string BuildReport(bool support, DateTimeOffset generatedUtc, int commandLogEntries, DiagnosticsReleaseCheckSnapshot releaseCheck, IReadOnlyList<string> warnings) {
        StringBuilder sb = new();
        sb.AppendLine(support ? "# XeCLI Support Bundle" : "# XeCLI Diagnostics Bundle");
        sb.AppendLine();
        sb.AppendLine($"Generated UTC: {generatedUtc:O}");
        sb.AppendLine($"Application version: {GetApplicationVersion()}");
        sb.AppendLine($"Command log entries: {commandLogEntries.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("Live console connection: not attempted");
        sb.AppendLine("Privacy: local paths, IP addresses, console IDs, and known secrets are redacted.");
        sb.AppendLine();
        sb.AppendLine("## Release Check");
        sb.AppendLine($"- Status: {EscapeMarkdown(releaseCheck.Status)}");
        if (releaseCheck.Available) {
            sb.AppendLine($"- Publish directory: {EscapeMarkdown(releaseCheck.PublishDir ?? string.Empty)}");
            sb.AppendLine($"- Project file: {EscapeMarkdown(releaseCheck.ProjectPath ?? string.Empty)}");
            if (!string.IsNullOrWhiteSpace(releaseCheck.ZipPath))
                sb.AppendLine($"- Release zip: {EscapeMarkdown(releaseCheck.ZipPath)}");
            sb.AppendLine($"- Checks reviewed: {releaseCheck.CheckCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"- Failures: {releaseCheck.FailureCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"- Warnings: {releaseCheck.WarningCount.ToString(CultureInfo.InvariantCulture)}");
        }
        else if (!string.IsNullOrWhiteSpace(releaseCheck.Detail)) {
            sb.AppendLine($"- Detail: {EscapeMarkdown(releaseCheck.Detail)}");
        }
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine("- manifest.json");
        sb.AppendLine(support ? "- support-report.md" : "- diagnostics-report.md");
        sb.AppendLine("- version.json");
        sb.AppendLine("- health.json");
        sb.AppendLine("- config-redacted.json");
        sb.AppendLine("- command-log.jsonl");
        sb.AppendLine("- command-log.csv");
        sb.AppendLine("- release-check.json");

        if (warnings.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            foreach (string warning in warnings)
                sb.AppendLine("- " + EscapeMarkdown(CommandLogRedactor.RedactFreeText(warning)));
        }

        return sb.ToString();
    }

    private static string BuildVersionJson(DateTimeOffset generatedUtc) {
        object payload = new {
            GeneratedUtc = generatedUtc,
            Application = "XeCLI",
            Version = GetApplicationVersion(),
            AssemblyVersion = typeof(DiagnosticsBundleCommand).Assembly.GetName().Version?.ToString(),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture,
            Environment.Is64BitProcess,
            CurrentCulture = CultureInfo.CurrentCulture.Name,
            CurrentUICulture = CultureInfo.CurrentUICulture.Name
        };

        return JsonSerializer.Serialize(payload, PrettyJsonOptions) + Environment.NewLine;
    }

    private static string BuildHealthJson(DateTimeOffset generatedUtc, HealthReport report) {
        object payload = new {
            SchemaVersion = 1,
            GeneratedUtc = generatedUtc,
            Healthy = report.IsHealthy,
            Checks = report.Checks.Select(check => new {
                check.Section,
                check.Name,
                check.Status,
                check.Severity,
                check.Code,
                Message = CommandLogRedactor.RedactFreeText(check.Message),
                Remediation = string.IsNullOrWhiteSpace(check.Remediation)
                    ? null
                    : CommandLogRedactor.RedactFreeText(check.Remediation)
            }).ToArray(),
            TargetSummary = new {
                report.Target.ConfigPresent,
                report.Target.ConfigParsed,
                report.Target.StorePresent,
                report.Target.StoreReadable,
                report.Target.ProfileCount,
                IssueCount = report.Target.Issues.Count
            },
            CommandLogSummary = new {
                report.CommandLog.Present,
                report.CommandLog.Readable,
                report.CommandLog.Entries,
                report.CommandLog.MalformedLines
            }
        };

        return JsonSerializer.Serialize(payload, PrettyJsonOptions) + Environment.NewLine;
    }

    private static string BuildReleaseCheckJson(DateTimeOffset generatedUtc, DiagnosticsReleaseCheckSnapshot releaseCheck) {
        object payload = new {
            SchemaVersion = 1,
            GeneratedUtc = generatedUtc,
            releaseCheck.Available,
            releaseCheck.Status,
            releaseCheck.Detail,
            releaseCheck.PublishDir,
            releaseCheck.ProjectPath,
            releaseCheck.ZipPath,
            releaseCheck.HashPath,
            releaseCheck.Passed,
            releaseCheck.FailureCount,
            releaseCheck.WarningCount,
            releaseCheck.CheckCount,
            Checks = releaseCheck.Checks.Select(check => new {
                check.Name,
                check.Status,
                check.Code,
                Detail = check.Detail
            }).ToArray()
        };

        return JsonSerializer.Serialize(payload, PrettyJsonOptions) + Environment.NewLine;
    }

    private static string BuildConfigJson(List<string> warnings) {
        bool present = File.Exists(CliPaths.ConfigPath);
        if (!present) {
            return JsonSerializer.Serialize(new {
                Present = false
            }, PrettyJsonOptions) + Environment.NewLine;
        }

        try {
            CliConfig config = CliConfig.Load();
            object payload = new {
                Present = true,
                Defaults = new {
                    TargetIp = RedactIfPresent(config.DefaultIp),
                    config.DefaultPort,
                    config.DefaultFtpPort,
                    DefaultFtpUser = RedactIfPresent(config.DefaultFtpUser),
                    DefaultFtpPassword = RedactIfPresent(config.DefaultFtpPassword)
                },
                Tools = new {
                    GhidraPath = RedactText(config.GhidraPath),
                    GhidraJavaPath = RedactText(config.GhidraJavaPath),
                    GhidraProjectsPath = RedactText(config.GhidraProjectsPath),
                    IdaPath = RedactText(config.IdaPath),
                    IdaPythonPath = RedactText(config.IdaPythonPath),
                    IdaUserPath = RedactText(config.IdaUserPath),
                    config.IdaPreferredBackend,
                    XtafCliPath = RedactText(config.XtafCliPath)
                },
                Avatar = new {
                    AvatarLibraryRoot = RedactText(config.AvatarLibraryRoot),
                    AvatarCachePath = RedactText(config.AvatarCachePath),
                    AvatarManifestUrl = RedactIfPresent(config.AvatarManifestUrl),
                    AvatarTitleMapUrl = RedactIfPresent(config.AvatarTitleMapUrl),
                    AvatarContentBaseUrl = RedactIfPresent(config.AvatarContentBaseUrl),
                    AvatarDownloadCachePath = RedactText(config.AvatarDownloadCachePath)
                },
                Ui = new {
                    config.UiLanguage,
                    config.PathPromptHandled
                },
                Discord = new {
                    DiscordClientId = RedactIfPresent(config.DiscordClientId)
                },
                Counts = new {
                    NotifyIcons = config.NotifyIcons?.Count ?? 0,
                    ModuleHandles = config.ModuleHandles?.Count ?? 0,
                    ExtraData = config.ExtraData?.Count ?? 0
                },
                PendingModuleOperation = config.PendingModuleOperation == null ? null : new {
                    config.PendingModuleOperation.Action,
                    ModuleName = RedactText(config.PendingModuleOperation.ModuleName),
                    ModulePath = RedactText(config.PendingModuleOperation.ModulePath),
                    config.PendingModuleOperation.SystemThread,
                    config.PendingModuleOperation.CreatedUtc
                },
                LastLedState = config.LastLedState,
                LastFanState = config.LastFanState
            };

            return JsonSerializer.Serialize(payload, PrettyJsonOptions) + Environment.NewLine;
        }
        catch (Exception ex) {
            warnings.Add("config: " + CommandLogRedactor.RedactFreeText(ex.Message));
            return JsonSerializer.Serialize(new {
                Present = true,
                Error = CommandLogRedactor.RedactFreeText(ex.Message)
            }, PrettyJsonOptions) + Environment.NewLine;
        }
    }

    private static string BuildManifestJson(bool support, DateTimeOffset generatedUtc, bool folder, string reportFileName, IReadOnlyList<string> files, IReadOnlyList<string> warnings) {
        object payload = new {
            Schema = 1,
            GeneratedUtc = generatedUtc,
            Kind = folder ? "folder" : "zip",
            Application = "XeCLI",
            Version = GetApplicationVersion(),
            Bundle = support ? "support" : "diagnostics",
            ReportFile = reportFileName,
            Files = files,
            Warnings = warnings.Select(CommandLogRedactor.RedactFreeText).ToArray(),
            LiveConsoleConnection = false,
            Redaction = "Local paths, IP addresses, console IDs, and known secrets are redacted."
        };

        return JsonSerializer.Serialize(payload, PrettyJsonOptions) + Environment.NewLine;
    }

    private static void WriteBundleFile(string root, string relativePath, string content, List<string> files) {
        string fullPath = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        files.Add(relativePath.Replace('\\', '/'));
    }

    private static void CopyStagedFolder(string stagingRoot, string outputPath, bool force) {
        if (Directory.Exists(outputPath) && !force && Directory.EnumerateFileSystemEntries(outputPath).Any())
            throw new IOException("Output folder already exists. Use --force to replace it.");

        if (Directory.Exists(outputPath) && force)
            Directory.Delete(outputPath, recursive: true);

        Directory.CreateDirectory(outputPath);
        foreach (string sourcePath in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(stagingRoot, sourcePath);
            string destinationPath = Path.Combine(outputPath, relativePath);
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourcePath, destinationPath, overwrite: force);
        }
    }

    private static void WriteZipArchive(string stagingRoot, string outputPath, bool force) {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(outputPath)) {
            if (!force)
                throw new IOException("Output zip already exists. Use --force to overwrite it.");

            File.Delete(outputPath);
        }

        ZipFile.CreateFromDirectory(stagingRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false, entryNameEncoding: Encoding.UTF8);
    }

    private static string GetApplicationVersion() {
        Assembly assembly = typeof(DiagnosticsBundleCommand).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static string? RedactText(string? value) {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CommandLogRedactor.RedactFreeText(value);
    }

    private static string? RedactIfPresent(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : "redacted";
    }

    private static string EscapeMarkdown(string value) {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static DiagnosticsReleaseCheckSnapshot BuildReleaseCheckSnapshot() {
        string? repoRoot = TryFindRepoRoot(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(repoRoot)) {
            return new DiagnosticsReleaseCheckSnapshot(
                Available: false,
                Status: "unavailable",
                Detail: "No local repository root was found.",
                PublishDir: null,
                ProjectPath: null,
                ZipPath: null,
                HashPath: null,
                Passed: false,
                FailureCount: 0,
                WarningCount: 0,
                CheckCount: 0,
                Checks: Array.Empty<DiagnosticsReleaseCheckItem>());
        }

        string publishDir = Path.Combine(repoRoot, "out", "win-x64");
        string projectPath = Path.Combine(repoRoot, "src", "Xbox360.Remote.Cli", "Xbox360.Remote.Cli.csproj");
        ReleaseCheckReport report = ReleaseCheckEngine.Check(new ReleaseCheckRequest(publishDir, projectPath, null, null));
        string status = report.Passed
            ? (report.WarningCount > 0 ? "warnings" : "passed")
            : "failed";

        return new DiagnosticsReleaseCheckSnapshot(
            Available: true,
            Status: status,
            Detail: report.Passed
                ? (report.WarningCount > 0 ? "Release check completed with warnings." : "Release check passed.")
                : "Release check failed.",
            PublishDir: Path.GetRelativePath(repoRoot, publishDir).Replace('\\', '/'),
            ProjectPath: Path.GetRelativePath(repoRoot, projectPath).Replace('\\', '/'),
            ZipPath: string.IsNullOrWhiteSpace(report.ZipPath) ? null : Path.GetRelativePath(repoRoot, report.ZipPath).Replace('\\', '/'),
            HashPath: string.IsNullOrWhiteSpace(report.HashPath) ? null : Path.GetRelativePath(repoRoot, report.HashPath).Replace('\\', '/'),
            Passed: report.Passed,
            FailureCount: report.FailureCount,
            WarningCount: report.WarningCount,
            CheckCount: report.Checks.Count,
            Checks: report.Checks.Select(check => new DiagnosticsReleaseCheckItem(
                check.Name,
                check.Status,
                check.Code,
                CommandLogRedactor.RedactFreeText(check.Detail))).ToArray());
    }

    private static string? TryFindRepoRoot(string startPath) {
        string? current = Directory.Exists(startPath) ? Path.GetFullPath(startPath) : Path.GetDirectoryName(Path.GetFullPath(startPath));
        while (!string.IsNullOrWhiteSpace(current)) {
            string gitDir = Path.Combine(current, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir))
                return current;

            DirectoryInfo? parent = Directory.GetParent(current);
            current = parent?.FullName;
        }

        return null;
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch {
            // Diagnostics cleanup should not hide the bundle result.
        }
    }
}

internal sealed record DiagnosticsReleaseCheckSnapshot(
    bool Available,
    string Status,
    string Detail,
    string? PublishDir,
    string? ProjectPath,
    string? ZipPath,
    string? HashPath,
    bool Passed,
    int FailureCount,
    int WarningCount,
    int CheckCount,
    IReadOnlyList<DiagnosticsReleaseCheckItem> Checks);

internal sealed record DiagnosticsReleaseCheckItem(
    string Name,
    string Status,
    string Code,
    string Detail);
