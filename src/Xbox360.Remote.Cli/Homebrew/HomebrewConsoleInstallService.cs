using System.Globalization;
using System.Text;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli.Homebrew;

internal enum HomebrewLaunchIniMode {
    Generated,
    Merge,
    Skip
}

internal sealed record HomebrewConsoleDevice(
    string RootName,
    string DisplayName,
    string LaunchAlias);

internal sealed record HomebrewConsoleInstallResult(
    string Ip,
    string DeviceRoot,
    string DeviceAlias,
    string LaunchIniPath,
    HomebrewLaunchIniMode LaunchIniMode,
    IReadOnlyList<InstalledHomebrewPackage> Packages,
    bool PluginsUploaded,
    bool LaunchIniWritten,
    bool LaunchIniBackedUp);

internal static class HomebrewConsoleInstallService {
    private static readonly string[] CandidateRoots = { "Hdd1", "Usb0", "Usb1", "Usb2" };

    public static async Task<HomebrewConsoleInstallResult> InstallAsync(
        HomebrewInstallCommand.Settings settings,
        CancellationToken cancellationToken) {
        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, cancellationToken);
        timeout = Math.Max(timeout, 15000);

        await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
        client.Config.ConnectTimeout = timeout;
        client.Config.ReadTimeout = timeout;
        client.Config.DataConnectionConnectTimeout = timeout;
        client.Config.DataConnectionReadTimeout = timeout;
        await client.Connect(cancellationToken);

        IReadOnlyList<HomebrewConsoleDevice> devices = await DetectDevicesAsync(client);
        if (devices.Count == 0)
            throw new InvalidOperationException("No supported console install targets were detected. XeCLI only installs to Hdd1, Usb0, Usb1, or Usb2.");

        HomebrewConsoleDevice device = ResolveDevice(settings.Device, devices, settings.AutoConfirm);
        HomebrewLaunchIniMode iniMode = ResolveLaunchIniMode(settings.IniMode, settings.AutoConfirm);
        string iniPath = FtpHelpers.NormalizePath(string.IsNullOrWhiteSpace(settings.IniPath) ? "/Hdd1/launch.ini" : settings.IniPath);

        if (!settings.AutoConfirm && !Console.IsInputRedirected) {
            Table summary = CliOutput.CreateTable();
            summary.AddColumn(new TableColumn("[white]Field[/]"));
            summary.AddColumn(new TableColumn("[white]Value[/]"));
            summary.AddRow("[white]Console[/]", $"[springgreen3_1]{Markup.Escape(ip)}[/]");
            summary.AddRow("[white]Packages[/]", $"[deepskyblue1]{Markup.Escape(string.Join(", ", HomebrewPackageService.ResolveSelection(settings.Package).Select(p => p.DisplayName)))}[/]");
            summary.AddRow("[white]Target Device[/]", $"[gold1]{Markup.Escape(device.DisplayName)}[/]");
            summary.AddRow("[white]Install Root[/]", $"[cyan]{Markup.Escape('/' + device.RootName)}[/]");
            summary.AddRow("[white]launch.ini[/]", $"[mediumpurple3]{Markup.Escape(DescribeIniMode(iniMode))}[/]");
            summary.AddRow("[white]launch.ini Path[/]", $"[grey]{Markup.Escape(iniPath)}[/]");
            AnsiConsole.Write(summary);
            if (!AnsiConsole.Confirm("Continue with the console install?", true))
                throw new OperationCanceledException("Console install cancelled by user.");
        }

