using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class XtafCliSupportConstants {
    public const string Owner = "SaveEditors";
    public const string Repository = "Xtaf-CLI";
    public const string ReleaseTag = "v1.0";
    public const string Version = "1.0.0";
    public const string WindowsZipAssetName = "Xtaf-CLI-v1.0.0-windows.zip";
    public const string WindowsZipUrl = "https://github.com/SaveEditors/Xtaf-CLI/releases/download/v1.0/Xtaf-CLI-v1.0.0-windows.zip";
    public const string WindowsZipSha256 = "D2C561074E55A61CC5D4DA9D3D7B9865EEC7886D2DA90BE49BB7AA8ED02B2E73";
    public const string WindowsExeSha256 = "78861947CD2F32865F782319EFD5B73E48F9DF4B42364511E593C2A82BDE6D01";
}

internal sealed record XtafCliStatusInfo(
    string? ConfiguredPath,
    string? ResolvedRoot,
    string? ResolvedExecutable,
    string ResolutionSource,
    string Version,
    bool Available,
    bool WinFspAvailable,
    string WinFspDetail) {
    public bool CoreReady => Available;
    public bool MountReady => CoreReady && WinFspAvailable;
    public bool Ready => MountReady;
}

internal static class XtafCliSupportHelpers {
    private static readonly string[] ExecutableNames = new[] {
        "Xtaf.exe",
        "Xtaf-CLI.exe",
        "xtaf.exe"
    };

    private static readonly string[] MountWorkflowFlags = new[] {
        "-m",
        "-a",
        "--all-partitions",
        "--mount-root"
    };

    public static string? ResolveConfiguredPath(CliConfig config, string? overridePath) {
        string? candidate = FirstNonEmpty(
            overridePath,
            config.XtafCliPath,
            Environment.GetEnvironmentVariable("XTAF_CLI_PATH"),
            Environment.GetEnvironmentVariable("XTAF_HOME"),
            Environment.GetEnvironmentVariable("XTAF_CLI_HOME"));

        return string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFullPath(candidate);
    }

    public static XtafCliStatusInfo BuildStatus(CliConfig config, string? overridePath) {
        string? configuredPath = ResolveConfiguredPath(config, overridePath);
        string? executable = ResolveExecutable(config, overridePath, out string source);
        string? root = !string.IsNullOrWhiteSpace(configuredPath)
            ? ResolveInstallRoot(configuredPath)
            : ResolveInstallRoot(executable);
        bool available = !string.IsNullOrWhiteSpace(executable) && File.Exists(executable);
        string version = available ? GetFileVersion(executable!) : "missing";
        bool winfspAvailable = IsWinFspAvailable(out string winfspDetail);

        return new XtafCliStatusInfo(
            configuredPath,
            root,
            executable,
            source,
            version,
            available,
            winfspAvailable,
            winfspDetail);
    }

    public static int GetStatusExitCode(XtafCliStatusInfo status, bool requireMount) {
        if (!status.CoreReady)
            return 1;

        return requireMount && !status.MountReady ? 1 : 0;
    }

    public static string? ResolveInstallRoot(string? configuredPath) {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        string path = Path.GetFullPath(configuredPath);
        if (Directory.Exists(path))
            return path;

        if (File.Exists(path))
            return Path.GetDirectoryName(path);

        return path;
    }

    public static string? ResolveExecutable(CliConfig config, string? overridePath, out string source) {
        string? configuredPath = ResolveConfiguredPath(config, overridePath);
        string? executable = ResolveExecutable(configuredPath, out source);
        if (!string.IsNullOrWhiteSpace(executable))
            return executable;

        string? pathExecutable = ResolveExecutableFromPath();
        if (!string.IsNullOrWhiteSpace(pathExecutable)) {
            source = "PATH";
            return pathExecutable;
        }

        source = configuredPath == null ? "unconfigured" : "configured path";
        return null;
    }

