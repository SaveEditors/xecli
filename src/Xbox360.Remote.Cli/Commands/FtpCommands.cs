using System.ComponentModel;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal static class FtpHelpers {
    private const int ReconnectAttempts = 3;
    private const int ReconnectDelaySeconds = 10;
    private const int UploadReconnectDelayMs = 500;
    private const int MaxRecursiveTransferDepth = 64;
    private static readonly HashSet<string> RootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Hdd1",
        "HddX",
        "Usb0",
        "Usb1",
        "Usb2",
        "System",
        "SysExt",
        "Game",
        "Cache",
        "Flash",
        "D"
    };

    public sealed record DownloadPlanItem(string RemotePath, string LocalPath, long Size);
    public sealed record UploadPlanItem(string LocalPath, string RemotePath, long Size);
    public sealed record HashResult(string RemotePath, string Algorithm, string Hash, long? Size, string TransferMode, bool DownloadedForHash);
    public sealed record TransferVerificationResult(string RemotePath, string Algorithm, string LocalHash, string RemoteHash);
    public sealed record PlannedTransferVerification(string Mode, string Algorithm);
    public sealed record UploadDryRunPayload(
        string Mode,
        string Direction,
        string RemoteRoot,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PlannedTransferVerification? PlannedVerification,
        int TotalFiles,
        int Planned,
        int Transferred,
        int Skipped,
        int Failed,
        long TotalBytes,
        long Bytes,
        IReadOnlyList<UploadTransferJsonFile> Files,
        IReadOnlyList<UploadTransferJsonFile> WouldUpload,
        IReadOnlyList<UploadTransferJsonFile> SkippedExisting);
    public sealed record DownloadDryRunPayload(
        string Mode,
        string Direction,
        string RemoteRoot,
        string LocalRoot,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PlannedTransferVerification? PlannedVerification,
        int TotalFiles,
        int Planned,
        int Transferred,
        int Skipped,
        int Failed,
        long TotalBytes,
        long Bytes,
        IReadOnlyList<DownloadTransferJsonFile> Files,
        IReadOnlyList<DownloadTransferJsonFile> WouldDownload,
        IReadOnlyList<DownloadTransferJsonFile> SkippedExisting);
    public sealed record DownloadTransferJsonFile(
        string RelativePath,
        string RemotePath,
        string LocalPath,
        long Size,
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Error,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TransferVerificationResult? Verification);
    public sealed record DownloadTransferJsonFailure(string RemotePath, string LocalPath, string Error);
    public sealed record DownloadTransferJsonPayload(
        string Mode,
        string Direction,
        string RemoteRoot,
        string LocalRoot,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PlannedTransferVerification? Verification,
        int TotalFiles,
        int Planned,
        int Transferred,
        int Skipped,
        int Failed,
        long TotalBytes,
        long Bytes,
        IReadOnlyList<DownloadTransferJsonFile> Files,
        IReadOnlyList<DownloadTransferJsonFailure> Failures);
    public sealed record UploadTransferJsonFile(
        string RelativePath,
        string LocalPath,
        string RemotePath,
        long Size,
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Error,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TransferVerificationResult? Verification);
    public sealed record UploadTransferJsonFailure(string LocalPath, string RemotePath, string Error);
    public sealed record UploadTransferJsonPayload(
        string Mode,
        string Direction,
        string RemoteRoot,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PlannedTransferVerification? Verification,
        int TotalFiles,
        int Planned,
        int Transferred,
        int Skipped,
        int Failed,
        long TotalBytes,
        long Bytes,
        IReadOnlyList<UploadTransferJsonFile> Files,
        IReadOnlyList<UploadTransferJsonFile> SkippedExisting,
        IReadOnlyList<UploadTransferJsonFailure> Failures);
    public sealed record DiffResult(string RemotePath, string LocalFileName, string Algorithm, bool Match, string LocalHash, string RemoteHash, long? LocalSize, long? RemoteSize);
    internal sealed record FtpPreflightRefusal(string Title, string Detail, string Code);

    public static async Task<(string Ip, int Port, string User, string Pass, int TimeoutMs)> ResolveAsync(FtpConnectionSettings settings, CancellationToken cancellationToken) {
        CliConfig config = CliConfig.Load();
        TargetProfileStoreData targetStore = TargetProfileStore.Load();
        if (!TargetProfileStore.TryResolveProfile(targetStore, config, settings.Profile, out TargetProfileRecord? profile, out string profileError))
            throw new InvalidOperationException(profileError);

        string? ip = settings.Ip ?? profile?.Ip ?? config.DefaultIp;
        if (string.IsNullOrWhiteSpace(ip)) {
            (string resolvedIp, int _, int __) = await CliHelpers.ResolveTargetAsync(
                new ConnectionSettings {
                    Profile = settings.Profile
                },
                config,
                cancellationToken,
                persistDefaultTarget: false);
            ip = resolvedIp;
        }

        FtpConnectionDefaults configured = FtpEndpointHelpers.ResolveConfiguredConnection(config, profile);
        int port = FtpEndpointHelpers.GetEffectiveFtpPort(settings.Port ?? configured.Port);
        string user = settings.User ?? configured.User;
        string pass = settings.Pass ?? configured.Pass;
        int timeout = settings.TimeoutMs ?? 5000;

        return (ip, port, user, pass, timeout);
    }

    public static bool TryBuildRuntimeFtpPreflightRefusal(out FtpPreflightRefusal? refusal) {
        RuntimePresenceSnapshot snapshot = RuntimePresenceState.Current;
        refusal = null;

        if (!snapshot.Connected)
            return false;

        string? titleName = TrimOrNull(snapshot.TitleName);
        string? runningXex = TrimOrNull(snapshot.RunningXex);

        if (IsKnownFtpProvider(titleName) || IsKnownFtpProvider(runningXex))
            return false;

        if (string.IsNullOrWhiteSpace(titleName) && string.IsNullOrWhiteSpace(runningXex))
            return false;

        string titleContext = !string.IsNullOrWhiteSpace(titleName)
            ? $"title \"{Markup.Escape(titleName)}\""
            : (snapshot.TitleId.HasValue ? $"title 0x{snapshot.TitleId.Value:X8}" : "the active title");
        string xexContext = string.IsNullOrWhiteSpace(runningXex) ? string.Empty : $" Running XEX: {Markup.Escape(runningXex)}.";
        string detail =
            $"XeCLI can see a connected console, but {titleContext} is not a known FTP-capable dashboard or plugin.{xexContext} " +
            "FTP is expected from Aurora, FreestyleDash/FSD, XexMenu, or the DashLaunch FTP plugin. XBDM can still work in games, " +
            "but FTP may not be available from this title. Run `rgh ftp check` or `rgh ftp doctor` to probe port 21 before retrying.";
        refusal = new FtpPreflightRefusal(
            "FTP preflight blocked",
            detail,
            "FTP_PREFLIGHT_BLOCKED");
        return true;
    }

    public static bool TryWriteRuntimeFtpPreflightRefusal(bool json) {
        if (!TryBuildRuntimeFtpPreflightRefusal(out FtpPreflightRefusal? refusal) || refusal == null)
            return false;

        WriteRuntimeFtpPreflightRefusal(json, refusal);
        return true;
    }

    public static int WriteRuntimeFtpPreflightRefusal(bool json, FtpPreflightRefusal refusal) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                refusal.Title,
                refusal.Detail,
                refusal.Code,
                new[] {
                    "Run `rgh ftp check` or `rgh ftp doctor` to probe port 21.",
                    "Switch the console to Aurora, FreestyleDash/FSD, XexMenu, or the DashLaunch FTP plugin before retrying."
                }));
        }
        else {
            OperationFeedback.WriteFailure(refusal.Title, refusal.Detail);
        }

        return 1;
    }

    internal enum FtpProbeStatus {
        Available,
        Unavailable,
        Unreachable
    }

    internal sealed record FtpProbeResult(FtpProbeStatus Status);
    internal sealed record FtpProbeJsonPayload(
        string Status,
        int ExitCode,
        string Ip,
        int Port,
        int TimeoutMs,
        string Message,
        string Source,
        string EffectiveTarget,
        string SavedFtpDefaults,
        string ResumeSupport);

    public static async Task<FtpProbeResult> ProbeServiceAsync(string ip, int port, int timeoutMs, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(ip) || port is < 1 or > 65535)
            return new FtpProbeResult(FtpProbeStatus.Unreachable);

        int effectiveTimeout = FtpEndpointHelpers.GetEffectiveProbeTimeoutMs(timeoutMs);
        using TcpClient tcp = new TcpClient();
        Task connectTask = tcp.ConnectAsync(ip, port);
        Task timeoutTask = Task.Delay(effectiveTimeout, cancellationToken);
        Task completed = await Task.WhenAny(connectTask, timeoutTask);
        if (completed == timeoutTask) {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            return new FtpProbeResult(FtpProbeStatus.Unreachable);
        }

        try {
            await connectTask;
            return new FtpProbeResult(FtpProbeStatus.Available);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused || LooksLikeRefused(ex.Message)) {
            return new FtpProbeResult(FtpProbeStatus.Unavailable);
        }
        catch (SocketException ex) when (LooksLikeUnreachable(ex.SocketErrorCode, ex.Message)) {
            return new FtpProbeResult(FtpProbeStatus.Unreachable);
        }
        catch (TimeoutException) {
            return new FtpProbeResult(FtpProbeStatus.Unreachable);
        }
        catch (IOException ex) when (LooksLikeRefused(ex.Message)) {
            return new FtpProbeResult(FtpProbeStatus.Unavailable);
        }
        catch (IOException ex) when (LooksLikeUnreachable(ex.Message)) {
            return new FtpProbeResult(FtpProbeStatus.Unreachable);
        }
    }

    private static bool LooksLikeRefused(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("refused", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeUnreachable(SocketError error, string? message) {
        return error is SocketError.HostNotFound or
                        SocketError.HostUnreachable or
                        SocketError.NetworkDown or
                        SocketError.NetworkReset or
                        SocketError.NetworkUnreachable or
                        SocketError.TimedOut or
                        SocketError.NoData or
                        SocketError.TryAgain or
                        SocketError.AddressNotAvailable ||
               (!string.IsNullOrWhiteSpace(message) &&
                (message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("timed out", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool LooksLikeUnreachable(string? message) {
        return !string.IsNullOrWhiteSpace(message) &&
               (message.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownFtpProvider(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", string.Empty);
        return normalized.Contains("aurora", StringComparison.Ordinal) ||
               normalized.Contains("freestyledash", StringComparison.Ordinal) ||
               normalized == "fsd" ||
               normalized.Contains("xexmenu", StringComparison.Ordinal) ||
               normalized.Contains("dashlaunch", StringComparison.Ordinal);
    }

    private static string? TrimOrNull(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static AsyncFtpClient CreateClient(string ip, int port, string user, string pass, int timeoutMs) {
        AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
        ConfigureClient(client, timeoutMs);
        return client;
    }

    public static void ConfigureClient(AsyncFtpClient client, int timeoutMs) {
        client.Config.ConnectTimeout = timeoutMs;
        client.Config.ReadTimeout = timeoutMs;
        client.Config.DataConnectionConnectTimeout = timeoutMs;
        client.Config.DataConnectionReadTimeout = timeoutMs;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.UploadDataType = FtpDataType.Binary;
        client.Config.DownloadDataType = FtpDataType.Binary;
        client.Config.RetryAttempts = ReconnectAttempts;
        client.Config.SocketKeepAlive = true;
    }

    public static async Task<int> WithClientAsync(FtpConnectionSettings settings, Func<AsyncFtpClient, Task<int>> action, CancellationToken cancellationToken) {
        return await WithClientAsync<int>(settings, action, cancellationToken);
    }

    public static async Task<T> WithClientAsync<T>(FtpConnectionSettings settings, Func<AsyncFtpClient, Task<T>> action, CancellationToken cancellationToken) {
        (string ip, int port, string user, string pass, int timeout) = await ResolveAsync(settings, cancellationToken);
        Exception? lastError = null;

        for (int attempt = 1; attempt <= ReconnectAttempts; attempt++) {
            await using AsyncFtpClient client = CreateClient(ip, port, user, pass, timeout);
            try {
                await client.Connect(cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex)) {
                lastError = ex;
                if (attempt >= ReconnectAttempts)
                    break;
                if (Console.IsInputRedirected)
                    break;
                if (await ShowReconnectCountdownAsync(attempt, ReconnectAttempts, ReconnectDelaySeconds, cancellationToken))
                    throw new OperationCanceledException("Reconnection cancelled by user.");
                continue;
            }

            return await action(client);
        }

        CliErrorEnvelope error = ConnectionFailureModel.BuildFtpError($"{ip}:{port}", lastError);
        throw new IOException(error.Message, lastError);
    }

    private static bool IsTransient(Exception ex) {
        if (ex is DirectoryNotFoundException || ex is FileNotFoundException)
            return false;
        return ex is IOException ||
               ex is SocketException ||
               ex is TimeoutException;
    }

    private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, CancellationToken cancellationToken) {
        Task<string?> stopTask = EnsureStopTask();
        for (int remaining = seconds; remaining > 0; remaining--) {
            AnsiConsole.MarkupLine($"[yellow]Attempting FTP reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");
            Task delay = Task.Delay(1000, cancellationToken);
            Task completed = await Task.WhenAny(delay, stopTask);
            if (completed == stopTask) {
                string? input = await stopTask;
                stopTask = EnsureStopTask();
                if (input != null && input.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static Task<string?> EnsureStopTask() {
        if (Console.IsInputRedirected)
            return Task.FromResult<string?>(null);
        return Task.Run(() => Console.ReadLine());
    }

    public static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        string cleaned = path.Trim().Replace('\\', '/');
        foreach (char c in cleaned) {
            if (c == '"' || char.IsControl(c))
                throw new ArgumentException($"FTP path cannot contain quotes or control characters: {path}", nameof(path));
        }

        int colon = cleaned.IndexOf(':');
        if (colon > 0) {
            string prefix = cleaned.Substring(0, colon);
            string rest = cleaned.Substring(colon + 1);
            bool isSingleLetterDrive = prefix.Length == 1 && char.IsLetter(prefix[0]);
            bool isKnownRoot = RootNames.Contains(prefix);
            if (!isSingleLetterDrive && !isKnownRoot)
                throw new ArgumentException($"Invalid FTP path root: {path}", nameof(path));
            if (rest.Length > 0 && !rest.StartsWith("/", StringComparison.Ordinal))
                throw new ArgumentException($"Invalid FTP path separator: {path}", nameof(path));
            cleaned = "/" + prefix + rest;
        }

        if (!cleaned.StartsWith("/", StringComparison.Ordinal))
            cleaned = "/" + cleaned.TrimStart('/');

        return NormalizeRelativeRemotePath(cleaned, path, allowEmpty: true, preserveLeadingSlash: true);
    }

    public static bool IsLikelyRootListing(string requestedPath, IReadOnlyList<FtpListItem> items) {
        if (string.Equals(requestedPath, "/", StringComparison.Ordinal))
            return false;
        int validItems = items.Count(i => i != null && !string.IsNullOrWhiteSpace(i.Name) && i.Name != "." && i.Name != "..");
        if (validItems == 0)
            return false;
        int rootMatches = items.Count(i => i != null && !string.IsNullOrWhiteSpace(i.Name) && RootNames.Contains(i.Name));
        int threshold = Math.Min(3, validItems);
        return rootMatches >= threshold;
    }

    public static async Task<(FtpListItem[] Items, bool RootListing)> GetListingWithFallbackAsync(AsyncFtpClient client, string path, CancellationToken cancellationToken = default) {
        FtpListItem[] items = await client.GetListing(path, FtpListOption.AllFiles, cancellationToken) ?? Array.Empty<FtpListItem>();
        bool rootListing = IsLikelyRootListing(path, items);
        if (rootListing) {
            FluentFTP.FtpReply cwd = await client.Execute($"CWD {path}", cancellationToken);
            if (cwd.Success) {
                FtpListItem[] cwdItems = await client.GetListing(".", FtpListOption.AllFiles, cancellationToken) ?? Array.Empty<FtpListItem>();
                items = cwdItems;
                rootListing = IsLikelyRootListing(path, cwdItems);
                await client.Execute("CWD /", cancellationToken);
            }
            else if (!string.Equals(path, "/", StringComparison.Ordinal)) {
                throw new DirectoryNotFoundException($"FTP path was not found: {path}");
            }
        }

        return (items, rootListing);
    }

    public static async Task<bool> DirectoryExistsByCwdAsync(AsyncFtpClient client, string path) {
        string normalized = NormalizePath(path);
        FluentFTP.FtpReply cwd = await client.Execute($"CWD {normalized}");
        if (cwd.Success)
            await client.Execute("CWD /");
        return cwd.Success;
    }

    public static async Task<long?> TryGetFileSizeAsync(AsyncFtpClient client, string path) {
        long? size = null;
        try {
            FtpListItem? info = await client.GetObjectInfo(path);
            if (info != null && info.Type == FtpObjectType.File && info.Size >= 0)
                size = Math.Max(size ?? -1, info.Size);
        }
        catch {
            // fall through
        }

        try {
            string normalized = NormalizePath(path).TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            if (slash >= 0) {
                string parent = slash == 0 ? "/" : normalized.Substring(0, slash);
                string name = normalized.Substring(slash + 1);
                (FtpListItem[] items, bool _) = await GetListingWithFallbackAsync(client, parent);
                FtpListItem? match = items.FirstOrDefault(item => item.Type == FtpObjectType.File && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.Size >= 0)
                    size = Math.Max(size ?? -1, match.Size);
            }
        }
        catch {
            // ignored
        }

        try {
            long fileSize = await client.GetFileSize(path);
            if (fileSize >= 0)
                size = Math.Max(size ?? -1, fileSize);
        }
        catch {
            // fall through
        }

        return size;
    }

    public static async Task EnsureRemoteDirectoryAsync(AsyncFtpClient client, string path) {
        string normalized = NormalizePath(path).Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder current = new StringBuilder();
        foreach (string part in parts) {
            current.Append('/').Append(part);
            string currentPath = current.ToString();
            if (!await client.DirectoryExists(currentPath))
                await client.CreateDirectory(currentPath, true);
        }
    }

    public static async Task<TransferVerificationResult?> UploadFileAtomicAsync(
        AsyncFtpClient client,
        string localPath,
        string remotePath,
        bool overwriteApproved,
        IProgress<FtpProgress>? progress,
        CancellationToken cancellationToken,
        string? verifyAlgorithm = null) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string stagingPath = BuildRemoteTransferWorkingPath(normalizedRemotePath, "upload");
        string? normalizedVerifyAlgorithm = null;
        TransferVerificationResult? verification = null;
        long expectedSize = new FileInfo(localPath).Length;

        if (!string.IsNullOrWhiteSpace(verifyAlgorithm)) {
            if (!TryNormalizeHashAlgorithm(verifyAlgorithm, out normalizedVerifyAlgorithm, out string algorithmError))
                throw new ArgumentException(algorithmError, nameof(verifyAlgorithm));
        }

        try {
            FtpStatus status = await client.UploadFile(
                localPath,
                stagingPath,
                FtpRemoteExists.Overwrite,
                false,
                FtpVerify.None,
                progress,
                cancellationToken);
            if (status != FtpStatus.Success)
                throw new IOException($"FTP upload did not complete for {normalizedRemotePath} (status: {status}).");

            long? stagedSize = await TryGetFileSizeAsync(client, stagingPath);
            if (!stagedSize.HasValue || stagedSize.Value != expectedSize) {
                throw new IOException(
                    $"FTP upload size mismatch for {normalizedRemotePath} (expected {expectedSize} bytes, got {(stagedSize.HasValue ? stagedSize.Value.ToString(CultureInfo.InvariantCulture) : "unknown")}).");
            }

            if (normalizedVerifyAlgorithm != null) {
                TransferVerificationResult stagedVerification = await VerifyTransferHashAsync(
                    client,
                    stagingPath,
                    localPath,
                    normalizedVerifyAlgorithm,
                    cancellationToken);
                verification = new TransferVerificationResult(
                    normalizedRemotePath,
                    stagedVerification.Algorithm,
                    stagedVerification.LocalHash,
                    stagedVerification.RemoteHash);
            }

            await CommitStagedRemoteFileAsync(
                client,
                stagingPath,
                normalizedRemotePath,
                expectedSize,
                overwriteApproved,
                cancellationToken);
            return verification;
        }
        catch {
            await TryDeleteRemoteFileAsync(client, stagingPath);
            throw;
        }
    }

    public static async Task<TransferVerificationResult?> UploadFileVerifiedAsync(
        string ip,
        int port,
        string user,
        string pass,
        int timeoutMs,
        string localPath,
        string remotePath,
        bool ensureRemoteDirectory,
        IProgress<FtpProgress>? progress,
        CancellationToken cancellationToken,
        string? verifyAlgorithm = null,
        bool resume = false,
        bool overwriteApproved = false) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string? parentDirectory = GetParentDirectory(normalizedRemotePath);
        Exception? lastError = null;
        string? normalizedVerifyAlgorithm = null;

        if (!string.IsNullOrWhiteSpace(verifyAlgorithm)) {
            if (!TryNormalizeHashAlgorithm(verifyAlgorithm, out normalizedVerifyAlgorithm, out string algorithmError))
                throw new ArgumentException(algorithmError, nameof(verifyAlgorithm));
        }

        for (int attempt = 1; attempt <= ReconnectAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await using AsyncFtpClient client = CreateClient(ip, port, user, pass, timeoutMs);
                await client.Connect(cancellationToken);

                if (ensureRemoteDirectory && parentDirectory != null)
                    await EnsureRemoteDirectoryAsync(client, parentDirectory);

                long expectedSize = new FileInfo(localPath).Length;
                if (!resume) {
                    return await UploadFileAtomicAsync(
                        client,
                        localPath,
                        normalizedRemotePath,
                        overwriteApproved,
                        progress,
                        cancellationToken,
                        normalizedVerifyAlgorithm);
                }

                FtpStatus status = await client.UploadFile(
                    localPath,
                    normalizedRemotePath,
                    FtpRemoteExists.Resume,
                    false,
                    FtpVerify.None,
                    progress,
                    cancellationToken);

                if (status != FtpStatus.Success)
                    throw new IOException($"FTP upload did not complete for {normalizedRemotePath} (status: {status}).");

                long? remoteSize = await TryGetFileSizeAsync(client, normalizedRemotePath);
                if (!remoteSize.HasValue || remoteSize.Value != expectedSize) {
                    throw new IOException(
                        $"FTP upload size mismatch for {normalizedRemotePath} (expected {expectedSize} bytes, got {(remoteSize.HasValue ? remoteSize.Value.ToString(CultureInfo.InvariantCulture) : "unknown")}).");
                }

                if (normalizedVerifyAlgorithm != null) {
                    return await VerifyTransferHashAsync(
                        client,
                        normalizedRemotePath,
                        localPath,
                        normalizedVerifyAlgorithm,
                        cancellationToken);
                }

                return null;
            }
            catch (Exception ex) when (attempt < ReconnectAttempts && IsTransient(ex)) {
                lastError = ex;
                await Task.Delay(UploadReconnectDelayMs, cancellationToken);
            }
            catch (Exception ex) {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new IOException($"Unable to upload {Path.GetFileName(localPath)}.");
    }

    public static async Task<TransferVerificationResult?> DownloadFileVerifiedAsync(
        string ip,
        int port,
        string user,
        string pass,
        int timeoutMs,
        string remotePath,
        string localPath,
        IProgress<FtpProgress>? progress,
        CancellationToken cancellationToken,
        string? verifyAlgorithm = null,
        bool resume = false) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string fullLocalPath = Path.GetFullPath(localPath);
        string? localDirectory = Path.GetDirectoryName(fullLocalPath);
        if (!string.IsNullOrWhiteSpace(localDirectory))
            Directory.CreateDirectory(localDirectory);

        string tempPath = $"{fullLocalPath}.xecli-{Guid.NewGuid():N}.part";
        Exception? lastError = null;
        string? normalizedVerifyAlgorithm = null;
        string downloadPath = resume ? fullLocalPath : tempPath;
        FtpLocalExists existsMode = resume ? FtpLocalExists.Resume : FtpLocalExists.Overwrite;
        bool completedTransfer = false;

        if (!string.IsNullOrWhiteSpace(verifyAlgorithm)) {
            if (!TryNormalizeHashAlgorithm(verifyAlgorithm, out normalizedVerifyAlgorithm, out string algorithmError))
                throw new ArgumentException(algorithmError, nameof(verifyAlgorithm));
        }

        for (int attempt = 1; attempt <= ReconnectAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await using AsyncFtpClient client = CreateClient(ip, port, user, pass, timeoutMs);
                await client.Connect(cancellationToken);

                long? expectedSize = await TryGetFileSizeAsync(client, normalizedRemotePath);
                FtpStatus status = await client.DownloadFile(
                    downloadPath,
                    normalizedRemotePath,
                    existsMode,
                    FtpVerify.None,
                    progress);

                if (status != FtpStatus.Success)
                    throw new IOException($"FTP download did not complete for {normalizedRemotePath} (status: {status}).");

                if (expectedSize.HasValue) {
                    long actualSize = new FileInfo(downloadPath).Length;
                    if (actualSize != expectedSize.Value) {
                        throw new IOException(
                            $"FTP download size mismatch for {normalizedRemotePath} (expected {expectedSize.Value} bytes, got {actualSize}).");
                    }
                }

                completedTransfer = true;

                if (normalizedVerifyAlgorithm != null) {
                    TransferVerificationResult verification = await VerifyTransferHashAsync(
                        client,
                        normalizedRemotePath,
                        downloadPath,
                        normalizedVerifyAlgorithm,
                        cancellationToken);
                    if (!resume) {
                        if (File.Exists(fullLocalPath))
                            File.Delete(fullLocalPath);
                        File.Move(tempPath, fullLocalPath);
                    }
                    return verification;
                }

                if (!resume) {
                    if (File.Exists(fullLocalPath))
                        File.Delete(fullLocalPath);
                    File.Move(tempPath, fullLocalPath);
                }
                return null;
            }
            catch (Exception ex) when (attempt < ReconnectAttempts && IsTransient(ex)) {
                lastError = ex;
                if (!resume)
                    TryDeleteLocalFile(tempPath);
                await Task.Delay(UploadReconnectDelayMs, cancellationToken);
            }
            catch (Exception ex) {
                lastError = ex;
                if (resume && completedTransfer)
                    TryDeleteLocalFile(fullLocalPath);
                else if (!resume)
                    TryDeleteLocalFile(tempPath);
                break;
            }
        }

        if (!resume)
            TryDeleteLocalFile(tempPath);
        throw lastError ?? new IOException($"Unable to download {normalizedRemotePath}.");
    }

    public static async Task UploadBytesVerifiedAsync(
        string ip,
        int port,
        string user,
        string pass,
        int timeoutMs,
        byte[] data,
        string remotePath,
        bool ensureRemoteDirectory,
        IProgress<FtpProgress>? progress,
        CancellationToken cancellationToken) {
        string tempPath = Path.Combine(Path.GetTempPath(), $"xecli-ftp-bytes-{Guid.NewGuid():N}.tmp");
        try {
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);
            await UploadFileVerifiedAsync(
                ip,
                port,
                user,
                pass,
                timeoutMs,
                tempPath,
                remotePath,
                ensureRemoteDirectory,
                progress,
                cancellationToken);
        }
        finally {
            try {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch {
                // ignored
            }
        }
    }

    public static bool TryNormalizeHashAlgorithm(string? algorithm, out string normalizedAlgorithm, out string error) {
        normalizedAlgorithm = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(algorithm)) {
            error = "Hash algorithm is required.";
            return false;
        }

        string candidate = algorithm.Trim();
        if (candidate.Equals("sha256", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("sha1", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("md5", StringComparison.OrdinalIgnoreCase)) {
            normalizedAlgorithm = candidate.ToUpperInvariant();
            return true;
        }

        error = "Unsupported hash algorithm. Use sha256, sha1, or md5.";
        return false;
    }

    public static int WriteVerificationFailure(bool json, string title, string detail) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                detail,
                "FTP_VERIFICATION_FAILED",
                new[] { "Use sha256, sha1, or md5 for --verify-hash or --algorithm." }));
        }
        else {
            OperationFeedback.WriteFailure(title, detail);
        }

        return 1;
    }

    public static int WriteValidationFailure(bool json, string title, string detail, string code, params string[] nextSteps) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                detail,
                code,
                nextSteps.Length == 0 ? new[] { "Correct the command arguments and retry." } : nextSteps));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(detail)}[/]");
        }

        return 1;
    }

    public static int WriteOperationFailure(bool json, string title, string detail, string code, params string[] nextSteps) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                RedactTransferError(detail),
                code,
                nextSteps.Length == 0 ? new[] { "Verify the FTP service and paths, then retry." } : nextSteps));
        }
        else {
            OperationFeedback.WriteFailure(title, detail);
        }

        return 1;
    }

    public static bool TryWriteJsonConnectionFailure(bool json, Exception ex) {
        if (!json)
            return false;

        string fallbackMessage = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        if (!ConnectionFailureModel.TryBuildFtpError(ex, fallbackMessage, out CliErrorEnvelope error))
            return false;

        CliOutput.EmitJsonError(error);
        return true;
    }

    public static string BuildConnectionFailureMessage(FtpConnectionSettings settings, Exception ex) {
        if (ConnectionFailureModel.TryBuildFtpError(ex, ex.Message, out CliErrorEnvelope error))
            return error.Message;

        string message = "FTP connection";
        if (!string.IsNullOrWhiteSpace(settings.Ip) && settings.Port > 0)
            message += $" to {settings.Ip}:{settings.Port}";
        message += " failed.";

        string detail = RedactTransferError(ex.Message);
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" ({Markup.Escape(detail)})";

        if (LooksLikeTimeout(ex)) {
            message += $" {ConnectionFailureModel.FtpTimeoutGuidance}";
        }

        return message;
    }

    public static string BuildTimeoutFailureMessage(string operation, int timeoutMs) {
        return $"{operation} timed out after {timeoutMs} ms. {ConnectionFailureModel.FtpTimeoutGuidance}";
    }

    public static IReadOnlyList<string> BuildTimeoutFailureNextSteps(string? targetDisplay = null) {
        if (string.IsNullOrWhiteSpace(targetDisplay)) {
            return new[] {
                "Switch the console to an FTP-enabled dashboard or plugin before retrying.",
                "Confirm the FTP port, user, and password are correct.",
                "Run rgh ftp target if the FTP service or credentials changed."
            };
        }

        return new[] {
            $"Switch the console at {targetDisplay} to an FTP-enabled dashboard or plugin (Aurora, FreestyleDash, XexMenu, or DashLaunch FTP plugin).",
            "Confirm the FTP port, user, and password are correct.",
            "Run rgh ftp target if the FTP service or credentials changed."
        };
    }

    private static bool LooksLikeTimeout(Exception ex) {
        foreach (Exception current in FlattenExceptions(ex)) {
            if (current is TimeoutException)
                return true;

            string message = current.Message;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("did not complete", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("cancelled read from socket stream", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("canceled read from socket stream", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception ex) {
        yield return ex;
        if (ex is AggregateException aggregate) {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions) {
                yield return inner;
                if (inner.InnerException != null) {
                    foreach (Exception nested in FlattenExceptions(inner.InnerException))
                        yield return nested;
                }
            }
        }
        else if (ex.InnerException != null) {
            foreach (Exception inner in FlattenExceptions(ex.InnerException))
                yield return inner;
        }
    }

    public static string ComputeBytesHashHex(byte[] data, string algorithm) {
        using HashAlgorithm hasher = CreateHashAlgorithm(algorithm);
        return Convert.ToHexString(hasher.ComputeHash(data));
    }

    public static string ComputeFileHashHex(string path, string algorithm) {
        using HashAlgorithm hasher = CreateHashAlgorithm(algorithm);
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(hasher.ComputeHash(stream));
    }

    public static async Task<HashResult> HashRemoteFileAsync(FtpConnectionSettings settings, string remotePath, string algorithm, CancellationToken cancellationToken) {
        int timeout = settings.TimeoutMs ?? 5000;
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        return await WithClientAsync(settings, async client => await HashRemoteFileAsync(client, remotePath, algorithm, timeoutCts.Token), cancellationToken);
    }

    public static async Task<HashResult> HashRemoteFileAsync(AsyncFtpClient client, string remotePath, string algorithm, CancellationToken cancellationToken) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string normalizedAlgorithm = algorithm.Trim().ToUpperInvariant();
        string tempPath = Path.Combine(Path.GetTempPath(), $"xecli-ftp-hash-{Guid.NewGuid():N}.tmp");
        try {
            long? remoteSize = await TryGetFileSizeAsync(client, normalizedRemotePath);
            FtpStatus status = await client.DownloadFile(
                tempPath,
                normalizedRemotePath,
                FtpLocalExists.Overwrite,
                FtpVerify.None,
                null,
                cancellationToken);

            if (status != FtpStatus.Success)
                throw new IOException($"FTP download did not complete for {normalizedRemotePath} (status: {status}).");

            long? size = remoteSize ?? new FileInfo(tempPath).Length;
            string hash = ComputeFileHashHex(tempPath, normalizedAlgorithm);
            return new HashResult(normalizedRemotePath, normalizedAlgorithm, hash, size, "download", true);
        }
        finally {
            TryDeleteLocalFile(tempPath);
        }
    }

    public static async Task<TransferVerificationResult> VerifyTransferHashAsync(
        AsyncFtpClient client,
        string remotePath,
        string localPath,
        string algorithm,
        CancellationToken cancellationToken) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string normalizedAlgorithm = algorithm.Trim().ToUpperInvariant();
        string localHash = ComputeFileHashHex(localPath, normalizedAlgorithm);
        HashResult remote = await HashRemoteFileAsync(client, normalizedRemotePath, normalizedAlgorithm, cancellationToken);
        if (!string.Equals(localHash, remote.Hash, StringComparison.OrdinalIgnoreCase)) {
            throw new IOException(
                $"FTP transfer hash mismatch for {normalizedRemotePath} (expected {remote.Hash}, got {localHash}).");
        }

        return new TransferVerificationResult(normalizedRemotePath, normalizedAlgorithm, localHash, remote.Hash);
    }

    public static async Task<DiffResult> DiffRemoteFileAsync(FtpConnectionSettings settings, string remotePath, string localPath, string algorithm, CancellationToken cancellationToken) {
        string normalizedRemotePath = NormalizePath(remotePath);
        string normalizedAlgorithm = algorithm.Trim().ToUpperInvariant();
        string fullLocalPath = Path.GetFullPath(localPath);
        if (!File.Exists(fullLocalPath))
            throw new FileNotFoundException("Local file was not found.");

        string localFileName = Path.GetFileName(fullLocalPath);
        long localSize = new FileInfo(fullLocalPath).Length;
        string localHash = ComputeFileHashHex(fullLocalPath, normalizedAlgorithm);
        int timeout = settings.TimeoutMs ?? 5000;
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        string? remoteHash = null;
        long? remoteSize = null;
        string tempPath = Path.Combine(Path.GetTempPath(), $"xecli-ftp-diff-{Guid.NewGuid():N}.tmp");
        try {
            await WithClientAsync(settings, async client => {
                long? expectedSize = await TryGetFileSizeAsync(client, normalizedRemotePath);
                FtpStatus status = await client.DownloadFile(
                    tempPath,
                    normalizedRemotePath,
                    FtpLocalExists.Overwrite,
                    FtpVerify.None,
                    null,
                    timeoutCts.Token);

                if (status != FtpStatus.Success)
                    throw new IOException($"FTP download did not complete for {normalizedRemotePath} (status: {status}).");

                remoteSize = expectedSize ?? new FileInfo(tempPath).Length;
                remoteHash = ComputeFileHashHex(tempPath, normalizedAlgorithm);
                return 0;
            }, cancellationToken);
        }
        finally {
            TryDeleteLocalFile(tempPath);
        }

        return new DiffResult(
            normalizedRemotePath,
            localFileName,
            normalizedAlgorithm,
            string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase),
            localHash,
            remoteHash ?? string.Empty,
            localSize,
            remoteSize);
    }

    public static string FormatBytes(long bytes) {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    public static async Task<bool> RemoteFileExistsAsync(AsyncFtpClient client, string remotePath) {
        string normalized = NormalizePath(remotePath);
        if (await TryGetFileSizeAsync(client, normalized) is >= 0)
            return true;
        try {
            if (await client.FileExists(normalized))
                return true;
        }
        catch {
            // fall through
        }

        return await TryGetEntryFromParentListingAsync(client, normalized) is { Type: FtpObjectType.File };
    }

    public static string CombinePath(string basePath, string childPath) {
        string normalizedBase = NormalizePath(basePath).TrimEnd('/');
        string cleanedChild = NormalizeRelativeRemotePath(childPath, childPath, allowEmpty: true, preserveLeadingSlash: false);
        if (string.IsNullOrWhiteSpace(cleanedChild))
            return string.IsNullOrWhiteSpace(normalizedBase) ? "/" : normalizedBase;
        if (string.IsNullOrWhiteSpace(normalizedBase))
            normalizedBase = "/";
        return string.Equals(normalizedBase, "/", StringComparison.Ordinal)
            ? "/" + cleanedChild
            : normalizedBase + "/" + cleanedChild;
    }

    public static string GetRemoteLeafName(string remotePath) {
        string normalized = NormalizePath(remotePath).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "/", StringComparison.Ordinal))
            return "root";

        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized.Substring(slash + 1);
    }

    internal static string GetRelativeRemotePath(string remoteRoot, string remotePath) {
        string root = NormalizePath(remoteRoot).TrimEnd('/');
        string path = NormalizePath(remotePath).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(root) || string.Equals(root, "/", StringComparison.Ordinal))
            return path.TrimStart('/');
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return GetRemoteLeafName(path);

        string rootPrefix = root + "/";
        if (path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return path.Substring(rootPrefix.Length);

        throw new IOException($"Remote path escapes intended root: {path} is not under {root}.");
    }

    public static async Task<bool> IsRemoteDirectoryAsync(AsyncFtpClient client, string remotePath) {
        string normalized = NormalizePath(remotePath);
        if (string.Equals(normalized, "/", StringComparison.Ordinal))
            return true;

        FtpListItem? entry = await TryGetEntryFromParentListingAsync(client, normalized);
        if (entry?.Type == FtpObjectType.Directory)
            return true;
        if (entry?.Type == FtpObjectType.File)
            return false;

        return await DirectoryExistsByCwdAsync(client, normalized);
    }

    public static async Task<(List<DownloadPlanItem> Plan, List<DownloadPlanItem> Skipped)> BuildDownloadPlanAsync(
        AsyncFtpClient client,
        string remotePath,
        string outputPath,
        bool recursive,
        bool overwrite,
        bool createOutputDirectory = true,
        bool resume = false) {
        string normalizedRemote = NormalizePath(remotePath);
        string fullOutput = Path.GetFullPath(outputPath);
        bool isDirectory = await IsRemoteDirectoryAsync(client, normalizedRemote);
        List<DownloadPlanItem> plan = new List<DownloadPlanItem>();
        List<DownloadPlanItem> skipped = new List<DownloadPlanItem>();

        if (isDirectory) {
            if (!recursive)
                throw new IOException($"{normalizedRemote} is a directory. Use --recursive to download directory trees.");

            if (createOutputDirectory)
                Directory.CreateDirectory(fullOutput);
            await CollectRemoteFilesAsync(client, normalizedRemote, string.Empty, fullOutput, overwrite, resume, plan, skipped, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
            return (plan, skipped);
        }

        long? size = await TryGetFileSizeAsync(client, normalizedRemote);
        if (!size.HasValue)
            throw new FileNotFoundException($"FTP file was not found: {normalizedRemote}");

        string localPath = Directory.Exists(fullOutput)
            ? Path.Combine(fullOutput, SanitizeLocalSegment(GetRemoteLeafName(normalizedRemote)))
            : fullOutput;
        string fullLocalPath = Path.GetFullPath(localPath);
        if (resume) {
            if (!File.Exists(fullLocalPath)) {
                plan.Add(new DownloadPlanItem(normalizedRemote, fullLocalPath, size.Value));
            }
            else {
                long localSize = new FileInfo(fullLocalPath).Length;
                if (localSize < size.Value)
                    plan.Add(new DownloadPlanItem(normalizedRemote, fullLocalPath, size.Value));
                else
                    skipped.Add(new DownloadPlanItem(normalizedRemote, fullLocalPath, size.Value));
            }
        }
        else if (overwrite || !File.Exists(fullLocalPath))
            plan.Add(new DownloadPlanItem(normalizedRemote, fullLocalPath, size.Value));
        else
            skipped.Add(new DownloadPlanItem(normalizedRemote, fullLocalPath, size.Value));
        return (plan, skipped);
    }

    public static DownloadDryRunPayload BuildDownloadDryRunPayload(
        string remoteRoot,
        string localRoot,
        IReadOnlyList<DownloadPlanItem> plan,
        IReadOnlyList<DownloadPlanItem> skipped,
        string? verifyAlgorithm) {
        List<DownloadTransferJsonFile> plannedFiles = BuildDownloadJsonFiles(
            remoteRoot,
            localRoot,
            plan,
            skipped,
            Array.Empty<(DownloadPlanItem Item, string Error)>(),
            new Dictionary<DownloadPlanItem, TransferVerificationResult>())
            .Select(file => string.Equals(file.Status, "transferred", StringComparison.OrdinalIgnoreCase)
                ? file with { Status = "planned" }
                : file)
            .ToList();
        List<DownloadTransferJsonFile> skippedFiles = plannedFiles
            .Where(file => string.Equals(file.Status, "skipped", StringComparison.OrdinalIgnoreCase))
            .ToList();
        long totalBytes = plan.Concat(skipped).Sum(item => item.Size);
        return new DownloadDryRunPayload(
            "dry-run",
            "download",
            remoteRoot,
            RedactTransferLocalRoot(localRoot),
            string.IsNullOrWhiteSpace(verifyAlgorithm) ? null : new PlannedTransferVerification("post-download", verifyAlgorithm),
            plan.Count + skipped.Count,
            plan.Count,
            0,
            skipped.Count,
            0,
            totalBytes,
            0,
            plannedFiles,
            plannedFiles.Where(file => string.Equals(file.Status, "planned", StringComparison.OrdinalIgnoreCase)).ToList(),
            skippedFiles);
    }

    public static List<UploadPlanItem> BuildUploadCandidates(string inputPath, string remotePath, bool recursive, bool remoteIsDirectory) {
        string fullInput = Path.GetFullPath(inputPath);
        string normalizedRemote = NormalizePath(remotePath);
        List<UploadPlanItem> plan = new List<UploadPlanItem>();

        if (File.Exists(fullInput)) {
            string destination = remoteIsDirectory
                ? CombinePath(normalizedRemote, Path.GetFileName(fullInput))
                : normalizedRemote;
            plan.Add(new UploadPlanItem(fullInput, destination, new FileInfo(fullInput).Length));
            return plan;
        }

        if (!Directory.Exists(fullInput))
            throw new FileNotFoundException($"Input path was not found: {fullInput}");
        if (!recursive)
            throw new IOException($"{fullInput} is a directory. Use --recursive to upload directory trees.");

        foreach (string file in Directory.EnumerateFiles(fullInput, "*", SearchOption.AllDirectories)) {
            string relative = Path.GetRelativePath(fullInput, file).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            string destination = CombinePath(normalizedRemote, relative);
            plan.Add(new UploadPlanItem(Path.GetFullPath(file), destination, new FileInfo(file).Length));
        }

        return plan.OrderBy(item => item.RemotePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<(List<UploadPlanItem> Plan, List<UploadPlanItem> Skipped, List<(UploadPlanItem Item, string Error)> Conflicts)> BuildSyncUploadPlanAsync(
        AsyncFtpClient client,
        IReadOnlyList<UploadPlanItem> candidates,
        string? verifyAlgorithm,
        CancellationToken cancellationToken,
        bool overwrite = false,
        bool resume = false) {
        List<UploadPlanItem> plan = new List<UploadPlanItem>();
        List<UploadPlanItem> skipped = new List<UploadPlanItem>();
        List<(UploadPlanItem Item, string Error)> conflicts = new List<(UploadPlanItem, string)>();

        foreach (UploadPlanItem item in candidates) {
            if (overwrite) {
                plan.Add(item);
                continue;
            }

            if (resume) {
                long? remoteSize = await TryGetFileSizeAsync(client, item.RemotePath);
                if (remoteSize.HasValue) {
                    if (remoteSize.Value < item.Size)
                        plan.Add(item);
                    else
                        skipped.Add(item);
                    continue;
                }

                if (!await RemoteFileExistsAsync(client, item.RemotePath)) {
                    plan.Add(item);
                    continue;
                }

                skipped.Add(item);
                continue;
            }

            if (!await RemoteFileExistsAsync(client, item.RemotePath)) {
                plan.Add(item);
                continue;
            }

            if (string.IsNullOrWhiteSpace(verifyAlgorithm)) {
                skipped.Add(item);
                continue;
            }

            string localHash = ComputeFileHashHex(item.LocalPath, verifyAlgorithm);
            HashResult remote = await HashRemoteFileAsync(client, item.RemotePath, verifyAlgorithm, cancellationToken);
            if (string.Equals(localHash, remote.Hash, StringComparison.OrdinalIgnoreCase))
                skipped.Add(item);
            else
                conflicts.Add((item, BuildUploadConflictMessage(verifyAlgorithm)));
        }

        return (plan, skipped, conflicts);
    }

    public static string BuildUploadConflictMessage(string? verifyAlgorithm) {
        string suffix = string.IsNullOrWhiteSpace(verifyAlgorithm)
            ? "."
            : $" ({verifyAlgorithm.ToUpperInvariant()} mismatch).";
        return $"Remote file already exists and differs from the local file{suffix} Use --overwrite to replace it.";
    }

    public static UploadDryRunPayload BuildUploadDryRunPayload(
        string remoteRoot,
        IReadOnlyList<UploadPlanItem> plan,
        IReadOnlyList<UploadPlanItem> skipped,
        IReadOnlyList<(UploadPlanItem Item, string Error)> conflicts,
        string? verifyAlgorithm) {
        List<UploadTransferJsonFile> plannedFiles = BuildUploadJsonFiles(
            remoteRoot,
            plan,
            skipped,
            conflicts,
            new Dictionary<UploadPlanItem, TransferVerificationResult>())
            .Select(file => string.Equals(file.Status, "transferred", StringComparison.OrdinalIgnoreCase)
                ? file with { Status = "planned" }
                : file)
            .ToList();
        List<UploadTransferJsonFile> skippedFiles = plannedFiles
            .Where(file => string.Equals(file.Status, "skipped", StringComparison.OrdinalIgnoreCase))
            .ToList();
        long totalBytes = plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size);
        return new UploadDryRunPayload(
            "dry-run",
            "upload",
            remoteRoot,
            string.IsNullOrWhiteSpace(verifyAlgorithm) ? null : new PlannedTransferVerification("post-upload", verifyAlgorithm),
            plan.Count + skipped.Count + conflicts.Count,
            plan.Count,
            0,
            skipped.Count,
            conflicts.Count,
            totalBytes,
            0,
            plannedFiles,
            plannedFiles.Where(file => string.Equals(file.Status, "planned", StringComparison.OrdinalIgnoreCase)).ToList(),
            skippedFiles);
    }

    public static List<DownloadTransferJsonFile> BuildDownloadJsonFiles(
        string remoteRoot,
        string localRoot,
        IReadOnlyList<DownloadPlanItem> plan,
        IReadOnlyList<DownloadPlanItem> skipped,
        IReadOnlyList<(DownloadPlanItem Item, string Error)> failures,
        IReadOnlyDictionary<DownloadPlanItem, TransferVerificationResult> verifications) {
        Dictionary<DownloadPlanItem, string> failedItems = failures.ToDictionary(failure => failure.Item, failure => failure.Error);
        List<DownloadTransferJsonFile> files = plan
            .Select(item => {
                bool failed = failedItems.TryGetValue(item, out string? error);
                string relativePath = GetDownloadRelativePath(remoteRoot, item.RemotePath);
                return new DownloadTransferJsonFile(
                    relativePath,
                    item.RemotePath,
                    RedactLocalPathForReport(item.LocalPath, localRoot),
                    item.Size,
                    failed ? "failed" : "transferred",
                    failed ? RedactTransferError(error) : null,
                    verifications.TryGetValue(item, out TransferVerificationResult? verification) ? verification : null);
            })
            .ToList();

        foreach (DownloadPlanItem item in skipped) {
            string relativePath = GetDownloadRelativePath(remoteRoot, item.RemotePath);
            files.Add(new DownloadTransferJsonFile(
                relativePath,
                item.RemotePath,
                RedactLocalPathForReport(item.LocalPath, localRoot),
                item.Size,
                "skipped",
                null,
                null));
        }

        return files;
    }

    public static List<UploadTransferJsonFile> BuildUploadJsonFiles(
        string remoteRoot,
        IReadOnlyList<UploadPlanItem> plan,
        IReadOnlyList<UploadPlanItem> skipped,
        IReadOnlyList<(UploadPlanItem Item, string Error)> failures,
        IReadOnlyDictionary<UploadPlanItem, TransferVerificationResult> verifications) {
        Dictionary<UploadPlanItem, string> failedItems = failures.ToDictionary(failure => failure.Item, failure => failure.Error);
        List<UploadTransferJsonFile> files = new List<UploadTransferJsonFile>();
        HashSet<UploadPlanItem> plannedItems = plan.ToHashSet();

        foreach (UploadPlanItem item in plan) {
            bool failed = failedItems.TryGetValue(item, out string? error);
            files.Add(new UploadTransferJsonFile(
                GetUploadRelativePath(remoteRoot, item.RemotePath),
                RedactLocalPathForReport(item.LocalPath, null),
                item.RemotePath,
                item.Size,
                failed ? "failed" : "transferred",
                failed ? RedactTransferError(error) : null,
                verifications.TryGetValue(item, out TransferVerificationResult? verification) ? verification : null));
        }

        foreach ((UploadPlanItem item, string error) in failures.Where(failure => !plannedItems.Contains(failure.Item))) {
            files.Add(new UploadTransferJsonFile(
                GetUploadRelativePath(remoteRoot, item.RemotePath),
                RedactLocalPathForReport(item.LocalPath, null),
                item.RemotePath,
                item.Size,
                "failed",
                RedactTransferError(error),
                null));
        }

        foreach (UploadPlanItem item in skipped) {
            files.Add(new UploadTransferJsonFile(
                GetUploadRelativePath(remoteRoot, item.RemotePath),
                RedactLocalPathForReport(item.LocalPath, null),
                item.RemotePath,
                item.Size,
                "skipped",
                null,
                null));
        }

        return files;
    }

    public static string FormatTransferSummary(int totalFiles, int planned, int skipped, int transferred, int failed, long totalBytes) {
        return $"total={totalFiles.ToString(CultureInfo.InvariantCulture)}  planned={planned.ToString(CultureInfo.InvariantCulture)}  skipped={skipped.ToString(CultureInfo.InvariantCulture)}  transferred={transferred.ToString(CultureInfo.InvariantCulture)}  failed={failed.ToString(CultureInfo.InvariantCulture)}  bytes={FormatBytes(totalBytes)}";
    }

    public static string RedactTransferLocalPath(string localPath, string? localRoot) {
        return RedactLocalPathForReport(localPath, localRoot);
    }

    public static string RedactTransferLocalRoot(string localRoot) {
        return CommandLogRedactor.RedactToken(Path.GetFullPath(localRoot));
    }

    public static string DescribeTransferException(Exception ex) {
        List<string> messages = FlattenExceptions(ex)
            .Select(static current => current.Message?.Trim())
            .Where(static message => !string.IsNullOrWhiteSpace(message))
            .Cast<string>()
            .Where(static message => !IsGenericTransferWrapperMessage(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (messages.Count == 0) {
            messages = FlattenExceptions(ex)
                .Select(static current => current.Message?.Trim())
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        return messages.Count == 0
            ? ex.GetType().Name
            : string.Join(" | ", messages);
    }

    public static string RedactTransferError(string? error) {
        return CommandLogRedactor.RedactFreeText(error);
    }

    private static bool IsGenericTransferWrapperMessage(string message) {
        return message.Contains("See InnerException", StringComparison.OrdinalIgnoreCase) ||
               message.StartsWith("One or more errors occurred.", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDownloadRelativePath(string remoteRoot, string remotePath) {
        return GetRelativeRemotePath(remoteRoot, remotePath);
    }

    private static string GetUploadRelativePath(string remoteRoot, string remotePath) {
        return GetRelativeRemotePath(remoteRoot, remotePath);
    }

    private static string RedactLocalPathForReport(string localPath, string? localRoot) {
        if (!string.IsNullOrWhiteSpace(localRoot)) {
            try {
                string relative = Path.GetRelativePath(Path.GetFullPath(localRoot), Path.GetFullPath(localPath));
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                    return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }
            catch {
                // fall through to leaf-safe redaction
            }
        }

        return CommandLogRedactor.RedactToken(localPath);
    }

    private static async Task TryDeleteRemoteFileAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath, CancellationToken cancellationToken) {
        try {
            await using AsyncFtpClient cleanupClient = CreateClient(ip, port, user, pass, timeoutMs);
            await cleanupClient.Connect(cancellationToken);
            await TryDeleteRemoteFileAsync(cleanupClient, remotePath);
        }
        catch {
            // ignored
        }
    }

    private static async Task<bool> TryDeleteRemoteFileAsync(AsyncFtpClient client, string remotePath) {
        try {
            if (await RemoteFileExistsAsync(client, remotePath))
                await client.DeleteFile(remotePath);
            return !await RemoteFileExistsAsync(client, remotePath);
        }
        catch {
            return false;
        }
    }

    private static async Task CollectRemoteFilesAsync(
        AsyncFtpClient client,
        string remoteDirectory,
        string relativeDirectory,
        string localRoot,
        bool overwrite,
        bool resume,
        List<DownloadPlanItem> plan,
        List<DownloadPlanItem> skipped,
        HashSet<string> visited,
        int depth) {
        string normalizedDirectory = NormalizePath(remoteDirectory).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
            normalizedDirectory = "/";
        if (!visited.Add(normalizedDirectory))
            return;
        if (depth > MaxRecursiveTransferDepth)
            throw new IOException($"FTP recursive download exceeded depth limit at {normalizedDirectory}.");

        (FtpListItem[] items, bool rootListing) = await GetListingWithFallbackAsync(client, normalizedDirectory);
        if (rootListing && !string.Equals(normalizedDirectory, "/", StringComparison.Ordinal))
            throw new IOException($"FTP server returned a root listing for {normalizedDirectory}; refusing unsafe recursive download.");

        foreach (FtpListItem item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Name == "." || item.Name == "..")
                continue;

            string childRemote = CombinePath(normalizedDirectory, item.Name);
            string childRelative = CombineRelativePath(relativeDirectory, item.Name);
            if (item.Type == FtpObjectType.Directory) {
                await CollectRemoteFilesAsync(client, childRemote, childRelative, localRoot, overwrite, resume, plan, skipped, visited, depth + 1);
                continue;
            }

            if (item.Type != FtpObjectType.File)
                continue;

            string localPath = BuildSafeLocalPath(localRoot, childRelative);
            if (resume) {
                long remoteSize = Math.Max(0, item.Size);
                if (!File.Exists(localPath)) {
                    plan.Add(new DownloadPlanItem(childRemote, localPath, remoteSize));
                }
                else {
                    long localSize = new FileInfo(localPath).Length;
                    if (remoteSize > localSize)
                        plan.Add(new DownloadPlanItem(childRemote, localPath, remoteSize));
                    else
                        skipped.Add(new DownloadPlanItem(childRemote, localPath, remoteSize));
                }
                continue;
            }

            if (!overwrite && File.Exists(localPath)) {
                skipped.Add(new DownloadPlanItem(childRemote, localPath, Math.Max(0, item.Size)));
                continue;
            }

            plan.Add(new DownloadPlanItem(childRemote, localPath, Math.Max(0, item.Size)));
        }
    }

    private static string CombineRelativePath(string basePath, string name) {
        string cleaned = name.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(basePath))
            return cleaned;
        return basePath.Trim('/') + "/" + cleaned;
    }

    internal static string BuildSafeLocalPath(string localRoot, string relativePath) {
        string fullRoot = Path.GetFullPath(localRoot);
        string[] parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = fullRoot;
        foreach (string part in parts) {
            string segment = part.Trim();
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                throw new IOException($"Refusing to write outside output directory: {relativePath}");
            current = Path.Combine(current, SanitizeLocalSegment(segment));
        }

        string fullPath = Path.GetFullPath(current);
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)) {
            throw new IOException($"Refusing to write outside output directory: {fullPath}");
        }

        return fullPath;
    }

    private static string SanitizeLocalSegment(string segment) {
        string cleaned = segment.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "_" : cleaned;
    }

    private static string NormalizeRelativeRemotePath(string path, string originalPath, bool allowEmpty, bool preserveLeadingSlash) {
        string cleaned = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (preserveLeadingSlash && !cleaned.StartsWith("/", StringComparison.Ordinal))
            cleaned = "/" + cleaned.TrimStart('/');

        List<string> segments = new List<string>();
        foreach (string rawSegment in cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            string segment = rawSegment.Trim();
            if (string.IsNullOrWhiteSpace(segment))
                continue;
            if (segment == "." || segment == "..")
                throw new ArgumentException($"Invalid FTP path segment: {originalPath}", nameof(originalPath));
            if (segment.IndexOf(':') >= 0)
                throw new ArgumentException($"Invalid FTP path segment: {originalPath}", nameof(originalPath));
            segments.Add(segment);
        }

        if (segments.Count == 0)
            return preserveLeadingSlash ? "/" : (allowEmpty ? string.Empty : "/");

        return preserveLeadingSlash ? "/" + string.Join("/", segments) : string.Join("/", segments);
    }

    private static void TryDeleteLocalFile(string path) {
        try {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch {
            // ignored
        }
    }

    private static HashAlgorithm CreateHashAlgorithm(string algorithm) {
        return algorithm.Trim().ToUpperInvariant() switch {
            "SHA256" => SHA256.Create(),
            "SHA1" => SHA1.Create(),
            "MD5" => MD5.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported hash algorithm.")
        };
    }

    private static string? GetParentDirectory(string remotePath) {
        string normalized = NormalizePath(remotePath).TrimEnd('/');
        int slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return null;
        return slash == 0 ? "/" : normalized.Substring(0, slash);
    }

    private static string GetLeafName(string remotePath) {
        string normalized = NormalizePath(remotePath).TrimEnd('/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized.Substring(slash + 1);
    }

    internal static string BuildRemoteTransferWorkingPath(string remotePath, string role) {
        string normalized = NormalizePath(remotePath);
        string parent = GetParentDirectory(normalized) ?? "/";
        string safeRole = role.Equals("backup", StringComparison.OrdinalIgnoreCase) ? "backup" : "upload";
        string leaf = $".xecli-{Guid.NewGuid():N}".Substring(0, 19) + $".{safeRole}";
        return parent == "/" ? $"/{leaf}" : $"{parent}/{leaf}";
    }

    private static async Task<bool?> TryRemoteFileExistsAsync(AsyncFtpClient client, string remotePath) {
        try {
            return await RemoteFileExistsAsync(client, remotePath);
        }
        catch {
            return null;
        }
    }

    private static async Task MoveRemoteFileObservedAsync(AsyncFtpClient client, string from, string to) {
        try {
            await MoveFileVerifiedAsync(client, from, to);
        }
        catch (Exception ex) {
            bool? sourceExists = await TryRemoteFileExistsAsync(client, from);
            bool? destinationExists = await TryRemoteFileExistsAsync(client, to);
            if (sourceExists == false && destinationExists == true)
                return;
            throw new IOException($"FTP rename could not be verified for {NormalizePath(from)} -> {NormalizePath(to)}.", ex);
        }
    }

    private static async Task CommitStagedRemoteFileAsync(
        AsyncFtpClient client,
        string stagingPath,
        string destinationPath,
        long expectedSize,
        bool overwriteApproved,
        CancellationToken cancellationToken) {
        string normalizedStagingPath = NormalizePath(stagingPath);
        string normalizedDestinationPath = NormalizePath(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        bool destinationExists = await RemoteFileExistsAsync(client, normalizedDestinationPath);
        if (destinationExists && !overwriteApproved) {
            throw new IOException(
                "The destination appeared after conflict checking; no file was overwritten. Retry the transfer and choose how to handle the conflict.");
        }

        string? backupPath = null;
        bool originalIsBackedUp = false;
        if (destinationExists) {
            backupPath = BuildRemoteTransferWorkingPath(normalizedDestinationPath, "backup");
            await MoveRemoteFileObservedAsync(client, normalizedDestinationPath, backupPath);
            originalIsBackedUp = true;
        }

        try {
            cancellationToken.ThrowIfCancellationRequested();
            await MoveRemoteFileObservedAsync(client, normalizedStagingPath, normalizedDestinationPath);
            long? committedSize = await TryGetFileSizeAsync(client, normalizedDestinationPath);
            if (!committedSize.HasValue || committedSize.Value != expectedSize) {
                throw new IOException(
                    $"FTP upload commit size mismatch for {normalizedDestinationPath} (expected {expectedSize} bytes, got {(committedSize.HasValue ? committedSize.Value.ToString(CultureInfo.InvariantCulture) : "unknown")}).");
            }
        }
        catch (Exception commitError) {
            Exception? rollbackError = null;
            if (originalIsBackedUp && backupPath != null) {
                try {
                    await TryDeleteRemoteFileAsync(client, normalizedDestinationPath);
                    await MoveRemoteFileObservedAsync(client, backupPath, normalizedDestinationPath);
                    originalIsBackedUp = false;
                }
                catch (Exception ex) {
                    rollbackError = ex;
                }
            }
            else {
                await TryDeleteRemoteFileAsync(client, normalizedDestinationPath);
            }

            if (rollbackError != null) {
                throw new IOException(
                    $"FTP upload commit failed and automatic rollback could not be verified. The original file may remain at {backupPath}.",
                    new AggregateException(commitError, rollbackError));
            }
            throw new IOException(
                destinationExists
                    ? "FTP upload commit failed. The existing destination was restored."
                    : "FTP upload commit failed. No destination file was left in place.",
                commitError);
        }

        if (originalIsBackedUp && backupPath != null)
            await TryDeleteRemoteFileAsync(client, backupPath);
    }

    public static async Task<FtpListItem?> TryGetEntryFromParentListingAsync(AsyncFtpClient client, string remotePath) {
        string normalized = NormalizePath(remotePath).TrimEnd('/');
        string? parent = GetParentDirectory(normalized);
        if (parent == null)
            return null;

        string leaf = GetLeafName(normalized);
        try {
            (FtpListItem[] items, bool _) = await GetListingWithFallbackAsync(client, parent);
            return EnumerateListingItems(items).FirstOrDefault(item => string.Equals(item.Name, leaf, StringComparison.OrdinalIgnoreCase));
        }
        catch {
            return null;
        }
    }

    public static async Task<bool> DeleteRemotePathAsync(AsyncFtpClient client, string remotePath, CancellationToken cancellationToken) {
        string normalized = NormalizePath(remotePath);
        FtpListItem? entry = await TryGetEntryFromParentListingAsync(client, normalized);
        if (entry == null)
            throw new DirectoryNotFoundException($"FTP path was not found: {normalized}");

        if (entry.Type == FtpObjectType.Directory) {
            await DeleteRemoteDirectoryTreeAsync(client, normalized, cancellationToken);
            return true;
        }

        await DeleteRemoteFileAsync(client, normalized, cancellationToken);
        return false;
    }

    private static async Task DeleteRemoteDirectoryTreeAsync(AsyncFtpClient client, string remotePath, CancellationToken cancellationToken) {
        string normalized = NormalizePath(remotePath).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "/";

        Exception? lastError = null;
        (FtpListItem[] items, bool rootListing) = await GetListingWithFallbackAsync(client, normalized, cancellationToken);
        if (rootListing && !string.Equals(normalized, "/", StringComparison.Ordinal))
            lastError ??= new IOException($"FTP server returned a root listing for {normalized}; refusing unsafe recursive delete.");

        foreach (FtpListItem item in EnumerateListingItems(items).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)) {
            string childPath = CombinePath(normalized, item.Name);
            if (item.Type == FtpObjectType.Directory)
                await DeleteRemoteDirectoryTreeAsync(client, childPath, cancellationToken);
            else
                await DeleteRemoteFileAsync(client, childPath, cancellationToken);
        }

        Exception? deleteError = await TryExecuteDeleteCommandAsync(client, $"RMD {normalized}", cancellationToken);
        lastError ??= deleteError;

        if (await TryGetEntryFromParentListingAsync(client, normalized) != null) {
            Exception? retryError = await TryExecuteDeleteCommandAsync(client, $"RMD {normalized}", cancellationToken);
            lastError ??= retryError;
            if (await TryGetEntryFromParentListingAsync(client, normalized) != null)
                throw lastError ?? new IOException($"FTP delete failed for {normalized}.");
        }
    }

    private static async Task DeleteRemoteFileAsync(AsyncFtpClient client, string remotePath, CancellationToken cancellationToken) {
        string normalized = NormalizePath(remotePath);
        Exception? lastError = await TryExecuteDeleteCommandAsync(client, $"DELE {normalized}", cancellationToken);

        if (await TryGetEntryFromParentListingAsync(client, normalized) != null) {
            Exception? retryError = await TryExecuteDeleteCommandAsync(client, $"DELE {normalized}", cancellationToken);
            lastError ??= retryError;
            if (await TryGetEntryFromParentListingAsync(client, normalized) != null)
                throw lastError ?? new IOException($"FTP delete failed for {normalized}.");
        }
    }

    private static async Task<Exception?> TryExecuteDeleteCommandAsync(AsyncFtpClient client, string command, CancellationToken cancellationToken) {
        try {
            FluentFTP.FtpReply reply = await client.Execute(command, cancellationToken);
            if (!reply.Success)
                return new IOException($"FTP delete failed: {reply.Code} {reply.Message}");
        }
        catch (Exception ex) {
            return ex;
        }

        return null;
    }

    internal static IEnumerable<FtpListItem> EnumerateListingItems(IEnumerable<FtpListItem>? items) {
        if (items == null)
            yield break;

        foreach (FtpListItem item in items) {
            if (item == null || string.IsNullOrWhiteSpace(item.Name) || item.Name == "." || item.Name == "..")
                continue;

            yield return item;
        }
    }

    public static async Task MoveFileVerifiedAsync(AsyncFtpClient client, string from, string to) {
        string normalizedFrom = NormalizePath(from);
        string normalizedTo = NormalizePath(to);
        await client.MoveFile(normalizedFrom, normalizedTo);
        if (await FileMoveVerifiedAsync(client, normalizedFrom, normalizedTo))
            return;

        string? fromParent = GetParentDirectory(normalizedFrom);
        string? toParent = GetParentDirectory(normalizedTo);
        if (!string.IsNullOrWhiteSpace(fromParent) && string.Equals(fromParent, toParent, StringComparison.OrdinalIgnoreCase)) {
            FluentFTP.FtpReply cwd = await client.Execute($"CWD {fromParent}");
            if (cwd.Success) {
                FluentFTP.FtpReply renameFrom = await client.Execute($"RNFR {GetLeafName(normalizedFrom)}");
                if (renameFrom.Success) {
                    FluentFTP.FtpReply renameTo = await client.Execute($"RNTO {GetLeafName(normalizedTo)}");
                    if (renameTo.Success && await FileMoveVerifiedAsync(client, normalizedFrom, normalizedTo))
                        return;
                }
            }
        }

        throw new IOException($"FTP move could not be verified for {normalizedFrom} -> {normalizedTo}.");
    }

    private static async Task<bool> FileMoveVerifiedAsync(AsyncFtpClient client, string from, string to) {
        bool? sourceExists = await TryRemoteFileExistsAsync(client, from);
        bool? destinationExists = await TryRemoteFileExistsAsync(client, to);
        if (sourceExists == false && destinationExists == true)
            return true;

        FtpListItem? destination = await TryGetEntryFromParentListingAsync(client, to);
        FtpListItem? source = await TryGetEntryFromParentListingAsync(client, from);
        return destination != null && destination.Type == FtpObjectType.File && source == null;
    }
}

public sealed class FtpListCommand : AsyncCommand<FtpListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote directory path (default: /).")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        int timeout = settings.TimeoutMs ?? 5000;
        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;
        using CancellationTokenSource timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(timeout);

        try {
            return await FtpHelpers.WithClientAsync(settings, async client => {
                string path = FtpHelpers.NormalizePath(settings.Path ?? "/");
                Task<(FtpListItem[] Items, bool RootListing)> listingTask = FtpHelpers.GetListingWithFallbackAsync(client, path, timeoutCts.Token);
                Task completed = await Task.WhenAny(listingTask, Task.Delay(timeout, timeoutCts.Token));
                if (completed != listingTask)
                    throw new TimeoutException($"FTP list timed out after {timeout} ms.");

                (FtpListItem[] items, bool rootListing) = await listingTask;
                if (settings.Json) {
                    CliOutput.EmitJson(items.Select(i => new {
                        i.FullName,
                        i.Name,
                        Type = i.Type.ToString(),
                        i.Size,
                        i.Modified
                    }));
                    return 0;
                }

                AnsiConsole.Write(new Rule($"[bold deepskyblue1]FTP List[/] [grey]{Markup.Escape(path)}[/]").RuleStyle("grey"));
                if (rootListing) {
                    AnsiConsole.MarkupLine("[yellow]Warning:[/] Server returned a root listing for this path. This FTP server may not support listing subfolders.");
                }
                Table table = CliOutput.CreateTable();
                table.AddColumn(new TableColumn("[green]Name[/]"));
                table.AddColumn(new TableColumn("[grey]Type[/]"));
                table.AddColumn(new TableColumn("[cyan]Size[/]"));
                table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
                foreach (FtpListItem item in items) {
                    table.AddRow(
                        $"[green]{Markup.Escape(item.Name)}[/]",
                        $"[grey]{item.Type}[/]",
                        item.Type == FtpObjectType.File ? $"[cyan]{FtpHelpers.FormatBytes(item.Size)}[/]" : "[grey]-[/]",
                        item.Modified != DateTime.MinValue
                            ? CliOutput.FormatTimestamp(item.Modified)
                            : "[grey]unknown[/]");
                }
                AnsiConsole.Write(table);
                return 0;
            }, CancellationToken.None);
        }
        catch (OperationCanceledException) {
            string message = FtpHelpers.BuildTimeoutFailureMessage("FTP list", timeout);
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "FTP list failed",
                    message,
                    "FTP_LIST_FAILED",
                    FtpHelpers.BuildTimeoutFailureNextSteps()));
            }
            else {
                OperationFeedback.WriteFailure("FTP list failed", message);
            }
            return 1;
        }
        catch (TimeoutException) {
            string message = FtpHelpers.BuildTimeoutFailureMessage("FTP list", timeout);
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "FTP list failed",
                    message,
                    "FTP_LIST_FAILED",
                    FtpHelpers.BuildTimeoutFailureNextSteps()));
            }
            else {
                OperationFeedback.WriteFailure("FTP list failed", message);
            }
            return 1;
        }
    }
}

