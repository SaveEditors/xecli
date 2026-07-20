using System.Text;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record OriginalXboxCompatibilityDefinition(
    string Id,
    string DisplayName,
    string Description,
    string Notes,
    string PrimaryUrl,
    string? MirrorUrl);

internal sealed record OriginalXboxCompatibilityInstallResult(
    string Mode,
    string SetId,
    string SetName,
    string Target,
    string CompatibilityPath,
    bool CompatibilityInstalled,
    bool FixerIncluded,
    bool FixerInstalled,
    string? FixerPath,
    int FileCount,
    long TotalBytes,
    string Instructions);

internal static class OriginalXboxCompatibilityService {
    private static readonly IReadOnlyList<OriginalXboxCompatibilityDefinition> XeFuDefinitions = new[] {
        new OriginalXboxCompatibilityDefinition(
            "hacked",
            "Hacked XeFu Pack",
            "Best overall choice for modded consoles. Removes the stock whitelist and restriction checks so more original Xbox titles can boot.",
            "Includes the standard XeFu set, extra emulator files from newer Xbox builds, and external config support. Requires an exploited console.",
            "https://consolemods.org/wiki/images/9/9d/Hacked_Xefu_Pack.zip",
            "https://consolemods.org/wiki/File:Hacked_Xefu_Pack.zip"),
        new OriginalXboxCompatibilityDefinition(
            "hud",
            "Hacked XeFu Pack with HUD",
            "Same compatibility-focused hacked pack, but also keeps the full Xbox 360 guide available while you are inside original Xbox titles.",
            "The guide and game-chat support use more resources, so some titles may run worse or behave less reliably than the standard hacked pack.",
            "https://consolemods.org/wiki/images/c/c4/Hacked_Xefu_Pack_with_HUD.zip",
            "https://consolemods.org/wiki/File:Hacked_Xefu_Pack_with_HUD.zip"),
        new OriginalXboxCompatibilityDefinition(
            "retail",
            "Unmodified Retail XeFu Pack",
            "Closest to the stock Microsoft emulator files. Use this if you want the original behavior instead of the hacked compatibility set.",
            "This keeps the official restrictions and whitelist behavior. It does not add the wider hacked-console compatibility options.",
            "https://consolemods.org/wiki/images/2/28/Unmodified_Retail_Xefu_Pack.zip",
            "https://consolemods.org/wiki/File:Unmodified_Retail_Xefu_Pack.zip")
    };

    private static readonly OriginalXboxCompatibilityDefinition PartitionFixerDefinition = new(
        "fixer",
        "HDD Compatibility Partition Fixer",
        "Creates the HddX compatibility partition required for original Xbox emulator files on non-standard drives.",
        "Run it on the console, press A to create the partition, then reboot before installing a XeFu set.",
        "https://consolemods.org/wiki/images/b/b2/Hdd_compat_partition_fixer_v1.zip",
        "https://consolemods.org/wiki/File:Hdd_compat_partition_fixer_v1.zip");

    public static IReadOnlyList<OriginalXboxCompatibilityDefinition> Catalog => XeFuDefinitions;

