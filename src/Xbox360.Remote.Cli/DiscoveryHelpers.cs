using System.Linq;
using System.Net.Sockets;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal static class DiscoveryHelpers {
    public static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(DiscoverySettings settings, CancellationToken token) {
        int[] ports = ParsePorts(settings.Ports);
        DiscoveryOptions options = new DiscoveryOptions {
            Ports = ports,
            TcpTimeoutMs = settings.TimeoutMs ?? 400,
            UseNapDiscovery = !settings.NoNap,
            UseTcpScan = !settings.NoTcp
        };

        try {
            return await ConsoleDiscovery.DiscoverAsync(options, token);
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

    private static int[] ParsePorts(string? ports) {
        if (string.IsNullOrWhiteSpace(ports))
            return new[] { 730, 731 };
        return ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out int port) ? port : 0)
            .Where(p => p > 0)
            .Distinct()
            .ToArray();
    }
}
