using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli;

internal static class CliHelpers {
    private const int MinimumReconnectTimeoutMs = 1_000;
    private const int ReconnectTimeoutMs = 10_000;

    private static readonly CancellationTokenSource ConsoleCancellationSource = CreateConsoleCancellationSource();
    private static Task<string?>? stopReadTask;

    public static async Task<XbdmClient> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken) {
        (string ip, int port, int timeout) = await ResolveTargetAsync(settings, cancellationToken);
        return await ConnectResolvedAsync(ip, port, timeout, cancellationToken);
    }

    public static async Task<XbdmClient> ConnectResolvedAsync(string ip, int port, int timeout, CancellationToken cancellationToken) {
        try {
            Stopwatch stopwatch = Stopwatch.StartNew();
            XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions { Host = ip, Port = port, TimeoutMs = timeout }, cancellationToken);
            RecentConsoleHistoryStore.RecordContact(ip, port, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds);
            return client;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException || ConnectionFailureModel.IsXbdmConnectionFailure(ex)) {
            throw CreateConnectionFailureException(ip, port, timeout, ex);
        }
    }

    public static Task<(string Ip, int Port, int TimeoutMs)> ResolveTargetAsync(ConnectionSettings settings, CancellationToken cancellationToken) {
        return ResolveTargetAsync(settings, CliConfig.Load(), cancellationToken, persistDefaultTarget: true);
    }

    internal static async Task<(string Ip, int Port, int TimeoutMs)> ResolveTargetAsync(
        ConnectionSettings settings,
        CliConfig config,
        CancellationToken cancellationToken,
        bool persistDefaultTarget,
        bool allowInteractivePrompt = true,
        Func<DiscoverySettings, CancellationToken, Task<IReadOnlyList<DiscoveredConsole>>>? discoverAsync = null) {
        TargetProfileStoreData targetStore = TargetProfileStore.Load();
        if (!TargetProfileStore.TryResolveProfile(targetStore, config, settings.Profile, out TargetProfileRecord? profile, out string profileError))
            throw new InvalidOperationException(profileError);

        bool hasExplicitIp = !string.IsNullOrWhiteSpace(settings.Ip);
        string? ip = settings.Ip ?? profile?.Ip ?? config.DefaultIp;
        if (string.IsNullOrWhiteSpace(ip)) {
            if (allowInteractivePrompt && Console.IsInputRedirected)
                throw new InvalidOperationException("No IP provided. Use --ip or run `rgh connect`.");

            discoverAsync ??= DiscoveryHelpers.DiscoverAsync;
            IReadOnlyList<DiscoveredConsole> consoles = await discoverAsync(new DiscoverySettings(), cancellationToken);
            if (consoles.Count == 0)
                throw new InvalidOperationException("No consoles discovered. Use --ip or run `rgh connect`.");

            ip = consoles.Count == 1 ? consoles[0].Ip.ToString() : CliOutput.PromptForConsole(consoles);
            if (string.IsNullOrWhiteSpace(ip))
                throw new InvalidOperationException("No console selected.");

            if (persistDefaultTarget) {
                config.DefaultIp = ip;
                config.DefaultPort ??= 730;
                config.Save();
                AnsiConsole.MarkupLine($"[green]Default console set to[/] {ip}");
            }
        }

        int port = settings.Port ?? (hasExplicitIp ? 730 : profile?.Port ?? config.DefaultPort ?? 730);
        int timeout = settings.TimeoutMs ?? CliPreferences.GetConnectionTimeoutMs(config);
        return (ip, port, timeout);
    }

    public static async Task<int> WithClientAsync(ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken) {
        (string ip, int port, int timeout) = await ResolveTargetAsync(settings, cancellationToken);
        return await WithClientAsync((ip, port, timeout), settings, action, cancellationToken);
    }

    public static async Task<int> WithClientOnceAsync(ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken) {
        (string ip, int port, int timeout) = await ResolveTargetAsync(settings, cancellationToken);
        return await WithClientOnceAsync((ip, port, timeout), settings, action, cancellationToken);
    }

    public static async Task<int> WithClientAsync((string Ip, int Port, int TimeoutMs) target, ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken) {
        Exception? lastError = null;
        CliConfig.TryLoad(out CliConfig config);
        int reconnectAttempts = settings.NoReconnect || config.AutoReconnectEnabled == false
            ? 1
            : CliPreferences.GetReconnectAttempts(config);
        int reconnectDelaySeconds = CliPreferences.GetReconnectDelaySeconds(config);

        for (int attempt = 1; attempt <= reconnectAttempts; attempt++) {
            int attemptTimeout = attempt == 1
                ? target.TimeoutMs
                : Math.Clamp(target.TimeoutMs, MinimumReconnectTimeoutMs, ReconnectTimeoutMs);
            XbdmClient? client = null;
            try {
                client = await ConnectResolvedAsync(target.Ip, target.Port, attemptTimeout, cancellationToken);
            }
            catch (Exception ex) when (ConnectionFailureModel.IsTransientXbdmConnectionFailure(ex)) {
                lastError = ex;
                if (attempt >= reconnectAttempts)
                    break;

                bool cancel = await ShowReconnectCountdownAsync(attempt, reconnectAttempts, reconnectDelaySeconds, settings.Json, cancellationToken);
                if (cancel)
                    throw new OperationCanceledException("Reconnection cancelled by user.");

                continue;
            }

            using (client) {
                EnsureDefaultTarget(settings, target.Ip, target.Port);
                return await action(client);
            }
        }

        throw lastError ?? new IOException("Unable to connect to console.");
    }

    public static async Task<int> WithClientOnceAsync((string Ip, int Port, int TimeoutMs) target, ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken) {
        using XbdmClient client = await ConnectResolvedAsync(target.Ip, target.Port, target.TimeoutMs, cancellationToken);

        EnsureDefaultTarget(settings, target.Ip, target.Port);
        return await action(client);
    }

    private static void EnsureDefaultTarget(ConnectionSettings settings, string ip, int port) {
        if (string.IsNullOrWhiteSpace(settings.Ip))
            return;
        CliConfig config = CliConfig.Load();
        if (string.Equals(config.DefaultIp, ip, StringComparison.OrdinalIgnoreCase) &&
            config.DefaultPort == port)
            return;

        config.DefaultIp = ip;
        config.DefaultPort = port;
        config.Save();
    }

    private static IOException CreateConnectionFailureException(string ip, int port, int timeout, Exception innerException) {
        string targetDisplay = $"{ip}:{port}";
        ConnectionFailureModel.XbdmFailureKind kind = ConnectionFailureModel.ClassifyXbdmFailure(innerException);
        string summary = kind switch {
            ConnectionFailureModel.XbdmFailureKind.ConnectTimeout => $"XBDM connection to {targetDisplay} did not complete within {timeout} ms.",
            ConnectionFailureModel.XbdmFailureKind.HandshakeStalled => $"XBDM handshake at {targetDisplay} stalled after TCP connect and did not return a greeting within {timeout} ms.",
            ConnectionFailureModel.XbdmFailureKind.HandshakeRejected => $"XBDM handshake at {targetDisplay} was rejected.",
            ConnectionFailureModel.XbdmFailureKind.PortClosedOrRefused => $"XBDM connection to {targetDisplay} was refused or closed before XBDM greeted the client.",
            _ when innerException is TimeoutException or OperationCanceledException => $"XBDM connection to {targetDisplay} did not complete within {timeout} ms.",
            _ => $"XBDM connection to {targetDisplay} failed."
        };

        CliErrorEnvelope error = ConnectionFailureModel.BuildXbdmError(summary, targetDisplay);
        return new IOException(
            error.Message,
            innerException);
    }

    private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, bool quiet, CancellationToken cancellationToken) {
        if (quiet) {
            await Task.Yield();
            return false;
        }

        Task<string?> stopTask = EnsureStopTask();
        for (int remaining = seconds; remaining > 0; remaining--) {
            AnsiConsole.MarkupLine(
                $"[yellow]Attempting reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");

            Task delay = Task.Delay(1000, cancellationToken);
            Task completed = await Task.WhenAny(delay, stopTask);
            if (completed == delay) {
                await delay;
                continue;
            }

            string? input = await stopTask;
            stopTask = EnsureStopTask();
            if (input != null && input.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Task<string?> EnsureStopTask() {
        if (Console.IsInputRedirected)
            return Task.FromResult<string?>(null);

        if (stopReadTask == null || stopReadTask.IsCompleted) {
            stopReadTask = Task.Run(() => Console.ReadLine());
        }

        return stopReadTask;
    }

    public static CancellationTokenSource CreateTimeoutTokenSource(ConnectionSettings settings, int? overrideMs = null) {
        CliConfig.TryLoad(out CliConfig config);
        int timeout = overrideMs ?? settings.TimeoutMs ?? CliPreferences.GetConnectionTimeoutMs(config);
        return new CancellationTokenSource(timeout);
    }

    public static CancellationToken ConsoleCancellationToken => ConsoleCancellationSource.Token;

    private static CancellationTokenSource CreateConsoleCancellationSource() {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => {
            args.Cancel = true;
            cancellationTokenSource.Cancel();
        };
        return cancellationTokenSource;
    }

    public static bool TryParseUInt32(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseThreadId(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (TryParseUInt32(text, out value))
            return true;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed)) {
            value = unchecked((uint) signed);
            return true;
        }
        return false;
    }

    public static bool TryParseRpcArgument(string text, out RpcArgument arg) {
        arg = new RpcArgument(RpcArgType.Int, 0);
        int idx = text.IndexOf(':');
        if (idx <= 0)
            return false;

        string type = text.Substring(0, idx).Trim().ToLowerInvariant();
        string value = text.Substring(idx + 1).Trim();
        switch (type) {
            case "int":
            case "i32":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32)) {
                    arg = new RpcArgument(RpcArgType.Int, i32);
                    return true;
                }
                return false;
            case "uint":
            case "u32":
                if (uint.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint u32)) {
                    arg = new RpcArgument(RpcArgType.UInt, u32);
                    return true;
                }
                if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out u32)) {
                    arg = new RpcArgument(RpcArgType.UInt, u32);
                    return true;
                }
                return false;
            case "bool":
                if (bool.TryParse(value, out bool b)) {
                    arg = new RpcArgument(RpcArgType.Bool, b);
                    return true;
                }
                return false;
            case "byte":
                if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b8)) {
                    arg = new RpcArgument(RpcArgType.Byte, b8);
                    return true;
                }
                return false;
            case "float":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) {
                    arg = new RpcArgument(RpcArgType.Float, f);
                    return true;
                }
                return false;
            case "double":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) {
                    arg = new RpcArgument(RpcArgType.Double, d);
                    return true;
                }
                return false;
            case "string":
                arg = new RpcArgument(RpcArgType.String, value);
                return true;
            case "u64":
            case "uint64":
            case "ulong":
                if (ulong.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.AsSpan(2) : value.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong u64)) {
                    arg = new RpcArgument(RpcArgType.UInt64, u64);
                    return true;
                }
                if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out u64)) {
                    arg = new RpcArgument(RpcArgType.UInt64, u64);
                    return true;
                }
                return false;
            case "i64":
            case "int64":
            case "long":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64)) {
                    arg = new RpcArgument(RpcArgType.Int64, i64);
                    return true;
                }
                return false;
            case "bytes":
            case "hex":
                if (TryConvertHexToBytes(value, out byte[] bytes)) {
                    arg = new RpcArgument(RpcArgType.Bytes, bytes);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool TryConvertHexToBytes(string hex, out byte[] bytes) {
        bytes = Array.Empty<byte>();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);
        if (hex.Length % 2 != 0)
            return false;
        bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return false;
        }

        return true;
    }
}
