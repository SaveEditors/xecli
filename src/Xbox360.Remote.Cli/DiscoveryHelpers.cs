using System.Linq;
using System.Net.Sockets;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal static class DiscoveryHelpers {
    public static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(DiscoverySettings settings, CancellationToken token) {
        if (!TryParsePorts(settings.Ports, out int[] ports, out string portError))
            throw new ArgumentException(portError, nameof(settings.Ports));

        DiscoveryOptions options = new DiscoveryOptions {
            Ports = ports,
            TcpTimeoutMs = settings.TimeoutMs ?? 400,
            UseNapDiscovery = !settings.NoNap,
            UseTcpScan = !settings.NoTcp
        };

        try {
            return await ConsoleDiscovery.DiscoverAsync(options, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) {
            throw;
        }
        catch (NotSupportedException) {
            return Array.Empty<DiscoveredConsole>();
        }
        catch (SocketException) {
            return Array.Empty<DiscoveredConsole>();
        }
        catch (AggregateException ex) {
            if (ex.InnerExceptions.All(e => e is NotSupportedException || e is SocketException))
                return Array.Empty<DiscoveredConsole>();
            return Array.Empty<DiscoveredConsole>();
        }
        catch {
            return Array.Empty<DiscoveredConsole>();
        }
    }

    internal static bool TryParsePorts(string? ports, out int[] parsedPorts, out string error) {
        parsedPorts = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(ports)) {
            parsedPorts = [730, 731];
            return true;
        }

        HashSet<int> validPorts = [];
        string? firstError = null;
        int invalidCount = 0;
        int totalCount = 0;

        foreach (string token in ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            totalCount++;
            if (!int.TryParse(token, out int port)) {
                firstError ??= $"Invalid port specification: '{token}' is not a valid integer.";
                invalidCount++;
                continue;
            }

            if (port < 1 || port > 65535) {
                firstError ??= $"Invalid port specification: {port} is out of valid range (1-65535).";
                invalidCount++;
                continue;
            }

            validPorts.Add(port);
        }

        if (invalidCount > 0) {
            if (invalidCount == totalCount) {
                error = "No valid discovery ports were supplied.";
            }
            else {
                error = firstError!;
            }
            return false;
        }

        parsedPorts = validPorts.ToArray();
        return true;
    }
}