    public static bool IsKnownSetId(string? setId) {
        if (string.IsNullOrWhiteSpace(setId))
            return false;
        return XeFuDefinitions.Any(definition => string.Equals(definition.Id, setId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static OriginalXboxCompatibilityDefinition ResolveSet(string setId) {
        OriginalXboxCompatibilityDefinition? match = XeFuDefinitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, setId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new InvalidOperationException($"Unknown Original Xbox compatibility set '{setId}'. Expected one of: {string.Join(", ", XeFuDefinitions.Select(definition => definition.Id))}.");
        return match;
    }

    public static string GetCacheRoot(string? explicitDirectory) {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return Path.GetFullPath(explicitDirectory);
        return Path.Combine(CliPaths.CachePath, "ogxbox");
    }

    public static string DescribeSet(OriginalXboxCompatibilityDefinition definition) {
        string source = HomebrewPackageService.GetSourceLabel(definition.PrimaryUrl);
        return $"{definition.DisplayName} - {definition.Description} {definition.Notes} (source: {source})";
    }

    public static string DescribeAction(bool consoleInstall, bool includeFixer) {
        string baseAction = consoleInstall
            ? "XeCLI will download the selected XeFu pack, extract it, and upload the compatibility files to HddX:\\Compatibility on the console."
            : "XeCLI will download the selected XeFu pack, extract it, and stage the compatibility files into a ready-to-copy Compatibility folder.";

        if (!includeFixer)
            return baseAction;

        return baseAction + " XeCLI will also prepare the HDD Compatibility Partition Fixer so you can create HddX first if your drive is missing that partition.";
    }

    public static async Task<OriginalXboxCompatibilityInstallResult> InstallToHostAsync(
        string setId,
        string targetRoot,
        string? explicitCacheDirectory,
        bool includeFixer,
        bool forceDownload,
        bool autoConfirm,
        CancellationToken cancellationToken) {
        OriginalXboxCompatibilityDefinition definition = ResolveSet(setId);
        string cacheRoot = GetCacheRoot(explicitCacheDirectory);
        string archiveRoot = Path.Combine(cacheRoot, "archives");
        string stagingRoot = HomebrewPackageService.ResolveStagingRoot(targetRoot, explicitCacheDirectory);
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(archiveRoot);
        Directory.CreateDirectory(stagingRoot);

        try {
            if (!autoConfirm) {
                ConfirmationHelpers.ThrowIfCannotPrompt("Interactive confirmation is required. Re-run with --auto-confirm.");
                Table summary = CliOutput.CreateTable();
                summary.AddColumn("[white]Field[/]");
                summary.AddColumn("[white]Value[/]");
                summary.AddRow("[white]Target[/]", $"[springgreen3_1]{Markup.Escape(targetRoot)}[/]");
                summary.AddRow("[white]XeFu Set[/]", $"[deepskyblue1]{Markup.Escape(definition.DisplayName)}[/]");
                summary.AddRow("[white]Details[/]", $"[grey]{Markup.Escape(definition.Description)}[/]");
                summary.AddRow("[white]Notes[/]", $"[grey]{Markup.Escape(definition.Notes)}[/]");
                summary.AddRow("[white]Partition Fixer[/]", includeFixer ? "[gold1]Included[/]" : "[grey]Not included[/]");
                summary.AddRow("[white]Action[/]", $"[grey]{Markup.Escape(DescribeAction(consoleInstall: false, includeFixer))}[/]");
                AnsiConsole.Write(summary);
                if (!ConfirmationHelpers.TryConfirm(
                        "Original Xbox compatibility staging",
                        "Continue with the Original Xbox compatibility staging?",
                        autoConfirm,
                        defaultValue: true,
                        emitWarning: false))
                    throw new OperationCanceledException("Original Xbox compatibility staging cancelled by user.");
            }

            string xefuArchive = await DownloadArchiveAsync(definition, archiveRoot, forceDownload, cancellationToken);
            string xefuExtract = Path.Combine(stagingRoot, definition.Id);
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Spectre.Console.Color.SpringGreen3_1, decoration: Decoration.Bold))
                .Start($"Extracting {definition.DisplayName}...", _ => HomebrewPackageService.ExtractArchive(xefuArchive, xefuExtract));

            string compatibilitySource = ResolveCompatibilityPayloadRoot(xefuExtract);
            string compatibilityTarget = Path.Combine(targetRoot, "Compatibility");
            (int fileCount, long totalBytes) = await HomebrewPackageService.CopyDirectoryAsync(
                compatibilitySource,
                compatibilityTarget,
                $"{definition.DisplayName} files",
                cancellationToken);

            bool fixerInstalled = false;
            string? fixerPath = null;
            if (includeFixer) {
                string fixerArchive = await DownloadArchiveAsync(PartitionFixerDefinition, archiveRoot, forceDownload, cancellationToken);
                string fixerExtract = Path.Combine(stagingRoot, "fixer");
                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(new Style(Spectre.Console.Color.SpringGreen3_1, decoration: Decoration.Bold))
                    .Start("Extracting HDD Compatibility Partition Fixer...", _ => HomebrewPackageService.ExtractArchive(fixerArchive, fixerExtract));

                string fixerSource = HomebrewPackageService.CollapseRootDirectory(fixerExtract);
                fixerPath = Path.Combine(targetRoot, "HddCompatibilityPartitionFixer");
                await HomebrewPackageService.CopyDirectoryAsync(
                    fixerSource,
                    fixerPath,
                    "HDD Compatibility Partition Fixer",
                    cancellationToken);
                fixerInstalled = true;
            }

            WriteInstructionsFile(targetRoot, definition, includeFixer);
            return new OriginalXboxCompatibilityInstallResult(
                "host",
                definition.Id,
                definition.DisplayName,
                targetRoot,
                compatibilityTarget,
                true,
                includeFixer,
                fixerInstalled,
                fixerPath,
                fileCount,
                totalBytes,
                BuildInstructions(definition, includeFixer, consoleInstall: false));
        }
        finally {
            HomebrewPackageService.TryDeleteDirectory(stagingRoot);
        }
    }

    public static async Task<OriginalXboxCompatibilityInstallResult> InstallToConsoleAsync(
        OriginalXboxCompatibilityInstallCommand.Settings settings,
        CancellationToken cancellationToken) {
        if (!settings.AutoConfirm && !ConfirmationHelpers.CanPromptForConfirmation())
            RefuseHeadlessConsoleInstall();

        OriginalXboxCompatibilityDefinition definition = ResolveSet(settings.SetId);
        string cacheRoot = GetCacheRoot(settings.CacheDirectory);
        string archiveRoot = Path.Combine(cacheRoot, "archives");
        string stagingRoot = Path.Combine(cacheRoot, "console-stage", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(archiveRoot);
        Directory.CreateDirectory(stagingRoot);

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, cancellationToken);
        timeout = Math.Max(timeout, 15000);

        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await client.Connect(cancellationToken);

        bool hasHddX = await SafeDirectoryExistsAsync(client, "/HddX");
        bool hasHdd1 = await SafeDirectoryExistsAsync(client, "/Hdd1");
        bool hasUsb0 = await SafeDirectoryExistsAsync(client, "/Usb0");
        bool hasUsb1 = await SafeDirectoryExistsAsync(client, "/Usb1");
        bool hasUsb2 = await SafeDirectoryExistsAsync(client, "/Usb2");

        string? fixerDevice = ResolveFixerDevice(hasHdd1, hasUsb0, hasUsb1, hasUsb2);

        if (!settings.AutoConfirm) {
            Table summary = CliOutput.CreateTable();
            summary.AddColumn("[white]Field[/]");
            summary.AddColumn("[white]Value[/]");
            summary.AddRow("[white]Console[/]", $"[springgreen3_1]{Markup.Escape(ip)}[/]");
            summary.AddRow("[white]XeFu Set[/]", $"[deepskyblue1]{Markup.Escape(definition.DisplayName)}[/]");
            summary.AddRow("[white]Details[/]", $"[grey]{Markup.Escape(definition.Description)}[/]");
            summary.AddRow("[white]Notes[/]", $"[grey]{Markup.Escape(definition.Notes)}[/]");
            summary.AddRow("[white]HddX Present[/]", hasHddX ? "[green]Yes[/]" : "[red]No[/]");
            summary.AddRow("[white]Partition Fixer[/]", settings.IncludeFixer ? "[gold1]Included[/]" : "[grey]Not included[/]");
            summary.AddRow("[white]Action[/]", $"[grey]{Markup.Escape(DescribeAction(consoleInstall: true, settings.IncludeFixer))}[/]");
            if (settings.IncludeFixer && !string.IsNullOrWhiteSpace(fixerDevice))
                summary.AddRow("[white]Fixer Target[/]", $"[cyan]{Markup.Escape(fixerDevice)}[/]");
            AnsiConsole.Write(summary);
            if (!ConfirmationHelpers.TryConfirm(
                    "Original Xbox compatibility install",
                    "Continue with the Original Xbox compatibility console install?",
                    settings.AutoConfirm,
                    defaultValue: true,
                    emitWarning: false))
                throw new OperationCanceledException("Original Xbox compatibility console install cancelled by user.");
        }

        try {
            string xefuArchive = await DownloadArchiveAsync(definition, archiveRoot, settings.ForceDownload, cancellationToken);
            string xefuExtract = Path.Combine(stagingRoot, definition.Id);
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(new Style(Spectre.Console.Color.SpringGreen3_1, decoration: Decoration.Bold))
                .Start($"Extracting {definition.DisplayName}...", _ => HomebrewPackageService.ExtractArchive(xefuArchive, xefuExtract));

            string compatibilitySource = ResolveCompatibilityPayloadRoot(xefuExtract);
            int fileCount = 0;
            long totalBytes = 0;
            bool compatibilityInstalled = false;
            string compatibilityPath = "/HddX/Compatibility";

            if (hasHddX) {
                (fileCount, totalBytes) = await UploadDirectoryAsync(
                    ip,
                    port,
                    user,
                    pass,
                    timeout,
                    compatibilitySource,
                    compatibilityPath,
                    $"{definition.DisplayName} upload",
                    cancellationToken);
                compatibilityInstalled = true;
            }
            else if (!settings.IncludeFixer) {
                throw new InvalidOperationException("HddX was not detected on the console. Re-run with --include-fixer to stage the HDD Compatibility Partition Fixer first.");
            }

            bool fixerInstalled = false;
            string? fixerPath = null;
            if (settings.IncludeFixer) {
                if (string.IsNullOrWhiteSpace(fixerDevice))
                    throw new InvalidOperationException("No writable Hdd1/Usb0/Usb1/Usb2 target was detected for the HDD Compatibility Partition Fixer.");

                string fixerArchive = await DownloadArchiveAsync(PartitionFixerDefinition, archiveRoot, settings.ForceDownload, cancellationToken);
                string fixerExtract = Path.Combine(stagingRoot, "fixer");
                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(new Style(Spectre.Console.Color.SpringGreen3_1, decoration: Decoration.Bold))
                    .Start("Extracting HDD Compatibility Partition Fixer...", _ => HomebrewPackageService.ExtractArchive(fixerArchive, fixerExtract));

                string fixerSource = HomebrewPackageService.CollapseRootDirectory(fixerExtract);
                fixerPath = $"/{fixerDevice}/HddCompatibilityPartitionFixer";
                await UploadDirectoryAsync(
                    ip,
                    port,
                    user,
                    pass,
                    timeout,
                    fixerSource,
                    fixerPath,
                    "HDD Compatibility Partition Fixer upload",
                    cancellationToken);
                fixerInstalled = true;
            }

            return new OriginalXboxCompatibilityInstallResult(
                "console",
                definition.Id,
                definition.DisplayName,
                ip,
                compatibilityPath,
                compatibilityInstalled,
                settings.IncludeFixer,
                fixerInstalled,
                fixerPath,
                fileCount,
                totalBytes,
                BuildInstructions(definition, settings.IncludeFixer, consoleInstall: true));
        }
        finally {
            HomebrewPackageService.TryDeleteDirectory(stagingRoot);
        }
    }

    public static void RenderList() {
        Table table = CliOutput.CreateTable();
        table.AddColumn("[white]Set[/]");
        table.AddColumn("[springgreen3_1]Name[/]");
        table.AddColumn("[cyan]Best For[/]");
        table.AddColumn("[grey]Notes[/]");
        foreach (var definition in XeFuDefinitions) {
            table.AddRow(
                $"[white]{Markup.Escape(definition.Id)}[/]",
                $"[springgreen3_1]{Markup.Escape(definition.DisplayName)}[/]",
                $"[cyan]{Markup.Escape(definition.Description)}[/]",
                $"[grey]{Markup.Escape(definition.Notes)}[/]");
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Optional helper:[/] [gold1]HDD Compatibility Partition Fixer[/] [grey]creates the HddX partition required on non-standard drives.[/]");
    }

    public static void RenderInstallResult(OriginalXboxCompatibilityInstallResult result) {
        if (result.CompatibilityInstalled) {
            OperationFeedback.WriteSuccess(
                "Original Xbox compatibility install complete",
                $"[green]{Markup.Escape(result.SetName)}[/] -> [cyan]{Markup.Escape(result.CompatibilityPath)}[/]");
        }
        else {
            OperationFeedback.WriteSuccess(
                "Partition fixer staged",
                $"[gold1]HddX[/] was not present, so XeCLI staged the fixer and did not write compatibility files yet.");
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn("[white]Field[/]");
        table.AddColumn("[white]Value[/]");
        table.AddRow("[white]Mode[/]", $"[springgreen3_1]{Markup.Escape(result.Mode)}[/]");
        table.AddRow("[white]XeFu Set[/]", $"[deepskyblue1]{Markup.Escape(result.SetName)}[/]");
        table.AddRow("[white]Target[/]", $"[cyan]{Markup.Escape(result.Target)}[/]");
        table.AddRow("[white]Compatibility Path[/]", $"[gold1]{Markup.Escape(result.CompatibilityPath)}[/]");
        table.AddRow("[white]Compatibility Files[/]", result.CompatibilityInstalled ? "[green]Installed[/]" : "[yellow]Pending HddX creation[/]");
        table.AddRow("[white]Fixer Included[/]", result.FixerIncluded ? "[green]Yes[/]" : "[grey]No[/]");
        if (result.FixerIncluded)
            table.AddRow("[white]Fixer Path[/]", string.IsNullOrWhiteSpace(result.FixerPath) ? "[grey]n/a[/]" : $"[mediumpurple3]{Markup.Escape(result.FixerPath)}[/]");
        if (result.CompatibilityInstalled) {
            table.AddRow("[white]Files[/]", $"[gold1]{result.FileCount}[/]");
            table.AddRow("[white]Size[/]", $"[grey]{Markup.Escape(HomebrewPackageService.FormatBytes(result.TotalBytes))}[/]");
        }
        AnsiConsole.Write(table);

        AnsiConsole.Write(new Spectre.Console.Panel($"[grey]{Markup.Escape(result.Instructions)}[/]")
            .Header("[bold deepskyblue1]Next Step[/]")
            .BorderColor(Spectre.Console.Color.Grey));
    }

    private static async Task<string> DownloadArchiveAsync(
        OriginalXboxCompatibilityDefinition definition,
        string archiveRoot,
        bool forceDownload,
        CancellationToken cancellationToken) {
        string extension = HomebrewPackageService.GetArchiveExtension(definition.PrimaryUrl, definition.MirrorUrl);
        string archivePath = Path.Combine(archiveRoot, $"{definition.Id}{extension}");
        if (!forceDownload && File.Exists(archivePath) && new FileInfo(archivePath).Length > 0 && HomebrewPackageService.IsRecognizedArchive(archivePath))
            return archivePath;

        if (File.Exists(archivePath))
            File.Delete(archivePath);

        Exception? lastError = null;
        foreach (string url in new[] { definition.PrimaryUrl, definition.MirrorUrl }.Where(candidate => !string.IsNullOrWhiteSpace(candidate))!) {
            try {
                using HttpClient client = HomebrewPackageService.CreateHttpClient();
                string resolvedUrl = await HomebrewPackageService.ResolveDownloadUrlAsync(client, url, cancellationToken);
                using HttpResponseMessage response = await client.GetAsync(resolvedUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                long? totalBytes = response.Content.Headers.ContentLength;
                await CliOutput.RunWithProgressAsync($"Download {definition.DisplayName}", totalBytes, async progress => {
                    await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using FileStream fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    byte[] buffer = new byte[64 * 1024];
                    long total = 0;
                    while (true) {
                        int read = await responseStream.ReadAsync(buffer, cancellationToken);
                        if (read <= 0)
                            break;
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        total += read;
                        progress.Report(new CliOutput.TransferProgressUpdate(total, "downloading"));
                    }

                    await fileStream.FlushAsync(cancellationToken);
                });

                if (!HomebrewPackageService.IsRecognizedArchive(archivePath)) {
                    File.Delete(archivePath);
                    throw new InvalidDataException($"Downloaded content for {definition.DisplayName} was not a supported archive.");
                }

                return archivePath;
            }
            catch (Exception ex) {
                lastError = ex;
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
        }

        throw new InvalidOperationException($"Failed to download {definition.DisplayName}: {lastError?.Message}", lastError);
    }

    private static string ResolveCompatibilityPayloadRoot(string extractRoot) {
        string payloadRoot = HomebrewPackageService.CollapseRootDirectory(extractRoot);
        string compatibility = Path.Combine(payloadRoot, "Compatibility");
        if (Directory.Exists(compatibility))
            return compatibility;
        return payloadRoot;
    }

    private static void WriteInstructionsFile(string targetRoot, OriginalXboxCompatibilityDefinition definition, bool includeFixer) {
        string path = Path.Combine(targetRoot, "XeCLI-OriginalXbox-Compatibility.txt");
        File.WriteAllText(path, BuildInstructions(definition, includeFixer, consoleInstall: false), Encoding.UTF8);
    }

    private static string BuildInstructions(OriginalXboxCompatibilityDefinition definition, bool includeFixer, bool consoleInstall) {
        StringBuilder builder = new StringBuilder();
        if (includeFixer) {
            builder.Append("If your hard drive does not already have HddX, run HDD Compatibility Partition Fixer first, press A to create the partition, then reboot the console. ");
        }

        builder.Append(consoleInstall
            ? $"XeCLI has prepared {definition.DisplayName}. Original Xbox emulator files belong in HddX:\\Compatibility. Disable stealth plugins before testing original Xbox games if they interfere with the emulator."
            : $"Copy the Compatibility folder to HddX:\\Compatibility on the Xbox 360 hard drive. {definition.DisplayName} is staged and ready. Disable stealth plugins before testing original Xbox games if they interfere with the emulator.");

        return builder.ToString();
    }

    private static async Task<bool> SafeDirectoryExistsAsync(AsyncFtpClient client, string path) {
        try {
            return await client.DirectoryExists(path);
        }
        catch {
            return false;
        }
    }

    private static string? ResolveFixerDevice(bool hasHdd1, bool hasUsb0, bool hasUsb1, bool hasUsb2) {
        if (hasHdd1)
            return "Hdd1";
        if (hasUsb0)
            return "Usb0";
        if (hasUsb1)
            return "Usb1";
        if (hasUsb2)
            return "Usb2";
        return null;
    }

    private static async Task<(int FileCount, long TotalBytes)> UploadDirectoryAsync(
        string ip,
        int port,
        string user,
        string pass,
        int timeout,
        string localRoot,
        string remoteRoot,
        string title,
        CancellationToken cancellationToken) {
        string[] files = Directory.GetFiles(localRoot, "*", SearchOption.AllDirectories);
        IReadOnlyList<CliOutput.TransferBatchItem> items = files
            .Select(file => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length))
            .ToArray();

        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await client.Connect(cancellationToken);
        await client.CreateDirectory(FtpHelpers.NormalizePath(remoteRoot), cancellationToken);

        await CliOutput.RunBatchProgressAsync(title, items, async batch => {
            foreach (string localFile in files) {
                string relative = Path.GetRelativePath(localRoot, localFile).Replace('\\', '/');
                string remoteFile = CombineRemotePath(remoteRoot, relative);
                long size = new FileInfo(localFile).Length;
                batch.StartFile(relative, size);
                Progress<FtpProgress> progress = new Progress<FtpProgress>(ftpProgress => {
                    if (ftpProgress.TransferredBytes > 0)
                        batch.ReportFileProgress(ftpProgress.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, items.Count)}");
                });
                await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, localFile, remoteFile, ensureRemoteDirectory: true, progress, cancellationToken);
                batch.CompleteFile();
            }
        });

        return (files.Length, files.Sum(file => new FileInfo(file).Length));
    }

    private static string CombineRemotePath(string root, string relative) {
        string normalizedRoot = FtpHelpers.NormalizePath(root).TrimEnd('/');
        string normalizedRelative = relative.Replace('\\', '/').TrimStart('/');
        return $"{normalizedRoot}/{normalizedRelative}";
    }

    private static void RefuseHeadlessConsoleInstall() {
        string detail = "Console install refused: use --auto-confirm when running noninteractively.";
        OperationFeedback.WriteFailure("Original Xbox compatibility install blocked", detail);
        throw new OperationCanceledException(detail);
    }
}