        string stagingRoot = Path.Combine(
            HomebrewPackageService.GetPackageCacheRoot(settings.CacheDirectory),
            "console-stage",
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try {
            HomebrewInstallResult staged = await HomebrewPackageService.StagePackagesAsync(
                settings.Package,
                stagingRoot,
                settings.CacheDirectory,
                settings.ForceDownload,
                cancellationToken);

            foreach (InstalledHomebrewPackage package in staged.Packages) {
                string remoteRoot = $"/{device.RootName}/{package.InstallFolderName}";
                await UploadDirectoryAsync(client, package.InstallPath, remoteRoot, $"Upload {package.DisplayName}", cancellationToken);
            }

            bool pluginsUploaded = false;
            string pluginRoot = Path.Combine(stagingRoot, "Plugins");
            if (Directory.Exists(pluginRoot)) {
                await UploadDirectoryAsync(client, pluginRoot, $"/{device.RootName}/Plugins", "Upload bundled plugins", cancellationToken);
                pluginsUploaded = true;
            }

            bool launchIniBackedUp = false;
            bool launchIniWritten = false;
            if (iniMode == HomebrewLaunchIniMode.Generated) {
                launchIniBackedUp = await BackupFileIfPresentAsync(client, iniPath);
                string text = HomebrewPackageService.CreateLaunchIniText(device.LaunchAlias);
                await client.UploadBytes(Encoding.ASCII.GetBytes(text), iniPath, FtpRemoteExists.Overwrite, false);
                launchIniWritten = true;
            }
            else if (iniMode == HomebrewLaunchIniMode.Merge) {
                (launchIniWritten, launchIniBackedUp) = await MergeLaunchIniAsync(client, ip, port, user, pass, timeout, iniPath, device.LaunchAlias);
            }

            return new HomebrewConsoleInstallResult(
                ip,
                device.RootName,
                device.LaunchAlias,
                iniPath,
                iniMode,
                staged.Packages,
                pluginsUploaded,
                launchIniWritten,
                launchIniBackedUp);
        }
        finally {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public static void RenderInstallResult(HomebrewConsoleInstallResult result) {
        long totalBytes = result.Packages.Sum(package => package.TotalBytes);
        OperationFeedback.WriteSuccess(
            "Console homebrew install complete",
            $"[green]{result.Packages.Count}[/] package(s) [silver]{FormatBytes(totalBytes)}[/] -> [cyan]{Markup.Escape(result.DeviceRoot)}[/] on [springgreen3_1]{Markup.Escape(result.Ip)}[/]");

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Package[/]"));
        table.AddColumn(new TableColumn("[cyan]Console Path[/]"));
        table.AddColumn(new TableColumn("[gold1]Files[/]"));
        table.AddColumn(new TableColumn("[grey]Size[/]"));
        foreach (InstalledHomebrewPackage package in result.Packages) {
            table.AddRow(
                $"[green]{Markup.Escape(package.DisplayName)}[/]",
                $"[cyan]/{Markup.Escape(result.DeviceRoot)}/{Markup.Escape(package.InstallFolderName)}[/]",
                $"[gold1]{package.FileCount}[/]",
                $"[grey]{FormatBytes(package.TotalBytes)}[/]");
        }
        AnsiConsole.Write(table);

        if (result.PluginsUploaded)
            AnsiConsole.MarkupLine($"[grey]Bundled plugins copied to[/] [springgreen3_1]/{Markup.Escape(result.DeviceRoot)}/Plugins[/]");

        string iniStatus = result.LaunchIniMode switch {
            HomebrewLaunchIniMode.Generated => "Generated XeCLI launch.ini",
            HomebrewLaunchIniMode.Merge => "Updated existing launch.ini plugin entries",
            _ => "launch.ini left unchanged"
        };
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(iniStatus)}[/] [mediumpurple3]{Markup.Escape(result.LaunchIniPath)}[/]");
        if (result.LaunchIniBackedUp)
            AnsiConsole.MarkupLine($"[grey]Backup written to[/] [mediumpurple3]{Markup.Escape(result.LaunchIniPath + ".bak")}[/]");
    }

    private static async Task<IReadOnlyList<HomebrewConsoleDevice>> DetectDevicesAsync(AsyncFtpClient client) {
        List<HomebrewConsoleDevice> devices = new List<HomebrewConsoleDevice>();
        foreach (string root in CandidateRoots) {
            try {
                if (!await client.DirectoryExists("/" + root))
                    continue;
            }
            catch {
                continue;
            }

            string alias = string.Equals(root, "Hdd1", StringComparison.OrdinalIgnoreCase) ? "Hdd" : root;
            devices.Add(new HomebrewConsoleDevice(root, alias, alias));
        }

        return devices;
    }

    private static HomebrewConsoleDevice ResolveDevice(string? requestedDevice, IReadOnlyList<HomebrewConsoleDevice> devices, bool autoConfirm) {
        if (!string.IsNullOrWhiteSpace(requestedDevice)) {
            HomebrewConsoleDevice? match = devices.FirstOrDefault(device =>
                string.Equals(device.RootName, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.DisplayName, requestedDevice, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new InvalidOperationException($"Console device '{requestedDevice}' was not detected. Available devices: {string.Join(", ", devices.Select(d => d.DisplayName))}.");
            return match;
        }

        if (autoConfirm || Console.IsInputRedirected) {
            HomebrewConsoleDevice? hdd = devices.FirstOrDefault(device => string.Equals(device.RootName, "Hdd1", StringComparison.OrdinalIgnoreCase));
            if (hdd != null)
                return hdd;
            if (devices.Count == 1)
                return devices[0];
            throw new InvalidOperationException("Multiple console devices were detected. Specify one with --device Hdd1, --device Usb0, --device Usb1, or --device Usb2.");
        }

        if (devices.Count == 1) {
            AnsiConsole.MarkupLine($"[green]Detected console install target:[/] [springgreen3_1]{Markup.Escape(devices[0].DisplayName)}[/]");
            return devices[0];
        }

        AnsiConsole.MarkupLine("[bold white]Choose which console drive to install the homebrew to.[/]");
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]#[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Drive[/]"));
        for (int i = 0; i < devices.Count; i++) {
            table.AddRow($"[white]{i + 1}[/]", $"[springgreen3_1]{Markup.Escape(devices[i].DisplayName)}[/]");
        }
        AnsiConsole.Write(table);

        while (true) {
            AnsiConsole.Markup("[white]Choose a drive number:[/] ");
            string? choice = Console.ReadLine();
            if (int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
                index >= 1 &&
                index <= devices.Count) {
                return devices[index - 1];
            }

            AnsiConsole.MarkupLine("[red]Enter a valid drive number.[/]");
        }
    }

    private static HomebrewLaunchIniMode ResolveLaunchIniMode(string? requestedMode, bool autoConfirm) {
        if (!string.IsNullOrWhiteSpace(requestedMode)) {
            if (TryParseIniMode(requestedMode, out HomebrewLaunchIniMode parsed))
                return parsed;
            throw new InvalidOperationException("Invalid --ini-mode. Expected generated, merge, or skip.");
        }

        if (autoConfirm || Console.IsInputRedirected)
            return HomebrewLaunchIniMode.Skip;

        AnsiConsole.MarkupLine("[bold white]Choose how XeCLI should handle launch.ini on the console.[/]");
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]#[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Mode[/]"));
        table.AddColumn(new TableColumn("[bold grey]Behavior[/]"));
        table.AddRow("[white]1[/]", "[springgreen3_1]generated[/]", "[grey]Write a new XeCLI launch.ini with Aurora and bundled plugins.[/]");
        table.AddRow("[white]2[/]", "[springgreen3_1]merge[/]", "[grey]Keep the existing launch.ini and only add/update bundled plugin entries.[/]");
        table.AddRow("[white]3[/]", "[springgreen3_1]skip[/]", "[grey]Do not touch launch.ini. Only install the homebrew files.[/]");
        AnsiConsole.Write(table);

        while (true) {
            AnsiConsole.Markup("[white]Choose a launch.ini mode [2]:[/] ");
            string? choice = Console.ReadLine();
            choice = string.IsNullOrWhiteSpace(choice) ? "2" : choice.Trim();
            if (choice == "1")
                return HomebrewLaunchIniMode.Generated;
            if (choice == "2")
                return HomebrewLaunchIniMode.Merge;
            if (choice == "3")
                return HomebrewLaunchIniMode.Skip;
            AnsiConsole.MarkupLine("[red]Enter 1, 2, or 3.[/]");
        }
    }

    private static bool TryParseIniMode(string value, out HomebrewLaunchIniMode mode) {
        switch (value.Trim().ToLowerInvariant()) {
            case "generated":
            case "create":
            case "new":
                mode = HomebrewLaunchIniMode.Generated;
                return true;
            case "merge":
            case "modify":
                mode = HomebrewLaunchIniMode.Merge;
                return true;
            case "skip":
            case "none":
            case "download":
                mode = HomebrewLaunchIniMode.Skip;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    private static string DescribeIniMode(HomebrewLaunchIniMode mode) {
        return mode switch {
            HomebrewLaunchIniMode.Generated => "Create a new XeCLI launch.ini",
            HomebrewLaunchIniMode.Merge => "Modify the existing launch.ini and add bundled plugins",
            _ => "Install homebrew only and leave launch.ini unchanged"
        };
    }

    private static async Task UploadDirectoryAsync(
        AsyncFtpClient client,
        string localRoot,
        string remoteRoot,
        string title,
        CancellationToken cancellationToken) {
        string[] files = Directory.GetFiles(localRoot, "*", SearchOption.AllDirectories);
        IReadOnlyList<CliOutput.TransferBatchItem> items = files
            .Select(file => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length))
            .ToArray();

        await client.CreateDirectory(FtpHelpers.NormalizePath(remoteRoot), cancellationToken);
        HashSet<string> createdDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            FtpHelpers.NormalizePath(remoteRoot)
        };

        await CliOutput.RunBatchProgressAsync(title, items, async batch => {
            foreach (string localFile in files) {
                string relative = Path.GetRelativePath(localRoot, localFile).Replace('\\', '/');
                string remoteFile = CombineRemotePath(remoteRoot, relative);
                string remoteDirectory = GetRemoteDirectory(remoteFile);
                if (createdDirectories.Add(remoteDirectory))
                    await client.CreateDirectory(remoteDirectory, cancellationToken);

                long size = new FileInfo(localFile).Length;
                batch.StartFile(relative, size);
                Progress<FtpProgress> progress = new Progress<FtpProgress>(ftpProgress => {
                    if (ftpProgress.TransferredBytes > 0)
                        batch.ReportFileProgress(ftpProgress.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, items.Count)}");
                });
                await UploadFileWithRetryAsync(client, localFile, remoteFile, progress, cancellationToken);
                batch.CompleteFile();
            }
        });
    }

