using System.ComponentModel;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaConfigCommand : Command<IdaConfigCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [Description("IDA install directory (must contain idat.exe).")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [Description("Python executable or command used for idalib helpers.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [Description("IDA user directory override for headless runs.")]
        public string? UserPath { get; init; }

        [CommandOption("--backend <NAME>")]
        [Description("Preferred backend: auto, batch, or idalib.")]
        public string? PreferredBackend { get; init; }

        [CommandOption("--clear")]
        [Description("Clear stored IDA settings.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        CliConfig config = CliConfig.Load();
        if (settings.Clear) {
            config.IdaPath = null;
            config.IdaPythonPath = null;
            config.IdaUserPath = null;
            config.IdaPreferredBackend = null;
            config.Save();
            OperationFeedback.WriteSuccess("IDA settings cleared", "[grey]Stored IDA paths and backend preferences were removed.[/]");
            return 0;
        }

        bool updated = false;
        if (!string.IsNullOrWhiteSpace(settings.IdaPath)) {
            string idaHome = Path.GetFullPath(settings.IdaPath);
            string batchExe = IdaPathHelpers.ResolveBatchExe(idaHome);
            if (!File.Exists(batchExe)) {
                OperationFeedback.WriteFailure("Invalid IDA install", $"idat.exe was not found under {idaHome}");
                return 1;
            }
            config.IdaPath = idaHome;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.PythonPath)) {
            config.IdaPythonPath = settings.PythonPath;
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.UserPath)) {
            config.IdaUserPath = Path.GetFullPath(settings.UserPath);
            updated = true;
        }

        if (!string.IsNullOrWhiteSpace(settings.PreferredBackend)) {
            try {
                config.IdaPreferredBackend = IdaPathHelpers.NormalizeBackend(settings.PreferredBackend);
            }
            catch (InvalidOperationException ex) {
                OperationFeedback.WriteFailure("Invalid backend", ex.Message);
                return 1;
            }
            updated = true;
        }

        if (updated) {
            config.Save();
            OperationFeedback.WriteSuccess("IDA settings updated", "[grey]Stored IDA install, python, and backend settings were saved.[/]");
            return 0;
        }

        string idaHomeDisplay = config.IdaPath ?? Environment.GetEnvironmentVariable("IDA_HOME") ?? "unknown";
        string pythonDisplay = config.IdaPythonPath ?? "python";
        string userDisplay = config.IdaUserPath ?? Path.Combine(CliPaths.CachePath, "ida", "user");
        string backendDisplay = config.IdaPreferredBackend ?? "auto";

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Setting[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]IDA[/]", $"[cyan]{Markup.Escape(idaHomeDisplay)}[/]");
        table.AddRow("[white]Python[/]", $"[cyan]{Markup.Escape(pythonDisplay)}[/]");
        table.AddRow("[white]IDAUSR[/]", $"[cyan]{Markup.Escape(userDisplay)}[/]");
        table.AddRow("[white]Preferred Backend[/]", $"[gold1]{Markup.Escape(backendDisplay)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class IdaCheckCommand : AsyncCommand<IdaCheckCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [Description("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [Description("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [Description("IDA user directory override.")]
        public string? UserPath { get; init; }

        [CommandOption("--backend <NAME>")]
        [Description("Preferred backend override.")]
        public string? PreferredBackend { get; init; }

        [CommandOption("--json")]
        [Description("Emit JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        CliConfig config = CliConfig.Load();
        string? idaHome = IdaPathHelpers.ResolveIdaHome(config, settings.IdaPath);
        if (string.IsNullOrWhiteSpace(idaHome)) {
            OperationFeedback.WriteFailure("IDA path not set", "Use `rgh ida config --path <dir>` first.");
            return 1;
        }

        string batchExe = IdaPathHelpers.ResolveBatchExe(idaHome);
        string pythonCommand = IdaPathHelpers.ResolvePythonCommand(config, settings.PythonPath);
        string userDirectory = IdaPathHelpers.ResolveUserDirectory(config, settings.UserPath);
        string preferredBackend = IdaPathHelpers.ResolvePreferredBackend(config, settings.PreferredBackend);
        string? productVersion = IdaRuntimeHelpers.TryGetProductVersion(batchExe);
        IdaLoaderStatus loaderStatus = IdaRuntimeHelpers.GetLoaderStatus(idaHome);
        bool tilPresent = File.Exists(Path.Combine(idaHome, "til", "ppc", "x360.til")) &&
                          File.Exists(Path.Combine(idaHome, "til", "ppc", "xkelib.til"));
        bool idalibAvailable = File.Exists(batchExe) && await IdaRuntimeHelpers.CanImportIdaproAsync(pythonCommand, CancellationToken.None);
        bool versionSupported = IdaRuntimeHelpers.IsSupportedProductVersion(productVersion);
        bool overallOk = File.Exists(batchExe) && versionSupported && loaderStatus.IsSupported && tilPresent && idalibAvailable;

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Install = idaHome,
                BatchExe = batchExe,
                ProductVersion = productVersion,
                VersionSupported = versionSupported,
                Idaxex = loaderStatus.Label,
                IdaxexSha256 = loaderStatus.Sha256,
                TilPresent = tilPresent,
                Python = pythonCommand,
                IdaUser = userDirectory,
                PreferredBackend = preferredBackend,
                IdalibAvailable = idalibAvailable,
                OverallOk = overallOk
            });
            return overallOk ? 0 : 1;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Check[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]Install[/]", $"[cyan]{Markup.Escape(idaHome)}[/]");
        table.AddRow("[white]Batch EXE[/]", File.Exists(batchExe)
            ? $"[green]{Markup.Escape(batchExe)}[/]"
            : $"[red]{Markup.Escape(batchExe)}[/]");
        table.AddRow("[white]IDA build[/]", string.IsNullOrWhiteSpace(productVersion)
            ? "[red]unknown[/]"
            : versionSupported
                ? $"[green]{Markup.Escape(productVersion)} (supported)[/]"
                : $"[yellow]{Markup.Escape(productVersion)}[/]");
        table.AddRow("[white]idaxex[/]", loaderStatus.IsSupported
            ? $"[green]{Markup.Escape(loaderStatus.Label)}[/]"
            : loaderStatus.IsKnownNewerVariant
                ? $"[yellow]{Markup.Escape(loaderStatus.Label)}[/]"
                : $"[red]{Markup.Escape(loaderStatus.Label)}[/]");
        table.AddRow("[white]TIL files[/]", tilPresent ? "[green]present[/]" : "[red]missing[/]");
        table.AddRow("[white]Python[/]", $"[cyan]{Markup.Escape(pythonCommand)}[/]");
        table.AddRow("[white]IDAUSR[/]", $"[cyan]{Markup.Escape(userDirectory)}[/]");
        table.AddRow("[white]Preferred backend[/]", $"[gold1]{Markup.Escape(preferredBackend)}[/]");
        table.AddRow("[white]idalib import[/]", idalibAvailable ? "[green]ok[/]" : "[red]missing[/]");
        AnsiConsole.Write(table);

        return overallOk ? 0 : 1;
    }
}

public sealed class IdaInstallLoaderCommand : AsyncCommand<IdaInstallLoaderCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [Description("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--archive <FILE>")]
        [Description("Use a local idaxex 0.42b archive instead of downloading one.")]
        public string? ArchivePath { get; init; }

        [CommandOption("--url <URL>")]
        [Description("Download idaxex from an explicit URL instead of the supported default.")]
        public string? Url { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        CliConfig config = CliConfig.Load();
        string? idaHome = IdaPathHelpers.ResolveIdaHome(config, settings.IdaPath);
        if (string.IsNullOrWhiteSpace(idaHome)) {
            OperationFeedback.WriteFailure("IDA path not set", "Use `rgh ida config --path <dir>` first.");
            return 1;
        }

        string batchExe = IdaPathHelpers.ResolveBatchExe(idaHome);
        if (!File.Exists(batchExe)) {
            OperationFeedback.WriteFailure("Invalid IDA install", $"idat.exe was not found under {idaHome}");
            return 1;
        }

        string archivePath;
        if (!string.IsNullOrWhiteSpace(settings.ArchivePath)) {
            archivePath = Path.GetFullPath(settings.ArchivePath);
            if (!File.Exists(archivePath)) {
                OperationFeedback.WriteFailure("Archive not found", archivePath);
                return 1;
            }
        }
        else {
            string url = !string.IsNullOrWhiteSpace(settings.Url)
                ? settings.Url
                : ReverseEngineeringSupportConstants.SupportedIdaxexArchiveUrl;
            archivePath = await ReverseEngineeringDownloadHelpers.DownloadToCacheAsync(
                "Download idaxex 0.42b",
                url,
                ReverseEngineeringDownloadHelpers.GetDownloadCacheDirectory("ida"),
                CancellationToken.None);
        }

        try {
            IdaCommandSupport.InstallSupportedIdaxexArchive(archivePath, idaHome);
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("idaxex install failed", ex.Message);
            return 1;
        }

        IdaLoaderStatus loaderStatus = IdaRuntimeHelpers.GetLoaderStatus(idaHome);
        if (!loaderStatus.IsSupported) {
            OperationFeedback.WriteFailure("Unexpected idaxex version", $"Installed loader hash did not match the supported {ReverseEngineeringSupportConstants.SupportedIdaxexLabel} build.");
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "IDA loader installed",
            $"[green]{Markup.Escape(ReverseEngineeringSupportConstants.SupportedIdaxexLabel)}[/] -> [white]{Markup.Escape(loaderStatus.LoaderPath ?? Path.Combine(idaHome, "loaders", "idaxex.dll"))}[/]");
        return 0;
    }
}

public sealed class IdaAnalyzeCommand : AsyncCommand<IdaAnalyzeCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [Description("Input file to analyze (XEX or existing database).")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [Description("Fetch the XEX via FTP before analysis.")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [Description("Use the running title XEX resolved over XBDM + FTP.")]
        public bool Running { get; init; }

        [CommandOption("--out-db <FILE>")]
        [Description("Output database path (.i64).")]
        public string? OutputDatabase { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite an existing database.")]
        public bool Overwrite { get; init; }

        [CommandOption("--path <DIR>")]
        [Description("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [Description("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [Description("IDA user directory override.")]
        public string? UserPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        string? inputPath = await IdaCommandSupport.ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
        if (string.IsNullOrWhiteSpace(inputPath))
            return 1;

        CliConfig config = CliConfig.Load();
        IdaEnvironmentContext environment;
        try {
            environment = IdaRuntimeHelpers.ResolveEnvironment(config, settings.IdaPath, settings.PythonPath, settings.UserPath, "batch");
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("IDA environment not ready", ex.Message);
            return 1;
        }

        if (!File.Exists(environment.BatchExe)) {
            OperationFeedback.WriteFailure("Invalid IDA install", $"idat.exe was not found under {environment.Home}");
            return 1;
        }

        string outputDatabase = Path.GetFullPath(settings.OutputDatabase ?? IdaCommandSupport.GetDefaultDatabasePath(inputPath));
        if (File.Exists(outputDatabase) && !settings.Overwrite) {
            OperationFeedback.WriteFailure("Database already exists", "Use --overwrite or choose a different --out-db path.");
            return 1;
        }

        try {
            IdaCommandSupport.IdaAnalyzeResult summary = await IdaCommandSupport.RunBatchAnalyzeAsync(environment, inputPath, outputDatabase, CancellationToken.None);
            OperationFeedback.WriteSuccess(
                "IDA analysis complete",
                $"[white]{Markup.Escape(summary.Database ?? outputDatabase)}[/] [grey]segments[/] [gold1]{summary.Segments}[/] [grey]functions[/] [gold1]{summary.Functions}[/]");
            return 0;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("IDA analysis failed", ex.Message);
            return 1;
        }
    }
}

public sealed class IdaDecompileCommand : AsyncCommand<IdaDecompileCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [Description("Input XEX or database path.")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [Description("Fetch the XEX via FTP before decompiling.")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [Description("Use the running title XEX resolved over XBDM + FTP.")]
        public bool Running { get; init; }

        [CommandOption("--out <DIR>")]
        [Description("Output directory for C files.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum number of functions to decompile (default: all).")]
        public int? MaxFunctions { get; init; }

        [CommandOption("--backend <NAME>")]
        [Description("Backend: auto, batch, or idalib.")]
        public string? Backend { get; init; }

        [CommandOption("--out-db <FILE>")]
        [Description("Database path to create or reuse for raw XEX input.")]
        public string? OutputDatabase { get; init; }

        [CommandOption("--overwrite-db")]
        [Description("Overwrite the database when importing a raw XEX.")]
        public bool OverwriteDatabase { get; init; }

        [CommandOption("--keep-db")]
        [Description("Keep the generated database even when using a temporary cache path.")]
        public bool KeepDatabase { get; init; }

        [CommandOption("--path <DIR>")]
        [Description("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [Description("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [Description("IDA user directory override.")]
        public string? UserPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        if (string.IsNullOrWhiteSpace(settings.OutputDirectory)) {
            OperationFeedback.WriteFailure("Missing output directory", "--out is required.");
            return 1;
        }

        string? resolvedInput = await IdaCommandSupport.ResolveInputAsync(settings.Input, settings.FtpPath, settings.Running);
        if (string.IsNullOrWhiteSpace(resolvedInput))
            return 1;

        string outputDirectory = Path.GetFullPath(settings.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        CliConfig config = CliConfig.Load();
        IdaEnvironmentContext environment;
        try {
            environment = IdaRuntimeHelpers.ResolveEnvironment(config, settings.IdaPath, settings.PythonPath, settings.UserPath, settings.Backend);
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("IDA environment not ready", ex.Message);
            return 1;
        }

        if (!File.Exists(environment.BatchExe)) {
            OperationFeedback.WriteFailure("Invalid IDA install", $"idat.exe was not found under {environment.Home}");
            return 1;
        }

        bool generatedDatabase = false;
        string workingDatabase = resolvedInput;
        try {
            if (!IdaRuntimeHelpers.IsDatabasePath(resolvedInput)) {
                workingDatabase = Path.GetFullPath(settings.OutputDatabase ?? IdaCommandSupport.GetDefaultDatabasePath(resolvedInput));
                bool keepDatabase = settings.KeepDatabase || !string.IsNullOrWhiteSpace(settings.OutputDatabase);
                generatedDatabase = true;

                if (!File.Exists(workingDatabase) || settings.OverwriteDatabase) {
                    IdaCommandSupport.IdaAnalyzeResult analyzeSummary = await IdaCommandSupport.RunBatchAnalyzeAsync(environment, resolvedInput, workingDatabase, CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(analyzeSummary.Database))
                        workingDatabase = analyzeSummary.Database!;
                }

                bool idalibAvailable = await IdaRuntimeHelpers.CanImportIdaproAsync(environment.PythonCommand, CancellationToken.None);
                string backend = IdaRuntimeHelpers.ResolveDecompileBackend(environment.PreferredBackend, workingDatabase, idalibAvailable);
                IdaCommandSupport.IdaDecompileResult result = backend == "idalib"
                    ? await IdaCommandSupport.RunIdalibDecompileAsync(environment, workingDatabase, outputDirectory, settings.MaxFunctions, CancellationToken.None)
                    : await IdaCommandSupport.RunBatchDecompileAsync(environment, workingDatabase, outputDirectory, settings.MaxFunctions, CancellationToken.None);
                OperationFeedback.WriteSuccess(
                    "IDA decompile complete",
                    $"[green]{result.Decompiled}[/] file(s) [grey]backend[/] [gold1]{Markup.Escape(result.Backend)}[/] [grey]output[/] [white]{Markup.Escape(outputDirectory)}[/]");

                if (!keepDatabase)
                    IdaCommandSupport.TryDeleteDatabaseArtifacts(workingDatabase);
                return 0;
            }

            bool idalibAvailableForDb = await IdaRuntimeHelpers.CanImportIdaproAsync(environment.PythonCommand, CancellationToken.None);
            string selectedBackend = IdaRuntimeHelpers.ResolveDecompileBackend(environment.PreferredBackend, workingDatabase, idalibAvailableForDb);
            IdaCommandSupport.IdaDecompileResult dbResult = selectedBackend == "idalib"
                ? await IdaCommandSupport.RunIdalibDecompileAsync(environment, workingDatabase, outputDirectory, settings.MaxFunctions, CancellationToken.None)
                : await IdaCommandSupport.RunBatchDecompileAsync(environment, workingDatabase, outputDirectory, settings.MaxFunctions, CancellationToken.None);
            OperationFeedback.WriteSuccess(
                "IDA decompile complete",
                $"[green]{dbResult.Decompiled}[/] file(s) [grey]backend[/] [gold1]{Markup.Escape(dbResult.Backend)}[/] [grey]output[/] [white]{Markup.Escape(outputDirectory)}[/]");
            return 0;
        }
        catch (Exception ex) {
            if (generatedDatabase && !settings.KeepDatabase && string.IsNullOrWhiteSpace(settings.OutputDatabase))
                IdaCommandSupport.TryDeleteDatabaseArtifacts(workingDatabase);
            OperationFeedback.WriteFailure("IDA decompile failed", ex.Message);
            return 1;
        }
    }
}

public sealed class IdaVerifyCommand : Command<IdaVerifyCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--dir <DIR>")]
        [Description("Directory containing IDA decompiler output to verify.")]
        public string? Directory { get; init; }

        [CommandOption("--pattern <REGEX>")]
        [Description("Regex pattern to flag (default: could not decompile|BAD).")]
        public string Pattern { get; init; } = "could not decompile|BAD";

        [CommandOption("--ext <EXT>")]
        [Description("File extension to scan (default: .c).")]
        public string Extension { get; init; } = ".c";

        [CommandOption("--max <N>")]
        [Description("Maximum matching files to display (default: 25).")]
        public int? MaxResults { get; init; }

        [CommandOption("--json")]
        [Description("Emit JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        if (string.IsNullOrWhiteSpace(settings.Directory)) {
            OperationFeedback.WriteFailure("Missing directory", "--dir is required.");
            return 1;
        }

        if (!Directory.Exists(settings.Directory)) {
            OperationFeedback.WriteFailure("Directory not found", settings.Directory);
            return 1;
        }

        string extension = settings.Extension.StartsWith(".", StringComparison.Ordinal) ? settings.Extension : "." + settings.Extension;
        Regex regex;
        try {
            regex = new Regex(settings.Pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex) {
            OperationFeedback.WriteFailure("Invalid regex", ex.Message);
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
                if (matches.Count < maxResults)
                    matches.Add((file, line.Trim()));
                break;
            }
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Directory = settings.Directory,
                Files = fileCount,
                Matches = matchCount,
                Examples = matches.Select(match => new { match.File, match.Line }).ToList()
            });
            return matchCount == 0 ? 0 : 1;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]IDA Verify[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]Scanned:[/] {fileCount} files");
        AnsiConsole.MarkupLine($"[grey]Flagged:[/] {matchCount} files");

        if (matchCount == 0) {
            AnsiConsole.MarkupLine("[green]No flagged files found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]File[/]"));
        table.AddColumn(new TableColumn("[yellow]Match[/]"));
        foreach ((string file, string line) in matches)
            table.AddRow(Markup.Escape(file), Markup.Escape(line));
        AnsiConsole.Write(table);
        return 1;
    }
}
