using System.ComponentModel;
using System.Globalization;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarCommandHelpers {
    internal sealed record AvatarResolvedPaths(
        string? ConfiguredLibraryRoot,
        string? EffectiveLibraryRoot,
        string? ConfiguredCachePath,
        string EffectiveCachePath,
        string? ConfiguredManifestUrl,
        string EffectiveManifestUrl,
        string? ConfiguredTitleMapUrl,
        string EffectiveTitleMapUrl,
        string? ConfiguredContentBaseUrl,
        string EffectiveContentBaseUrl,
        string? ConfiguredDownloadCachePath,
        string EffectiveDownloadCachePath,
        bool RemoteMode);
    internal sealed record AvatarResolvedOwnership(string? Gamertag, ulong Xuid, string XuidText, string SignInState, bool FromCurrentUser);

    public static AvatarResolvedPaths ResolvePaths(
        string? explicitLibraryRoot,
        string? explicitCachePath,
        bool forceRemote = false,
        string? explicitManifestUrl = null,
        string? explicitTitleMapUrl = null,
        string? explicitContentBaseUrl = null,
        string? explicitDownloadCachePath = null) {
        CliConfig config = CliConfig.Load();
        string? configuredLibraryRoot = string.IsNullOrWhiteSpace(config.AvatarLibraryRoot) ? null : config.AvatarLibraryRoot;
        string? configuredCachePath = string.IsNullOrWhiteSpace(config.AvatarCachePath) ? null : config.AvatarCachePath;
        string? configuredManifestUrl = string.IsNullOrWhiteSpace(config.AvatarManifestUrl) ? null : config.AvatarManifestUrl;
        string? configuredTitleMapUrl = string.IsNullOrWhiteSpace(config.AvatarTitleMapUrl) ? null : config.AvatarTitleMapUrl;
        string? configuredContentBaseUrl = string.IsNullOrWhiteSpace(config.AvatarContentBaseUrl) ? null : config.AvatarContentBaseUrl;
        string? configuredDownloadCachePath = string.IsNullOrWhiteSpace(config.AvatarDownloadCachePath) ? null : config.AvatarDownloadCachePath;

        string? preferredLibraryRoot = !string.IsNullOrWhiteSpace(explicitLibraryRoot) ? explicitLibraryRoot : configuredLibraryRoot;
        string? preferredCachePath = !string.IsNullOrWhiteSpace(explicitCachePath) ? explicitCachePath : configuredCachePath;
        string effectiveManifestUrl = !string.IsNullOrWhiteSpace(explicitManifestUrl)
            ? explicitManifestUrl
            : configuredManifestUrl ?? AvatarRemoteService.GetDefaultManifestUrl();
        string effectiveTitleMapUrl = !string.IsNullOrWhiteSpace(explicitTitleMapUrl)
            ? explicitTitleMapUrl
            : configuredTitleMapUrl ?? AvatarRemoteService.GetDefaultTitleMapUrl();
        string effectiveContentBaseUrl = !string.IsNullOrWhiteSpace(explicitContentBaseUrl)
            ? explicitContentBaseUrl
            : configuredContentBaseUrl ?? AvatarRemoteService.GetDefaultContentBaseUrl();
        string effectiveDownloadCachePath = AvatarRemoteService.GetDownloadCacheDirectory(
            !string.IsNullOrWhiteSpace(explicitDownloadCachePath) ? explicitDownloadCachePath : configuredDownloadCachePath);

        bool remoteMode = forceRemote;
        string? effectiveLibraryRoot = null;
        if (!remoteMode) {
            if (!string.IsNullOrWhiteSpace(explicitLibraryRoot)) {
                effectiveLibraryRoot = AvatarLibraryService.ResolveCollectionRoot(explicitLibraryRoot);
            }
            else {
                try {
                    effectiveLibraryRoot = AvatarLibraryService.ResolveCollectionRoot(preferredLibraryRoot);
                }
                catch {
                    remoteMode = true;
                }
            }
        }

        string effectiveCachePath = remoteMode
            ? AvatarRemoteService.GetCachePath(preferredCachePath)
            : AvatarIndexCache.GetCachePath(preferredCachePath);
        return new AvatarResolvedPaths(
            configuredLibraryRoot,
            effectiveLibraryRoot,
            configuredCachePath,
            effectiveCachePath,
            configuredManifestUrl,
            effectiveManifestUrl,
            configuredTitleMapUrl,
            effectiveTitleMapUrl,
            configuredContentBaseUrl,
            effectiveContentBaseUrl,
            configuredDownloadCachePath,
            effectiveDownloadCachePath,
            remoteMode);
    }

    public static async Task<AvatarLibraryIndex> LoadIndexAsync(AvatarResolvedPaths paths, bool useCache, CancellationToken cancellationToken) {
        if (paths.RemoteMode) {
            return await AvatarRemoteService.LoadIndexAsync(
                paths.EffectiveManifestUrl,
                paths.EffectiveTitleMapUrl,
                paths.EffectiveContentBaseUrl,
                useCache,
                paths.EffectiveCachePath,
                cancellationToken);
        }

        return AvatarLibraryService.LoadIndex(paths.EffectiveLibraryRoot!, useCache, paths.EffectiveCachePath);
    }

    public static void SaveConfiguredPaths(
        string? libraryRoot,
        string? cachePath,
        string? manifestUrl,
        string? titleMapUrl,
        string? contentBaseUrl,
        string? downloadCachePath,
        bool clear) {
        CliConfig config = CliConfig.Load();
        if (clear) {
            config.AvatarLibraryRoot = null;
            config.AvatarCachePath = null;
            config.AvatarManifestUrl = null;
            config.AvatarTitleMapUrl = null;
            config.AvatarContentBaseUrl = null;
            config.AvatarDownloadCachePath = null;
        }
        else {
            if (!string.IsNullOrWhiteSpace(libraryRoot))
                config.AvatarLibraryRoot = Path.GetFullPath(libraryRoot);
            if (!string.IsNullOrWhiteSpace(cachePath))
                config.AvatarCachePath = AvatarIndexCache.GetCachePath(cachePath);
            if (!string.IsNullOrWhiteSpace(manifestUrl))
                config.AvatarManifestUrl = manifestUrl.Trim();
            if (!string.IsNullOrWhiteSpace(titleMapUrl))
                config.AvatarTitleMapUrl = titleMapUrl.Trim();
            if (!string.IsNullOrWhiteSpace(contentBaseUrl))
                config.AvatarContentBaseUrl = contentBaseUrl.Trim().TrimEnd('/') + "/";
            if (!string.IsNullOrWhiteSpace(downloadCachePath))
                config.AvatarDownloadCachePath = AvatarRemoteService.GetDownloadCacheDirectory(downloadCachePath);
        }

        config.Save();
    }

    public static bool TryParseXuid(string? text, out ulong xuid) {
        xuid = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];

        return ulong.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out xuid) ||
               ulong.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out xuid);
    }

    public static int ParseLimit(int? limit, int defaultValue = 100) {
        if (!limit.HasValue)
            return defaultValue;
        return limit.Value <= 0 ? int.MaxValue : limit.Value;
    }

    public static string DescribeLayout(AvatarItemRecord item) {
        return string.IsNullOrWhiteSpace(item.ContentType) ? "root" : item.ContentType;
    }

    public static string DescribeItem(AvatarItemRecord item) {
        return $"{item.TitleName} - {item.DisplayName}";
    }

    public static async Task<AvatarResolvedOwnership> ResolveOwnershipAsync(
        AvatarInstallSettingsBase settings,
        string targetIp,
        CancellationToken cancellationToken) {
        if (TryParseXuid(settings.Xuid, out ulong explicitXuid)) {
            return new AvatarResolvedOwnership(
                settings.Gamertag,
                explicitXuid,
                $"0x{explicitXuid:X16}",
                "Explicit XUID",
                false);
        }

        int xbdmPort = settings.XbdmPort ?? CliConfig.Load().DefaultPort ?? 730;
        int xbdmTimeout = settings.TimeoutMs ?? 5000;
        ProfileHelpers.XamUserInfo? user = await ProfileHelpers.TryGetSignedInXamUserAsync(targetIp, xbdmPort, xbdmTimeout, cancellationToken);
        if (user == null || string.IsNullOrWhiteSpace(user.Xuid) || !TryParseXuid(user.Xuid, out ulong resolvedXuid)) {
            string currentUserText = string.IsNullOrWhiteSpace(user?.Gamertag) ? "none" : user!.Gamertag!;
            throw new InvalidOperationException($"Please sign in the account you'd like to receive avatar items for. Current user: {currentUserText}.");
        }

        return new AvatarResolvedOwnership(
            user.Gamertag,
            resolvedXuid,
            $"0x{resolvedXuid:X16}",
            HardwareHelpers.DescribeSignInState(user.SignInState),
            true);
    }

    public static IEnumerable<AvatarTitleSummary> FilterTitles(IEnumerable<AvatarTitleSummary> titles, string? search, int limit) {
        IEnumerable<AvatarTitleSummary> filtered = titles;
        if (!string.IsNullOrWhiteSpace(search)) {
            string needle = search.Trim();
            filtered = filtered.Where(title =>
                title.TitleName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                title.TitleId.ToString("X8", CultureInfo.InvariantCulture).Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                title.Publishers.Any(p => p.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered
            .OrderBy(title => title.TitleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(title => title.TitleId)
            .Take(limit);
    }

    public static IReadOnlyList<AvatarItemRecord> FilterItems(AvatarLibraryIndex index, AvatarItemsCommand.Settings settings) {
        uint? titleId = null;
        if (!string.IsNullOrWhiteSpace(settings.TitleId)) {
            if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsedTitleId))
                throw new InvalidOperationException("Invalid --titleid.");
            titleId = parsedTitleId;
        }

        AvatarItemQuery query = new AvatarItemQuery(
            TitleId: titleId,
            Search: settings.Search,
            Publisher: settings.Publisher,
            Tag: settings.Tag,
            Limit: ParseLimit(settings.Limit));

        IEnumerable<AvatarItemRecord> filtered = AvatarLibraryService.SearchItems(index, query);
        if (!string.IsNullOrWhiteSpace(settings.Game)) {
            string game = settings.Game.Trim();
            filtered = filtered.Where(item =>
                item.TitleName.Contains(game, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(game, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered.ToArray();
    }

    public static IReadOnlyList<AvatarItemRecord> SelectInstallItems(AvatarLibraryIndex index, AvatarInstallCommand.Settings settings) {
        if (!string.IsNullOrWhiteSpace(settings.ContentId)) {
            uint? titleId = null;
            if (!string.IsNullOrWhiteSpace(settings.TitleId)) {
                if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint parsedTitleId))
                    throw new InvalidOperationException("Invalid --titleid.");
                titleId = parsedTitleId;
            }

            AvatarItemRecord? item = AvatarLibraryService.FindByContentId(index, settings.ContentId, titleId);
            if (item == null)
                throw new FileNotFoundException($"Avatar item {settings.ContentId} was not found in the library.");
            return new[] { item };
        }

        if (!SaveHelpers.TryParseTitleId(settings.TitleId, out uint installTitleId))
            throw new InvalidOperationException("Provide --contentid or --titleid.");
        if (!settings.All)
            throw new InvalidOperationException("Use --all with --titleid to install every item for a title.");

        IReadOnlyList<AvatarItemRecord> items = AvatarLibraryService.SearchItems(
            index,
            new AvatarItemQuery(TitleId: installTitleId, Limit: int.MaxValue));
        if (items.Count == 0)
            throw new FileNotFoundException($"No avatar items were found for title 0x{installTitleId:X8}.");
        return items;
    }

    public static async Task<IReadOnlyList<AvatarItemRecord>> MaterializeInstallItemsAsync(
        IReadOnlyList<AvatarItemRecord> items,
        AvatarResolvedPaths paths,
        CancellationToken cancellationToken) {
        if (!paths.RemoteMode)
            return items;

        List<AvatarItemRecord> materialized = new List<AvatarItemRecord>(items.Count);
        await CliOutput.RunBatchProgressAsync(
            $"Avatar download {items.Count} item(s)",
            items.Select(item => new CliOutput.TransferBatchItem(DescribeItem(item), item.SizeBytes)).ToList(),
            async batch => {
                foreach (AvatarItemRecord item in items) {
                    batch.StartFile(DescribeItem(item), item.SizeBytes);
                    Progress<long> progress = new Progress<long>(value => batch.ReportFileProgress(value));
                    string localPath = await AvatarRemoteService.EnsureLocalPackageAsync(
                        item,
                        paths.EffectiveDownloadCachePath,
                        progress,
                        cancellationToken);
                    materialized.Add(item with { SourcePath = localPath });
                    batch.CompleteFile();
                }
            });

        return materialized;
    }

    public static async Task<string?> TryGetRemoteFileSizeTextAsync(
        AvatarInstallSettingsBase settings,
        string remotePath,
        CancellationToken cancellationToken) {
        try {
            (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(CreateFtpSettings(settings), cancellationToken);
            await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
            client.Config.ConnectTimeout = timeout;
            client.Config.ReadTimeout = timeout;
            client.Config.DataConnectionConnectTimeout = timeout;
            client.Config.DataConnectionReadTimeout = timeout;
            await client.Connect(cancellationToken);
            long? size = await FtpHelpers.TryGetFileSizeAsync(client, remotePath);
            return size.HasValue ? FtpHelpers.FormatBytes(size.Value) : null;
        }
        catch {
            try {
                (string ip, int port, int timeout) = await ResolveXbdmTargetAsync(settings, cancellationToken);
                using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                    Host = ip,
                    Port = port,
                    TimeoutMs = timeout
                }, cancellationToken);
                return await TryGetRemoteFileSizeTextViaXbdmAsync(client, remotePath, cancellationToken);
            }
            catch {
                return null;
            }
        }
    }

    public static async Task<(string Ip, int Port, int TimeoutMs)> ResolveXbdmTargetAsync(
        AvatarInstallSettingsBase settings,
        CancellationToken cancellationToken) {
        ConnectionSettings connection = new ConnectionSettings {
            Ip = settings.Ip,
            Port = settings.XbdmPort,
            TimeoutMs = settings.TimeoutMs
        };
        return await CliHelpers.ResolveTargetAsync(connection, cancellationToken);
    }

    public static FtpConnectionSettings CreateFtpSettings(AvatarInstallSettingsBase settings) {
        return new FtpConnectionSettings {
            Ip = settings.Ip,
            Port = settings.Port,
            User = settings.User,
            Pass = settings.Pass,
            TimeoutMs = settings.TimeoutMs,
            Json = settings.Json
        };
    }

    public static string ToXbdmPath(string remotePath) {
        string normalized = remotePath.Replace('/', '\\').Trim();
        normalized = normalized.TrimStart('\\');
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Remote path cannot be empty.");

        int slash = normalized.IndexOf('\\');
        if (slash < 0) {
            return normalized.EndsWith(":", StringComparison.Ordinal) ? normalized : normalized + ":";
        }

        string root = normalized[..slash];
        if (!root.EndsWith(":", StringComparison.Ordinal))
            root += ":";
        string rest = normalized[(slash + 1)..].TrimStart('\\');
        return string.IsNullOrWhiteSpace(rest) ? root : $"{root}\\{rest}";
    }

    public static async Task EnsureRemoteDirectoryViaXbdmAsync(
        XbdmClient client,
        string remoteDirectory,
        CancellationToken cancellationToken) {
        string xbdmPath = ToXbdmPath(remoteDirectory).TrimEnd('\\');
        int slash = xbdmPath.IndexOf('\\');
        if (slash < 0)
            return;

        string current = xbdmPath[..slash];
        string remainder = xbdmPath[(slash + 1)..];
        foreach (string segment in remainder.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) {
            current = $"{current}\\{segment}";
            try {
                await client.CreateDirectoryAsync(current, cancellationToken);
            }
            catch {
                // XBDM mkdir is not consistent when a directory already exists.
            }
        }
    }

    public static async Task<bool> RemoteFileExistsViaXbdmAsync(
        XbdmClient client,
        string remotePath,
        CancellationToken cancellationToken) {
        string xbdmPath = ToXbdmPath(remotePath);
        string? parent = Path.GetDirectoryName(xbdmPath);
        string fileName = Path.GetFileName(xbdmPath);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(fileName))
            return false;

        try {
            IReadOnlyList<XbdmFileEntry> entries = await client.GetDirectoryAsync(parent.EndsWith("\\", StringComparison.Ordinal) ? parent : parent + "\\", cancellationToken);
            return entries.Any(entry => !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch {
            return false;
        }
    }

    public static async Task<string?> TryGetRemoteFileSizeTextViaXbdmAsync(
        XbdmClient client,
        string remotePath,
        CancellationToken cancellationToken) {
        string xbdmPath = ToXbdmPath(remotePath);
        string? parent = Path.GetDirectoryName(xbdmPath);
        string fileName = Path.GetFileName(xbdmPath);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(fileName))
            return null;

        try {
            IReadOnlyList<XbdmFileEntry> entries = await client.GetDirectoryAsync(parent.EndsWith("\\", StringComparison.Ordinal) ? parent : parent + "\\", cancellationToken);
            XbdmFileEntry? match = entries.FirstOrDefault(entry => !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
            return match != null ? FtpHelpers.FormatBytes(checked((long) match.Size)) : null;
        }
        catch {
            return null;
        }
    }
}

public abstract class AvatarLibrarySettings : CommandSettings {
    [CommandOption("--library <DIR>")]
    [Description("Avatar item library root. Defaults to config or Avatar-Item-Collection.")]
    public string? LibraryRoot { get; init; }

    [CommandOption("--cache <PATH>")]
    [Description("Avatar index cache file or directory.")]
    public string? CachePath { get; init; }

    [CommandOption("--remote")]
    [Description("Use the hosted avatar library instead of the local corpus.")]
    public bool Remote { get; init; }

    [CommandOption("--manifest-url <URL>")]
    [Description("Override the hosted avatar manifest URL.")]
    public string? ManifestUrl { get; init; }

    [CommandOption("--title-map-url <URL>")]
    [Description("Override the hosted avatar title map URL.")]
    public string? TitleMapUrl { get; init; }

    [CommandOption("--content-base-url <URL>")]
    [Description("Override the hosted avatar content base URL.")]
    public string? ContentBaseUrl { get; init; }

    [CommandOption("--download-cache <DIR>")]
    [Description("Directory used to cache downloaded avatar packages.")]
    public string? DownloadCachePath { get; init; }

    [CommandOption("--json")]
    [Description("Emit JSON output.")]
    public bool Json { get; init; }
}

public sealed class AvatarLibraryShowCommand : Command<AvatarLibraryShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [Description("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            config.AvatarLibraryRoot,
            config.AvatarCachePath,
            false,
            config.AvatarManifestUrl,
            config.AvatarTitleMapUrl,
            config.AvatarContentBaseUrl,
            config.AvatarDownloadCachePath);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                ConfiguredLibraryRoot = paths.ConfiguredLibraryRoot,
                EffectiveLibraryRoot = paths.EffectiveLibraryRoot,
                ConfiguredCachePath = paths.ConfiguredCachePath,
                EffectiveCachePath = paths.EffectiveCachePath,
                ConfiguredManifestUrl = paths.ConfiguredManifestUrl,
                EffectiveManifestUrl = paths.EffectiveManifestUrl,
                ConfiguredTitleMapUrl = paths.ConfiguredTitleMapUrl,
                EffectiveTitleMapUrl = paths.EffectiveTitleMapUrl,
                ConfiguredContentBaseUrl = paths.ConfiguredContentBaseUrl,
                EffectiveContentBaseUrl = paths.EffectiveContentBaseUrl,
                ConfiguredDownloadCachePath = paths.ConfiguredDownloadCachePath,
                EffectiveDownloadCachePath = paths.EffectiveDownloadCachePath
            });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
        table.AddRow("[white]Mode[/]", paths.RemoteMode ? "[gold1]remote[/]" : "[springgreen3_1]local[/]");
        table.AddRow("[white]Configured Library[/]", string.IsNullOrWhiteSpace(paths.ConfiguredLibraryRoot) ? "[grey70]auto[/]" : $"[green]{Markup.Escape(paths.ConfiguredLibraryRoot)}[/]");
        table.AddRow("[white]Effective Library[/]", paths.RemoteMode || string.IsNullOrWhiteSpace(paths.EffectiveLibraryRoot) ? "[grey70]n/a[/]" : $"[springgreen3_1]{Markup.Escape(paths.EffectiveLibraryRoot)}[/]");
        table.AddRow("[white]Configured Cache[/]", string.IsNullOrWhiteSpace(paths.ConfiguredCachePath) ? "[grey70]default[/]" : $"[green]{Markup.Escape(paths.ConfiguredCachePath)}[/]");
        table.AddRow("[white]Effective Cache[/]", $"[cyan]{Markup.Escape(paths.EffectiveCachePath)}[/]");
        table.AddRow("[white]Manifest URL[/]", $"[deepskyblue1]{Markup.Escape(paths.EffectiveManifestUrl)}[/]");
        table.AddRow("[white]Title Map URL[/]", $"[deepskyblue1]{Markup.Escape(paths.EffectiveTitleMapUrl)}[/]");
        table.AddRow("[white]Content Base URL[/]", $"[deepskyblue1]{Markup.Escape(paths.EffectiveContentBaseUrl)}[/]");
        table.AddRow("[white]Download Cache[/]", $"[cyan]{Markup.Escape(paths.EffectiveDownloadCachePath)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class AvatarLibrarySetCommand : Command<AvatarLibrarySetCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--path <DIR>")]
        [Description("Set the default Avatar-Item-Collection root.")]
        public string? Path { get; init; }

        [CommandOption("--cache <PATH>")]
        [Description("Set the avatar index cache file or directory.")]
        public string? CachePath { get; init; }

        [CommandOption("--manifest-url <URL>")]
        [Description("Set the hosted avatar manifest URL.")]
        public string? ManifestUrl { get; init; }

        [CommandOption("--title-map-url <URL>")]
        [Description("Set the hosted avatar title map URL.")]
        public string? TitleMapUrl { get; init; }

        [CommandOption("--content-base-url <URL>")]
        [Description("Set the hosted avatar content base URL.")]
        public string? ContentBaseUrl { get; init; }

        [CommandOption("--download-cache <DIR>")]
        [Description("Set the cache directory for downloaded avatar packages.")]
        public string? DownloadCachePath { get; init; }

        [CommandOption("--clear")]
        [Description("Clear saved avatar library settings.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Clear) {
            AvatarCommandHelpers.SaveConfiguredPaths(null, null, null, null, null, null, true);
            OperationFeedback.WriteSuccess("Avatar library settings cleared", "[grey]The default avatar library and cache paths were removed.[/]");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(settings.Path) &&
            string.IsNullOrWhiteSpace(settings.CachePath) &&
            string.IsNullOrWhiteSpace(settings.ManifestUrl) &&
            string.IsNullOrWhiteSpace(settings.TitleMapUrl) &&
            string.IsNullOrWhiteSpace(settings.ContentBaseUrl) &&
            string.IsNullOrWhiteSpace(settings.DownloadCachePath)) {
            AnsiConsole.MarkupLine("[red]Provide --path, --cache, --manifest-url, --title-map-url, --content-base-url, --download-cache, or --clear.[/]");
            return 1;
        }

        AvatarCommandHelpers.SaveConfiguredPaths(
            settings.Path,
            settings.CachePath,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath,
            false);
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.Path,
            settings.CachePath,
            false,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        OperationFeedback.WriteSuccess(
            "Avatar library settings updated",
            $"[grey]Mode:[/] [{(paths.RemoteMode ? "gold1" : "springgreen3_1")}]{(paths.RemoteMode ? "remote" : "local")}[/]\n[grey]Library:[/] {(string.IsNullOrWhiteSpace(paths.EffectiveLibraryRoot) ? "[grey70]n/a[/]" : $"[springgreen3_1]{Markup.Escape(paths.EffectiveLibraryRoot)}[/]")}\n[grey]Cache:[/] [cyan]{Markup.Escape(paths.EffectiveCachePath)}[/]\n[grey]Manifest:[/] [deepskyblue1]{Markup.Escape(paths.EffectiveManifestUrl)}[/]\n[grey]Content Base:[/] [deepskyblue1]{Markup.Escape(paths.EffectiveContentBaseUrl)}[/]");
        return 0;
    }
}

