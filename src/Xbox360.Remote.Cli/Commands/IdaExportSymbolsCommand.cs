using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class IdaExportSymbolsCommand : AsyncCommand<IdaExportSymbolsCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--in <FILE>")]
        [LocalizedDescription("Input IDA database or XEX.")]
        public string? Input { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Output symbol sidecar JSON file.")]
        public string? Output { get; init; }

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

        if (string.IsNullOrWhiteSpace(settings.Input)) {
            OperationFeedback.WriteFailure("Missing input", "--in is required.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Output)) {
            OperationFeedback.WriteFailure("Missing output", "--out is required.");
            return 1;
        }

        string inputPath = Path.GetFullPath(settings.Input);
        if (!File.Exists(inputPath)) {
            OperationFeedback.WriteFailure("Input file not found", inputPath);
            return 1;
        }

        string outputPath = Path.GetFullPath(settings.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

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

        string moduleName = Path.GetFileNameWithoutExtension(inputPath);
        string? temporaryDatabase = null;
        try {
            string databasePath = inputPath;
            if (!IdaRuntimeHelpers.IsDatabasePath(inputPath)) {
                temporaryDatabase = Path.Combine(
                    IdaCommandSupport.GetCacheRoot(),
                    $"{Path.GetFileNameWithoutExtension(inputPath)}-{Guid.NewGuid():N}.i64");
                IdaCommandSupport.IdaAnalyzeResult analyzeSummary = await IdaCommandSupport.RunBatchAnalyzeAsync(
                    environment,
                    inputPath,
                    temporaryDatabase,
                    CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(analyzeSummary.Database))
                    databasePath = analyzeSummary.Database!;
                else
                    databasePath = temporaryDatabase;
                temporaryDatabase = databasePath;
            }

            await IdaCommandSupport.RunBatchExportSymbolsAsync(environment, databasePath, outputPath, moduleName, CancellationToken.None);

            IdaSymbolSidecarLoadResult sidecar = IdaSymbolSidecarStore.LoadRaw(outputPath);
            if (!string.IsNullOrWhiteSpace(sidecar.Issue)) {
                OperationFeedback.WriteFailure("IDA symbol export failed", sidecar.Issue);
                return 1;
            }

            OperationFeedback.WriteSuccess(
                "IDA symbols exported",
                $"[green]{sidecar.Data.Symbols.Count}[/] function symbol(s) [grey]module[/] [white]{Markup.Escape(sidecar.Data.Module)}[/] [grey]output[/] [white]{Markup.Escape(outputPath)}[/]");
            return 0;
        }
        catch (Exception ex) {
            OperationFeedback.WriteFailure("IDA symbol export failed", ex.Message);
            return 1;
        }
        finally {
            if (!string.IsNullOrWhiteSpace(temporaryDatabase))
                IdaCommandSupport.TryDeleteDatabaseArtifacts(temporaryDatabase);
        }
    }
}
