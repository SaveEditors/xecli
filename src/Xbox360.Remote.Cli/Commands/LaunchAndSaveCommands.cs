using System.ComponentModel;
using System.Globalization;
using System.Text;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LaunchCommand : AsyncCommand<LaunchCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[XEX]")]
        [Description("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
        public string? Xex { get; init; }

        [CommandOption("--xex <PATH>")]
        [Description("XEX path to launch, for example Hdd1:\\Aurora\\Aurora.xex.")]
        public string? XexPath { get; init; }

        [CommandOption("--directory <DIR>")]
        [Description("Working directory passed to XBDM. Defaults to the XEX folder.")]
        public string? Directory { get; init; }

        [CommandOption("--args <TEXT>")]
        [Description("Command-line arguments passed to the XEX.")]
        public string? Arguments { get; init; }

        [CommandOption("--titleid <TITLEID>")]
        [Description("Optional Title ID for display/logging.")]
        public string? TitleId { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show the generated XBDM command without executing it.")]
        public bool DryRun { get; init; }

        [CommandOption("--notify")]
        [Description("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [Description("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [Description("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? xexPath = !string.IsNullOrWhiteSpace(settings.XexPath) ? settings.XexPath : settings.Xex;
        if (string.IsNullOrWhiteSpace(xexPath)) {
            AnsiConsole.MarkupLine("[red]Provide a XEX path with `rgh launch <path>` or `--xex <path>`.[/]");
            return 1;
        }

        string workingDirectory = !string.IsNullOrWhiteSpace(settings.Directory)
            ? settings.Directory
            : DeriveDirectory(xexPath);

        string command = BuildMagicBootCommand(xexPath, workingDirectory, settings.Arguments);
        string? titleSummary = TryFormatTitleId(settings.TitleId);

        if (settings.DryRun) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Launch Preview[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine($"[grey]Command:[/] {Markup.Escape(command)}");
            if (!string.IsNullOrWhiteSpace(titleSummary))
                AnsiConsole.MarkupLine($"[grey]Title:[/] {Markup.Escape(titleSummary)}");
            return 0;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            await client.SendCommandAsync(command, CancellationToken.None);
            OperationFeedback.WriteSuccess("Launch requested", $"[green]{Markup.Escape(xexPath)}[/]");
            if (!string.IsNullOrWhiteSpace(titleSummary))
                AnsiConsole.MarkupLine($"[grey]Target title:[/] {Markup.Escape(titleSummary)}");
            if (!string.IsNullOrWhiteSpace(settings.Arguments))
                AnsiConsole.MarkupLine($"[grey]Arguments:[/] {Markup.Escape(settings.Arguments)}");
            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            return 0;
        }, CancellationToken.None);
    }

    private static string BuildMagicBootCommand(string xexPath, string workingDirectory, string? arguments) {
        List<string> parts = new List<string> {
            "magicboot",
            $"title=\"{EscapeQuoted(xexPath)}\""
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            parts.Add($"directory=\"{EscapeQuoted(workingDirectory)}\"");
        if (!string.IsNullOrWhiteSpace(arguments))
            parts.Add($"cmdline=\"{EscapeQuoted(arguments)}\"");

        return string.Join(' ', parts);
    }

    private static string EscapeQuoted(string text) {
        return text.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string DeriveDirectory(string xexPath) {
        int slash = Math.Max(xexPath.LastIndexOf('\\'), xexPath.LastIndexOf('/'));
        return slash > 0 ? xexPath.Substring(0, slash) : xexPath;
    }

    private static string? TryFormatTitleId(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (!TryParseHex(text, out uint titleId))
            return text;

        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) && entry != null)
            return $"{entry.Name} (0x{titleId:X8})";

        return $"0x{titleId:X8}";
    }

    private static bool TryParseHex(string text, out uint value) {
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}

public sealed class SaveListCommand : AsyncCommand<SaveListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [Description("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Restrict to one 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--device <ROOTS>")]
        [Description("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
        public string? Devices { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
            return 1;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveFileRecord> files = await SaveHelpers.EnumerateSaveFilesAsync(ip, port, user, pass, timeout, titleId, settings.ProfileId, settings.Devices);
        if (settings.Json) {
            CliOutput.EmitJson(files);
            return 0;
        }

        string titleLabel = SaveHelpers.FormatTitleLabel(titleId);
        AnsiConsole.Write(new Rule($"[bold deepskyblue1]Save Files[/] [grey]{Markup.Escape(titleLabel)}[/]").RuleStyle("grey"));
        if (files.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[green]Path[/]"));
        table.AddColumn(new TableColumn("[grey]Profile[/]"));
        table.AddColumn(new TableColumn("[cyan]Size[/]"));
        table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
        foreach (SaveHelpers.SaveFileRecord file in files.OrderBy(f => f.Device, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.ProfileId, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)) {
            table.AddRow(
                $"[green]{Markup.Escape(file.RemotePath)}[/]",
                $"[grey]{Markup.Escape(file.ProfileId)}[/]",
                $"[cyan]{FtpHelpers.FormatBytes(file.Size)}[/]",
                file.Modified != DateTime.MinValue ? CliOutput.FormatTimestamp(file.Modified) : "[grey]unknown[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class SaveExtractCommand : AsyncCommand<SaveExtractCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [Description("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Restrict to one 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--out <DIR>")]
        [Description("Destination directory.")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--device <ROOTS>")]
        [Description("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
        public string? Devices { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.OutputDirectory)) {
            AnsiConsole.MarkupLine("[red]--out is required.[/]");
            return 1;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveFileRecord> files = await SaveHelpers.EnumerateSaveFilesAsync(ip, port, user, pass, timeout, titleId, settings.ProfileId, settings.Devices);
        if (files.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
            return 0;
        }

        string titleFolder = SaveHelpers.BuildOutputFolderName(titleId);
        string outputRoot = Path.Combine(settings.OutputDirectory!, titleFolder);
        Directory.CreateDirectory(outputRoot);

        List<(SaveHelpers.SaveFileRecord File, string LocalPath)> plan = new List<(SaveHelpers.SaveFileRecord, string)>();
        int skipped = 0;
        foreach (SaveHelpers.SaveFileRecord file in files) {
            string localPath = Path.Combine(outputRoot, file.Device, file.ProfileId, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string? localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(localDir))
                Directory.CreateDirectory(localDir);

            if (!settings.Overwrite && File.Exists(localPath)) {
                skipped++;
                continue;
            }

            plan.Add((file, localPath));
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning("Save extract skipped", $"all matching files already exist in [grey]{Markup.Escape(outputRoot)}[/]");
            return 0;
        }

        await CliOutput.RunBatchProgressAsync(
            $"Save extract {SaveHelpers.FormatTitleLabel(titleId)}",
            plan.Select(item => new CliOutput.TransferBatchItem(item.File.RemotePath, item.File.Size)).ToList(),
            async batch => {
                foreach ((SaveHelpers.SaveFileRecord file, string localPath) in plan) {
                    batch.StartFile(file.RemotePath, file.Size);
                    Progress<long> perFile = new Progress<long>(value => batch.ReportFileProgress(value));
                    await SaveHelpers.DownloadFileAsync(ip, port, user, pass, timeout, file.RemotePath, localPath, perFile);
                    batch.CompleteFile();
                }
            });

        OperationFeedback.WriteSuccess(
            "Save extract complete",
            $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum(item => item.File.Size))}[/] -> [white]{Markup.Escape(outputRoot)}[/]");
        if (skipped > 0)
            OperationFeedback.WriteWarning("Save extract skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

public sealed class SaveInjectCommand : AsyncCommand<SaveInjectCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--titleid <TITLEID>")]
        [Description("Title ID in hex (for example 4D530805).")]
        public string? TitleId { get; init; }

        [CommandOption("--profile <PROFILE>")]
        [Description("Destination 16-character profile ID.")]
        public string? ProfileId { get; init; }

        [CommandOption("--device <ROOT>")]
        [Description("Destination storage root, for example Hdd1 or Usb0 (default: Hdd1).")]
        public string? Device { get; init; }

        [CommandOption("--in <PATH>")]
        [Description("Local file or directory to upload.")]
        public string? InputPath { get; init; }

        [CommandOption("--remote-path <RELATIVE>")]
        [Description("Relative save path to use when --in points to a single file.")]
        public string? RemotePath { get; init; }

        [CommandOption("--overwrite")]
        [Description("Overwrite remote files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--dry-run")]
        [Description("Show the remote paths that would be uploaded without writing anything.")]
        public bool DryRun { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint titleId)) {
            AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.ProfileId) || !SaveHelpers.IsProfileId(settings.ProfileId)) {
            AnsiConsole.MarkupLine("[red]--profile must be a 16-character profile id.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.InputPath)) {
            AnsiConsole.MarkupLine("[red]--in is required.[/]");
            return 1;
        }

        string inputPath = Path.GetFullPath(settings.InputPath);
        bool isDirectory = Directory.Exists(inputPath);
        bool isFile = File.Exists(inputPath);
        if (!isDirectory && !isFile) {
            AnsiConsole.MarkupLine("[red]Input path was not found.[/]");
            return 1;
        }

        string device = string.IsNullOrWhiteSpace(settings.Device) ? "Hdd1" : settings.Device.Trim();
        string titleRoot = SaveHelpers.BuildTitleRoot(device, settings.ProfileId, titleId);
        List<SaveHelpers.SaveUploadRecord> uploads = isDirectory
            ? SaveHelpers.BuildUploadPlanFromDirectory(inputPath, titleRoot)
            : SaveHelpers.BuildUploadPlanFromFile(inputPath, titleRoot, settings.RemotePath);

        if (uploads.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No files found to upload.[/]");
            return 0;
        }

        if (settings.Json || settings.DryRun) {
            object payload = uploads.Select(u => new {
                u.LocalPath,
                u.RemotePath,
                u.Size
            }).ToList();

            if (settings.Json) {
                CliOutput.EmitJson(payload);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Save Inject Preview[/]").RuleStyle("grey"));
            Table preview = CliOutput.CreateTable();
            preview.AddColumn(new TableColumn("[green]Local[/]"));
            preview.AddColumn(new TableColumn("[cyan]Remote[/]"));
            preview.AddColumn(new TableColumn("[grey]Size[/]"));
            foreach (SaveHelpers.SaveUploadRecord upload in uploads) {
                preview.AddRow(
                    $"[green]{Markup.Escape(upload.LocalPath)}[/]",
                    $"[cyan]{Markup.Escape(upload.RemotePath)}[/]",
                    $"[grey]{FtpHelpers.FormatBytes(upload.Size)}[/]");
            }
            AnsiConsole.Write(preview);
            return 0;
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        List<SaveHelpers.SaveUploadRecord> plan = new List<SaveHelpers.SaveUploadRecord>();
        int skipped = 0;
        foreach (SaveHelpers.SaveUploadRecord upload in uploads) {
            if (!settings.Overwrite) {
                bool exists = await SaveHelpers.RemoteFileExistsAsync(ip, port, user, pass, timeout, upload.RemotePath);
                if (exists) {
                    skipped++;
                    continue;
                }
            }

            plan.Add(upload);
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning("Save inject skipped", $"all target files already exist in [grey]{Markup.Escape(titleRoot)}[/]");
            return 0;
        }

        await CliOutput.RunBatchProgressAsync(
            $"Save inject {SaveHelpers.FormatTitleLabel(titleId)}",
            plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
            async batch => {
                foreach (SaveHelpers.SaveUploadRecord upload in plan) {
                    batch.StartFile(upload.RemotePath, upload.Size);
                    Progress<long> perFile = new Progress<long>(value => batch.ReportFileProgress(value));
                    await SaveHelpers.UploadFileAsync(ip, port, user, pass, timeout, upload.LocalPath, upload.RemotePath, perFile);
                    batch.CompleteFile();
                }
            });

        OperationFeedback.WriteSuccess(
            "Save inject complete",
            $"[green]{plan.Count}[/] file(s)  [silver]{FtpHelpers.FormatBytes(plan.Sum(item => item.Size))}[/] -> [white]{Markup.Escape(titleRoot)}[/]");
        if (skipped > 0)
            OperationFeedback.WriteWarning("Save inject skipped existing files", skipped.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

internal static class SaveHelpers {
    private static readonly HashSet<string> SupportedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Hdd1",
        "HddX",
        "Usb0",
        "Usb1",
        "Usb2",
        "UsbMu",
        "Mu",
        "IntMu",
        "MmcMu"
    };

    internal sealed record SaveFileRecord(
        string Device,
        string ProfileId,
        string RemotePath,
        string RelativePath,
        long Size,
        DateTime Modified);

    internal sealed record SaveUploadRecord(
        string LocalPath,
        string RemotePath,
        long Size);

    public static async Task<List<SaveFileRecord>> EnumerateSaveFilesAsync(string ip, int port, string user, string pass, int timeoutMs, uint titleId, string? profileId, string? devices) {
        string titleText = titleId.ToString("X8", CultureInfo.InvariantCulture);
        HashSet<string> filterProfiles = string.IsNullOrWhiteSpace(profileId)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(new[] { profileId }, StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedRoots = ParseRootFilter(devices);

        List<SaveFileRecord> files = new List<SaveFileRecord>();
        foreach (string root in await GetAvailableRootsAsync(ip, port, user, pass, timeoutMs, requestedRoots)) {
            string contentRoot = $"/{root}/Content";
            Trace($"content {contentRoot}");
            if (filterProfiles.Count > 0) {
                foreach (string filteredProfile in filterProfiles) {
                    string titleRoot = $"{contentRoot}/{filteredProfile}/{titleText}";
                    Trace($"direct {titleRoot}");
                    await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, filteredProfile, titleRoot, files);
                }
                continue;
            }

            (FtpListItem[] profiles, bool rootListing)? profileListing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, contentRoot);
            if (profileListing == null || profileListing.Value.rootListing)
                continue;

            foreach (FtpListItem profile in profileListing.Value.profiles) {
                if (profile.Type != FtpObjectType.Directory || !IsProfileId(profile.Name))
                    continue;
                string titleRoot = $"{contentRoot}/{profile.Name}/{titleText}";
                Trace($"walk {titleRoot}");
                await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, profile.Name, titleRoot, files);
            }
        }

        return files;
    }

    public static async Task DownloadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath, string localPath, IProgress<long>? progress = null) {
        await WithFreshClientAsync(ip, port, user, pass, timeoutMs, async client => {
            Progress<FtpProgress>? ftpProgress = progress == null
                ? null
                : new Progress<FtpProgress>(p => {
                    if (p.TransferredBytes >= 0)
                        progress.Report(p.TransferredBytes);
                });
            await client.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, ftpProgress);
        });
    }

    public static async Task UploadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string localPath, string remotePath, IProgress<long>? progress = null) {
        Progress<FtpProgress>? ftpProgress = progress == null
            ? null
            : new Progress<FtpProgress>(p => {
                if (p.TransferredBytes >= 0)
                    progress.Report(p.TransferredBytes);
            });
        await FtpHelpers.UploadFileVerifiedAsync(
            ip,
            port,
            user,
            pass,
            timeoutMs,
            localPath,
            remotePath,
            ensureRemoteDirectory: true,
            progress: ftpProgress,
            cancellationToken: CancellationToken.None);
    }

    public static async Task<bool> RemoteFileExistsAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath) {
        try {
            return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, client => client.FileExists(remotePath));
        }
        catch {
            return false;
        }
    }

    public static string FormatTitleLabel(uint titleId) {
        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) && entry != null)
            return $"{entry.Name} (0x{titleId:X8})";
        return $"0x{titleId:X8}";
    }

    public static string BuildOutputFolderName(uint titleId) {
        string titleLabel = FormatTitleLabel(titleId);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            titleLabel = titleLabel.Replace(invalid, '_');
        return titleLabel;
    }

    public static bool TryParseTitleId(string? text, out uint titleId) {
        titleId = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId);
    }

    public static bool IsProfileId(string name) {
        if (name.Length != 16)
            return false;
        return name.All(ch => Uri.IsHexDigit(ch));
    }

    public static string BuildTitleRoot(string device, string profileId, uint titleId) {
        string cleanedDevice = device.Trim().Trim(':', '\\', '/');
        return $"/{cleanedDevice}/Content/{profileId}/{titleId:X8}";
    }

    public static List<SaveUploadRecord> BuildUploadPlanFromDirectory(string localRoot, string remoteRoot) {
        List<SaveUploadRecord> uploads = new List<SaveUploadRecord>();
        string rootName = Path.GetFileName(localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (string file in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(localRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            string remotePath = NormalizeRemotePath($"{remoteRoot}/{rootName}/{relative}");
            uploads.Add(new SaveUploadRecord(file, remotePath, new FileInfo(file).Length));
        }
        return uploads;
    }

    public static List<SaveUploadRecord> BuildUploadPlanFromFile(string localPath, string remoteRoot, string? relativeRemotePath) {
        string relative = string.IsNullOrWhiteSpace(relativeRemotePath)
            ? Path.GetFileName(localPath)
            : relativeRemotePath.Trim().Replace('\\', '/');
        string remotePath = NormalizeRemotePath($"{remoteRoot}/{relative}");
        return new List<SaveUploadRecord> {
            new SaveUploadRecord(localPath, remotePath, new FileInfo(localPath).Length)
        };
    }

    private static HashSet<string> ParseRootFilter(string? devices) {
        if (string.IsNullOrWhiteSpace(devices))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(root => SupportedRoots.Contains(root)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> GetAvailableRootsAsync(string ip, int port, string user, string pass, int timeoutMs, HashSet<string> requestedRoots) {
        List<string> roots = new List<string>();
        (FtpListItem[] items, bool _)? listing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, "/");
        if (listing != null) {
            foreach (FtpListItem item in listing.Value.items.Where(i => i.Type == FtpObjectType.Directory)) {
                if (!SupportedRoots.Contains(item.Name))
                    continue;
                if (requestedRoots.Count > 0 && !requestedRoots.Contains(item.Name))
                    continue;
                roots.Add(item.Name);
            }
        }

        if (roots.Count > 0)
            return roots;

        if (requestedRoots.Count > 0)
            return requestedRoots.ToList();

        return SupportedRoots.ToList();
    }

    private static async Task<(FtpListItem[] items, bool rootListing)?> TryGetListingAsync(string ip, int port, string user, string pass, int timeoutMs, string path) {
        try {
            Trace($"list {path}");
            return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, client => FtpHelpers.GetListingWithFallbackAsync(client, path));
        }
        catch {
            Trace($"fail {path}");
            return null;
        }
    }

    private static async Task CollectTitleFilesAsync(string ip, int port, string user, string pass, int timeoutMs, string root, string profileId, string titleRoot, List<SaveFileRecord> files) {
        Queue<(string Path, string Relative)> pending = new Queue<(string, string)>();
        pending.Enqueue((titleRoot, string.Empty));
        while (pending.Count > 0) {
            (string current, string relative) = pending.Dequeue();
            Trace($"open {current}");
            (FtpListItem[] items, bool listingRoot)? listing = await TryGetListingAsync(ip, port, user, pass, timeoutMs, current);
            if (listing == null)
                continue;
            if (listing.Value.listingRoot && !string.Equals(current, titleRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (FtpListItem item in listing.Value.items) {
                if (item.Name is "." or "..")
                    continue;
                string nextRelative = string.IsNullOrWhiteSpace(relative) ? item.Name : $"{relative}/{item.Name}";
                string remotePath = string.IsNullOrWhiteSpace(item.FullName)
                    ? $"{current.TrimEnd('/')}/{item.Name}"
                    : item.FullName;
                remotePath = NormalizeRemotePath(remotePath);

                if (item.Type == FtpObjectType.Directory) {
                    pending.Enqueue((remotePath, nextRelative));
                    continue;
                }

                files.Add(new SaveFileRecord(root, profileId, remotePath, nextRelative, item.Size, item.Modified));
                Trace($"file {remotePath}");
            }
        }
    }

    private static async Task<T> WithFreshClientAsync<T>(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task<T>> action) {
        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
        await client.Connect();
        return await action(client);
    }

    private static async Task WithFreshClientAsync(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task> action) {
        await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
        await client.Connect();
        await action(client);
    }

    private static string NormalizeRemotePath(string path) {
        string normalized = path.Replace('\\', '/');
        while (normalized.Contains("/./", StringComparison.Ordinal))
            normalized = normalized.Replace("/./", "/", StringComparison.Ordinal);
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.TrimEnd('/');
    }

    private static void Trace(string message) {
        if (!string.Equals(Environment.GetEnvironmentVariable("XECLI_DEBUG_SAVE"), "1", StringComparison.Ordinal))
            return;
        Console.Error.WriteLine($"[save-debug] {message}");
    }
}
