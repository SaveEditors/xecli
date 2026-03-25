using System.Globalization;
using System.IO;
using System.Net.Sockets;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal static class CliHelpers {
    private const int ReconnectAttempts = 3;
    private const int ReconnectTimeoutMs = 10_000;

    private static Task<string?>? stopReadTask;

    public static async Task<XbdmClient> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken) {
        (string ip, int port, int timeout) = await ResolveTargetAsync(settings, cancellationToken);
        return await ConnectResolvedAsync(ip, port, timeout, cancellationToken);
    }

    public static async Task<XbdmClient> ConnectResolvedAsync(string ip, int port, int timeout, CancellationToken cancellationToken) {
        try {
            return await XbdmClient.ConnectAsync(new XbdmConnectionOptions { Host = ip, Port = port, TimeoutMs = timeout }, cancellationToken);
        }
        catch (Exception ex) when (IsConnectionFailure(ex)) {
            throw CreateConnectionFailureException(ip, port, timeout, ex);
        }
    }

    public static async Task<(string Ip, int Port, int TimeoutMs)> ResolveTargetAsync(ConnectionSettings settings, CancellationToken cancellationToken) {
        CliConfig config = CliConfig.Load();
        string? ip = settings.Ip ?? config.DefaultIp;
        if (string.IsNullOrWhiteSpace(ip)) {
            if (Console.IsInputRedirected)
                throw new InvalidOperationException("No IP provided. Use --ip or run `rgh connect`.");

            IReadOnlyList<DiscoveredConsole> consoles = await DiscoveryHelpers.DiscoverAsync(new DiscoverySettings(), cancellationToken);
            if (consoles.Count == 0)
                throw new InvalidOperationException("No consoles discovered. Use --ip or run `rgh connect`.");

            ip = consoles.Count == 1 ? consoles[0].Ip.ToString() : CliOutput.PromptForConsole(consoles);
            if (string.IsNullOrWhiteSpace(ip))
                throw new InvalidOperationException("No console selected.");

            config.DefaultIp = ip;
            config.DefaultPort ??= 730;
            config.Save();
            AnsiConsole.MarkupLine($"[green]Default console set to[/] {ip}");
        }

        int port = settings.Port ?? config.DefaultPort ?? 730;
        int timeout = settings.TimeoutMs ?? 5000;
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

        for (int attempt = 1; attempt <= ReconnectAttempts; attempt++) {
            int attemptTimeout = attempt == 1 ? target.TimeoutMs : ReconnectTimeoutMs;
            try {
                using XbdmClient client = await ConnectResolvedAsync(target.Ip, target.Port, attemptTimeout, cancellationToken);

                EnsureDefaultTarget(settings, target.Ip, target.Port);
                return await action(client);
            }
            catch (Exception ex) when (IsTransient(ex)) {
                lastError = ex;
                if (attempt >= ReconnectAttempts)
                    break;

                bool cancel = await ShowReconnectCountdownAsync(attempt, ReconnectAttempts, 10, cancellationToken);
                if (cancel)
                    throw new OperationCanceledException("Reconnection cancelled by user.");
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

    private static bool IsTransient(Exception ex) {
        return ex is IOException ||
               ex is SocketException ||
               ex is TimeoutException;
    }

    private static bool IsConnectionFailure(Exception ex) {
        return ex is TimeoutException ||
               ex is SocketException ||
               ex is OperationCanceledException operationCanceled && IsGenericCancellationMessage(operationCanceled.Message) ||
               ex is IOException ioException && LooksLikeConnectionFailure(ioException.Message);
    }

    private static bool IsGenericCancellationMessage(string? message) {
        return string.IsNullOrWhiteSpace(message) ||
               message.Equals("The operation was canceled.", StringComparison.OrdinalIgnoreCase) ||
               message.Equals("A task was canceled.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeConnectionFailure(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
    }

    private static IOException CreateConnectionFailureException(string ip, int port, int timeout, Exception innerException) {
        return new IOException(
            $"Console connection to {ip}:{port} did not complete within {timeout} ms. Check that the console is powered on, not frozen, and reachable, then try again or re-run rgh connect if the target changed.",
            innerException);
    }

    private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, CancellationToken cancellationToken) {
        Task<string?> stopTask = EnsureStopTask();
        for (int remaining = seconds; remaining > 0; remaining--) {
            AnsiConsole.MarkupLine(
                $"[yellow]Attempting reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");

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

        if (stopReadTask == null || stopReadTask.IsCompleted) {
            stopReadTask = Task.Run(() => Console.ReadLine());
        }

        return stopReadTask;
    }

    public static CancellationTokenSource CreateTimeoutTokenSource(ConnectionSettings settings, int? overrideMs = null) {
        int timeout = overrideMs ?? settings.TimeoutMs ?? 5000;
        return new CancellationTokenSource(timeout);
    }

    public static bool TryParseUInt32(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseThreadId(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
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

        string type = text.Substring(0, idx).ToLowerInvariant();
        string value = text.Substring(idx + 1);
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
                arg = new RpcArgument(RpcArgType.Bytes, ConvertHexToBytes(value));
                return true;
            default:
                return false;
        }
    }

    private static byte[] ConvertHexToBytes(string hex) {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);
        if (hex.Length % 2 != 0)
            throw new InvalidOperationException("Hex string must have even length.");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
