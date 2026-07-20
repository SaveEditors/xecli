using System.ComponentModel;
using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaConfigCommand : Command<IdaConfigCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("IDA install directory (must contain idat.exe).")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [LocalizedDescription("Python executable or command used for idalib helpers.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [LocalizedDescription("IDA user directory override for headless runs.")]
        public string? UserPath { get; init; }

        [CommandOption("--backend <NAME>")]
        [LocalizedDescription("Preferred backend: auto, batch, or idalib.")]
        public string? PreferredBackend { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear stored IDA settings.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice();

        if (!CliConfig.TryLoad(out CliConfig config)) {
            OperationFeedback.WriteFailure(
                "IDA settings unavailable",
                "Config file is present but could not be parsed. Fix or remove config.json, then run rgh ida config again.");
            return 1;
        }
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
        [LocalizedDescription("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [LocalizedDescription("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [LocalizedDescription("IDA user directory override.")]
        public string? UserPath { get; init; }

        [CommandOption("--backend <NAME>")]
        [LocalizedDescription("Preferred backend override.")]
        public string? PreferredBackend { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice(settings.Json);

        CliConfig config = CliConfig.Load();
        string? idaHome = IdaPathHelpers.ResolveIdaHome(config, settings.IdaPath);
        string batchExe = string.IsNullOrWhiteSpace(idaHome)
            ? "missing"
            : IdaPathHelpers.ResolveBatchExe(idaHome);
        string? batchProductVersion = string.IsNullOrWhiteSpace(idaHome)
            ? null
            : IdaRuntimeHelpers.TryGetProductVersion(batchExe);
        string batchProductVersionDisplay = IdaRuntimeHelpers.DescribeProductVersion(batchProductVersion);
        string activationScript = string.IsNullOrWhiteSpace(idaHome)
            ? "missing"
            : IdaPathHelpers.ResolveActivationScript(idaHome) ?? "missing";
        string userDirectory = IdaPathHelpers.ResolveUserDirectory(config, settings.UserPath);
        string? configuredPython = !string.IsNullOrWhiteSpace(settings.PythonPath)
            ? settings.PythonPath
            : config.IdaPythonPath;
        string pythonDisplay = string.IsNullOrWhiteSpace(configuredPython) ? "unconfigured" : configuredPython;
        string preferredBackend = IdaPathHelpers.ResolvePreferredBackend(config, settings.PreferredBackend);
        bool batchReady = File.Exists(batchExe);
        bool activationReady = File.Exists(activationScript);
        IdaLoaderStatus loaderStatus = string.IsNullOrWhiteSpace(idaHome)
            ? new IdaLoaderStatus(null, null, "missing", false, false)
            : IdaRuntimeHelpers.GetLoaderStatus(idaHome);
        IdaxexBuildSpec? expectedLoaderBuild = ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildForProductVersion(batchProductVersion);
        bool idaxexReady = IdaRuntimeHelpers.IsLoaderCompatibleWithProductVersion(loaderStatus, batchProductVersion);
        string idaxexStatus = loaderStatus.IsSupported && !idaxexReady && expectedLoaderBuild != null
            ? $"{loaderStatus.Label}; expected {expectedLoaderBuild.Label}"
            : loaderStatus.Label;
        bool idalibFilesReady = !string.IsNullOrWhiteSpace(idaHome) &&
                                (activationReady || Directory.Exists(Path.Combine(idaHome, "idalib", "python")));
        bool? idalibImportReady = null;
        if (!string.IsNullOrWhiteSpace(configuredPython)) {
            try {
                idalibImportReady = await IdaRuntimeHelpers.CanImportIdaproAsync(configuredPython, CancellationToken.None);
            }
            catch {
                idalibImportReady = false;
            }
        }

        string batchExeDisplay = batchReady ? batchExe : "missing";
        string activationScriptDisplay = activationReady ? activationScript : "missing";
        bool batchProductVersionSupported = IdaRuntimeHelpers.IsSupportedProductVersion(batchProductVersion);
        bool batchBackendReady = batchReady && batchProductVersionSupported && idaxexReady;
        bool idalibBackendReady = batchBackendReady && idalibFilesReady && idalibImportReady == true;
        bool preferredBackendReady = IdaRuntimeHelpers.IsPreferredBackendReady(preferredBackend, batchBackendReady, idalibBackendReady);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                InstallPath = string.IsNullOrWhiteSpace(idaHome) ? "unknown" : idaHome,
                BatchExecutable = batchExeDisplay,
                IdaBuild = batchProductVersionDisplay,
                IdaBuildSupported = batchProductVersionSupported,
                ActivationScript = activationScriptDisplay,
                IdaUser = userDirectory,
                Python = pythonDisplay,
                PreferredBackend = preferredBackend,
                Ready = preferredBackendReady,
                BatchBackendReady = batchBackendReady,
                IdalibBackendReady = idalibBackendReady,
                PreferredBackendReady = preferredBackendReady,
                Idaxex = idaxexReady,
                IdaxexPath = loaderStatus.LoaderPath,
                IdaxexSha256 = loaderStatus.Sha256,
                IdaxexStatus = idaxexStatus,
                IdaxexExpectedBuild = expectedLoaderBuild?.Label,
                IdaxexExpectedSha256 = expectedLoaderBuild?.DllSha256,
                IdaxexBuildMatchesIda = idaxexReady,
                IdalibFiles = idalibFilesReady,
                IdalibImport = idalibImportReady,
                BatchExecutableReady = batchReady,
                BatchExecutableExpected = !string.IsNullOrWhiteSpace(idaHome),
                ActivationScriptReady = activationReady,
                ActivationScriptExpected = !string.IsNullOrWhiteSpace(idaHome),
                IdaxexReady = idaxexReady,
                IdaxexExpected = !string.IsNullOrWhiteSpace(idaHome),
                IdalibFilesReady = idalibFilesReady,
                IdalibFilesExpected = !string.IsNullOrWhiteSpace(idaHome),
                IdalibImportReady = idalibImportReady,
                IdalibImportExpected = !string.IsNullOrWhiteSpace(configuredPython)
            });
            return preferredBackendReady ? 0 : 1;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]Install[/]", $"[cyan]{Markup.Escape(string.IsNullOrWhiteSpace(idaHome) ? "unknown" : idaHome)}[/]");
        table.AddRow("[white]Batch EXE[/]", batchReady ? $"[green]{Markup.Escape(batchExe)}[/]" : "[red]missing[/]");
        table.AddRow("[white]IDA build[/]", batchReady
            ? batchProductVersionSupported
                ? $"[green]{Markup.Escape(batchProductVersionDisplay)}[/]"
                : $"[yellow]{Markup.Escape(batchProductVersionDisplay)}[/]"
            : "[red]missing[/]");
        table.AddRow("[white]Activation Script[/]", activationReady ? $"[green]{Markup.Escape(activationScript)}[/]" : "[red]missing[/]");
        table.AddRow("[white]IDAUSR[/]", $"[cyan]{Markup.Escape(userDirectory)}[/]");
        table.AddRow("[white]Python[/]", $"[cyan]{Markup.Escape(pythonDisplay)}[/]");
        table.AddRow("[white]Preferred backend[/]", $"[gold1]{Markup.Escape(preferredBackend)}[/]");
        table.AddRow("[white]Batch backend[/]", batchBackendReady ? "[green]ready[/]" : "[red]not ready[/]");
        table.AddRow("[white]idalib backend[/]", idalibBackendReady ? "[green]ready[/]" : "[yellow]not ready[/]");
        table.AddRow("[white]Selected readiness[/]", preferredBackendReady ? "[green]ready[/]" : "[red]not ready[/]");
        string idaxexStatusMarkup = idaxexReady
            ? $"[green]{Markup.Escape(idaxexStatus)}[/]"
            : loaderStatus.LoaderPath == null
                ? "[red]missing[/]"
                : $"[yellow]{Markup.Escape(idaxexStatus)}[/]";
        table.AddRow("[white]idaxex loader[/]", idaxexStatusMarkup);
        table.AddRow("[white]idaxex SHA-256[/]", loaderStatus.Sha256 == null
            ? "[grey]not available[/]"
            : $"[grey]{Markup.Escape(loaderStatus.Sha256)}[/]");
        table.AddRow("[white]idalib files[/]", idalibFilesReady ? "[green]present[/]" : "[red]missing[/]");
        table.AddRow("[white]idalib import[/]", !idalibImportReady.HasValue
            ? "[grey]not checked[/]"
            : idalibImportReady.Value
                ? "[green]ok[/]"
                : "[red]failed[/]");
        AnsiConsole.Write(table);

        return preferredBackendReady ? 0 : 1;
    }
}

public sealed class IdaInstallLoaderCommand : AsyncCommand<IdaInstallLoaderCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--archive <FILE>")]
        [LocalizedDescription("Use a local idaxex archive. Requires --sha256; every installed payload is checked against the detected IDA build manifest.")]
        public string? ArchivePath { get; init; }

        [CommandOption("--url <URL>")]
        [LocalizedDescription("Download idaxex from an explicit URL. Requires --sha256; every installed payload is checked against the detected IDA build manifest.")]
        public string? Url { get; init; }

        [CommandOption("--sha256 <HASH>")]
        [LocalizedDescription("Required SHA256 for a custom --archive or --url source.")]
        public string? Sha256 { get; init; }
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

        string? productVersion = IdaRuntimeHelpers.TryGetProductVersion(batchExe);
        IdaxexBuildSpec? targetBuild = ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildForProductVersion(productVersion);
        if (targetBuild == null) {
            OperationFeedback.WriteFailure(
                "Unsupported IDA build",
                $"Detected {productVersion ?? "unknown"}. Supported loader targets: {ReverseEngineeringSupportConstants.SupportedIdaSummary}.");
            return 1;
        }

        bool hasArchive = !string.IsNullOrWhiteSpace(settings.ArchivePath);
        bool hasUrl = !string.IsNullOrWhiteSpace(settings.Url);
        if (hasArchive && hasUrl) {
            OperationFeedback.WriteFailure("Conflicting idaxex sources", "Use only one of --archive or --url.");
            return 1;
        }

        bool customSource = hasArchive || hasUrl;
        string expectedArchiveSha256 = customSource
            ? settings.Sha256 ?? string.Empty
            : targetBuild.ArchiveSha256;
        if (customSource && string.IsNullOrWhiteSpace(settings.Sha256)) {
            OperationFeedback.WriteFailure(
                "Custom idaxex source requires SHA256",
                "Pass `--sha256 <HASH>` with --archive or --url, or omit the custom source to use the pinned build for the detected IDA version.");
            return 1;
        }
        if (!ReverseEngineeringDownloadHelpers.TryNormalizeSha256(expectedArchiveSha256, out _, out string hashFormatError)) {
            OperationFeedback.WriteFailure("Invalid idaxex SHA256", hashFormatError);
            return 1;
        }

        string archivePath;
        string sourceLabel = customSource ? $"authenticated custom source for {targetBuild.Label}" : targetBuild.Label;
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
                : targetBuild.ArchiveUrl;
            IdaxexBuildSpec? sourceBuild = hasUrl
                ? ReverseEngineeringSupportConstants.ResolveSupportedIdaxexBuildByArchiveUrl(url)
                : targetBuild;
            if (sourceBuild != null &&
                !string.Equals(sourceBuild.DllSha256, targetBuild.DllSha256, StringComparison.OrdinalIgnoreCase)) {
                OperationFeedback.WriteFailure(
                    "Incompatible idaxex build",
                    $"{sourceBuild.Label} does not match the detected {targetBuild.IdaDisplayVersion} install.");
                return 1;
            }
            archivePath = await ReverseEngineeringDownloadHelpers.DownloadToCacheAsync(
                $"Download {sourceLabel}",
                url,
                ReverseEngineeringDownloadHelpers.GetDownloadCacheDirectory("ida"),
                CancellationToken.None);
        }

        try {
            IdaCommandSupport.InstallSupportedIdaxexArchive(
                archivePath,
                idaHome,
                targetBuild,
                expectedArchiveSha256);
        }
        catch (UnauthorizedAccessException) {
            string loaderPath = Path.Combine(idaHome, "loaders", "idaxex.dll");
            OperationFeedback.WriteFailure(
                "idaxex install blocked",
                $"XeCLI could not write {loaderPath}. Configure a writable IDA install with `rgh ida config --path <dir>` or rerun this command with administrator rights.");
            return 1;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("idaxex install failed", ex.Message);
            return 1;
        }

        IdaLoaderStatus loaderStatus = IdaRuntimeHelpers.GetLoaderStatus(idaHome);
        if (!loaderStatus.IsSupported) {
            OperationFeedback.WriteFailure("Unexpected idaxex version", $"Installed loader hash did not match the supported {targetBuild.Label} build.");
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "IDA loader installed",
            $"[green]{Markup.Escape(sourceLabel)}[/] -> [white]{Markup.Escape(loaderStatus.LoaderPath ?? Path.Combine(idaHome, "loaders", "idaxex.dll"))}[/]");
        return 0;
    }
}

