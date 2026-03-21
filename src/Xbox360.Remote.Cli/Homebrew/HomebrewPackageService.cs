using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HomebrewPackageDefinition(
    string Id,
    string DisplayName,
    string InstallFolderName,
    string PrimaryUrl,
    string? MirrorUrl);

internal sealed record InstalledHomebrewPackage(
    string Id,
    string DisplayName,
    string ArchivePath,
    string InstallPath,
    int FileCount,
    long TotalBytes);

internal sealed record HomebrewInstallResult(
    string TargetRoot,
    IReadOnlyList<InstalledHomebrewPackage> Packages,
    bool LaunchIniWritten,
    bool PluginsCopied);

internal static class HomebrewPackageService {
    private static readonly IReadOnlyList<HomebrewPackageDefinition> Definitions = new[] {
        new HomebrewPackageDefinition(
            "aurora",
            "Aurora 0.7b.2",
            "Aurora",
            "http://phoenix.xboxunity.net/downloads/Aurora%200.7b.2%20-%20Release%20Package.rar",
            "https://consolemods.org/wiki/images/d/dd/Aurora_0.7b.2_-_Release_Package.rar"),
        new HomebrewPackageDefinition(
            "dashlaunch",
            "DashLaunch 3.21",
            "DashLaunch",
            "https://consolemods.org/wiki/File:DashLaunch_v3.21.7z",
            null),
        new HomebrewPackageDefinition(
            "xexmenu",
            "XeXMenu 1.2",
            "XeXMenu",
            "https://consolemods.org/wiki/images/5/5c/XeXmenu_1.2.7z",
            null),
        new HomebrewPackageDefinition(
            "fsd",
            "Freestyle Dash 3",
            "FreestyleDash",
            "https://consolemods.org/wiki/images/a/a0/Fsd3.zip",
            "https://consolemods.org/wiki/images/7/76/TeamFSD.Freestyle3.0.775.7z")
    };

    public static IReadOnlyList<string> KnownPackageIds => Definitions.Select(definition => definition.Id).Append("all").ToArray();