public sealed class FtpFindCommand : AsyncCommand<FtpFindCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Root path to search (default: /).")]
        public string? Path { get; init; }

        [CommandOption("--name <PATTERN>")]
        [LocalizedDescription("File or folder name pattern (supports * and ? wildcards).")]
        public string? Name { get; init; }

        [CommandOption("--regex")]
        [LocalizedDescription("Interpret --name as a regular expression.")]
        public bool Regex { get; init; }

        [CommandOption("--depth <N>")]
        [LocalizedDescription("Maximum recursion depth (default: 6).")]
        public int? Depth { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum results to return (default: 200).")]
        public int? Max { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP find failed", "--name is required.", "FTP_FIND_NAME_REQUIRED");
        }

        int maxDepth = settings.Depth.GetValueOrDefault(6);
        if (maxDepth < 0) maxDepth = 0;
        int maxResults = settings.Max.GetValueOrDefault(200);
        if (maxResults <= 0) maxResults = 200;

        Regex matcher;
        try {
            matcher = settings.Regex
                ? new Regex(settings.Name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                : BuildWildcardRegex(settings.Name);
        }
        catch (ArgumentException ex) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP find failed", $"Invalid --name regular expression: {ex.Message}", "FTP_FIND_PATTERN_INVALID");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string startPath = FtpHelpers.NormalizePath(settings.Path ?? "/");
            Queue<(string Path, int Depth)> pending = new Queue<(string, int)>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FtpFindResult> matches = new List<FtpFindResult>();

            pending.Enqueue((startPath, 0));
            while (pending.Count > 0 && matches.Count < maxResults) {
                (string current, int depth) = pending.Dequeue();
                if (!visited.Add(current))
                    continue;

                FtpListItem[] items;
                bool rootListing;
                try {
                    (items, rootListing) = await FtpHelpers.GetListingWithFallbackAsync(client, current);
                }
                catch {
                    continue;
                }

                if (rootListing && !string.Equals(current, "/", StringComparison.Ordinal)) {
                    continue;
                }

                foreach (FtpListItem item in items) {
                    if (string.IsNullOrWhiteSpace(item.Name) || item.Name == "." || item.Name == "..")
                        continue;
                    string itemPath = ResolveListedItemPath(current, item.Name);

                    if (matcher.IsMatch(item.Name)) {
                        matches.Add(new FtpFindResult {
                            Path = itemPath,
                            Name = item.Name,
                            Type = item.Type,
                            Size = item.Size,
                            Modified = item.Modified
                        });
                        if (matches.Count >= maxResults)
                            break;
                    }

                    if (item.Type == FtpObjectType.Directory && depth < maxDepth) {
                        pending.Enqueue((itemPath, depth + 1));
                    }
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(matches.Select(i => new {
                    Path = i.Path,
                    i.Name,
                    Type = i.Type.ToString(),
                    i.Size,
                    i.Modified
                }));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]FTP Find[/] [grey]{Markup.Escape(startPath)}[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[green]Path[/]"));
            table.AddColumn(new TableColumn("[grey]Type[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
            foreach (FtpFindResult item in matches) {
                table.AddRow(
                    $"[green]{Markup.Escape(item.Path)}[/]",
                    $"[grey]{item.Type}[/]",
                    item.Type == FtpObjectType.File ? $"[cyan]{FtpHelpers.FormatBytes(item.Size)}[/]" : "[grey]-[/]",
                    item.Modified != DateTime.MinValue
                        ? CliOutput.FormatTimestamp(item.Modified)
                        : "[grey]unknown[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }

    private static Regex BuildWildcardRegex(string pattern) {
        string escaped = Regex.Escape(pattern);
        escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string ResolveListedItemPath(string currentPath, string itemName) =>
        FtpHelpers.CombinePath(currentPath, itemName);

    private sealed class FtpFindResult {
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public FtpObjectType Type { get; init; }
        public long Size { get; init; }
        public DateTime Modified { get; init; }
    }
}

public sealed class FtpTargetCommand : AsyncCommand<FtpTargetCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--set <IP>")]
        [LocalizedDescription("Set the default FTP IP.")]
        public string? Ip { get; init; }

        [CommandOption("--port <PORT>")]
        [LocalizedDescription("Set the default FTP port (default: 21).")]
        public int? Port { get; init; }

        [CommandOption("--user <USER>")]
        [LocalizedDescription("Set the default FTP username.")]
        public string? User { get; init; }

        [CommandOption("--pass <PASS>")]
        [LocalizedDescription("Set the default FTP password.")]
        public string? Pass { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear saved FTP target settings.")]
        public bool Clear { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        if (settings.Clear) {
            config.DefaultFtpPort = null;
            config.DefaultFtpUser = null;
            config.DefaultFtpPassword = null;
            config.Save();
            AnsiConsole.MarkupLine("[green]FTP target cleared.[/]");
            return Task.FromResult(0);
        }

        if (settings.Port is < 1 or > 65535) {
            AnsiConsole.MarkupLine("[red]FTP port must be between 1 and 65535.[/]");
            return Task.FromResult(1);
        }

        bool updated = false;
        if (!string.IsNullOrWhiteSpace(settings.Ip)) {
            config.DefaultIp = settings.Ip;
            updated = true;
        }
        if (settings.Port.HasValue) {
            config.DefaultFtpPort = settings.Port;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.User)) {
            config.DefaultFtpUser = settings.User;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.Pass)) {
            config.DefaultFtpPassword = settings.Pass;
            updated = true;
        }
        if (updated) {
            config.Save();
            AnsiConsole.MarkupLine("[green]FTP target updated.[/]");
            return Task.FromResult(0);
        }

        TargetProfileStoreData targetStore = TargetProfileStore.Load();
        TargetProfileRecord? selectedProfile = string.IsNullOrWhiteSpace(targetStore.CurrentProfileName)
            ? null
            : TargetProfileStore.FindProfile(targetStore, targetStore.CurrentProfileName);
        TargetProfileRecord? resolvedTarget = TargetProfileStore.ResolveCurrentProfile(targetStore, config);

        string effectiveIp = resolvedTarget?.Ip ?? config.DefaultIp ?? "unknown";
        int effectivePort = FtpEndpointHelpers.GetEffectiveFtpPort(selectedProfile?.FtpPort ?? config.DefaultFtpPort);
        string effectiveUser = selectedProfile?.FtpUser ?? config.DefaultFtpUser ?? "xboxftp";

        string savedIp = config.DefaultIp ?? "unknown";
        int savedPort = FtpEndpointHelpers.GetConfiguredFtpPort(config);
        string savedUser = config.DefaultFtpUser ?? "xboxftp";

        string source = selectedProfile != null
            ? $"profile {selectedProfile.Name}"
            : string.IsNullOrWhiteSpace(config.DefaultIp)
                ? "saved FTP defaults"
                : "saved default target";

        AnsiConsole.MarkupLine(
            $"[green]Effective FTP target:[/] {Markup.Escape(effectiveIp)}:{effectivePort} user={Markup.Escape(effectiveUser)} [grey](from {Markup.Escape(source)})[/]");
        AnsiConsole.MarkupLine($"[grey]Saved FTP defaults:[/] {Markup.Escape(savedIp)}:{savedPort} user={Markup.Escape(savedUser)}");
        return Task.FromResult(0);
    }
}

public sealed class FtpCheckCommand : AsyncCommand<FtpCheckCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        TargetProfileStoreData targetStore = TargetProfileStore.Load();
        TargetProfileRecord? selectedProfile = string.IsNullOrWhiteSpace(targetStore.CurrentProfileName)
            ? null
            : TargetProfileStore.FindProfile(targetStore, targetStore.CurrentProfileName);
        TargetProfileRecord? resolvedTarget = TargetProfileStore.ResolveCurrentProfile(targetStore, config);

        (string ip, int port, string user, _, int timeoutMs) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
        FtpHelpers.FtpProbeResult result = await FtpHelpers.ProbeServiceAsync(ip, port, timeoutMs, CancellationToken.None);

        int exitCode = result.Status switch {
            FtpHelpers.FtpProbeStatus.Available => 0,
            FtpHelpers.FtpProbeStatus.Unavailable => 1,
            FtpHelpers.FtpProbeStatus.Unreachable => 2,
            _ => 2
        };

        string source = DescribeSource(settings, selectedProfile, resolvedTarget, config);
        string effectiveTarget = DescribeEffectiveTarget(ip, port, user);
        string savedDefaults = DescribeSavedDefaults(config);
        string resumeSupport = "ftp get --resume, ftp put --resume, ftp sync --resume";
        string detail = BuildDetail(result.Status, effectiveTarget, savedDefaults, resumeSupport);

        if (settings.Json) {
            CliOutput.EmitJson(new FtpHelpers.FtpProbeJsonPayload(
                result.Status.ToString().ToLowerInvariant(),
                exitCode,
                ip,
                port,
                timeoutMs,
                detail,
                source,
                effectiveTarget,
                savedDefaults,
                resumeSupport));
            return exitCode;
        }

        if (selectedProfile != null && !string.IsNullOrWhiteSpace(selectedProfile.Name))
            AnsiConsole.MarkupLine($"[grey]Current profile:[/] {Markup.Escape(selectedProfile.Name)}");
        AnsiConsole.MarkupLine($"[grey]Effective FTP target:[/] {Markup.Escape(effectiveTarget)} [grey](from {Markup.Escape(source)})[/]");
        AnsiConsole.MarkupLine($"[grey]Saved FTP defaults:[/] {Markup.Escape(savedDefaults)}");
        AnsiConsole.MarkupLine($"[grey]Resume support:[/] {Markup.Escape(resumeSupport)}");

        switch (result.Status) {
            case FtpHelpers.FtpProbeStatus.Available:
                OperationFeedback.WriteSuccess("FTP service available", detail);
                break;
            case FtpHelpers.FtpProbeStatus.Unavailable:
                OperationFeedback.WriteFailure("FTP service unavailable", detail);
                break;
            case FtpHelpers.FtpProbeStatus.Unreachable:
                OperationFeedback.WriteFailure("FTP service unreachable", detail);
                break;
            default:
                OperationFeedback.WriteFailure("FTP service probe failed", detail);
                break;
        }

        return exitCode;
    }

    private static string DescribeSource(Settings settings, TargetProfileRecord? selectedProfile, TargetProfileRecord? resolvedTarget, CliConfig config) {
        if (!string.IsNullOrWhiteSpace(settings.Profile))
            return $"profile {settings.Profile}";

        if (selectedProfile != null && !string.IsNullOrWhiteSpace(selectedProfile.Name))
            return $"profile {selectedProfile.Name}";

        if (!string.IsNullOrWhiteSpace(settings.Ip))
            return "explicit FTP IP";

        if (!string.IsNullOrWhiteSpace(config.DefaultIp))
            return "saved default target";

        if (resolvedTarget != null && string.IsNullOrWhiteSpace(resolvedTarget.Name))
            return "saved default target";

        return "resolved target";
    }

    private static string DescribeEffectiveTarget(string ip, int port, string user) {
        string endpoint = TargetProfileStore.FormatEndpoint(ip, port);
        return $"{endpoint} user={user}";
    }

    private static string DescribeSavedDefaults(CliConfig config) {
        if (string.IsNullOrWhiteSpace(config.DefaultIp))
            return "not set";

        string endpoint = TargetProfileStore.FormatEndpoint(config.DefaultIp, FtpEndpointHelpers.GetConfiguredFtpPort(config));
        string user = string.IsNullOrWhiteSpace(config.DefaultFtpUser) ? "xboxftp" : config.DefaultFtpUser;
        string passwordState = string.IsNullOrWhiteSpace(config.DefaultFtpPassword) ? "none" : "set";
        return $"{endpoint} user={user} pass={passwordState}";
    }

    private static string BuildDetail(FtpHelpers.FtpProbeStatus status, string effectiveTarget, string savedDefaults, string resumeSupport) {
        string retryHelp = $"Use {resumeSupport} to continue partial single-file transfers once the service is available again.";
        return status switch {
            FtpHelpers.FtpProbeStatus.Available => $"FTP service available at {effectiveTarget}. {retryHelp}",
            FtpHelpers.FtpProbeStatus.Unavailable => $"FTP service unavailable at {effectiveTarget}. Launch Aurora, FreestyleDash, XexMenu, or enable the DashLaunch FTP plugin, then retry. Saved defaults: {savedDefaults}. {retryHelp}",
            FtpHelpers.FtpProbeStatus.Unreachable => $"FTP service unreachable at {effectiveTarget}. Check the console network path, then launch Aurora, FreestyleDash, XexMenu, or enable the DashLaunch FTP plugin, then retry. Saved defaults: {savedDefaults}. {retryHelp}",
            _ => $"FTP service probe returned an unknown state for {effectiveTarget}. Saved defaults: {savedDefaults}. {retryHelp}"
        };
    }
}

public sealed class FtpGetCommand : AsyncCommand<FtpGetCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote file or directory path.")]
        public string? Path { get; init; }

        [CommandOption("--out <PATH>")]
        [LocalizedDescription("Local output file or directory.")]
        public string? Output { get; init; }

        [CommandOption("--recursive")]
        [LocalizedDescription("Download directory trees recursively.")]
        public bool Recursive { get; init; }

        [CommandOption("--overwrite")]
        [LocalizedDescription("Overwrite local files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--resume")]
        [LocalizedDescription("Resume partial single-file downloads.")]
        public bool Resume { get; init; }

        [CommandOption("--verify-hash <ALGORITHM>")]
        [LocalizedDescription("Verify with sha256, sha1, or md5.")]
        public string? VerifyHash { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Output)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP download failed", "--path and --out are required.", "FTP_GET_PATHS_REQUIRED");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);

        string? verifyAlgorithm = null;
        if (settings.VerifyHash != null &&
            !FtpHelpers.TryNormalizeHashAlgorithm(settings.VerifyHash, out verifyAlgorithm, out string verifyError)) {
            return FtpHelpers.WriteVerificationFailure(settings.Json, "FTP download verification failed", verifyError);
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);

        await using AsyncFtpClient planClient = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await planClient.Connect(CancellationToken.None);
        List<FtpHelpers.DownloadPlanItem> plan;
        List<FtpHelpers.DownloadPlanItem> skipped;
        try {
            (plan, skipped) = await FtpHelpers.BuildDownloadPlanAsync(
                planClient,
                remote,
                settings.Output!,
                settings.Recursive,
                settings.Overwrite,
                resume: settings.Resume);
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP download plan failed", ex.Message, "FTP_GET_PLAN_FAILED");
        }

        if (settings.Json) {
            List<(FtpHelpers.DownloadPlanItem Item, string Error)> jsonFailures = new List<(FtpHelpers.DownloadPlanItem, string)>();
            Dictionary<FtpHelpers.DownloadPlanItem, FtpHelpers.TransferVerificationResult> jsonVerifications = new Dictionary<FtpHelpers.DownloadPlanItem, FtpHelpers.TransferVerificationResult>();
            foreach (FtpHelpers.DownloadPlanItem item in plan) {
                try {
                    FtpHelpers.TransferVerificationResult? verification = await FtpHelpers.DownloadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.RemotePath,
                        item.LocalPath,
                        null,
                        CancellationToken.None,
                        verifyAlgorithm);
                    if (verification != null)
                        jsonVerifications[item] = verification;
                }
                catch (Exception ex) {
                    jsonFailures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                }
            }

            IReadOnlyList<FtpHelpers.DownloadTransferJsonFile> files = FtpHelpers.BuildDownloadJsonFiles(
                remote,
                settings.Output!,
                plan,
                skipped,
                jsonFailures,
                jsonVerifications);
            int transferred = plan.Count - jsonFailures.Count;
            long totalBytes = plan.Concat(skipped).Sum(item => item.Size);
            HashSet<FtpHelpers.DownloadPlanItem> failedItems = jsonFailures.Select(failure => failure.Item).ToHashSet();
            long transferredBytes = plan.Where(item => !failedItems.Contains(item)).Sum(item => item.Size);
            CliOutput.EmitJson(new FtpHelpers.DownloadTransferJsonPayload(
                Mode: settings.Recursive ? "recursive" : "single",
                Direction: "download",
                RemoteRoot: remote,
                LocalRoot: FtpHelpers.RedactTransferLocalRoot(settings.Output!),
                Verification: verifyAlgorithm == null ? null : new FtpHelpers.PlannedTransferVerification("post-download", verifyAlgorithm),
                TotalFiles: plan.Count + skipped.Count,
                Planned: plan.Count,
                Transferred: transferred,
                Skipped: skipped.Count,
                Failed: jsonFailures.Count,
                TotalBytes: totalBytes,
                Bytes: transferredBytes,
                Files: files,
                Failures: jsonFailures.Select(failure => new FtpHelpers.DownloadTransferJsonFailure(
                    failure.Item.RemotePath,
                    FtpHelpers.RedactTransferLocalPath(failure.Item.LocalPath, settings.Output!),
                    CommandLogRedactor.RedactFreeText(failure.Error))).ToList()));
            return jsonFailures.Count == 0 ? 0 : 1;
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning(
                "FTP download skipped",
                FtpHelpers.FormatTransferSummary(skipped.Count, 0, skipped.Count, 0, 0, skipped.Sum(item => item.Size)) + "\nall target local files already exist; use --overwrite to replace them.");
            return 0;
        }

        List<(FtpHelpers.DownloadPlanItem Item, string Error)> failures = new List<(FtpHelpers.DownloadPlanItem, string)>();
        if (plan.Count == 1) {
            FtpHelpers.DownloadPlanItem item = plan[0];
            try {
                await CliOutput.RunWithProgressAsync($"FTP download {item.RemotePath}", item.Size > 0 ? item.Size : null, async progress => {
                    Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                        if (p.TransferredBytes >= 0)
                            progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "receiving"));
                    });
                    if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                        AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                    await FtpHelpers.DownloadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.RemotePath,
                        item.LocalPath,
                        ftpProgress,
                        CancellationToken.None,
                        verifyAlgorithm,
                        settings.Resume);
                });
            }
            catch (Exception ex) {
                failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
            }
        }
        else {
            await CliOutput.RunBatchProgressAsync(
                $"FTP download {remote}",
                plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
                async batch => {
                    foreach (FtpHelpers.DownloadPlanItem item in plan) {
                        batch.StartFile(item.RemotePath, item.Size);
                        try {
                            Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                                if (p.TransferredBytes >= 0)
                                    batch.ReportFileProgress(p.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, plan.Count)}");
                            });
                            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                                AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                            await FtpHelpers.DownloadFileVerifiedAsync(
                                ip,
                                port,
                                user,
                                pass,
                                timeout,
                                item.RemotePath,
                                item.LocalPath,
                                ftpProgress,
                                CancellationToken.None,
                                verifyAlgorithm,
                                settings.Resume);
                            batch.CompleteFile();
                        }
                        catch (Exception ex) {
                            failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                            batch.ReportFileProgress(item.Size, "failed");
                            batch.CompleteFile();
                        }
                    }
                });
        }

        if (failures.Count > 0) {
            OperationFeedback.WriteFailure(
                "FTP download failed",
                FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count - failures.Count, failures.Count, plan.Concat(skipped).Sum(item => item.Size)));
            foreach ((FtpHelpers.DownloadPlanItem item, string error) in failures.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "FTP download complete",
            FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count, 0, plan.Concat(skipped).Sum(item => item.Size)) +
            $"\n[green]{plan.Count}[/] file(s) -> [white]{Markup.Escape(FtpHelpers.RedactTransferLocalRoot(settings.Output!))}[/]" +
            (verifyAlgorithm != null ? $" [grey]|[/] verify [white]{verifyAlgorithm}[/]" : string.Empty));
        if (skipped.Count > 0)
            OperationFeedback.WriteWarning("FTP download skipped existing files", skipped.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

public sealed class FtpPutCommand : AsyncCommand<FtpPutCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote destination file or directory.")]
        public string? Path { get; init; }

        [CommandOption("--in <PATH>")]
        [LocalizedDescription("Local input file or directory.")]
        public string? Input { get; init; }

        [CommandOption("--recursive")]
        [LocalizedDescription("Upload directory trees recursively.")]
        public bool Recursive { get; init; }

        [CommandOption("--overwrite")]
        [LocalizedDescription("Overwrite remote files that already exist.")]
        public bool Overwrite { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show upload plan without writing remote files.")]
        public bool DryRun { get; init; }

        [CommandOption("--resume")]
        [LocalizedDescription("Resume partial single-file uploads.")]
        public bool Resume { get; init; }

        [CommandOption("--verify-hash <ALGORITHM>")]
        [LocalizedDescription("Verify with sha256, sha1, or md5.")]
        public string? VerifyHash { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Input)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP upload failed", "--path and --in are required.", "FTP_PUT_PATHS_REQUIRED");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);

        string? verifyAlgorithm = null;
        if (settings.VerifyHash != null &&
            !FtpHelpers.TryNormalizeHashAlgorithm(settings.VerifyHash, out verifyAlgorithm, out string verifyError)) {
            return FtpHelpers.WriteVerificationFailure(settings.Json, "FTP upload verification failed", verifyError);
        }

        string input;
        try {
            input = Path.GetFullPath(settings.Input);
        }
        catch (ArgumentException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload blocked", "invalid --in path.", "FTP_PUT_INPUT_INVALID");
        }
        catch (NotSupportedException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload blocked", "invalid --in path.", "FTP_PUT_INPUT_INVALID");
        }
        catch (PathTooLongException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload blocked", "invalid --in path.", "FTP_PUT_INPUT_INVALID");
        }
        catch (SecurityException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload blocked", "invalid --in path.", "FTP_PUT_INPUT_INVALID");
        }
        bool inputIsFile = File.Exists(input);
        bool inputIsDirectory = Directory.Exists(input);
        if (!inputIsFile && !inputIsDirectory) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP upload blocked", "Input path not found.", "FTP_PUT_INPUT_NOT_FOUND");
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);

        if (inputIsDirectory && !settings.Recursive) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload blocked", "input is a directory; use --recursive to upload directory trees.", "FTP_PUT_RECURSIVE_REQUIRED");
        }

        await using AsyncFtpClient planClient = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await planClient.Connect(CancellationToken.None);
        bool remoteLooksDirectory = inputIsDirectory ||
            settings.Path!.EndsWith("/", StringComparison.Ordinal) ||
            settings.Path.EndsWith("\\", StringComparison.Ordinal);
        bool remoteIsDirectory = remoteLooksDirectory || await FtpHelpers.IsRemoteDirectoryAsync(planClient, remote);
        List<FtpHelpers.UploadPlanItem> candidates;
        try {
            candidates = FtpHelpers.BuildUploadCandidates(input, remote, settings.Recursive, remoteIsDirectory);
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP upload plan failed", ex.Message, "FTP_PUT_PLAN_FAILED");
        }

        (List<FtpHelpers.UploadPlanItem> plan, List<FtpHelpers.UploadPlanItem> skipped, List<(FtpHelpers.UploadPlanItem Item, string Error)> conflicts) = await FtpHelpers.BuildSyncUploadPlanAsync(
            planClient,
            candidates,
            verifyAlgorithm,
            CancellationToken.None,
            settings.Overwrite,
            settings.Resume);

        if (settings.DryRun) {
            FtpHelpers.UploadDryRunPayload payload = FtpHelpers.BuildUploadDryRunPayload(remote, plan, skipped, conflicts, verifyAlgorithm);

            if (settings.Json) {
                CliOutput.EmitJson(payload);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Upload Preview[/]").RuleStyle("grey"));
            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                AnsiConsole.MarkupLine($"[grey]Planned verification:[/] [white]{verifyAlgorithm}[/] [grey](post-upload)[/]");
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {Markup.Escape(FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count + conflicts.Count, plan.Count, skipped.Count, 0, conflicts.Count, plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)))}");
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Action[/]"));
            table.AddColumn(new TableColumn("[white]Local[/]"));
            table.AddColumn(new TableColumn("[cyan]Remote[/]"));
            table.AddColumn(new TableColumn("[grey]Size[/]"));
            foreach (FtpHelpers.UploadPlanItem item in plan) {
                table.AddRow("[green]upload[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            foreach (FtpHelpers.UploadPlanItem item in skipped) {
                table.AddRow("[yellow]skip[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            foreach ((FtpHelpers.UploadPlanItem item, string _) in conflicts) {
                table.AddRow("[red]conflict[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }

        if (settings.Json) {
            List<(FtpHelpers.UploadPlanItem Item, string Error)> jsonFailures = new List<(FtpHelpers.UploadPlanItem, string)>(conflicts);
            Dictionary<FtpHelpers.UploadPlanItem, FtpHelpers.TransferVerificationResult> jsonVerifications = new Dictionary<FtpHelpers.UploadPlanItem, FtpHelpers.TransferVerificationResult>();
            foreach (FtpHelpers.UploadPlanItem item in plan) {
                try {
                    FtpHelpers.TransferVerificationResult? verification = await FtpHelpers.UploadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.LocalPath,
                        item.RemotePath,
                        ensureRemoteDirectory: true,
                        progress: null,
                        cancellationToken: CancellationToken.None,
                        verifyAlgorithm: verifyAlgorithm,
                        resume: settings.Resume,
                        overwriteApproved: settings.Overwrite);
                    if (verification != null)
                        jsonVerifications[item] = verification;
                }
                catch (Exception ex) {
                    jsonFailures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                }
            }

            IReadOnlyList<FtpHelpers.UploadTransferJsonFile> files = FtpHelpers.BuildUploadJsonFiles(
                remote,
                plan,
                skipped,
                jsonFailures,
                jsonVerifications);
            IReadOnlyList<FtpHelpers.UploadTransferJsonFile> skippedFiles = files
                .Where(file => string.Equals(file.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                .ToList();
            HashSet<FtpHelpers.UploadPlanItem> failedItems = jsonFailures.Select(failure => failure.Item).ToHashSet();
            int transferred = plan.Count - plan.Count(item => failedItems.Contains(item));
            long totalBytes = plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size);
            long transferredBytes = plan.Where(item => !failedItems.Contains(item)).Sum(item => item.Size);
            CliOutput.EmitJson(new FtpHelpers.UploadTransferJsonPayload(
                Mode: settings.Recursive ? "recursive" : "single",
                Direction: "upload",
                RemoteRoot: remote,
                Verification: verifyAlgorithm == null ? null : new FtpHelpers.PlannedTransferVerification("post-upload", verifyAlgorithm),
                TotalFiles: plan.Count + skipped.Count + conflicts.Count,
                Planned: plan.Count,
                Transferred: transferred,
                Skipped: skipped.Count,
                Failed: jsonFailures.Count,
                TotalBytes: totalBytes,
                Bytes: transferredBytes,
                Files: files,
                SkippedExisting: skippedFiles,
                Failures: jsonFailures.Select(failure => new FtpHelpers.UploadTransferJsonFailure(
                    FtpHelpers.RedactTransferLocalPath(failure.Item.LocalPath, null),
                    failure.Item.RemotePath,
                    CommandLogRedactor.RedactFreeText(failure.Error))).ToList()));
            return jsonFailures.Count == 0 ? 0 : 1;
        }

        if (plan.Count == 0 && conflicts.Count > 0) {
            OperationFeedback.WriteFailure(
                "FTP upload blocked",
                FtpHelpers.FormatTransferSummary(conflicts.Count + skipped.Count, 0, skipped.Count, 0, conflicts.Count, skipped.Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)));
            foreach ((FtpHelpers.UploadPlanItem item, string error) in conflicts.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))} -> {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning(
                "FTP upload skipped",
                FtpHelpers.FormatTransferSummary(skipped.Count, 0, skipped.Count, 0, 0, skipped.Sum(item => item.Size)) + "\nall target remote files already exist; use --overwrite to replace them.");
            return 0;
        }

        List<(FtpHelpers.UploadPlanItem Item, string Error)> failures = new List<(FtpHelpers.UploadPlanItem, string)>(conflicts);
        if (plan.Count == 1) {
            FtpHelpers.UploadPlanItem item = plan[0];
            try {
                await CliOutput.RunWithProgressAsync($"FTP upload {item.RemotePath}", item.Size > 0 ? item.Size : null, async progress => {
                    Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                        if (p.TransferredBytes >= 0)
                            progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "sending"));
                    });
                    if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                        AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                    await FtpHelpers.UploadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.LocalPath,
                        item.RemotePath,
                        ensureRemoteDirectory: true,
                        progress: ftpProgress,
                        cancellationToken: CancellationToken.None,
                        verifyAlgorithm: verifyAlgorithm,
                        resume: settings.Resume,
                        overwriteApproved: settings.Overwrite);
                });
            }
            catch (Exception ex) {
                failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
            }
        }
        else {
            await CliOutput.RunBatchProgressAsync(
                $"FTP upload {remote}",
                plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
                async batch => {
                    foreach (FtpHelpers.UploadPlanItem item in plan) {
                        batch.StartFile(item.RemotePath, item.Size);
                        try {
                            Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                                if (p.TransferredBytes >= 0)
                                    batch.ReportFileProgress(p.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, plan.Count)}");
                            });
                            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                                AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                            await FtpHelpers.UploadFileVerifiedAsync(
                                ip,
                                port,
                                user,
                                pass,
                                timeout,
                                item.LocalPath,
                                item.RemotePath,
                                ensureRemoteDirectory: true,
                                progress: ftpProgress,
                                cancellationToken: CancellationToken.None,
                                verifyAlgorithm: verifyAlgorithm,
                                resume: settings.Resume,
                                overwriteApproved: settings.Overwrite);
                            batch.CompleteFile();
                        }
                        catch (Exception ex) {
                            failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                            batch.ReportFileProgress(item.Size, "failed");
                            batch.CompleteFile();
                        }
                    }
                });
        }

        if (failures.Count > 0) {
            HashSet<FtpHelpers.UploadPlanItem> failedItems = failures.Select(failure => failure.Item).ToHashSet();
            int transferred = plan.Count - plan.Count(item => failedItems.Contains(item));
            OperationFeedback.WriteFailure(
                "FTP upload failed",
                FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count + conflicts.Count, plan.Count, skipped.Count, transferred, failures.Count, plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)));
            foreach ((FtpHelpers.UploadPlanItem item, string error) in failures.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))} -> {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "FTP upload complete",
            FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count, 0, plan.Concat(skipped).Sum(item => item.Size)) +
            $"\n[green]{plan.Count}[/] file(s) -> [cyan]{Markup.Escape(remote)}[/]" +
            (verifyAlgorithm != null ? $" [grey]|[/] verify [white]{verifyAlgorithm}[/]" : string.Empty));
        if (skipped.Count > 0)
            OperationFeedback.WriteWarning("FTP upload skipped existing files", skipped.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

public sealed class FtpSyncCommand : AsyncCommand<FtpSyncCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--direction <DIRECTION>")]
        [LocalizedDescription("upload|download.")]
        public string? Direction { get; init; }

        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote source or destination path.")]
        public string? Path { get; init; }

        [CommandOption("--in <PATH>")]
        [LocalizedDescription("Local input or output path.")]
        public string? Input { get; init; }

        [CommandOption("--recursive")]
        [LocalizedDescription("Transfer directory trees recursively.")]
        public bool Recursive { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the sync plan without transferring files.")]
        public bool DryRun { get; init; }

        [CommandOption("--resume")]
        [LocalizedDescription("Resume partial single-file transfers.")]
        public bool Resume { get; init; }

        [CommandOption("--verify-hash <ALGORITHM>")]
        [LocalizedDescription("Verify with sha256, sha1, or md5.")]
        public string? VerifyHash { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Direction) ||
            string.IsNullOrWhiteSpace(settings.Path) ||
            string.IsNullOrWhiteSpace(settings.Input)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP sync failed", "--direction, --path, and --in are required.", "FTP_SYNC_ARGUMENTS_REQUIRED");
        }

        string? verifyAlgorithm = null;
        if (settings.VerifyHash != null &&
            !FtpHelpers.TryNormalizeHashAlgorithm(settings.VerifyHash, out verifyAlgorithm, out string verifyError)) {
            return FtpHelpers.WriteVerificationFailure(settings.Json, "FTP sync verification failed", verifyError);
        }

        if (settings.Direction.Equals("upload", StringComparison.OrdinalIgnoreCase))
            return await ExecuteUploadAsync(settings, verifyAlgorithm);

        if (settings.Direction.Equals("download", StringComparison.OrdinalIgnoreCase))
            return await ExecuteDownloadAsync(settings, verifyAlgorithm);

        return FtpHelpers.WriteValidationFailure(settings.Json, "FTP sync failed", "--direction must be upload or download.", "FTP_SYNC_DIRECTION_INVALID");
    }

    private static async Task<int> ExecuteUploadAsync(Settings settings, string? verifyAlgorithm) {
        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path!);
        string input;
        try {
            input = Path.GetFullPath(settings.Input!);
        }
        catch (ArgumentException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync blocked", "invalid --in path.", "FTP_SYNC_INPUT_INVALID");
        }
        catch (NotSupportedException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync blocked", "invalid --in path.", "FTP_SYNC_INPUT_INVALID");
        }
        catch (PathTooLongException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync blocked", "invalid --in path.", "FTP_SYNC_INPUT_INVALID");
        }
        catch (SecurityException) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync blocked", "invalid --in path.", "FTP_SYNC_INPUT_INVALID");
        }

        bool inputIsFile = File.Exists(input);
        bool inputIsDirectory = Directory.Exists(input);
        if (!inputIsFile && !inputIsDirectory) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP sync blocked", "Input path not found.", "FTP_SYNC_INPUT_NOT_FOUND");
        }

        if (inputIsDirectory && !settings.Recursive) {
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync blocked", "input is a directory; use --recursive to transfer directory trees.", "FTP_SYNC_RECURSIVE_REQUIRED");
        }

        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);

        await using AsyncFtpClient planClient = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await planClient.Connect(CancellationToken.None);

        bool remoteLooksDirectory = inputIsDirectory ||
            settings.Path!.EndsWith("/", StringComparison.Ordinal) ||
            settings.Path.EndsWith("\\", StringComparison.Ordinal);
        bool remoteIsDirectory = remoteLooksDirectory || await FtpHelpers.IsRemoteDirectoryAsync(planClient, remote);

        List<FtpHelpers.UploadPlanItem> candidates;
        try {
            candidates = FtpHelpers.BuildUploadCandidates(input, remote, settings.Recursive, remoteIsDirectory);
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync plan failed", ex.Message, "FTP_SYNC_PLAN_FAILED");
        }

        (List<FtpHelpers.UploadPlanItem> plan, List<FtpHelpers.UploadPlanItem> skipped, List<(FtpHelpers.UploadPlanItem Item, string Error)> conflicts) = await FtpHelpers.BuildSyncUploadPlanAsync(
            planClient,
            candidates,
            verifyAlgorithm,
            CancellationToken.None,
            overwrite: false,
            resume: settings.Resume);

        if (settings.DryRun) {
            FtpHelpers.UploadDryRunPayload payload = FtpHelpers.BuildUploadDryRunPayload(remote, plan, skipped, conflicts, verifyAlgorithm);
            if (settings.Json) {
                CliOutput.EmitJson(payload);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Sync Preview[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine("[grey]Direction:[/] [white]upload[/]");
            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                AnsiConsole.MarkupLine($"[grey]Planned verification:[/] [white]{verifyAlgorithm}[/] [grey](post-upload)[/]");
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {Markup.Escape(FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count + conflicts.Count, plan.Count, skipped.Count, 0, conflicts.Count, plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)))}");
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Action[/]"));
            table.AddColumn(new TableColumn("[white]Local[/]"));
            table.AddColumn(new TableColumn("[cyan]Remote[/]"));
            table.AddColumn(new TableColumn("[grey]Size[/]"));
            foreach (FtpHelpers.UploadPlanItem item in plan) {
                table.AddRow("[green]upload[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            foreach (FtpHelpers.UploadPlanItem item in skipped) {
                table.AddRow("[yellow]skip[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            foreach ((FtpHelpers.UploadPlanItem item, string _) in conflicts) {
                table.AddRow("[red]conflict[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))}[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }

        if (settings.Json) {
            List<(FtpHelpers.UploadPlanItem Item, string Error)> jsonFailures = new List<(FtpHelpers.UploadPlanItem, string)>(conflicts);
            Dictionary<FtpHelpers.UploadPlanItem, FtpHelpers.TransferVerificationResult> jsonVerifications = new Dictionary<FtpHelpers.UploadPlanItem, FtpHelpers.TransferVerificationResult>();
            foreach (FtpHelpers.UploadPlanItem item in plan) {
                try {
                    FtpHelpers.TransferVerificationResult? verification = await FtpHelpers.UploadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.LocalPath,
                        item.RemotePath,
                        ensureRemoteDirectory: true,
                        progress: null,
                        cancellationToken: CancellationToken.None,
                        verifyAlgorithm: verifyAlgorithm,
                        resume: settings.Resume,
                        overwriteApproved: false);
                    if (verification != null)
                        jsonVerifications[item] = verification;
                }
                catch (Exception ex) {
                    jsonFailures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                }
            }

            IReadOnlyList<FtpHelpers.UploadTransferJsonFile> files = FtpHelpers.BuildUploadJsonFiles(
                remote,
                plan,
                skipped,
                jsonFailures,
                jsonVerifications);
            IReadOnlyList<FtpHelpers.UploadTransferJsonFile> skippedFiles = files
                .Where(file => string.Equals(file.Status, "skipped", StringComparison.OrdinalIgnoreCase))
                .ToList();
            HashSet<FtpHelpers.UploadPlanItem> failedItems = jsonFailures.Select(failure => failure.Item).ToHashSet();
            int transferred = plan.Count - plan.Count(item => failedItems.Contains(item));
            long totalBytes = plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size);
            long transferredBytes = plan.Where(item => !failedItems.Contains(item)).Sum(item => item.Size);
            CliOutput.EmitJson(new FtpHelpers.UploadTransferJsonPayload(
                Mode: settings.Recursive ? "recursive" : "single",
                Direction: "upload",
                RemoteRoot: remote,
                Verification: verifyAlgorithm == null ? null : new FtpHelpers.PlannedTransferVerification("post-upload", verifyAlgorithm),
                TotalFiles: plan.Count + skipped.Count + conflicts.Count,
                Planned: plan.Count,
                Transferred: transferred,
                Skipped: skipped.Count,
                Failed: jsonFailures.Count,
                TotalBytes: totalBytes,
                Bytes: transferredBytes,
                Files: files,
                SkippedExisting: skippedFiles,
                Failures: jsonFailures.Select(failure => new FtpHelpers.UploadTransferJsonFailure(
                    FtpHelpers.RedactTransferLocalPath(failure.Item.LocalPath, null),
                    failure.Item.RemotePath,
                    CommandLogRedactor.RedactFreeText(failure.Error))).ToList()));
            return jsonFailures.Count == 0 ? 0 : 1;
        }

        if (plan.Count == 0 && conflicts.Count > 0) {
            OperationFeedback.WriteFailure(
                "FTP sync blocked",
                FtpHelpers.FormatTransferSummary(conflicts.Count + skipped.Count, 0, skipped.Count, 0, conflicts.Count, skipped.Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)));
            foreach ((FtpHelpers.UploadPlanItem item, string error) in conflicts.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))} -> {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning(
                "FTP sync skipped",
                FtpHelpers.FormatTransferSummary(skipped.Count, 0, skipped.Count, 0, 0, skipped.Sum(item => item.Size)) + "\nall target remote files already exist.");
            return 0;
        }

        List<(FtpHelpers.UploadPlanItem Item, string Error)> failures = new List<(FtpHelpers.UploadPlanItem, string)>(conflicts);
        if (plan.Count == 1) {
            FtpHelpers.UploadPlanItem item = plan[0];
            try {
                await CliOutput.RunWithProgressAsync($"FTP sync upload {item.RemotePath}", item.Size > 0 ? item.Size : null, async progress => {
                    Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                        if (p.TransferredBytes >= 0)
                            progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "sending"));
                    });
                    if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                        AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                    await FtpHelpers.UploadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.LocalPath,
                        item.RemotePath,
                        ensureRemoteDirectory: true,
                        progress: ftpProgress,
                        cancellationToken: CancellationToken.None,
                        verifyAlgorithm: verifyAlgorithm,
                        resume: settings.Resume,
                        overwriteApproved: false);
                });
            }
            catch (Exception ex) {
                failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
            }
        }
        else {
            await CliOutput.RunBatchProgressAsync(
                $"FTP sync upload {remote}",
                plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
                async batch => {
                    foreach (FtpHelpers.UploadPlanItem item in plan) {
                        batch.StartFile(item.RemotePath, item.Size);
                        try {
                            Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                                if (p.TransferredBytes >= 0)
                                    batch.ReportFileProgress(p.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, plan.Count)}");
                            });
                            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                                AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                            await FtpHelpers.UploadFileVerifiedAsync(
                                ip,
                                port,
                                user,
                                pass,
                                timeout,
                                item.LocalPath,
                                item.RemotePath,
                                ensureRemoteDirectory: true,
                                progress: ftpProgress,
                                cancellationToken: CancellationToken.None,
                                verifyAlgorithm: verifyAlgorithm,
                                resume: settings.Resume,
                                overwriteApproved: false);
                            batch.CompleteFile();
                        }
                        catch (Exception ex) {
                            failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                            batch.ReportFileProgress(item.Size, "failed");
                            batch.CompleteFile();
                        }
                    }
                });
        }

        if (failures.Count > 0) {
            HashSet<FtpHelpers.UploadPlanItem> failedItems = failures.Select(failure => failure.Item).ToHashSet();
            int transferred = plan.Count - plan.Count(item => failedItems.Contains(item));
            OperationFeedback.WriteFailure(
                "FTP sync failed",
                FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count + conflicts.Count, plan.Count, skipped.Count, transferred, failures.Count, plan.Concat(skipped).Concat(conflicts.Select(conflict => conflict.Item)).Sum(item => item.Size)));
            foreach ((FtpHelpers.UploadPlanItem item, string error) in failures.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, null))} -> {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "FTP sync complete",
            FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count, 0, plan.Concat(skipped).Sum(item => item.Size)) +
            $"\n[green]{plan.Count}[/] file(s) -> [cyan]{Markup.Escape(remote)}[/]" +
            (verifyAlgorithm != null ? $" [grey]|[/] verify [white]{verifyAlgorithm}[/]" : string.Empty));
        if (skipped.Count > 0) {
            OperationFeedback.WriteWarning(
                settings.Resume ? "FTP sync resume skipped existing files" : "FTP sync skipped existing files",
                settings.Resume
                    ? "destination was not shorter than the source."
                    : skipped.Count.ToString(CultureInfo.InvariantCulture));
        }
        return 0;
    }

    private static async Task<int> ExecuteDownloadAsync(Settings settings, string? verifyAlgorithm) {
        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path!);
        (string ip, int port, string user, string pass, int timeout) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);

        await using AsyncFtpClient planClient = FtpHelpers.CreateClient(ip, port, user, pass, timeout);
        await planClient.Connect(CancellationToken.None);

        List<FtpHelpers.DownloadPlanItem> plan;
        List<FtpHelpers.DownloadPlanItem> skipped;
        try {
            (plan, skipped) = await FtpHelpers.BuildDownloadPlanAsync(
                planClient,
                remote,
                settings.Input!,
                settings.Recursive,
                overwrite: false,
                createOutputDirectory: !settings.DryRun,
                resume: settings.Resume);
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP sync plan failed", ex.Message, "FTP_SYNC_PLAN_FAILED");
        }

        if (settings.DryRun) {
            FtpHelpers.DownloadDryRunPayload payload = FtpHelpers.BuildDownloadDryRunPayload(remote, settings.Input!, plan, skipped, verifyAlgorithm);
            if (settings.Json) {
                CliOutput.EmitJson(payload);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]FTP Sync Preview[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine("[grey]Direction:[/] [white]download[/]");
            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                AnsiConsole.MarkupLine($"[grey]Planned verification:[/] [white]{verifyAlgorithm}[/] [grey](post-download)[/]");
            AnsiConsole.MarkupLine($"[grey]Summary:[/] {Markup.Escape(FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, 0, 0, plan.Concat(skipped).Sum(item => item.Size)))}");
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Action[/]"));
            table.AddColumn(new TableColumn("[cyan]Remote[/]"));
            table.AddColumn(new TableColumn("[white]Local[/]"));
            table.AddColumn(new TableColumn("[grey]Size[/]"));
            foreach (FtpHelpers.DownloadPlanItem item in plan) {
                table.AddRow("[green]download[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, settings.Input!))}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            foreach (FtpHelpers.DownloadPlanItem item in skipped) {
                table.AddRow("[yellow]skip[/]", $"[cyan]{Markup.Escape(item.RemotePath)}[/]", $"[white]{Markup.Escape(FtpHelpers.RedactTransferLocalPath(item.LocalPath, settings.Input!))}[/]", $"[grey]{FtpHelpers.FormatBytes(item.Size)}[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }

        if (settings.Json) {
            List<(FtpHelpers.DownloadPlanItem Item, string Error)> jsonFailures = new List<(FtpHelpers.DownloadPlanItem, string)>();
            Dictionary<FtpHelpers.DownloadPlanItem, FtpHelpers.TransferVerificationResult> jsonVerifications = new Dictionary<FtpHelpers.DownloadPlanItem, FtpHelpers.TransferVerificationResult>();
            foreach (FtpHelpers.DownloadPlanItem item in plan) {
                try {
                    FtpHelpers.TransferVerificationResult? verification = await FtpHelpers.DownloadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.RemotePath,
                        item.LocalPath,
                        null,
                        CancellationToken.None,
                        verifyAlgorithm,
                        settings.Resume);
                    if (verification != null)
                        jsonVerifications[item] = verification;
                }
                catch (Exception ex) {
                    jsonFailures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                }
            }

            IReadOnlyList<FtpHelpers.DownloadTransferJsonFile> files = FtpHelpers.BuildDownloadJsonFiles(
                remote,
                settings.Input!,
                plan,
                skipped,
                jsonFailures,
                jsonVerifications);
            int transferred = plan.Count - jsonFailures.Count;
            long totalBytes = plan.Concat(skipped).Sum(item => item.Size);
            HashSet<FtpHelpers.DownloadPlanItem> failedItems = jsonFailures.Select(failure => failure.Item).ToHashSet();
            long transferredBytes = plan.Where(item => !failedItems.Contains(item)).Sum(item => item.Size);
            CliOutput.EmitJson(new FtpHelpers.DownloadTransferJsonPayload(
                Mode: settings.Recursive ? "recursive" : "single",
                Direction: "download",
                RemoteRoot: remote,
                LocalRoot: FtpHelpers.RedactTransferLocalRoot(settings.Input!),
                Verification: verifyAlgorithm == null ? null : new FtpHelpers.PlannedTransferVerification("post-download", verifyAlgorithm),
                TotalFiles: plan.Count + skipped.Count,
                Planned: plan.Count,
                Transferred: transferred,
                Skipped: skipped.Count,
                Failed: jsonFailures.Count,
                TotalBytes: totalBytes,
                Bytes: transferredBytes,
                Files: files,
                Failures: jsonFailures.Select(failure => new FtpHelpers.DownloadTransferJsonFailure(
                    failure.Item.RemotePath,
                    FtpHelpers.RedactTransferLocalPath(failure.Item.LocalPath, settings.Input!),
                    CommandLogRedactor.RedactFreeText(failure.Error))).ToList()));
            return jsonFailures.Count == 0 ? 0 : 1;
        }

        if (plan.Count == 0) {
            OperationFeedback.WriteWarning(
                "FTP sync skipped",
                FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, 0, skipped.Count, 0, 0, skipped.Sum(item => item.Size)) + "\nall target local files already exist.");
            return 0;
        }

        List<(FtpHelpers.DownloadPlanItem Item, string Error)> failures = new List<(FtpHelpers.DownloadPlanItem, string)>();
        if (plan.Count == 1) {
            FtpHelpers.DownloadPlanItem item = plan[0];
            try {
                await CliOutput.RunWithProgressAsync($"FTP sync download {item.RemotePath}", item.Size > 0 ? item.Size : null, async progress => {
                    Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                        if (p.TransferredBytes >= 0)
                            progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "receiving"));
                    });
                    if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                        AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                    await FtpHelpers.DownloadFileVerifiedAsync(
                        ip,
                        port,
                        user,
                        pass,
                        timeout,
                        item.RemotePath,
                        item.LocalPath,
                        ftpProgress,
                        CancellationToken.None,
                        verifyAlgorithm,
                        settings.Resume);
                });
            }
            catch (Exception ex) {
                failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
            }
        }
        else {
            await CliOutput.RunBatchProgressAsync(
                $"FTP sync download {remote}",
                plan.Select(item => new CliOutput.TransferBatchItem(item.RemotePath, item.Size)).ToList(),
                async batch => {
                    foreach (FtpHelpers.DownloadPlanItem item in plan) {
                        batch.StartFile(item.RemotePath, item.Size);
                        try {
                            Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                                if (p.TransferredBytes >= 0)
                                    batch.ReportFileProgress(p.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, plan.Count)}");
                            });
                            if (!string.IsNullOrWhiteSpace(verifyAlgorithm))
                                AnsiConsole.MarkupLine($"[silver]Verifying[/] [cyan1]{Markup.Escape(item.RemotePath)}[/] [grey]|[/] [white]{verifyAlgorithm}[/]");
                            await FtpHelpers.DownloadFileVerifiedAsync(
                                ip,
                                port,
                                user,
                                pass,
                                timeout,
                                item.RemotePath,
                                item.LocalPath,
                                ftpProgress,
                                CancellationToken.None,
                                verifyAlgorithm,
                                settings.Resume);
                            batch.CompleteFile();
                        }
                        catch (Exception ex) {
                            failures.Add((item, FtpHelpers.DescribeTransferException(ex)));
                            batch.ReportFileProgress(item.Size, "failed");
                            batch.CompleteFile();
                        }
                    }
                });
        }

        if (failures.Count > 0) {
            OperationFeedback.WriteFailure(
                "FTP sync failed",
                FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count - failures.Count, failures.Count, plan.Concat(skipped).Sum(item => item.Size)));
            foreach ((FtpHelpers.DownloadPlanItem item, string error) in failures.Take(5)) {
                AnsiConsole.MarkupLine($"[red]-[/] {Markup.Escape(item.RemotePath)}: {Markup.Escape(FtpHelpers.RedactTransferError(error))}");
            }
            return 1;
        }

        OperationFeedback.WriteSuccess(
            "FTP sync complete",
            FtpHelpers.FormatTransferSummary(plan.Count + skipped.Count, plan.Count, skipped.Count, plan.Count, 0, plan.Concat(skipped).Sum(item => item.Size)) +
            $"\n[green]{plan.Count}[/] file(s) -> [white]{Markup.Escape(FtpHelpers.RedactTransferLocalRoot(settings.Input!))}[/]" +
            (verifyAlgorithm != null ? $" [grey]|[/] verify [white]{verifyAlgorithm}[/]" : string.Empty));
        if (skipped.Count > 0)
            OperationFeedback.WriteWarning(
                settings.Resume ? "FTP sync resume skipped existing files" : "FTP sync skipped existing files",
                settings.Resume
                    ? "destination was not shorter than the source."
                    : skipped.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}