    public static bool IsMountWorkflow(IReadOnlyList<string> args) {
        foreach (string arg in args) {
            string normalized = arg.Trim();
            if (normalized.Length == 0)
                continue;

            if (normalized.Equals("mount", StringComparison.OrdinalIgnoreCase))
                return true;

            if (MountWorkflowFlags.Any(flag =>
                    normalized.Equals(flag, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public static bool IsWinFspAvailable(out string detail) {
        if (!OperatingSystem.IsWindows()) {
            detail = "WinFsp is only relevant on Windows.";
            return false;
        }

        (bool available, string probeDetail) = ProbeWinFspAvailability(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            File.Exists,
            Directory.Exists);

        detail = probeDetail;
        return available;
    }

    internal static (bool Available, string Detail) ProbeWinFspAvailability(
        string windowsDirectory,
        string programFilesDirectory,
        string programFilesX86Directory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists) {
        string[] driverCandidates = new[] {
            Path.Combine(windowsDirectory, "System32", "drivers", "winfsp-x64.sys"),
            Path.Combine(windowsDirectory, "System32", "drivers", "winfsp-x86.sys")
        };
        string? matchedDriver = driverCandidates.FirstOrDefault(fileExists);
        if (!string.IsNullOrWhiteSpace(matchedDriver)) {
            return (true, $"WinFsp driver file detected at {matchedDriver}.");
        }

        string[] installCandidates = new[] {
            Path.Combine(programFilesDirectory, "WinFsp"),
            Path.Combine(programFilesX86Directory, "WinFsp")
        };
        string? matchedInstall = installCandidates.FirstOrDefault(directoryExists);
        if (!string.IsNullOrWhiteSpace(matchedInstall)) {
            return (true, $"WinFsp install directory detected at {matchedInstall}.");
        }

        return (false, $"WinFsp 2.x was not detected under {programFilesDirectory} or {programFilesX86Directory}.");
    }

    public static string GetMountGuidance() {
        return "WinFsp 2.x is required for mount commands. Install WinFsp, then rerun the command.";
    }

    public static ProcessStartInfo CreateProcessStartInfo(string executablePath, IReadOnlyList<string> args, string? workingDirectory) {
        string path = Path.GetFullPath(executablePath);
        if (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            path = ResolveExecutableSibling(path) ?? throw new InvalidOperationException("Configured Xtaf-CLI path points to a .cmd wrapper. Use Xtaf.exe or Xtaf-CLI.exe so raw arguments are not routed through cmd.exe.");

        ProcessStartInfo psi = new ProcessStartInfo(path) {
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
        };
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    public static string InstallArchive(string archivePath, string installRoot) {
        string extractRoot = Path.Combine(CliPaths.CachePath, "xtaf-cli", $"extract-{Guid.NewGuid():N}");
        try {
            ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);
            Directory.CreateDirectory(installRoot);
            InstallHelpers.MirrorDirectory(extractRoot, installRoot);

            string? executable = ResolveExecutable(installRoot, out _);
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                throw new InvalidDataException("The archive did not contain Xtaf.exe, Xtaf-CLI.exe, or xtaf.exe.");

            return executable;
        }
        finally {
            ReverseEngineeringDownloadHelpers.TryDeleteDirectory(extractRoot);
        }
    }

    public static string GetDownloadCacheDirectory() {
        return ReverseEngineeringDownloadHelpers.GetDownloadCacheDirectory("xtaf-cli");
    }

    public static bool IsOfficialWindowsZipAssetName(string assetName) {
        return assetName.Equals(XtafCliSupportConstants.WindowsZipAssetName, StringComparison.Ordinal);
    }

    public static bool VerifySha256(string filePath, string expectedSha256, out string actualSha256, out string errorMessage) {
        return ReverseEngineeringDownloadHelpers.VerifySha256(filePath, expectedSha256, out actualSha256, out errorMessage);
    }

    private static string? ResolveExecutable(string? configuredPath, out string source) {
        source = "missing";
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        string path = Path.GetFullPath(configuredPath);
        if (File.Exists(path)) {
            if (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) {
                string? siblingExecutable = ResolveExecutableSibling(path);
                if (string.IsNullOrWhiteSpace(siblingExecutable))
                    return null;

                source = "configured wrapper";
                return siblingExecutable;
            }

            source = "configured file";
            return path;
        }

        if (Directory.Exists(path)) {
            foreach (string name in ExecutableNames) {
                string candidate = Path.Combine(path, name);
                if (File.Exists(candidate)) {
                    source = "configured directory";
                    return candidate;
                }
            }

            foreach (string candidate in Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories)) {
                if (string.Equals(Path.GetFileName(candidate), "Xtaf.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(candidate), "Xtaf-CLI.exe", StringComparison.OrdinalIgnoreCase)) {
                    source = "configured directory";
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? ResolveExecutableSibling(string commandWrapperPath) {
        string directory = Path.GetDirectoryName(Path.GetFullPath(commandWrapperPath)) ?? Environment.CurrentDirectory;
        foreach (string executableName in ExecutableNames) {
            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ResolveExecutableFromPath() {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (string entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            foreach (string name in ExecutableNames) {
                string candidate = Path.Combine(entry, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string GetFileVersion(string path) {
        try {
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(versionInfo.ProductVersion) && versionInfo.ProductVersion != "0.0.0.0")
                return versionInfo.ProductVersion!;
            if (!string.IsNullOrWhiteSpace(versionInfo.FileVersion) && versionInfo.FileVersion != "0.0.0.0")
                return versionInfo.FileVersion!;

            string sha256 = ReverseEngineeringDownloadHelpers.ComputeSha256(path);
            if (sha256.Equals(XtafCliSupportConstants.WindowsExeSha256, StringComparison.OrdinalIgnoreCase))
                return XtafCliSupportConstants.Version;
        }
        catch {
            // best-effort status only
        }

        return "unknown";
    }

    private static string Quote(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";

        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private static string? FirstNonEmpty(params string?[] candidates) {
        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public sealed class XtafCliConfigCommand : Command<XtafCliConfigCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Xtaf-CLI install directory or executable path override.")]
        public string? Path { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear the stored Xtaf-CLI path.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!CliConfig.TryLoad(out CliConfig config)) {
            OperationFeedback.WriteFailure(
                "Xtaf-CLI settings unavailable",
                "Config file is present but could not be parsed. Fix or remove config.json, then run rgh xtaf-cli config again.");
            return 1;
        }

        if (settings.Clear) {
            config.XtafCliPath = null;
            config.Save();
            OperationFeedback.WriteSuccess("Xtaf-CLI settings cleared", "[grey]Stored Xtaf-CLI paths were removed.[/]");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.Path)) {
            config.XtafCliPath = Path.GetFullPath(settings.Path);
            config.Save();
            OperationFeedback.WriteSuccess("Xtaf-CLI settings updated", $"[grey]Stored Xtaf-CLI path:[/] [white]{Markup.Escape(config.XtafCliPath)}[/]");
            return 0;
        }

        string? configured = config.XtafCliPath;
        string? resolved = XtafCliSupportHelpers.ResolveExecutable(config, null, out string source);

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Setting[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]Configured path[/]", $"[cyan]{Markup.Escape(configured ?? "unknown")}[/]");
        table.AddRow("[white]Resolved executable[/]", string.IsNullOrWhiteSpace(resolved)
            ? "[red]missing[/]"
            : $"[green]{Markup.Escape(resolved)}[/]");
        table.AddRow("[white]Resolution source[/]", $"[gold1]{Markup.Escape(source)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class XtafCliStatusCommand : Command<XtafCliStatusCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Xtaf-CLI install directory or executable path override.")]
        public string? Path { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit machine-readable output.")]
        public bool Json { get; init; }

        [CommandOption("--require-mount")]
        [LocalizedDescription("Require WinFsp mount support in addition to the Xtaf-CLI executable.")]
        public bool RequireMount { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        XtafCliStatusInfo status = XtafCliSupportHelpers.BuildStatus(config, settings.Path);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                status.ConfiguredPath,
                status.ResolvedRoot,
                status.ResolvedExecutable,
                status.ResolutionSource,
                status.Version,
                status.CoreReady,
                status.MountReady,
                status.Ready,
                status.Available,
                status.WinFspAvailable,
                status.WinFspDetail,
                MountRequired = settings.RequireMount
            });
            return XtafCliSupportHelpers.GetStatusExitCode(status, settings.RequireMount);
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]Configured path[/]", $"[cyan]{Markup.Escape(status.ConfiguredPath ?? "unknown")}[/]");
        table.AddRow("[white]Resolved root[/]", $"[cyan]{Markup.Escape(status.ResolvedRoot ?? "missing")}[/]");
        table.AddRow("[white]Executable[/]", status.Available
            ? $"[green]{Markup.Escape(status.ResolvedExecutable ?? "missing")}[/]"
            : "[red]missing[/]");
        table.AddRow("[white]Version[/]", $"[gold1]{Markup.Escape(status.Version)}[/]");
        table.AddRow("[white]Resolution source[/]", $"[grey]{Markup.Escape(status.ResolutionSource)}[/]");
        table.AddRow("[white]Core CLI[/]", status.CoreReady ? "[green]ready[/]" : "[red]not ready[/]");
        table.AddRow("[white]WinFsp mount support[/]", status.WinFspAvailable ? "[green]available[/]" : "[yellow]not detected[/]");
        table.AddRow("[white]Mount workflow[/]", status.MountReady ? "[green]ready[/]" : "[yellow]not ready[/]");
        AnsiConsole.Write(table);

        if (!status.WinFspAvailable)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(XtafCliSupportHelpers.GetMountGuidance())}[/]");

        return XtafCliSupportHelpers.GetStatusExitCode(status, settings.RequireMount);
    }
}

public sealed class XtafCliInstallCommand : AsyncCommand<XtafCliInstallCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Install directory override.")]
        public string? Path { get; init; }

        [CommandOption("--archive <FILE>")]
        [LocalizedDescription("Use a local Xtaf-CLI archive. Requires --sha256.")]
        public string? ArchivePath { get; init; }

        [CommandOption("--url <URL>")]
        [LocalizedDescription("Download Xtaf-CLI from an explicit URL. Requires --sha256.")]
        public string? Url { get; init; }

        [CommandOption("--sha256 <HASH>")]
        [LocalizedDescription("Required SHA256 for a custom --archive or --url source.")]
        public string? Sha256 { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();

        string installRoot = !string.IsNullOrWhiteSpace(settings.Path)
            ? Path.GetFullPath(settings.Path)
            : !string.IsNullOrWhiteSpace(config.XtafCliPath)
                ? Path.GetFullPath(config.XtafCliPath)
                : Path.Combine(CliPaths.CachePath, "xtaf-cli", "install");

        bool hasArchive = !string.IsNullOrWhiteSpace(settings.ArchivePath);
        bool hasUrl = !string.IsNullOrWhiteSpace(settings.Url);
        if (hasArchive && hasUrl) {
            OperationFeedback.WriteFailure("Conflicting Xtaf-CLI sources", "Use only one of --archive or --url.");
            return 1;
        }

        bool customSource = hasArchive || hasUrl;
        string expectedSha256 = customSource
            ? settings.Sha256 ?? string.Empty
            : XtafCliSupportConstants.WindowsZipSha256;
        if (customSource && string.IsNullOrWhiteSpace(settings.Sha256)) {
            OperationFeedback.WriteFailure(
                "Custom Xtaf-CLI source requires SHA256",
                "Pass `--sha256 <HASH>` with --archive or --url, or omit the custom source to use the pinned release.");
            return 1;
        }
        if (!ReverseEngineeringDownloadHelpers.TryNormalizeSha256(expectedSha256, out _, out string hashFormatError)) {
            OperationFeedback.WriteFailure("Invalid Xtaf-CLI SHA256", hashFormatError);
            return 1;
        }

        string archivePath;
        if (hasArchive) {
            archivePath = Path.GetFullPath(settings.ArchivePath!);
            if (!File.Exists(archivePath)) {
                OperationFeedback.WriteFailure("Archive not found", archivePath);
                return 1;
            }
        }
        else {
            string url = hasUrl
                ? settings.Url!
                : XtafCliSupportConstants.WindowsZipUrl;

            archivePath = await ReverseEngineeringDownloadHelpers.DownloadToCacheAsync(
                $"Download Xtaf-CLI {XtafCliSupportConstants.Version}",
                url,
                XtafCliSupportHelpers.GetDownloadCacheDirectory(),
                CancellationToken.None);
        }

        if (!XtafCliSupportHelpers.VerifySha256(archivePath, expectedSha256, out _, out string hashError)) {
            OperationFeedback.WriteFailure("Archive hash mismatch", hashError);
            return 1;
        }

        string executable = XtafCliSupportHelpers.InstallArchive(archivePath, installRoot);
        config.XtafCliPath = installRoot;
        config.Save();

        OperationFeedback.WriteSuccess(
            "Xtaf-CLI installed",
            $"[green]{Markup.Escape(Path.GetFileName(executable))} {XtafCliSupportConstants.Version}[/] -> [white]{Markup.Escape(executable)}[/]");
        return 0;
    }
}

public sealed class XtafCliRunCommand : AsyncCommand<XtafCliRunCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Xtaf-CLI install directory or executable path override.")]
        public string? Path { get; init; }

        [CommandOption("--arg <ARG>")]
        [LocalizedDescription("Argument to pass through to the Xtaf-CLI executable. Repeat for each raw argument.")]
        public string[] Args { get; init; } = Array.Empty<string>();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        string? executable = XtafCliSupportHelpers.ResolveExecutable(config, settings.Path, out _);
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) {
            OperationFeedback.WriteFailure(
                "Xtaf-CLI not configured",
                $"Configure a path with `rgh xtaf-cli config --path <dir>` or install pinned version {XtafCliSupportConstants.Version} with `rgh xtaf-cli install`.");
            return 1;
        }

        if (XtafCliSupportHelpers.IsMountWorkflow(settings.Args) && !XtafCliSupportHelpers.IsWinFspAvailable(out _)) {
            OperationFeedback.WriteFailure("WinFsp required", XtafCliSupportHelpers.GetMountGuidance());
            return 1;
        }

        string? workingDirectory = XtafCliSupportHelpers.ResolveInstallRoot(executable);
        ProcessStartInfo psi = XtafCliSupportHelpers.CreateProcessStartInfo(executable, settings.Args, workingDirectory);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;

        ProcessRunResult result = await ReverseEngineeringProcessHelpers.RunAsync(psi, null, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            Console.Write(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            Console.Error.Write(result.StandardError);

        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardError))
            OperationFeedback.WriteWarning("Xtaf-CLI exited with a non-zero code", $"Exit code {result.ExitCode}.");

        return result.ExitCode;
    }
}