public sealed class AvatarGamesCommand : AsyncCommand<AvatarGamesCommand.Settings> {
    public sealed class Settings : AvatarLibrarySettings {
        [CommandOption("--search <TEXT>")]
        [Description("Filter titles by name, publisher, or title id.")]
        public string? Search { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Maximum titles to show (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [Description("Force a fresh library scan instead of using the cache.")]
        public bool NoCache { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None);
        IReadOnlyList<AvatarTitleSummary> titles = AvatarCommandHelpers.FilterTitles(index.Titles, settings.Search, AvatarCommandHelpers.ParseLimit(settings.Limit)).ToArray();

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                paths.EffectiveLibraryRoot,
                paths.EffectiveCachePath,
                paths.EffectiveManifestUrl,
                paths.EffectiveContentBaseUrl,
                Count = titles.Count,
                Titles = titles
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar Games[/]").RuleStyle("grey"));
        if (titles.Count == 0) {
            OperationFeedback.WriteWarning("Avatar games", "No matching avatar titles were found.");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
        table.AddColumn(new TableColumn("[green]Game[/]"));
        table.AddColumn(new TableColumn("[deepskyblue1]Items[/]"));
        table.AddColumn(new TableColumn("[gold1]Size[/]"));
        table.AddColumn(new TableColumn("[grey]Publishers[/]"));
        foreach (AvatarTitleSummary title in titles) {
            string publishers = title.Publishers.Count == 0 ? "-" : string.Join(", ", title.Publishers.Take(3));
            table.AddRow(
                $"[cyan]0x{title.TitleId:X8}[/]",
                $"[green]{Markup.Escape(title.TitleName)}[/]",
                $"[deepskyblue1]{title.ItemCount}[/]",
                $"[gold1]{FtpHelpers.FormatBytes(title.TotalBytes)}[/]",
                $"[grey]{Markup.Escape(publishers)}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class AvatarItemsCommand : AsyncCommand<AvatarItemsCommand.Settings> {
    public sealed class Settings : AvatarLibrarySettings {
        [CommandOption("--titleid <TITLEID>")]
        [Description("Restrict items to a Title ID.")]
        public string? TitleId { get; init; }

        [CommandOption("--game <TEXT>")]
        [Description("Restrict items to a game name match.")]
        public string? Game { get; init; }

        [CommandOption("--search <TEXT>")]
        [Description("Search by item name, game name, or content id.")]
        public string? Search { get; init; }

        [CommandOption("--publisher <TEXT>")]
        [Description("Restrict items to one publisher.")]
        public string? Publisher { get; init; }

        [CommandOption("--tag <TEXT>")]
        [Description("Restrict items to one derived tag.")]
        public string? Tag { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Maximum items to show (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [Description("Force a fresh library scan instead of using the cache.")]
        public bool NoCache { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, !settings.NoCache, CancellationToken.None);
        IReadOnlyList<AvatarItemRecord> items = AvatarCommandHelpers.FilterItems(index, settings);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                paths.EffectiveLibraryRoot,
                paths.EffectiveCachePath,
                paths.EffectiveManifestUrl,
                paths.EffectiveContentBaseUrl,
                Count = items.Count,
                Items = items
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar Items[/]").RuleStyle("grey"));
        if (items.Count == 0) {
            OperationFeedback.WriteWarning("Avatar items", "No matching avatar items were found.");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
        table.AddColumn(new TableColumn("[green]Game[/]"));
        table.AddColumn(new TableColumn("[springgreen3_1]Item[/]"));
        table.AddColumn(new TableColumn("[gold1]Content ID[/]"));
        table.AddColumn(new TableColumn("[deepskyblue1]Layout[/]"));
        table.AddColumn(new TableColumn("[grey]Size[/]"));
        foreach (AvatarItemRecord item in items) {
            table.AddRow(
                $"[cyan]0x{item.TitleId:X8}[/]",
                $"[green]{Markup.Escape(item.TitleName)}[/]",
                $"[springgreen3_1]{Markup.Escape(item.DisplayName)}[/]",
                $"[gold1]{Markup.Escape(item.ContentId)}[/]",
                $"[deepskyblue1]{Markup.Escape(AvatarCommandHelpers.DescribeLayout(item))}[/]",
                $"[grey]{FtpHelpers.FormatBytes(item.SizeBytes)}[/]");
        }
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class AvatarInstallCommand : AsyncCommand<AvatarInstallCommand.Settings> {
    public class Settings : AvatarInstallSettingsBase {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index = await AvatarCommandHelpers.LoadIndexAsync(paths, true, CancellationToken.None);
        IReadOnlyList<AvatarItemRecord> items = AvatarCommandHelpers.SelectInstallItems(index, settings);
        return await AvatarInstallFlow.RunAsync(paths, settings, items, CancellationToken.None);
    }
}
