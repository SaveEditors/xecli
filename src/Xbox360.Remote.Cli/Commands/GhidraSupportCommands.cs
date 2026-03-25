using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GhidraInstallLoaderCommand : AsyncCommand<GhidraInstallLoaderCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [LocalizedDescription("Ghidra install directory override.")]
        public string? GhidraPath { get; init; }

        [CommandOption("--archive <FILE>")]
        [LocalizedDescription("Use a local XEXLoaderWV archive instead of downloading one.")]
        public string? ArchivePath { get; init; }

        [CommandOption("--url <URL>")]
        [LocalizedDescription("Download XEXLoaderWV from an explicit URL instead of the latest GitHub release.")]
        public string? Url { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        ReverseEngineeringNoticeHelpers.WriteGhidraNotice();

        CliConfig config = CliConfig.Load();
        string? ghidraHome = GhidraInstallHelpers.ResolveGhidraHome(config, settings.GhidraPath);
        if (string.IsNullOrWhiteSpace(ghidraHome)) {
            OperationFeedback.WriteFailure("Ghidra path not set", "Use `rgh ghidra config --path <dir>` first.");
            return 1;
        }

        string analyzeHeadlessPath = GhidraInstallHelpers.GetAnalyzeHeadlessPath(ghidraHome);
        if (!File.Exists(analyzeHeadlessPath)) {
            OperationFeedback.WriteFailure("Invalid Ghidra install", $"analyzeHeadless.bat was not found at {analyzeHeadlessPath}");
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
                : await ReverseEngineeringDownloadHelpers.ResolveLatestGithubZipAssetUrlAsync(
                    ReverseEngineeringSupportConstants.GhidraLatestLoaderApiUrl,
                    assetName => assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                    CancellationToken.None);
            archivePath = await ReverseEngineeringDownloadHelpers.DownloadToCacheAsync(
                "Download XEXLoaderWV",
                url,
                ReverseEngineeringDownloadHelpers.GetDownloadCacheDirectory("ghidra"),
                CancellationToken.None);
        }

        string jarPath = GhidraInstallHelpers.InstallXexLoader(archivePath, ghidraHome);
        OperationFeedback.WriteSuccess(
            "Ghidra XEX loader installed",
            $"[green]XEXLoaderWV[/] -> [white]{Markup.Escape(jarPath)}[/]");
        return 0;
    }
}

