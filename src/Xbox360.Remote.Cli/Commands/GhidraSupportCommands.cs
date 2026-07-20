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
        [LocalizedDescription("Use a local XEXLoaderWV archive. Requires --sha256.")]
        public string? ArchivePath { get; init; }

        [CommandOption("--url <URL>")]
        [LocalizedDescription("Download XEXLoaderWV from an explicit URL. Requires --sha256.")]
        public string? Url { get; init; }

        [CommandOption("--sha256 <HASH>")]
        [LocalizedDescription("Required SHA256 for a custom --archive or --url source.")]
        public string? Sha256 { get; init; }
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

        IReadOnlyList<string> installedRoots = GhidraInstallHelpers.FindInstalledXexLoaderRoots(ghidraHome);
        if (installedRoots.Count > 1) {
            OperationFeedback.WriteFailure(
                "Conflicting Ghidra XEX loaders",
                "Multiple XEXLoaderWV modules are installed. Remove the duplicate module before updating: " +
                string.Join("; ", installedRoots));
            return 1;
        }

        bool hasArchive = !string.IsNullOrWhiteSpace(settings.ArchivePath);
        bool hasUrl = !string.IsNullOrWhiteSpace(settings.Url);
        if (hasArchive && hasUrl) {
            OperationFeedback.WriteFailure("Conflicting XEXLoaderWV sources", "Use only one of --archive or --url.");
            return 1;
        }

        bool customSource = hasArchive || hasUrl;
        string expectedSha256 = customSource
            ? settings.Sha256 ?? string.Empty
            : ReverseEngineeringSupportConstants.GhidraLoaderArchiveSha256;
        if (customSource && string.IsNullOrWhiteSpace(settings.Sha256)) {
            OperationFeedback.WriteFailure(
                "Custom XEXLoaderWV source requires SHA256",
                "Pass `--sha256 <HASH>` with --archive or --url, or omit the custom source to use the pinned release.");
            return 1;
        }
        if (!ReverseEngineeringDownloadHelpers.TryNormalizeSha256(expectedSha256, out _, out string hashFormatError)) {
            OperationFeedback.WriteFailure("Invalid XEXLoaderWV SHA256", hashFormatError);
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
                : ReverseEngineeringSupportConstants.GhidraLoaderArchiveUrl;
            archivePath = await ReverseEngineeringDownloadHelpers.DownloadToCacheAsync(
                $"Download XEXLoaderWV {ReverseEngineeringSupportConstants.GhidraLoaderVersion}",
                url,
                ReverseEngineeringDownloadHelpers.GetDownloadCacheDirectory("ghidra"),
                CancellationToken.None);
        }

        if (!ReverseEngineeringDownloadHelpers.VerifySha256(archivePath, expectedSha256, out _, out string hashError)) {
            OperationFeedback.WriteFailure("XEXLoaderWV archive hash mismatch", hashError);
            return 1;
        }

        string jarPath = GhidraInstallHelpers.InstallXexLoader(archivePath, ghidraHome);
        OperationFeedback.WriteSuccess(
            "Ghidra XEX loader installed",
            $"[green]XEXLoaderWV {Markup.Escape(ReverseEngineeringSupportConstants.GhidraLoaderVersion)}[/] -> [white]{Markup.Escape(jarPath)}[/]");
        return 0;
    }
}