public sealed class FtpDeleteCommand : AsyncCommand<FtpDeleteCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP delete failed", "--path is required.", "FTP_DELETE_PATH_REQUIRED");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);
        try {
            (string ip, int port, string user, string pass, int timeoutMs) = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
            bool existedAsDirectory;
            Exception? deleteError = null;
            {
                await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
                await client.Connect(CancellationToken.None);

                FtpListItem? entry = await FtpHelpers.TryGetEntryFromParentListingAsync(client, remote);
                if (entry == null)
                    return WriteFailure(settings.Json, remote, $"{remote} was not found.");

                existedAsDirectory = entry.Type == FtpObjectType.Directory;
                try {
                    await FtpHelpers.DeleteRemotePathAsync(client, remote, CancellationToken.None);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    deleteError = ex;
                }
            }

            if (deleteError == null) {
                string deletedType = existedAsDirectory ? "directory" : "file";
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Path = remote,
                        Type = deletedType,
                        Deleted = true
                    });
                    return 0;
                }

                AnsiConsole.MarkupLine($"[green]Deleted {deletedType}[/] {Markup.Escape(remote)}");
                return 0;
            }

            string detail = deleteError != null
                ? FtpHelpers.RedactTransferError(deleteError.Message)
                : $"FTP delete failed for {remote}.";
            return WriteFailure(settings.Json, remote, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            return WriteFailure(settings.Json, remote, FtpHelpers.RedactTransferError(ex.Message));
        }
    }

    private static int WriteFailure(bool json, string remote, string detail) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "FTP delete failed",
                detail,
                "FTP_DELETE_FAILED",
                new[] {
                    "Confirm the path still exists on the FTP server.",
                    "Use `ftp list` on the parent directory to verify the remote layout before retrying."
                }));
        }
        else {
            OperationFeedback.WriteFailure("FTP delete failed", detail);
        }

        return 1;
    }

}