    private static async Task<bool> BackupFileIfPresentAsync(AsyncFtpClient client, string remotePath) {
        string tempFile = Path.Combine(Path.GetTempPath(), $"xecli-launch-backup-{Guid.NewGuid():N}.tmp");
        try {
            FtpStatus status = await client.DownloadFile(tempFile, remotePath, FtpLocalExists.Overwrite, FtpVerify.None);
            if (status != FtpStatus.Success)
                return false;

            await client.UploadFile(tempFile, remotePath + ".bak", FtpRemoteExists.Overwrite, false, FtpVerify.None);
            return true;
        }
        catch {
            return false;
        }
        finally {
            try {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch {
                // ignored
            }
        }
    }

    private static async Task<(bool Written, bool BackedUp)> MergeLaunchIniAsync(
        AsyncFtpClient client,
        string ip,
        int port,
        string user,
        string pass,
        int timeout,
        string iniPath,
        string launchAlias) {
        string[] pluginPaths = {
            $"{launchAlias}:\\Plugins\\xbdm.xex",
            $"{launchAlias}:\\Plugins\\JRPC2.xex",
            $"{launchAlias}:\\Plugins\\XDRPC.xex"
        };

        bool exists = await client.FileExists(iniPath);
        if (!exists) {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[Plugins]");
            builder.AppendLine($"plugin1 = {pluginPaths[0]}");
            builder.AppendLine($"plugin2 = {pluginPaths[1]}");
            builder.AppendLine($"plugin3 = {pluginPaths[2]}");
            await client.UploadBytes(Encoding.ASCII.GetBytes(builder.ToString()), iniPath, FtpRemoteExists.Overwrite, false);
            return (true, false);
        }

        PluginHelpers.PluginConfig config = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, iniPath);
        string tempBackupPath = Path.Combine(Path.GetTempPath(), $"xecli-ini-backup-{Guid.NewGuid():N}.ini");
        try {
            await File.WriteAllTextAsync(tempBackupPath, string.Join("\r\n", config.Lines), Encoding.UTF8);
            await UploadBackupWithFreshClientAsync(ip, port, user, pass, timeout, tempBackupPath, iniPath + ".bak");
        }
        finally {
            try {
                if (File.Exists(tempBackupPath))
                    File.Delete(tempBackupPath);
            }
            catch {
                // ignored
            }
        }
        foreach (string pluginPath in pluginPaths) {
            string fileName = Path.GetFileName(pluginPath);
            int slot = config.Slots
                .OrderBy(pair => pair.Key)
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .FirstOrDefault(pair => string.Equals(Path.GetFileName(pair.Value), fileName, StringComparison.OrdinalIgnoreCase))
                .Key;

            if (slot <= 0) {
                slot = config.Slots
                    .OrderBy(pair => pair.Key)
                    .FirstOrDefault(pair => string.IsNullOrWhiteSpace(pair.Value))
                    .Key;
            }

            if (slot <= 0) {
                slot = config.Slots.Keys.DefaultIfEmpty(0).Max() + 1;
            }

            config.SetSlot(slot, pluginPath);
        }

        await PluginHelpers.SaveAsync(ip, port, user, pass, timeout, config, backup: false);
        return (true, true);
    }

