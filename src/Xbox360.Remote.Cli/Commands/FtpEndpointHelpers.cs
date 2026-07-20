using System.Globalization;
using System.Net.Sockets;

namespace Xbox360.Remote.Cli.Commands;

internal sealed record FtpEndpointResolution(int Port, bool? Reachable);

internal sealed record FtpConnectionDefaults(int Port, string User, string Pass);

internal static class FtpEndpointHelpers {
    private const int DefaultProbeTimeoutMs = 5000;

    internal const string DefaultUser = "xboxftp";

    internal const string DefaultPassword = "xboxftp";

    internal static int GetEffectiveFtpPort(int? configuredPort, int fallbackPort = 21) {
        return configuredPort is > 0 and <= 65535
            ? configuredPort.Value
            : fallbackPort;
    }

    internal static int GetConfiguredFtpPort(CliConfig config) {
        return GetEffectiveFtpPort(config.DefaultFtpPort);
    }

    internal static FtpConnectionDefaults ResolveConfiguredConnection(CliConfig config, TargetProfileRecord? profile = null) {
        int port = GetEffectiveFtpPort(profile?.FtpPort ?? config.DefaultFtpPort);
        string user = FirstNonBlank(profile?.FtpUser, config.DefaultFtpUser) ?? DefaultUser;
        string pass = profile?.FtpPassword ?? config.DefaultFtpPassword ?? DefaultPassword;
        return new FtpConnectionDefaults(port, user, pass);
    }

    internal static Task<FtpEndpointResolution> ResolveBrowserEndpointAsync(
        string host,
        CliConfig config,
        int probeTimeoutMs,
        CancellationToken cancellationToken,
        Func<string, int, int, CancellationToken, Task<bool?>>? probeAsync = null) {
        return ResolveBrowserEndpointAsync(host, GetConfiguredFtpPort(config), probeTimeoutMs, cancellationToken, probeAsync);
    }

    internal static Task<FtpEndpointResolution> ResolveBrowserEndpointAsync(
        string host,
        int configuredPort,
        int probeTimeoutMs,
        CancellationToken cancellationToken,
        Func<string, int, int, CancellationToken, Task<bool?>>? probeAsync = null) {
        int effectivePort = GetEffectiveFtpPort(configuredPort);
        if (effectivePort == 21)
            return Task.FromResult(new FtpEndpointResolution(21, null));

        return ResolveStatusEndpointAsync(host, effectivePort, allowProbe: true, probeTimeoutMs, cancellationToken, probeAsync);
    }

    internal static async Task<FtpEndpointResolution> ResolveStatusEndpointAsync(
        string host,
        CliConfig config,
        bool allowProbe,
        int probeTimeoutMs,
        CancellationToken cancellationToken,
        Func<string, int, int, CancellationToken, Task<bool?>>? probeAsync = null) {
        return await ResolveStatusEndpointAsync(host, GetConfiguredFtpPort(config), allowProbe, probeTimeoutMs, cancellationToken, probeAsync);
    }

    internal static async Task<FtpEndpointResolution> ResolveStatusEndpointAsync(
        string host,
        int configuredPort,
        bool allowProbe,
        int probeTimeoutMs,
        CancellationToken cancellationToken,
        Func<string, int, int, CancellationToken, Task<bool?>>? probeAsync = null) {
        configuredPort = GetEffectiveFtpPort(configuredPort);
        if (!allowProbe)
            return new FtpEndpointResolution(configuredPort, null);

        probeAsync ??= TryProbeTcpPortAsync;
        int effectiveTimeout = GetEffectiveProbeTimeoutMs(probeTimeoutMs);

        if (configuredPort == 21) {
            bool? reachable = await probeAsync(host, 21, effectiveTimeout, cancellationToken);
            return new FtpEndpointResolution(21, reachable);
        }

        bool? configuredReachable = await probeAsync(host, configuredPort, effectiveTimeout, cancellationToken);
        if (configuredReachable == true)
            return new FtpEndpointResolution(configuredPort, true);

        bool? defaultReachable = await probeAsync(host, 21, effectiveTimeout, cancellationToken);
        if (defaultReachable == true)
            return new FtpEndpointResolution(21, true);

        return new FtpEndpointResolution(21, defaultReachable);
    }

    internal static string FormatResolvedFtpPortText(FtpEndpointResolution endpoint, int configuredPort) {
        string portText = endpoint.Port.ToString(CultureInfo.InvariantCulture);
        if (endpoint.Port != configuredPort)
            portText += $" (configured {configuredPort})";
        return portText;
    }

    internal static int GetEffectiveProbeTimeoutMs(int timeoutMs) {
        return timeoutMs > 0 ? timeoutMs : DefaultProbeTimeoutMs;
    }

    internal static async Task<bool?> TryProbeTcpPortAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            return null;

        int effectiveTimeout = GetEffectiveProbeTimeoutMs(timeoutMs);
        for (int attempt = 1; attempt <= 2; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                using TcpClient tcp = new TcpClient();
                Task connectTask = tcp.ConnectAsync(host, port);
                Task completed = await Task.WhenAny(connectTask, Task.Delay(effectiveTimeout, cancellationToken));
                if (completed == connectTask) {
                    await connectTask;
                    return true;
                }

                if (attempt == 2)
                    return false;
            }
            catch (SocketException) {
                if (attempt == 2)
                    return false;
            }
            catch (TimeoutException) {
                if (attempt == 2)
                    return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch {
                if (attempt == 2)
                    return false;
            }
        }

        return false;
    }

    private static string? FirstNonBlank(string? first, string? second) {
        if (!string.IsNullOrWhiteSpace(first))
            return first.Trim();
        if (!string.IsNullOrWhiteSpace(second))
            return second.Trim();
        return null;
    }
}