public sealed class FtpMkdirCommand : AsyncCommand<FtpMkdirCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "FTP directory path required",
                    "--path is required.",
                    "FTP_MKDIR_PATH_REQUIRED",
                    new[] { "Pass an FTP directory path such as /Hdd1/XeCLI_Test/." }));
            }
            else {
                AnsiConsole.MarkupLine("[red]--path is required.[/]");
            }

            return 1;
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);
        try {
            return await FtpHelpers.WithClientAsync(settings, async client => {
                bool alreadyExisted = await FtpHelpers.DirectoryExistsByCwdAsync(client, remote);
                if (!alreadyExisted)
                    await client.CreateDirectory(remote);

                if (!await FtpHelpers.DirectoryExistsByCwdAsync(client, remote))
                    throw new IOException($"FTP directory creation could not be verified for {remote}.");

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Path = remote,
                        Type = "directory",
                        Created = !alreadyExisted,
                        AlreadyExisted = alreadyExisted,
                        Verified = true
                    });
                }
                else if (alreadyExisted) {
                    AnsiConsole.MarkupLine($"[yellow]Directory already exists[/] {Markup.Escape(remote)}");
                }
                else {
                    AnsiConsole.MarkupLine($"[green]Created directory[/] {Markup.Escape(remote)}");
                }

                return 0;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            if (settings.Json) {
                if (FtpHelpers.TryWriteJsonConnectionFailure(true, ex))
                    return 1;

                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "FTP directory creation failed",
                    FtpHelpers.RedactTransferError(ex.Message),
                    "FTP_MKDIR_FAILED",
                    new[] {
                        "Confirm the parent path is writable and the directory name is valid.",
                        "Use `ftp list` on the parent directory before retrying."
                    }));
            }
            else {
                OperationFeedback.WriteFailure("FTP directory creation failed", FtpHelpers.RedactTransferError(ex.Message));
            }

            return 1;
        }
    }
}

