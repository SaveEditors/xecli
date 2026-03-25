using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Xbox360.Remote;

public sealed record DiscoveredConsole {
    public required IPAddress Ip { get; init; }
    public int Port { get; init; }
    public string? DebugName { get; init; }
    public string? ConsoleId { get; init; }
    public string Source { get; init; } = "scan";
}

public sealed class DiscoveryOptions {
    public int[] Ports { get; init; } = new[] { 730, 731 };
    public int TcpTimeoutMs { get; init; } = 400;
    public int NapTimeoutMs { get; init; } = 1200;
    public int MaxConcurrency { get; init; } = 128;
    public bool UseNapDiscovery { get; init; } = true;
    public bool UseTcpScan { get; init; } = true;
    public IReadOnlyList<IPNetwork>? Networks { get; init; }
}

public sealed record IPNetwork(IPAddress Network, IPAddress Mask) {
    public IEnumerable<IPAddress> GetHosts(int maxHosts) {
        uint net = ToUInt32(Network);
        uint mask = ToUInt32(Mask);
        uint broadcast = net | ~mask;
        uint first = net + 1;
        uint last = broadcast - 1;
        int count = 0;

        for (uint addr = first; addr <= last; addr++) {
            if (count >= maxHosts)
                yield break;
            yield return FromUInt32(addr);
            count++;
        }
    }

    public int HostCount {
        get {
            uint mask = ToUInt32(Mask);
            uint size = ~mask;
            if (size == 0)
                return 0;
            return (int) Math.Min(size - 1, int.MaxValue);
        }
    }

    private static uint ToUInt32(IPAddress ip) {
        byte[] bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static IPAddress FromUInt32(uint value) {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes);
    }
}

public static class ConsoleDiscovery {
    public static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(DiscoveryOptions options, CancellationToken cancellationToken) {
        List<IPNetwork> networks = options.Networks?.ToList() ?? new List<IPNetwork>();
        if (options.Networks == null) {
            try {
                networks = GetLocalNetworks();
            }
            catch (NotSupportedException) {
                networks = new List<IPNetwork>();
            }
        }
        ConcurrentDictionary<IPAddress, DiscoveredConsole> results = new ConcurrentDictionary<IPAddress, DiscoveredConsole>();

        if (options.UseNapDiscovery) {
            try {
                await DiscoverViaNapAsync(results, options, cancellationToken);
            }
            catch (NotSupportedException) {
                // ignore unsupported environments
            }
        }

        if (options.UseTcpScan) {
            try {
                await DiscoverViaTcpAsync(results, networks, options, cancellationToken);
            }
            catch (NotSupportedException) {
                // ignore unsupported environments
            }
            catch (SocketException) {
                // ignore network errors
            }
        }

        return results.Values.OrderBy(x => x.Ip.ToString()).ToList();
    }