public sealed class IdaAnalyzeCommand : AsyncCommand<IdaAnalyzeCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [LocalizedDescription("Input file to analyze (XEX or existing database).")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [LocalizedDescription("Fetch the XEX via FTP before analysis.")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [LocalizedDescription("Use the running title XEX resolved over XBDM + FTP.")]
        public bool Running { get; init; }

        [CommandOption("--out-db <FILE>")]
        [LocalizedDescription("Output database path (.i64).")]
        public string? OutputDatabase { get; init; }

        [CommandOption("--overwrite")]
        [LocalizedDescription("Overwrite an existing database.")]
        public bool Overwrite { get; init; }

        [CommandOption("--path <DIR>")]
        [LocalizedDescription("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [LocalizedDescription("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [LocalizedDescription("IDA user directory override.")]
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
        [LocalizedDescription("Input XEX or database path.")]
        public string? Input { get; init; }

        [CommandOption("--ftp-path <PATH>")]
        [LocalizedDescription("Fetch the XEX via FTP before decompiling.")]
        public string? FtpPath { get; init; }

        [CommandOption("--running")]
        [LocalizedDescription("Use the running title XEX resolved over XBDM + FTP.")]
        public bool Running { get; init; }

        [CommandOption("--out <DIR>")]
        [LocalizedDescription("Output directory for C files.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum number of functions to decompile (default: all).")]
        public int? MaxFunctions { get; init; }

        [CommandOption("--backend <NAME>")]
        [LocalizedDescription("Backend: auto, batch, or idalib.")]
        public string? Backend { get; init; }

        [CommandOption("--out-db <FILE>")]
        [LocalizedDescription("Database path to create or reuse for raw XEX input.")]
        public string? OutputDatabase { get; init; }

        [CommandOption("--overwrite-db")]
        [LocalizedDescription("Overwrite the database when importing a raw XEX.")]
        public bool OverwriteDatabase { get; init; }

        [CommandOption("--keep-db")]
        [LocalizedDescription("Keep the generated database even when using a temporary cache path.")]
        public bool KeepDatabase { get; init; }

        [CommandOption("--path <DIR>")]
        [LocalizedDescription("IDA install directory override.")]
        public string? IdaPath { get; init; }

        [CommandOption("--python <EXE>")]
        [LocalizedDescription("Python executable override.")]
        public string? PythonPath { get; init; }

        [CommandOption("--user <DIR>")]
        [LocalizedDescription("IDA user directory override.")]
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
        [LocalizedDescription("Directory containing IDA decompiler output to verify.")]
        public string? Directory { get; init; }

        [CommandOption("--pattern <REGEX>")]
        [LocalizedDescription("Regex pattern to flag (default: could not decompile|BAD).")]
        public string Pattern { get; init; } = "could not decompile|BAD";

        [CommandOption("--ext <EXT>")]
        [LocalizedDescription("File extension to scan (default: .c).")]
        public string Extension { get; init; } = ".c";

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum matching files to display (default: 25).")]
        public int? MaxResults { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteIdaNotice(settings.Json);

        if (string.IsNullOrWhiteSpace(settings.Directory)) {
            if (WriteJsonError(settings.Json, "IDA verify validation failed", "--dir is required.", "IDA_VERIFY_VALIDATION_FAILED"))
                return 1;
            OperationFeedback.WriteFailure("Missing directory", "--dir is required.");
            return 1;
        }

        if (!Directory.Exists(settings.Directory)) {
            if (WriteJsonError(settings.Json, "IDA verify validation failed", "Directory not found.", "IDA_VERIFY_VALIDATION_FAILED"))
                return 1;
            OperationFeedback.WriteFailure("Directory not found", settings.Directory);
            return 1;
        }

        string extension = settings.Extension.StartsWith(".", StringComparison.Ordinal) ? settings.Extension : "." + settings.Extension;
        Regex regex;
        try {
            regex = new Regex(settings.Pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex) {
            if (WriteJsonError(settings.Json, "IDA verify validation failed", $"Invalid regex: {ex.Message}", "IDA_VERIFY_VALIDATION_FAILED"))
                return 1;
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

    private static bool WriteJsonError(bool json, string title, string message, string code) {
        if (!json)
            return false;

        CliOutput.EmitJsonError(new CliErrorEnvelope(
            title,
            message,
            code,
            new[] { "Fix the command arguments and retry." }));
        return true;
    }
}