public sealed class FtpMoveCommand : AsyncCommand<FtpMoveCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--from <PATH>")]
        public string? From { get; init; }

        [CommandOption("--to <PATH>")]
        public string? To { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP move failed", "--from and --to are required.", "FTP_MOVE_PATHS_REQUIRED");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string from = FtpHelpers.NormalizePath(settings.From);
        string to = FtpHelpers.NormalizePath(settings.To);
        try {
            return await FtpHelpers.WithClientAsync(settings, async client => {
                FtpListItem? sourceEntry = await FtpHelpers.TryGetEntryFromParentListingAsync(client, from);
                bool isDirectory = sourceEntry?.Type == FtpObjectType.Directory;
                if (isDirectory) {
                    await client.MoveDirectory(from, to);
                    bool sourceExists = await FtpHelpers.DirectoryExistsByCwdAsync(client, from);
                    bool destinationExists = await FtpHelpers.DirectoryExistsByCwdAsync(client, to);
                    if (sourceExists || !destinationExists)
                        throw new IOException($"FTP directory move could not be verified for {from} -> {to}.");
                }
                else {
                    await FtpHelpers.MoveFileVerifiedAsync(client, from, to);
                }

                string movedType = isDirectory ? "directory" : "file";
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        From = from,
                        To = to,
                        Type = movedType,
                        Moved = true,
                        Verified = true
                    });
                }
                else {
                    AnsiConsole.MarkupLine($"[green]Moved {movedType}[/] {Markup.Escape(from)} -> {Markup.Escape(to)}");
                }

                return 0;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(
                settings.Json,
                "FTP move failed",
                ex.Message,
                "FTP_MOVE_FAILED",
                "Confirm the source exists and the destination parent is writable, then retry.");
        }
    }
}

