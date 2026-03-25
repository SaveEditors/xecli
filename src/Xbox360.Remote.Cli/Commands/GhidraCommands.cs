using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GhidraConfigCommand : Command<GhidraConfigCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [Description("Ghidra install directory (contains support/analyzeHeadless.bat).")]
        public string? Path { get; init; }

        [CommandOption("--java <DIR>")]
        [Description("JAVA_HOME to use for Ghidra.")]
        public string? JavaPath { get; init; }

        [CommandOption("--projects <DIR>")]
        [Description("Default Ghidra projects directory.")]
        public string? ProjectsPath { get; init; }

        [CommandOption("--clear")]
        [Description("Clear stored Ghidra settings.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteGhidraNotice();
        CliConfig config = CliConfig.Load();
        if (settings.Clear) {
            config.GhidraPath = null;
            config.GhidraJavaPath = null;
            config.GhidraProjectsPath = null;
            config.Save();
            AnsiConsole.MarkupLine("[green]Ghidra settings cleared.[/]");
            return 0;
        }

        bool updated = false;
        if (!string.IsNullOrWhiteSpace(settings.Path)) {
            config.GhidraPath = settings.Path;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.JavaPath)) {
            config.GhidraJavaPath = settings.JavaPath;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.ProjectsPath)) {
            config.GhidraProjectsPath = settings.ProjectsPath;
            updated = true;
        }

        if (updated) {
            config.Save();
            AnsiConsole.MarkupLine("[green]Ghidra settings updated.[/]");
            return 0;
        }

        string ghidra = config.GhidraPath ?? Environment.GetEnvironmentVariable("GHIDRA_HOME") ?? "unknown";
        string java = config.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME") ?? "unknown";
        string projects = config.GhidraProjectsPath ?? "unknown";
        AnsiConsole.MarkupLine($"[green]Ghidra:[/] {Markup.Escape(ghidra)}");
        AnsiConsole.MarkupLine($"[green]JAVA_HOME:[/] {Markup.Escape(java)}");
        AnsiConsole.MarkupLine($"[green]Projects:[/] {Markup.Escape(projects)}");
        return 0;
    }
}