    public static bool IsKnownPackageId(string? packageId) {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        return KnownPackageIds.Contains(packageId.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<HomebrewPackageDefinition> ResolveSelection(string packageId) {
        if (string.Equals(packageId, "all", StringComparison.OrdinalIgnoreCase))
            return Definitions;

        HomebrewPackageDefinition? match = Definitions.FirstOrDefault(definition =>
            string.Equals(definition.Id, packageId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new InvalidOperationException($"Unknown package '{packageId}'. Expected one of: {string.Join(", ", KnownPackageIds)}.");
        return new[] { match };
    }

    public static string GetPackageCacheRoot(string? explicitDirectory) {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return Path.GetFullPath(explicitDirectory);

        return Path.Combine(CliPaths.CachePath, "packages");
    }

    public static async Task<HomebrewInstallResult> InstallAsync(
        string packageId,
        string targetRoot,
        string? explicitCacheDirectory,
        bool forceDownload,
        bool autoConfirm,
        CancellationToken cancellationToken) {
        IReadOnlyList<HomebrewPackageDefinition> packages = ResolveSelection(packageId);
        string cacheRoot = GetPackageCacheRoot(explicitCacheDirectory);
        string archiveRoot = Path.Combine(cacheRoot, "archives");
        string extractRoot = ResolveStagingRoot(targetRoot, explicitCacheDirectory);
        Directory.CreateDirectory(archiveRoot);
        Directory.CreateDirectory(extractRoot);
        Directory.CreateDirectory(targetRoot);

        try {
            if (!autoConfirm && !Console.IsInputRedirected) {
                Table summary = CliOutput.CreateTable();
                summary.AddColumn(new TableColumn("[white]Field[/]"));
                summary.AddColumn(new TableColumn("[white]Value[/]"));
                summary.AddRow("[white]Target[/]", $"[springgreen3_1]{Markup.Escape(targetRoot)}[/]");
                summary.AddRow("[white]Packages[/]", $"[deepskyblue1]{Markup.Escape(string.Join(", ", packages.Select(p => p.DisplayName)))}[/]");
                summary.AddRow("[white]Archive Cache[/]", $"[cyan]{Markup.Escape(archiveRoot)}[/]");
                summary.AddRow("[white]Staging[/]", $"[gold1]{Markup.Escape(extractRoot)}[/]");
                AnsiConsole.Write(summary);
                if (!AnsiConsole.Confirm("Continue with the install?", true))
                    throw new OperationCanceledException("Install cancelled by user.");
            }

            List<InstalledHomebrewPackage> installed = new List<InstalledHomebrewPackage>();
            foreach (HomebrewPackageDefinition definition in packages) {
                string archivePath = await EnsureArchiveAsync(definition, archiveRoot, forceDownload, cancellationToken);
                string packageExtractRoot = Path.Combine(extractRoot, definition.Id);
                AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(new Style(Spectre.Console.Color.SpringGreen3_1, decoration: Decoration.Bold))
                    .Start($"Extracting {definition.DisplayName}...", _ => ExtractArchive(archivePath, packageExtractRoot));

                string payloadRoot = CollapseRootDirectory(packageExtractRoot);
                string installRoot = Path.Combine(targetRoot, definition.InstallFolderName);
                (int fileCount, long totalBytes) = await CopyDirectoryAsync(payloadRoot, installRoot, $"{definition.DisplayName} files", cancellationToken);
                installed.Add(new InstalledHomebrewPackage(
                    definition.Id,
                    definition.DisplayName,
                    archivePath,
                    installRoot,
                    fileCount,
                    totalBytes));
            }

            bool pluginsCopied = await CopyConsoleDependenciesAsync(targetRoot, cancellationToken);
            bool launchIniWritten = WriteLaunchIni(targetRoot, installed);

            return new HomebrewInstallResult(targetRoot, installed, launchIniWritten, pluginsCopied);
        }
        finally {
            TryDeleteDirectory(extractRoot);
        }
    }

    public static void RenderInstallResult(HomebrewInstallResult result) {
        long totalBytes = result.Packages.Sum(package => package.TotalBytes);
        OperationFeedback.WriteSuccess(
            "Homebrew install complete",
            $"[green]{result.Packages.Count}[/] package(s) [silver]{FormatBytes(totalBytes)}[/] -> [cyan]{Markup.Escape(result.TargetRoot)}[/]");

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Package[/]"));
        table.AddColumn(new TableColumn("[cyan]Installed To[/]"));
        table.AddColumn(new TableColumn("[gold1]Files[/]"));
        table.AddColumn(new TableColumn("[grey]Size[/]"));
        foreach (InstalledHomebrewPackage package in result.Packages) {
            table.AddRow(
                $"[green]{Markup.Escape(package.DisplayName)}[/]",
                $"[cyan]{Markup.Escape(package.InstallPath)}[/]",
                $"[gold1]{package.FileCount}[/]",
                $"[grey]{FormatBytes(package.TotalBytes)}[/]");
        }
        AnsiConsole.Write(table);

        if (result.LaunchIniWritten)
            AnsiConsole.MarkupLine($"[grey]Generated[/] [springgreen3_1]{Markup.Escape(Path.Combine(result.TargetRoot, "launch.ini"))}[/]");
        if (result.PluginsCopied)
            AnsiConsole.MarkupLine($"[grey]Copied bundled console plugins into[/] [springgreen3_1]{Markup.Escape(Path.Combine(result.TargetRoot, "Plugins"))}[/]");
    }

    private static async Task<string> EnsureArchiveAsync(
        HomebrewPackageDefinition definition,
        string archiveRoot,
        bool forceDownload,
        CancellationToken cancellationToken) {
        string extension = GetArchiveExtension(definition.PrimaryUrl, definition.MirrorUrl);
        string archivePath = Path.Combine(archiveRoot, $"{definition.Id}{extension}");
        if (!forceDownload && File.Exists(archivePath) && new FileInfo(archivePath).Length > 0 && IsRecognizedArchive(archivePath))
            return archivePath;

        if (File.Exists(archivePath))
            File.Delete(archivePath);

        Exception? lastError = null;
        foreach (string url in new[] { definition.PrimaryUrl, definition.MirrorUrl }.Where(url => !string.IsNullOrWhiteSpace(url))!) {
            try {
                using HttpClient client = CreateHttpClient();
                string resolvedUrl = await ResolveDownloadUrlAsync(client, url!, cancellationToken);
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

                if (!IsRecognizedArchive(archivePath)) {
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

    private static void ExtractArchive(string archivePath, string extractRoot) {
        if (Directory.Exists(extractRoot))
            Directory.Delete(extractRoot, recursive: true);
        Directory.CreateDirectory(extractRoot);

        if (Path.GetExtension(archivePath).Equals(".7z", StringComparison.OrdinalIgnoreCase)) {
            using FileStream sevenZipStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using IArchive archive = ArchiveFactory.OpenArchive(sevenZipStream);
            foreach (IArchiveEntry entry in archive.Entries.Where(entry => !entry.IsDirectory)) {
                string destinationPath = Path.Combine(extractRoot, (entry.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);
                entry.WriteToFile(destinationPath, new ExtractionOptions {
                    ExtractFullPath = true,
                    Overwrite = true
                });
            }
            return;
        }

        using FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using IReader reader = ReaderFactory.OpenReader(stream);
        while (reader.MoveToNextEntry()) {
            if (reader.Entry.IsDirectory)
                continue;

            string destinationPath = Path.Combine(extractRoot, (reader.Entry.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            reader.WriteEntryToFile(destinationPath, new ExtractionOptions {
                ExtractFullPath = true,
                Overwrite = true
            });
        }
    }

    private static string CollapseRootDirectory(string extractRoot) {
        string[] directories = Directory.GetDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly);
        string[] files = Directory.GetFiles(extractRoot, "*", SearchOption.TopDirectoryOnly);
        if (directories.Length == 1 && files.Length == 0)
            return directories[0];
        return extractRoot;
    }

    private static async Task<(int FileCount, long TotalBytes)> CopyDirectoryAsync(
        string sourceRoot,
        string targetRoot,
        string title,
        CancellationToken cancellationToken) {
        string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
        IReadOnlyList<CliOutput.TransferBatchItem> items = files
            .Select(file => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length))
            .ToArray();

        Directory.CreateDirectory(targetRoot);
        await CliOutput.RunBatchProgressAsync(title, items, async batch => {
            foreach (string sourceFile in files) {
                string relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                string destinationPath = Path.Combine(targetRoot, relativePath);
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                long fileSize = new FileInfo(sourceFile).Length;
                batch.StartFile(relativePath, fileSize);
                await using FileStream sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using FileStream destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[64 * 1024];
                long copied = 0;
                while (true) {
                    int read = await sourceStream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                        break;
                    await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    batch.ReportFileProgress(copied);
                }
                await destinationStream.FlushAsync(cancellationToken);
                batch.CompleteFile();
            }
        });

        return (files.Length, files.Sum(file => new FileInfo(file).Length));
    }

    private static async Task<bool> CopyConsoleDependenciesAsync(string targetRoot, CancellationToken cancellationToken) {
        string sourceRoot = Path.Combine(AppContext.BaseDirectory, "ConsoleDependencies");
        if (!Directory.Exists(sourceRoot))
            return false;

        string destinationRoot = Path.Combine(targetRoot, "Plugins");
        await CopyDirectoryAsync(sourceRoot, destinationRoot, "Bundled console plugins", cancellationToken);
        return true;
    }

    private static bool WriteLaunchIni(string targetRoot, IReadOnlyList<InstalledHomebrewPackage> installedPackages) {
        if (!installedPackages.Any(package => string.Equals(package.Id, "aurora", StringComparison.OrdinalIgnoreCase))) {
            string auroraPath = Path.Combine(targetRoot, "Aurora", "Aurora.xex");
            if (!File.Exists(auroraPath))
                return false;
        }

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("[Paths]");
        builder.AppendLine("Default = Usb:\\Aurora\\Aurora.xex");
        builder.AppendLine();
        builder.AppendLine("[Plugins]");
        builder.AppendLine("plugin1 = Usb:\\Plugins\\xbdm.xex");
        builder.AppendLine("plugin2 = Usb:\\Plugins\\JRPC2.xex");
        builder.AppendLine("plugin3 = Usb:\\Plugins\\XDRPC.xex");
        builder.AppendLine();
        builder.AppendLine("[Settings]");
        builder.AppendLine("nxemini = false");
        builder.AppendLine("ftpserv = true");
        builder.AppendLine("pingpatch = true");
        builder.AppendLine("contpatch = true");
        builder.AppendLine("fatalfreeze = false");
        builder.AppendLine("livestrong = false");

        File.WriteAllText(Path.Combine(targetRoot, "launch.ini"), builder.ToString(), Encoding.ASCII);
        return true;
    }

    private static string GetArchiveExtension(string primaryUrl, string? mirrorUrl) {
        foreach (string? candidate in new[] { primaryUrl, mirrorUrl }) {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string path = new Uri(candidate).AbsolutePath;
            if (path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                return ".7z";
            if (path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                return ".rar";
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return ".zip";
        }

        return ".pkg";
    }

    private static string ResolveStagingRoot(string targetRoot, string? explicitCacheDirectory) {
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        if (!string.IsNullOrWhiteSpace(explicitCacheDirectory))
            return Path.Combine(GetPackageCacheRoot(explicitCacheDirectory), "staging", stamp);

        string targetVolumeRoot = Path.GetPathRoot(Path.GetFullPath(targetRoot)) ?? targetRoot;
        return Path.Combine(targetVolumeRoot, "XeCLI-Staging", stamp);
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);

            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent) &&
                Directory.Exists(parent) &&
                !Directory.EnumerateFileSystemEntries(parent).Any() &&
                string.Equals(Path.GetFileName(parent), "XeCLI-Staging", StringComparison.OrdinalIgnoreCase)) {
                Directory.Delete(parent, recursive: false);
            }
        }
        catch {
            // ignored
        }
    }

    private static HttpClient CreateHttpClient() {
        HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XeCLI/1.0.1");
        return client;
    }

    private static async Task<string> ResolveDownloadUrlAsync(HttpClient client, string url, CancellationToken cancellationToken) {
        if (!url.Contains("/wiki/File:", StringComparison.OrdinalIgnoreCase))
            return url;

        string html = await client.GetStringAsync(url, cancellationToken);
        Match match = Regex.Match(
            html,
            "href=\"(?<path>/wiki/images/[^\"]+\\.(?:7z|zip|rar))\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return url;

        Uri baseUri = new Uri(url);
        return new Uri(baseUri, match.Groups["path"].Value).AbsoluteUri;
    }

    private static string FormatBytes(long bytes) {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }

    private static bool IsRecognizedArchive(string archivePath) {
        string extension = Path.GetExtension(archivePath);
        byte[] header = new byte[8];
        using FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = stream.Read(header, 0, header.Length);
        if (read < 4)
            return false;

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
            return header[0] == 0x50 && header[1] == 0x4B &&
                   (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
                   (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
        }

        if (extension.Equals(".rar", StringComparison.OrdinalIgnoreCase)) {
            return read >= 7 &&
                   header[0] == 0x52 &&
                   header[1] == 0x61 &&
                   header[2] == 0x72 &&
                   header[3] == 0x21 &&
                   header[4] == 0x1A &&
                   header[5] == 0x07 &&
                   (header[6] == 0x00 || header[6] == 0x01);
        }

        if (extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)) {
            return read >= 6 &&
                   header[0] == 0x37 &&
                   header[1] == 0x7A &&
                   header[2] == 0xBC &&
                   header[3] == 0xAF &&
                   header[4] == 0x27 &&
                   header[5] == 0x1C;
        }

        return true;
    }
}