    private static async Task DiscoverViaNapAsync(ConcurrentDictionary<IPAddress, DiscoveredConsole> results, DiscoveryOptions options, CancellationToken cancellationToken) {
        using UdpClient udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.ReceiveTimeout = options.NapTimeoutMs;

        byte[] packet = { 3, 0 }; // Type 3 (wildcard), length 0
        await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 731));

        DateTime deadline = DateTime.UtcNow.AddMilliseconds(options.NapTimeoutMs);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested) {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(remaining);
            try {
                UdpReceiveResult result = await udp.ReceiveAsync(timeoutCts.Token);
                if (result.Buffer.Length < 2)
                    continue;
                if (result.Buffer[0] != 2)
                    continue;

                int nameLen = result.Buffer[1];
                string? name = null;
                if (nameLen > 0 && result.Buffer.Length >= 2 + nameLen) {
                    name = Encoding.ASCII.GetString(result.Buffer, 2, nameLen);
                }

                results.TryAdd(result.RemoteEndPoint.Address, new DiscoveredConsole {
                    Ip = result.RemoteEndPoint.Address,
                    Port = 731,
                    DebugName = name,
                    Source = "nap"
                });
            }
            catch (SocketException) {
                break;
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }

    private static async Task DiscoverViaTcpAsync(ConcurrentDictionary<IPAddress, DiscoveredConsole> results, List<IPNetwork> networks, DiscoveryOptions options, CancellationToken cancellationToken) {
        List<Task> tasks = new List<Task>();
        using SemaphoreSlim semaphore = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);

        foreach (IPNetwork network in networks) {
            foreach (IPAddress ip in network.GetHosts(maxHosts: 4096)) {
                await semaphore.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    try {
                        foreach (int port in options.Ports) {
                            DiscoveredConsole? console = await ProbeAsync(ip, port, options.TcpTimeoutMs, cancellationToken);
                            if (console != null) {
                                results[console.Ip] = console;
                                break;
                            }
                        }
                    }
                    finally {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    private static async Task<DiscoveredConsole?> ProbeAsync(IPAddress ip, int port, int timeoutMs, CancellationToken cancellationToken) {
        using TcpClient client = new TcpClient();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try {
            await client.ConnectAsync(ip, port, cts.Token);
        }
        catch {
            return null;
        }

        if (!client.Connected)
            return null;

        NetworkStream stream = client.GetStream();
        XbdmLineReader reader = new XbdmLineReader(stream, 1024);
        string line;
        try {
            line = await reader.ReadLineAsync(cts.Token);
        }
        catch {
            return null;
        }

        XbdmResponse response;
        try {
            response = XbdmResponseParser.Parse(line);
        }
        catch {
            return null;
        }

        if (response.StatusCode != 200 && response.StatusCode != 201)
            return null;

        string? debugName = null;
        string? consoleId = null;
        try {
            await WriteLineAsync(stream, "dbgname", cts.Token);
            XbdmResponse dbg = XbdmResponseParser.Parse(await reader.ReadLineAsync(cts.Token));
            debugName = dbg.Message;

            await WriteLineAsync(stream, "getconsoleid", cts.Token);
            XbdmResponse id = XbdmResponseParser.Parse(await reader.ReadLineAsync(cts.Token));
            consoleId = XbdmParamUtils.TryGetString(id.Message, "consoleid", out string? parsedId) ? parsedId : id.Message;
        }
        catch {
            // ignore probe enrichment errors
        }

        return new DiscoveredConsole {
            Ip = ip,
            Port = port,
            DebugName = debugName,
            ConsoleId = consoleId,
            Source = "tcp"
        };
    }

    private static async Task WriteLineAsync(NetworkStream stream, string command, CancellationToken cancellationToken) {
        byte[] bytes = Encoding.ASCII.GetBytes(command + "\r\n");
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static List<IPNetwork> GetLocalNetworks() {
        List<IPNetwork> networks = new List<IPNetwork>();
        foreach (NetworkInterface iface in NetworkInterface.GetAllNetworkInterfaces()) {
            if (iface.OperationalStatus != OperationalStatus.Up)
                continue;
            IPInterfaceProperties props;
            try {
                props = iface.GetIPProperties();
            }
            catch {
                continue;
            }

            foreach (UnicastIPAddressInformation addr in props.UnicastAddresses) {
                try {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork || addr.IPv4Mask == null)
                        continue;

                    IPNetwork network = ComputeNetwork(addr.Address, addr.IPv4Mask);
                    if (network.HostCount > 4096) {
                        IPAddress mask = IPAddress.Parse("255.255.255.0");
                        network = ComputeNetwork(addr.Address, mask);
                    }

                    networks.Add(network);
                }
                catch {
                    // ignore unsupported address
                }
            }
        }

        return networks;
    }

    private static IPNetwork ComputeNetwork(IPAddress address, IPAddress mask) {
        byte[] addrBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        byte[] netBytes = new byte[4];
        for (int i = 0; i < 4; i++) {
            netBytes[i] = (byte) (addrBytes[i] & maskBytes[i]);
        }

        return new IPNetwork(new IPAddress(netBytes), mask);
    }
}