public sealed class GhidraAnalyzeCommand : AsyncCommand<GhidraAnalyzeCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [Description("Input file to analyze (XEX).")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [Description("Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex).")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [Description("Use the running title XEX (resolved via XBDM + FTP).")]
        public bool Running { get; init; }

        [CommandOption("--project <NAME>")]
        [Description("Project name (default: <file>_headless).")]
        public string? ProjectName { get; init; }

        [CommandOption("--projects <DIR>")]
        [Description("Project root directory override.")]
        public string? ProjectsPath { get; init; }

        [CommandOption("--path <DIR>")]
        [Description("Ghidra install directory override.")]
        public string? GhidraPath { get; init; }

        [CommandOption("--java <DIR>")]
        [Description("JAVA_HOME override.")]
        public string? JavaPath { get; init; }

        [CommandOption("--loader <NAME>")]
        [Description("Explicit loader name.")]
        public string? Loader { get; init; }

        [CommandOption("--timeout <SEC>")]
        [Description("Analysis timeout per file (seconds, default: 5000).")]
        public int? TimeoutSeconds { get; init; } = 5000;

        [CommandOption("--delete-project")]
        [Description("Delete the project before import.")]
        public bool DeleteProject { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite existing file in project.")]
        public bool Overwrite { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteGhidraNotice();
        string? inputPath = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
        if (string.IsNullOrWhiteSpace(inputPath))
            return 1;

        CliConfig config = CliConfig.Load();
        string? ghidraHome = settings.GhidraPath ?? config.GhidraPath ?? Environment.GetEnvironmentVariable("GHIDRA_HOME");
        if (string.IsNullOrWhiteSpace(ghidraHome)) {
            AnsiConsole.MarkupLine("[red]Ghidra path not set. Use `rgh ghidra config --path <dir>`.[/]");
            return 1;
        }

        string analyzePath = Path.Combine(ghidraHome, "support", "analyzeHeadless.bat");
        if (!File.Exists(analyzePath)) {
            AnsiConsole.MarkupLine($"[red]analyzeHeadless.bat not found at[/] {Markup.Escape(analyzePath)}");
            return 1;
        }

        string projectRoot = settings.ProjectsPath ?? config.GhidraProjectsPath ??
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ghidra_projects");
        string projectName = settings.ProjectName ?? $"{Path.GetFileNameWithoutExtension(inputPath)}_headless";

        List<string> args = new List<string> {
            projectRoot,
            projectName
        };

        if (settings.DeleteProject)
            args.Add("-deleteProject");
        if (settings.Overwrite)
            args.Add("-overwrite");

        args.Add("-import");
        args.Add(inputPath);

        if (!string.IsNullOrWhiteSpace(settings.Loader)) {
            args.Add("-loader");
            args.Add(settings.Loader!);
        }
        else if (GhidraLoaderHelpers.IsXexFile(inputPath)) {
            if (GhidraLoaderHelpers.HasXexLoader(ghidraHome))
                AnsiConsole.MarkupLine("[grey]XEX loader detected; using auto-detect.[/]");
            else
                OperationFeedback.WriteWarning("XEX loader not detected", "[grey]Install XEXLoaderWV with `rgh ghidra install-loader` after configuring the Ghidra path.[/]");
        }

        if (settings.TimeoutSeconds.HasValue) {
            args.Add("-analysisTimeoutPerFile");
            args.Add(settings.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }

        string? javaHome = settings.JavaPath ?? config.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME");

        string commandLine = Quote(analyzePath) + " " + string.Join(" ", args.Select(Quote));
        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c {commandLine}") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(javaHome))
            psi.Environment["JAVA_HOME"] = javaHome;
        string ghidraUserDir = Path.Combine(CliPaths.CachePath, "ghidra_user");
        Directory.CreateDirectory(ghidraUserDir);
        psi.Environment["GHIDRA_USER_DIR"] = ghidraUserDir;
        psi.Environment["APPDATA"] = ghidraUserDir;
        psi.Environment["LOCALAPPDATA"] = ghidraUserDir;
        psi.Environment["USERPROFILE"] = ghidraUserDir;

        using Process proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.WriteLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Data)}[/]");
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    private static string Quote(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
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
}

public sealed class GhidraDecompileCommand : AsyncCommand<GhidraDecompileCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [Description("Input file to analyze (XEX).")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [Description("Fetch the XEX via FTP before analysis (e.g. /Hdd1/Aurora/Aurora.xex).")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [Description("Use the running title XEX (resolved via XBDM + FTP).")]
        public bool Running { get; init; }

        [CommandOption("--out <DIR>")]
        [Description("Output directory for decompiled C files.")]
        public string? Output { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum number of functions to decompile (default: all).")]
        public int? MaxFunctions { get; init; }

        [CommandOption("--func-timeout <SEC>")]
        [Description("Decompile timeout per function (seconds, default: 5000).")]
        public int? FunctionTimeoutSeconds { get; init; } = 5000;

        [CommandOption("--project <NAME>")]
        [Description("Project name (default: <file>_headless).")]
        public string? ProjectName { get; init; }

        [CommandOption("--projects <DIR>")]
        [Description("Project root directory override.")]
        public string? ProjectsPath { get; init; }

        [CommandOption("--path <DIR>")]
        [Description("Ghidra install directory override.")]
        public string? GhidraPath { get; init; }

        [CommandOption("--java <DIR>")]
        [Description("JAVA_HOME override.")]
        public string? JavaPath { get; init; }

        [CommandOption("--loader <NAME>")]
        [Description("Explicit loader name.")]
        public string? Loader { get; init; }

        [CommandOption("--timeout <SEC>")]
        [Description("Analysis timeout per file (seconds, default: 5000).")]
        public int? TimeoutSeconds { get; init; } = 5000;

        [CommandOption("--delete-project")]
        [Description("Delete the project before import.")]
        public bool DeleteProject { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite existing file in project.")]
        public bool Overwrite { get; init; }

        [CommandOption("--script-path <DIR>")]
        [Description("Override script path (default: ghidra_scripts next to rgh.exe).")]
        public string? ScriptPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteGhidraNotice();
        if (string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--out is required.[/]");
            return 1;
        }

        string? inputPath = await ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
        if (string.IsNullOrWhiteSpace(inputPath))
            return 1;

        CliConfig config = CliConfig.Load();
        string? ghidraHome = settings.GhidraPath ?? config.GhidraPath ?? Environment.GetEnvironmentVariable("GHIDRA_HOME");
        if (string.IsNullOrWhiteSpace(ghidraHome)) {
            AnsiConsole.MarkupLine("[red]Ghidra path not set. Use `rgh ghidra config --path <dir>`.[/]");
            return 1;
        }

        string analyzePath = Path.Combine(ghidraHome, "support", "analyzeHeadless.bat");
        if (!File.Exists(analyzePath)) {
            AnsiConsole.MarkupLine($"[red]analyzeHeadless.bat not found at[/] {Markup.Escape(analyzePath)}");
            return 1;
        }

        string scriptRoot = settings.ScriptPath ?? Path.Combine(AppContext.BaseDirectory, "ghidra_scripts");
        string scriptFile = Path.Combine(scriptRoot, "DecompileAllToC.java");
        if (!File.Exists(scriptFile)) {
            AnsiConsole.MarkupLine($"[red]Decompile script not found at[/] {Markup.Escape(scriptFile)}");
            return 1;
        }

        string projectRoot = settings.ProjectsPath ?? config.GhidraProjectsPath ??
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ghidra_projects");
        string projectName = settings.ProjectName ?? $"{Path.GetFileNameWithoutExtension(inputPath)}_headless";

        List<string> args = new List<string> {
            projectRoot,
            projectName
        };

        if (settings.DeleteProject)
            args.Add("-deleteProject");
        if (settings.Overwrite)
            args.Add("-overwrite");

        args.Add("-import");
        args.Add(inputPath);

        if (!string.IsNullOrWhiteSpace(settings.Loader)) {
            args.Add("-loader");
            args.Add(settings.Loader!);
        }
        else if (GhidraLoaderHelpers.IsXexFile(inputPath)) {
            if (GhidraLoaderHelpers.HasXexLoader(ghidraHome))
                AnsiConsole.MarkupLine("[grey]XEX loader detected; using auto-detect.[/]");
            else
                OperationFeedback.WriteWarning("XEX loader not detected", "[grey]Install XEXLoaderWV with `rgh ghidra install-loader` after configuring the Ghidra path.[/]");
        }

        if (settings.TimeoutSeconds.HasValue) {
            args.Add("-analysisTimeoutPerFile");
            args.Add(settings.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-scriptPath");
        args.Add(scriptRoot);
        args.Add("-postScript");
        args.Add("DecompileAllToC.java");
        args.Add(settings.Output);

        if (settings.MaxFunctions.HasValue) {
            args.Add(settings.MaxFunctions.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (settings.FunctionTimeoutSeconds.HasValue) {
            args.Add(settings.FunctionTimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture));
        }

        string? javaHome = settings.JavaPath ?? config.GhidraJavaPath ?? Environment.GetEnvironmentVariable("JAVA_HOME");

        string commandLine = Quote(analyzePath) + " " + string.Join(" ", args.Select(Quote));
        ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c {commandLine}") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(javaHome))
            psi.Environment["JAVA_HOME"] = javaHome;
        string ghidraUserDir = Path.Combine(CliPaths.CachePath, "ghidra_user");
        Directory.CreateDirectory(ghidraUserDir);
        psi.Environment["GHIDRA_USER_DIR"] = ghidraUserDir;
        psi.Environment["APPDATA"] = ghidraUserDir;
        psi.Environment["LOCALAPPDATA"] = ghidraUserDir;
        psi.Environment["USERPROFILE"] = ghidraUserDir;

        using Process proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.WriteLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Data)}[/]");
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    private static string Quote(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";
        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
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
}

public sealed class GhidraVerifyCommand : Command<GhidraVerifyCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--dir <DIR>")]
        [Description("Directory containing decompiled output to verify.")]
        public string? Directory { get; init; }

        [CommandOption("--pattern <REGEX>")]
        [Description("Regex pattern to flag (default: baddata|Bad instruction).")]
        public string Pattern { get; init; } = "baddata|Bad instruction";

        [CommandOption("--ext <EXT>")]
        [Description("File extension to scan (default: .c).")]
        public string Extension { get; init; } = ".c";

        [CommandOption("--max <N>")]
        [Description("Maximum matching files to display (default: 25).")]
        public int? MaxResults { get; init; }

        [CommandOption("--json")]
        [Description("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteGhidraNotice();
        if (string.IsNullOrWhiteSpace(settings.Directory)) {
            AnsiConsole.MarkupLine("[red]--dir is required.[/]");
            return 1;
        }

        if (!Directory.Exists(settings.Directory)) {
            AnsiConsole.MarkupLine("[red]Directory not found.[/]");
            return 1;
        }

        string extension = settings.Extension;
        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;

        Regex regex;
        try {
            regex = new Regex(settings.Pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex) {
            AnsiConsole.MarkupLine($"[red]Invalid regex:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        int maxResults = Math.Max(1, settings.MaxResults ?? 25);
        int fileCount = 0;
        int matchCount = 0;
        List<(string File, string Line)> matches = new List<(string, string)>();

        foreach (string file in Directory.EnumerateFiles(settings.Directory, "*" + extension, SearchOption.AllDirectories)) {
            fileCount++;
            foreach (string line in File.ReadLines(file)) {
                if (!regex.IsMatch(line))
                    continue;
                matchCount++;
                if (matches.Count < maxResults) {
                    matches.Add((file, line.Trim()));
                }
                break;
            }
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Directory = settings.Directory,
                Files = fileCount,
                Matches = matchCount,
                Examples = matches.Select(m => new { m.File, m.Line }).ToList()
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Ghidra Verify[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]Scanned:[/] {fileCount} files");
        AnsiConsole.MarkupLine($"[grey]Flagged:[/] {matchCount} files");

        if (matchCount == 0) {
            AnsiConsole.MarkupLine("[green]No flagged files found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]File[/]"));
        table.AddColumn(new TableColumn("[yellow]Match[/]"));
        foreach ((string file, string line) in matches) {
            table.AddRow(Markup.Escape(file), Markup.Escape(line));
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

internal static class GhidraInputHelpers {
    public static async Task<string?> ResolveRunningFtpPathAsync() {
        try {
            using XbdmClient client = await CliHelpers.ConnectAsync(new ConnectionSettings(), CancellationToken.None);
            string? devicePath = await client.GetRunningXexPathAsync(null, CancellationToken.None);
            return MapDevicePathToFtpPath(devicePath);
        }
        catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]Failed to resolve running XEX:[/] {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    public static async Task<string?> DownloadViaFtpAsync(string ftpPath) {
        string normalized = FtpHelpers.NormalizePath(ftpPath);
        string fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "downloaded.xex";

        string cacheDir = CliPaths.CachePath;
        Directory.CreateDirectory(cacheDir);
        string localPath = Path.Combine(cacheDir, fileName);

        await FtpHelpers.WithClientAsync(new FtpConnectionSettings(), async client => {
            long size = await FtpHelpers.TryGetFileSizeAsync(client, normalized) ?? 0;
            await CliOutput.RunWithProgressAsync($"FTP fetch {Markup.Escape(normalized)}", size > 0 ? size : null, async progress => {
                Progress<FluentFTP.FtpProgress> ftpProgress = new Progress<FluentFTP.FtpProgress>(p => {
                    if (p.TransferredBytes >= 0)
                        progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "receiving"));
                });
                await client.DownloadFile(localPath, normalized, FluentFTP.FtpLocalExists.Overwrite, FluentFTP.FtpVerify.None, ftpProgress);
            });
            return 0;
        }, CancellationToken.None);

        OperationFeedback.WriteSuccess(
            "FTP fetch complete",
            $"[cyan]{Markup.Escape(normalized)}[/] -> [white]{Markup.Escape(localPath)}[/]");
        return localPath;
    }

    public static string? MapDevicePathToFtpPath(string? devicePath) {
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;
        string path = devicePath.Trim().Replace('\\', '/');
        if (!path.StartsWith("/", StringComparison.Ordinal))
            path = "/" + path;

        if (TryMapPrefix(path, "/Device/Harddisk0/Partition1/", "/Hdd1/", out string mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Harddisk0/Partition2/", "/HddX/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Harddisk0/Partition0/", "/Hdd0/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Usb0/", "/Usb0/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Usb1/", "/Usb1/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Usb2/", "/Usb2/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Mu/", "/Mu/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/IntMu/", "/IntMu/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/MmcMu/", "/MmcMu/", out mapped))
            return mapped;
        if (TryMapPrefix(path, "/Device/Cdrom0/", "/D/", out mapped))
            return mapped;

        return null;
    }

    private static bool TryMapPrefix(string path, string prefix, string target, out string mapped) {
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
            string rest = path.Substring(prefix.Length);
            mapped = target + rest;
            return true;
        }

        mapped = string.Empty;
        return false;
    }
}

internal static class GhidraLoaderHelpers {
    public static bool IsXexFile(string path) {
        if (path.EndsWith(".xex", StringComparison.OrdinalIgnoreCase))
            return true;
        try {
            using FileStream stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) != 4)
                return false;
            return magic[0] == (byte) 'X' && magic[1] == (byte) 'E' && magic[2] == (byte) 'X' && magic[3] == (byte) '2';
        }
        catch {
            return false;
        }
    }

    public static bool HasXexLoader(string? ghidraHome) {
        if (string.IsNullOrWhiteSpace(ghidraHome))
            return CheckUserExtensions();
        string direct = Path.Combine(ghidraHome, "Ghidra", "Extensions", "XEXLoaderWV", "lib", "XEXLoaderWV.jar");
        if (File.Exists(direct))
            return true;
        string extensions = Path.Combine(ghidraHome, "Ghidra", "Extensions");
        if (!Directory.Exists(extensions))
            return CheckUserExtensions();
        if (Directory.EnumerateFiles(extensions, "XEXLoaderWV.jar", SearchOption.AllDirectories).Any())
            return true;
        return CheckUserExtensions();
    }

    private static bool CheckUserExtensions() {
        try {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                return false;
            string ghidraRoot = Path.Combine(appData, "ghidra");
            if (!Directory.Exists(ghidraRoot))
                return false;
            return Directory.EnumerateFiles(ghidraRoot, "XEXLoaderWV.jar", SearchOption.AllDirectories).Any();
        }
        catch {
            return false;
        }
    }
}
