using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarCommandHelpers {
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(30);

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
    internal sealed record AvatarInstallProfilePreview(
        string Mode,
        string Detail,
        string? Gamertag,
        string? XuidText,
        bool RequiresConsoleProbe);

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
            AvatarLibraryIndex remoteIndex = await AvatarRemoteService.LoadIndexAsync(
                paths.EffectiveManifestUrl,
                paths.EffectiveTitleMapUrl,
                paths.EffectiveContentBaseUrl,
                useCache,
                paths.EffectiveCachePath,
                cancellationToken);

            string? overlayRoot = null;
            if (!string.IsNullOrWhiteSpace(paths.ConfiguredLibraryRoot)) {
                try {
                    overlayRoot = AvatarLibraryService.ResolveCollectionRoot(paths.ConfiguredLibraryRoot);
                }
                catch {
                    overlayRoot = null;
                }
            }

            if (string.IsNullOrWhiteSpace(overlayRoot)) {
                try {
                    overlayRoot = AvatarLibraryService.ResolveCollectionRoot();
                }
                catch {
                    overlayRoot = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(overlayRoot)) {
                string overlayCachePath = Path.Combine(CliPaths.CachePath, "avatar", "avatar-local-overlay-index.v2.json");
                AvatarLibraryIndex overlayIndex = AvatarLibraryService.LoadIndex(overlayRoot, useCache, overlayCachePath);
                remoteIndex = AvatarLibraryService.MergeMetadata(remoteIndex, overlayIndex);
            }

            return remoteIndex;
        }

        return AvatarLibraryService.LoadIndex(paths.EffectiveLibraryRoot!, useCache, paths.EffectiveCachePath);
    }

    public static AvatarCacheStatus DescribeCacheStatus(AvatarResolvedPaths paths) {
        return paths.RemoteMode
            ? AvatarRemoteService.DescribeStatus(
                paths.EffectiveCachePath,
                paths.EffectiveManifestUrl,
                paths.EffectiveTitleMapUrl,
                paths.EffectiveContentBaseUrl)
            : AvatarIndexCache.DescribeStatus(
                paths.EffectiveCachePath,
                AvatarLibraryService.BuildFingerprint(paths.EffectiveLibraryRoot!));
    }

    public static AvatarInstallProfilePreview DescribeProfileIntent(AvatarInstallSettingsBase settings) {
        if (TryResolveExplicitOwnership(settings, out AvatarResolvedOwnership ownership, out string? explicitError)) {
            if (explicitError != null)
                throw new InvalidOperationException(explicitError);

            return new AvatarInstallProfilePreview(
                "explicit",
                "Explicit XUID supplied on the command line.",
                ownership.Gamertag,
                ownership.XuidText,
                false);
        }

        if (!string.IsNullOrWhiteSpace(GetOptionText(settings, "Xuid") ?? GetCommandLineOptionText("xuid"))) {
            throw new InvalidOperationException("Invalid XUID.");
        }

        string? gamertag = GetOptionText(settings, "Gamertag") ?? GetCommandLineOptionText("gamertag");
        return new AvatarInstallProfilePreview(
            "current-user",
            "Live console probe skipped for dry-run.",
            string.IsNullOrWhiteSpace(gamertag) ? null : gamertag.Trim(),
            null,
            true);
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

    public static string ResolveItemDisplayName(AvatarItemRecord item) {
        return AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName);
    }

    public static string DescribeItem(AvatarItemRecord item) {
        return $"{item.TitleName} - {ResolveItemDisplayName(item)}";
    }

    internal static bool TryResolveExplicitOwnership(
        AvatarInstallSettingsBase settings,
        out AvatarResolvedOwnership ownership,
        out string? error) {
        ownership = null!;
        error = null;

        string? explicitXuidText = GetOptionText(settings, "Xuid") ?? GetCommandLineOptionText("xuid");
        if (string.IsNullOrWhiteSpace(explicitXuidText))
            return false;

        string? explicitGamertag = GetOptionText(settings, "Gamertag") ?? GetCommandLineOptionText("gamertag");
        if (!TryParseXuid(explicitXuidText, out ulong explicitXuid)) {
            error = "Invalid XUID.";
            return false;
        }

        ownership = new AvatarResolvedOwnership(
            explicitGamertag,
            explicitXuid,
            $"0x{explicitXuid:X16}",
            "Explicit XUID",
            false);
        return true;
    }

    public static async Task<AvatarResolvedOwnership> ResolveOwnershipAsync(
        AvatarInstallSettingsBase settings,
        string targetIp,
        CancellationToken cancellationToken) {
        if (TryResolveExplicitOwnership(settings, out AvatarResolvedOwnership explicitOwnership, out string? explicitError))
            return explicitOwnership;
        if (explicitError != null)
            throw new InvalidOperationException(explicitError);

        CliConfig config = CliConfig.Load();
        int xbdmPort = settings.XbdmPort ?? config.DefaultPort ?? 730;
        int xbdmTimeout = settings.TimeoutMs ?? 5000;
        using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
            Host = targetIp,
            Port = xbdmPort,
            TimeoutMs = xbdmTimeout
        }, cancellationToken);

        ProfileHelpers.ResolvedIdentityInfo identity = await ProfileHelpers.ResolveSignedInIdentityAsync(
            client,
            targetIp,
            xbdmPort,
            xbdmTimeout,
            config,
            allowF3: true,
            allowProfilePackage: true,
            cancellationToken);

        if (!TryParseXuid(identity.Xuid, out ulong resolvedXuid)) {
            string currentUserText = !string.IsNullOrWhiteSpace(identity.Gamertag)
                ? identity.Gamertag!
                : "none";
            if (string.Equals(currentUserText, "none", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("It seems you are not signed in. Please sign in and run `rgh signin state` to verify.");
            }

            throw new InvalidOperationException($"It seems no usable signed-in profile was detected. Current user: {currentUserText}. Please sign in again and run `rgh signin state` to verify.");
        }

        return new AvatarResolvedOwnership(
            identity.Gamertag,
            resolvedXuid,
            $"0x{resolvedXuid:X16}",
            identity.SignInStateText,
            true);
    }

    internal static async Task<AvatarResolvedOwnership> ResolveBrowseOwnershipAsync(
        AvatarInstallSettingsBase settings,
        CancellationToken cancellationToken) {
        if (TryResolveExplicitOwnership(settings, out AvatarResolvedOwnership explicitOwnership, out string? explicitError))
            return explicitOwnership;
        if (explicitError != null)
            throw new InvalidOperationException(explicitError);

        (string xbdmIp, _, _) = await ResolveXbdmTargetAsync(settings, cancellationToken);
        return await ResolveOwnershipAsync(settings, xbdmIp, cancellationToken);
    }

    public static async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken = default) {
        return await RunWithTimeoutCoreAsync(action, timeout, cancellationToken);
    }

    public static async Task RunWithTimeoutAsync(Func<CancellationToken, Task> action, TimeSpan timeout, CancellationToken cancellationToken = default) {
        await RunWithTimeoutCoreAsync(async token => {
            await action(token);
            return 0;
        }, timeout, cancellationToken);
    }

    private static async Task<T> RunWithTimeoutCoreAsync<T>(Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken) {
        using CancellationTokenSource timeoutSource = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        Task<T> actionTask = action(timeoutSource.Token);
        Task timeoutTask = Task.Delay(timeout);
        Task? cancellationTask = cancellationToken.CanBeCanceled
            ? Task.Delay(Timeout.Infinite, cancellationToken)
            : null;

        Task completed = cancellationTask == null
            ? await Task.WhenAny(actionTask, timeoutTask).ConfigureAwait(false)
            : await Task.WhenAny(actionTask, timeoutTask, cancellationTask).ConfigureAwait(false);

        if (completed == actionTask)
            return await actionTask.ConfigureAwait(false);

        if (completed == cancellationTask)
            throw new OperationCanceledException(cancellationToken);

        timeoutSource.Cancel();
        Task graceTask = Task.Delay(TimeSpan.FromSeconds(1));
        Task finished = await Task.WhenAny(actionTask, graceTask).ConfigureAwait(false);
        if (finished == actionTask)
            return await actionTask.ConfigureAwait(false);

        throw new TimeoutException($"Operation timed out after {FormatTimeout(timeout)}.");
    }

    private static string FormatTimeout(TimeSpan timeout) {
        if (timeout.TotalSeconds >= 1)
            return timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " seconds";

        return timeout.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
    }

    internal static string GetAvatarBrowseGuiCommand() {
        return "avatar browse --remote";
    }

    internal static bool TryBuildAvatarBrowseGuiCommand(RuntimePresenceSnapshot? snapshot, out string command, out string? actionableMessage) {
        command = string.Empty;
        actionableMessage = null;

        if (snapshot == null || !snapshot.Connected) {
            actionableMessage = "Connect to a console first.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(snapshot.Ip) || !snapshot.Port.HasValue) {
            actionableMessage = "Connected target is unavailable.";
            return false;
        }

        StringBuilder commandBuilder = new StringBuilder();
        commandBuilder.Append("avatar browse --remote --ip ");
        commandBuilder.Append(QuoteArgument(snapshot.Ip));
        commandBuilder.Append(" --xbdm-port ");
        commandBuilder.Append(snapshot.Port.Value.ToString(CultureInfo.InvariantCulture));

        string? xuid = string.IsNullOrWhiteSpace(snapshot.Xuid) ? null : snapshot.Xuid.Trim();
        string? gamertag = string.IsNullOrWhiteSpace(snapshot.Gamertag) ? null : snapshot.Gamertag.Trim();
        if (!string.IsNullOrWhiteSpace(xuid)) {
            commandBuilder.Append(" --xuid ");
            commandBuilder.Append(QuoteArgument(xuid));
            if (!string.IsNullOrWhiteSpace(gamertag)) {
                commandBuilder.Append(" --gamertag ");
                commandBuilder.Append(QuoteArgument(gamertag));
            }
        }
        else if (IsSignedInPresenceState(snapshot.SignInStateText)) {
            commandBuilder.Append(" --current-user");
            if (!string.IsNullOrWhiteSpace(gamertag)) {
                commandBuilder.Append(" --gamertag ");
                commandBuilder.Append(QuoteArgument(gamertag));
            }
        }
        else {
            actionableMessage = "Sign in on the connected console first.";
            return false;
        }

        command = commandBuilder.ToString();
        return true;
    }

    internal static async Task RunWithWatchdogAsync(Func<Task> action, TimeSpan timeout, Action onTimeout) {
        Task actionTask = action();
        Task timeoutTask = Task.Delay(timeout);
        if (await Task.WhenAny(actionTask, timeoutTask) == timeoutTask) {
            onTimeout();
            return;
        }

        await actionTask;
    }

    private static string? GetOptionText(object settings, string propertyName) {
        PropertyInfo? property = settings.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null)
            return null;

        object? value = property.GetValue(settings);
        return value?.ToString();
    }

    private static string? GetCommandLineOptionText(string optionName) {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (arg.Equals($"--{optionName}", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals($"-{optionName}", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 < args.Length)
                    return args[i + 1];
                return null;
            }

            string prefix = $"--{optionName}=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }

    private static bool IsSignedInPresenceState(string? text) {
        string? normalized = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Equals("not detected", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("not signed in", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return normalized.IndexOf("signed", StringComparison.OrdinalIgnoreCase) >= 0 &&
            normalized.IndexOf("not", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string QuoteArgument(string value) {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";

        return value.Contains(' ') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
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
            titleId = ParseTitleId(settings.TitleId);
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
                titleId = ParseTitleId(settings.TitleId);
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

    public static uint ParseTitleId(string titleId) {
        if (!SaveHelpers.TryParseTitleId(titleId, out uint parsedTitleId))
            throw new InvalidOperationException("Invalid --titleid.");

        return parsedTitleId;
    }

    public static void ValidateTitleIdIfPresent(string? titleId) {
        if (!string.IsNullOrWhiteSpace(titleId))
            ParseTitleId(titleId);
    }

    public static async Task<IReadOnlyList<AvatarItemRecord>> MaterializeInstallItemsAsync(
        IReadOnlyList<AvatarItemRecord> items,
        AvatarResolvedPaths paths,
        CancellationToken cancellationToken,
        bool showProgress = true) {
        if (!paths.RemoteMode)
            return items;

        List<AvatarItemRecord> materialized = new List<AvatarItemRecord>(items.Count);
        async Task DownloadAsync(TransferBatchScope? batch) {
            foreach (AvatarItemRecord item in items) {
                batch?.StartFile(DescribeItem(item), item.SizeBytes);
                Progress<long>? progress = batch is null
                    ? null
                    : new Progress<long>(value => batch.ReportFileProgress(value));
                string localPath = await AvatarRemoteService.EnsureLocalPackageAsync(
                    item,
                    paths.EffectiveDownloadCachePath,
                    progress,
                    cancellationToken);
                materialized.Add(item with { SourcePath = localPath });
                batch?.CompleteFile();
            }
        }

        if (showProgress) {
            await CliOutput.RunBatchProgressAsync(
                $"Avatar download {items.Count} item(s)",
                items.Select(item => new CliOutput.TransferBatchItem(DescribeItem(item), item.SizeBytes)).ToList(),
                DownloadAsync);
        }
        else {
            await DownloadAsync(null);
        }

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
                await using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
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
            await EnsureRemoteDirectorySegmentViaXbdmAsync(client, current, cancellationToken);
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

    private static async Task EnsureRemoteDirectorySegmentViaXbdmAsync(
        XbdmClient client,
        string xbdmPath,
        CancellationToken cancellationToken) {
        Exception? createError = null;
        try {
            await client.CreateDirectoryAsync(xbdmPath, cancellationToken);
        }
        catch (Exception ex) {
            createError = ex;
        }

        if (await RemoteDirectoryExistsViaXbdmAsync(client, xbdmPath, cancellationToken))
            return;

        string displayPath = ToDisplayRemotePath(xbdmPath);
        if (createError != null)
            throw new InvalidOperationException($"Unable to create remote directory {displayPath}.", createError);

        throw new InvalidOperationException($"Unable to verify remote directory {displayPath}.");
    }

    private static async Task<bool> RemoteDirectoryExistsViaXbdmAsync(
        XbdmClient client,
        string xbdmPath,
        CancellationToken cancellationToken) {
        string? parent = Path.GetDirectoryName(xbdmPath);
        string directoryName = Path.GetFileName(xbdmPath);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(directoryName))
            return false;

        try {
            IReadOnlyList<XbdmFileEntry> entries = await client.GetDirectoryAsync(
                parent.EndsWith("\\", StringComparison.Ordinal) ? parent : parent + "\\",
                cancellationToken);
            return entries.Any(entry =>
                entry.IsDirectory &&
                string.Equals(entry.Name, directoryName, StringComparison.OrdinalIgnoreCase));
        }
        catch {
            return false;
        }
    }

    private static string ToDisplayRemotePath(string xbdmPath) {
        return "/" + xbdmPath.Replace('\\', '/').TrimStart('/').Replace(":/", "/", StringComparison.Ordinal);
    }
}

public abstract class AvatarLibrarySettings : CommandSettings {
    [CommandOption("--library <DIR>")]
    [LocalizedDescription("Avatar item library root. Defaults to config or Avatar-Item-Collection.")]
    public string? LibraryRoot { get; init; }

    [CommandOption("--cache <PATH>")]
    [LocalizedDescription("Avatar index cache file or directory.")]
    public string? CachePath { get; init; }

    [CommandOption("--remote")]
    [LocalizedDescription("Use the hosted avatar library instead of the local corpus.")]
    public bool Remote { get; init; }

    [CommandOption("--manifest-url <URL>")]
    [LocalizedDescription("Override the hosted avatar manifest URL.")]
    public string? ManifestUrl { get; init; }

    [CommandOption("--title-map-url <URL>")]
    [LocalizedDescription("Override the hosted avatar title map URL.")]
    public string? TitleMapUrl { get; init; }

    [CommandOption("--content-base-url <URL>")]
    [LocalizedDescription("Override the hosted avatar content base URL.")]
    public string? ContentBaseUrl { get; init; }

    [CommandOption("--download-cache <DIR>")]
    [LocalizedDescription("Directory used to cache downloaded avatar packages.")]
    public string? DownloadCachePath { get; init; }

    [CommandOption("--json")]
    [LocalizedDescription("Emit JSON output.")]
    public bool Json { get; init; }
}

public sealed class AvatarLibraryShowCommand : Command<AvatarLibraryShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
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
        [LocalizedDescription("Set the default Avatar-Item-Collection root.")]
        public string? Path { get; init; }

        [CommandOption("--cache <PATH>")]
        [LocalizedDescription("Set the avatar index cache file or directory.")]
        public string? CachePath { get; init; }

        [CommandOption("--manifest-url <URL>")]
        [LocalizedDescription("Set the hosted avatar manifest URL.")]
        public string? ManifestUrl { get; init; }

        [CommandOption("--title-map-url <URL>")]
        [LocalizedDescription("Set the hosted avatar title map URL.")]
        public string? TitleMapUrl { get; init; }

        [CommandOption("--content-base-url <URL>")]
        [LocalizedDescription("Set the hosted avatar content base URL.")]
        public string? ContentBaseUrl { get; init; }

        [CommandOption("--download-cache <DIR>")]
        [LocalizedDescription("Set the cache directory for downloaded avatar packages.")]
        public string? DownloadCachePath { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear saved avatar library settings.")]
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

public sealed class AvatarCacheStatusCommand : Command<AvatarCacheStatusCommand.Settings> {
    public sealed class Settings : AvatarLibrarySettings {
    }

    public override int Execute(CommandContext context, Settings settings) {
        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarCacheStatus status = AvatarCommandHelpers.DescribeCacheStatus(paths);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                paths.EffectiveCachePath,
                status.State,
                status.Detail,
                status.ExpectedSignature,
                status.CachedSignature,
                status.GeneratedUtc,
                status.ItemCount,
                status.TotalBytes,
                paths.ConfiguredLibraryRoot,
                paths.EffectiveLibraryRoot,
                paths.ConfiguredCachePath,
                paths.ConfiguredManifestUrl,
                paths.EffectiveManifestUrl,
                paths.ConfiguredTitleMapUrl,
                paths.EffectiveTitleMapUrl,
                paths.ConfiguredContentBaseUrl,
                paths.EffectiveContentBaseUrl
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Avatar cache[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
        table.AddRow("[white]Mode[/]", paths.RemoteMode ? "[gold1]remote[/]" : "[springgreen3_1]local[/]");
        table.AddRow("[white]Cache Path[/]", $"[cyan]{Markup.Escape(status.CachePath)}[/]");
        table.AddRow("[white]State[/]", FormatCacheState(status.State));
        table.AddRow("[white]Detail[/]", $"[grey]{Markup.Escape(status.Detail)}[/]");
        table.AddRow("[white]Expected[/]", $"[deepskyblue1]{Markup.Escape(status.ExpectedSignature)}[/]");
        table.AddRow("[white]Cached[/]", string.IsNullOrWhiteSpace(status.CachedSignature) ? "[grey70]n/a[/]" : $"[deepskyblue1]{Markup.Escape(status.CachedSignature)}[/]");
        table.AddRow("[white]Generated[/]", status.GeneratedUtc.HasValue ? $"[grey]{Markup.Escape(status.GeneratedUtc.Value.UtcDateTime.ToString("u", CultureInfo.InvariantCulture))}[/]" : "[grey70]n/a[/]");
        table.AddRow("[white]Items[/]", status.ItemCount.HasValue ? $"[cyan]{status.ItemCount.Value.ToString(CultureInfo.InvariantCulture)}[/]" : "[grey70]n/a[/]");
        table.AddRow("[white]Bytes[/]", status.TotalBytes.HasValue ? $"[cyan]{FtpHelpers.FormatBytes(status.TotalBytes.Value)}[/]" : "[grey70]n/a[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    private static string FormatCacheState(string state) {
        return state.ToLowerInvariant() switch {
            "ready" => "[springgreen3_1]ready[/]",
            "stale" => "[gold1]stale[/]",
            "missing" => "[grey70]missing[/]",
            "corrupt" => "[red]corrupt[/]",
            _ => $"[white]{Markup.Escape(state)}[/]"
        };
    }
}

public sealed class AvatarCacheRefreshCommand : AsyncCommand<AvatarCacheRefreshCommand.Settings> {
    public sealed class Settings : AvatarLibrarySettings {
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

        AvatarLibraryIndex index;
        try {
            index = await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarCommandHelpers.LoadIndexAsync(paths, false, token),
                AvatarCommandHelpers.DefaultOperationTimeout);
        }
        catch (OperationCanceledException) {
            OperationFeedback.WriteFailure("Avatar cache refresh", $"Avatar cache refresh timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException) {
            OperationFeedback.WriteFailure("Avatar cache refresh", $"Avatar cache refresh timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (Exception) {
            OperationFeedback.WriteFailure("Avatar cache refresh", "Avatar cache refresh failed. Check the avatar library or remote endpoints and try again.");
            return 1;
        }

        if (!paths.RemoteMode)
            AvatarIndexCache.Save(paths.EffectiveCachePath, index);

        AvatarCacheStatus status = AvatarCommandHelpers.DescribeCacheStatus(paths);
        if (settings.Json) {
            CliOutput.EmitJson(new {
                Mode = paths.RemoteMode ? "remote" : "local",
                paths.EffectiveCachePath,
                status.State,
                status.Detail,
                GeneratedUtc = index.GeneratedUtc,
                ItemCount = index.Items.Count,
                TitleCount = index.Titles.Count,
                TotalBytes = index.Items.Sum(item => item.SizeBytes)
            });
            return 0;
        }

        OperationFeedback.WriteSuccess(
            "Avatar cache refreshed",
            $"[grey]Mode:[/] [{(paths.RemoteMode ? "gold1" : "springgreen3_1")}]{(paths.RemoteMode ? "remote" : "local")}[/]\n[grey]Cache:[/] [cyan]{Markup.Escape(status.CachePath)}[/]\n[grey]State:[/] [springgreen3_1]ready[/]\n[grey]Items:[/] [cyan]{index.Items.Count.ToString(CultureInfo.InvariantCulture)}[/]\n[grey]Titles:[/] [cyan]{index.Titles.Count.ToString(CultureInfo.InvariantCulture)}[/]");
        return 0;
    }
}

public sealed class AvatarGamesCommand : AsyncCommand<AvatarGamesCommand.Settings> {
    public sealed class Settings : AvatarLibrarySettings {
        [CommandOption("--search <TEXT>")]
        [LocalizedDescription("Filter titles by name, publisher, or title id.")]
        public string? Search { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum titles to show (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [LocalizedDescription("Force a fresh library scan instead of using the cache.")]
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
        [LocalizedDescription("Restrict items to a Title ID.")]
        public string? TitleId { get; init; }

        [CommandOption("--game <TEXT>")]
        [LocalizedDescription("Restrict items to a game name match.")]
        public string? Game { get; init; }

        [CommandOption("--search <TEXT>")]
        [LocalizedDescription("Search by item name, game name, or content id.")]
        public string? Search { get; init; }

        [CommandOption("--publisher <TEXT>")]
        [LocalizedDescription("Restrict items to one publisher.")]
        public string? Publisher { get; init; }

        [CommandOption("--tag <TEXT>")]
        [LocalizedDescription("Restrict items to one derived tag.")]
        public string? Tag { get; init; }

        [CommandOption("--limit <N>")]
        [LocalizedDescription("Maximum items to show (default: 100, 0 = no limit).")]
        public int? Limit { get; init; }

        [CommandOption("--no-cache")]
        [LocalizedDescription("Force a fresh library scan instead of using the cache.")]
        public bool NoCache { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        AvatarCommandHelpers.ValidateTitleIdIfPresent(settings.TitleId);
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
                $"[springgreen3_1]{Markup.Escape(AvatarCommandHelpers.ResolveItemDisplayName(item))}[/]",
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
        try {
            if (!string.IsNullOrWhiteSpace(settings.TitleId))
                AvatarCommandHelpers.ParseTitleId(settings.TitleId);
        }
        catch (InvalidOperationException ex) when (settings.Json) {
            return WriteJsonFailure(ex.Message, "AVATAR_INSTALL_TITLE_ID_INVALID");
        }

        AvatarCommandHelpers.AvatarResolvedPaths paths = AvatarCommandHelpers.ResolvePaths(
            settings.LibraryRoot,
            settings.CachePath,
            settings.Remote,
            settings.ManifestUrl,
            settings.TitleMapUrl,
            settings.ContentBaseUrl,
            settings.DownloadCachePath);
        AvatarLibraryIndex index;
        try {
            index = await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarCommandHelpers.LoadIndexAsync(paths, true, token),
                AvatarCommandHelpers.DefaultOperationTimeout);
        }
        catch (OperationCanceledException) {
            if (settings.Json)
                return WriteJsonFailure($"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.", "AVATAR_INSTALL_LOAD_TIMEOUT");
            OperationFeedback.WriteFailure("Avatar install", $"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException) {
            if (settings.Json)
                return WriteJsonFailure($"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.", "AVATAR_INSTALL_LOAD_TIMEOUT");
            OperationFeedback.WriteFailure("Avatar install", $"Avatar loading timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (Exception) {
            if (settings.Json)
                return WriteJsonFailure("Avatar loading failed. Check the avatar library and try again.", "AVATAR_INSTALL_LOAD_FAILED");
            OperationFeedback.WriteFailure("Avatar install", "Avatar loading failed. Check the avatar library and try again.");
            return 1;
        }
        IReadOnlyList<AvatarItemRecord> items;
        try {
            items = AvatarCommandHelpers.SelectInstallItems(index, settings);
        }
        catch (InvalidOperationException ex) when (settings.Json) {
            return WriteJsonFailure(ex.Message, "AVATAR_INSTALL_SELECTION_INVALID");
        }
        try {
            return await AvatarCommandHelpers.RunWithTimeoutAsync(
                token => AvatarInstallFlow.RunAsync(paths, settings, items, token),
                AvatarCommandHelpers.DefaultOperationTimeout);
        }
        catch (OperationCanceledException) {
            if (settings.Json)
                return WriteJsonFailure($"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.", "AVATAR_INSTALL_TIMEOUT");
            OperationFeedback.WriteFailure("Avatar install", $"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (TimeoutException) {
            if (settings.Json)
                return WriteJsonFailure($"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.", "AVATAR_INSTALL_TIMEOUT");
            OperationFeedback.WriteFailure("Avatar install", $"Avatar install timed out after {(int)AvatarCommandHelpers.DefaultOperationTimeout.TotalSeconds} seconds.");
            return 1;
        }
        catch (Exception) {
            if (settings.Json)
                return WriteJsonFailure("Avatar install failed. Check available storage and try again.", "AVATAR_INSTALL_FAILED");
            OperationFeedback.WriteFailure("Avatar install", "Avatar install failed. Check available storage and try again.");
            return 1;
        }
    }

    private static int WriteJsonFailure(string message, string code) {
        CliOutput.EmitJsonError(new CliErrorEnvelope(
            "Avatar install failed",
            message,
            code,
            new[] { "Correct the command arguments or connection state and retry." }));
        return 1;
    }
}