public sealed class FtpCatCommand : AsyncCommand<FtpCatCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }

        [CommandOption("--max <BYTES>")]
        [LocalizedDescription("Maximum bytes to read (default: 65536).")]
        public string? MaxBytes { get; init; }

        [CommandOption("--encoding <ENC>")]
        [LocalizedDescription("ascii|utf8|utf-8 (default: utf8).")]
        public string? EncodingName { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP cat failed", "--path is required.", "FTP_CAT_PATH_REQUIRED");
        }

        int maxBytes = 65536;
        if (!string.IsNullOrWhiteSpace(settings.MaxBytes) &&
            !int.TryParse(settings.MaxBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBytes)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP cat failed", "Invalid --max value.", "FTP_CAT_MAX_INVALID");
        }
        if (maxBytes is < 1 or > 16 * 1024 * 1024) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP cat failed", "--max must be between 1 and 16777216 bytes.", "FTP_CAT_MAX_INVALID");
        }

        if (!TryParseEncoding(settings.EncodingName, out Encoding encoding, out string? encodingError)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP cat failed", encodingError ?? "Invalid --encoding value.", "FTP_CAT_ENCODING_INVALID");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);

        try {
            return await FtpHelpers.WithClientAsync(settings, async client => {
                using Stream stream = await client.OpenRead(remote);
                byte[] buffer = new byte[Math.Min(maxBytes, 1024 * 1024)];
                int totalRead = 0;
                using MemoryStream ms = new MemoryStream();
                while (totalRead < maxBytes) {
                    int toRead = Math.Min(buffer.Length, maxBytes - totalRead);
                    int read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                    if (read <= 0)
                        break;
                    ms.Write(buffer, 0, read);
                    totalRead += read;
                }

                string content = encoding.GetString(ms.ToArray());
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        RemotePath = remote,
                        Encoding = encoding.WebName,
                        BytesRead = totalRead,
                        LimitBytes = maxBytes,
                        LimitReached = totalRead >= maxBytes,
                        Content = content
                    });
                }
                else {
                    AnsiConsole.WriteLine(content);
                }
                return 0;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP cat failed", ex.Message, "FTP_CAT_FAILED");
        }
    }

    internal static bool TryParseEncoding(string? encodingName, out Encoding encoding, out string? errorMessage) {
        encoding = Encoding.UTF8;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(encodingName))
            return true;

        switch (encodingName.Trim().ToLowerInvariant()) {
            case "ascii":
                encoding = Encoding.ASCII;
                return true;
            case "utf8":
            case "utf-8":
                return true;
            default:
                errorMessage = "Invalid --encoding value.";
                return false;
        }
    }
}