    private static string CombineRemotePath(string root, string relative) {
        string normalizedRoot = FtpHelpers.NormalizePath(root).TrimEnd('/');
        string normalizedRelative = relative.Replace('\\', '/').TrimStart('/');
        return $"{normalizedRoot}/{normalizedRelative}";
    }

    private static async Task UploadBackupWithFreshClientAsync(string ip, int port, string user, string pass, int timeout, string localPath, string remotePath) {
        await using AsyncFtpClient backupClient = new AsyncFtpClient(ip, user, pass, port);
        backupClient.Config.ConnectTimeout = timeout;
        backupClient.Config.ReadTimeout = timeout;
        backupClient.Config.DataConnectionConnectTimeout = timeout;
        backupClient.Config.DataConnectionReadTimeout = timeout;
        await backupClient.Connect();
        FtpStatus status = await backupClient.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, false, FtpVerify.None);
        if (status != FtpStatus.Success)
            throw new IOException($"Unable to create launch.ini backup at {remotePath}.");
    }

    private static async Task UploadFileWithRetryAsync(
        AsyncFtpClient client,
        string localFile,
        string remoteFile,
        IProgress<FtpProgress> progress,
        CancellationToken cancellationToken) {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++) {
            try {
                await client.UploadFile(localFile, remoteFile, FtpRemoteExists.Overwrite, true, FtpVerify.None, progress, cancellationToken);
                return;
            }
            catch (Exception ex) {
                lastError = ex;
                if (attempt >= 3)
                    break;

                try {
                    if (client.IsConnected)
                        await client.Disconnect(cancellationToken);
                }
                catch {
                    // ignored
                }

                await Task.Delay(500, cancellationToken);
                await client.Connect(cancellationToken);
            }
        }

        throw lastError ?? new IOException($"Unable to upload {Path.GetFileName(localFile)}.");
    }

    private static string GetRemoteDirectory(string remoteFile) {
        int slash = remoteFile.LastIndexOf('/');
        if (slash <= 0)
            return "/";
        return remoteFile.Substring(0, slash);
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch {
            // ignored
        }
    }

    private static string FormatBytes(long bytes) {
        return FtpHelpers.FormatBytes(bytes);
    }
}