public sealed class FtpHashCommand : AsyncCommand<FtpHashCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote FTP file path. Use --remote as an alias.")]
        public string? Path { get; init; }

        [CommandOption("--remote <PATH>")]
        [LocalizedDescription("Alias for --path.")]
        public string? Remote {
            get => Path;
            init => Path = value;
        }

        [CommandOption("--algorithm <ALGORITHM>")]
        [LocalizedDescription("Hash algorithm: sha256, sha1, or md5.")]
        public string? Algorithm { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP hash failed", "--path is required.", "FTP_HASH_PATH_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(settings.Algorithm)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP hash failed", "--algorithm is required.", "FTP_HASH_ALGORITHM_REQUIRED");
        }

        if (!FtpHelpers.TryNormalizeHashAlgorithm(settings.Algorithm, out string algorithm, out string algorithmError)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP hash failed", algorithmError, "FTP_HASH_ALGORITHM_INVALID", "Use sha256, sha1, or md5.");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);

        try {
            FtpHelpers.HashResult result = await FtpHelpers.HashRemoteFileAsync(settings, remote, algorithm, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    result.RemotePath,
                    result.Algorithm,
                    result.Hash,
                    result.Size,
                    result.TransferMode,
                    result.DownloadedForHash
                });
                return 0;
            }

            string sizeText = result.Size.HasValue ? FtpHelpers.FormatBytes(result.Size.Value) : "unknown";
            OperationFeedback.WriteSuccess(
                "FTP hash complete",
                $"[cyan1]{Markup.Escape(result.RemotePath)}[/] [grey]|[/] [white]{result.Algorithm}[/] [grey]|[/] [gold1]{result.Hash}[/] [grey]|[/] [silver]{Markup.Escape(sizeText)}[/]");
            return 0;
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP hash failed", FtpHelpers.BuildConnectionFailureMessage(settings, ex), "FTP_HASH_FAILED");
        }
    }
}

public sealed class FtpDiffCommand : AsyncCommand<FtpDiffCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [LocalizedDescription("Remote FTP file path.")]
        public string? Path { get; init; }

        [CommandOption("--remote <PATH>", IsHidden = true)]
        [LocalizedDescription("Alias for --path.")]
        public string? Remote {
            init => Path = value;
        }

        [CommandOption("--local <FILE>")]
        [LocalizedDescription("Local file to compare.")]
        public string? Local { get; init; }

        [CommandOption("--algorithm <ALGORITHM>")]
        [LocalizedDescription("Hash algorithm: sha256, sha1, or md5.")]
        public string? Algorithm { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path) ||
            string.IsNullOrWhiteSpace(settings.Local) ||
            string.IsNullOrWhiteSpace(settings.Algorithm)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP diff failed", "--path, --local, and --algorithm are required.", "FTP_DIFF_ARGUMENTS_REQUIRED");
        }

        if (!FtpHelpers.TryNormalizeHashAlgorithm(settings.Algorithm, out string algorithm, out string algorithmError)) {
            return FtpHelpers.WriteValidationFailure(settings.Json, "FTP diff failed", algorithmError, "FTP_DIFF_ALGORITHM_INVALID", "Use sha256, sha1, or md5.");
        }

        if (FtpHelpers.TryWriteRuntimeFtpPreflightRefusal(settings.Json))
            return 1;

        string remote = FtpHelpers.NormalizePath(settings.Path);

        try {
            FtpHelpers.DiffResult result = await FtpHelpers.DiffRemoteFileAsync(settings, remote, settings.Local, algorithm, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    result.Match,
                    result.LocalHash,
                    result.RemoteHash,
                    result.Algorithm,
                    result.RemotePath,
                    result.LocalFileName,
                    result.LocalSize,
                    result.RemoteSize
                });
                return result.Match ? 0 : 1;
            }

            string localName = Markup.Escape(result.LocalFileName);
            string remotePath = Markup.Escape(result.RemotePath);
            if (result.Match) {
                OperationFeedback.WriteSuccess(
                    "FTP diff match",
                    $"[cyan1]{remotePath}[/] [grey]|[/] [white]{localName}[/] [grey]|[/] [green]{result.Algorithm}[/] [grey]|[/] [gold1]{Markup.Escape(result.LocalHash)}[/]");
                return 0;
            }

            OperationFeedback.WriteFailure(
                "FTP diff mismatch",
                $"[cyan1]{remotePath}[/] [grey]|[/] [white]{localName}[/] [grey]|[/] local=[white]{Markup.Escape(result.LocalHash)}[/] remote=[gold1]{Markup.Escape(result.RemoteHash)}[/]");
            return 1;
        }
        catch (Exception ex) {
            if (FtpHelpers.TryWriteJsonConnectionFailure(settings.Json, ex))
                return 1;
            return FtpHelpers.WriteOperationFailure(settings.Json, "FTP diff failed", FtpHelpers.BuildConnectionFailureMessage(settings, ex), "FTP_DIFF_FAILED");
        }
    }
}
