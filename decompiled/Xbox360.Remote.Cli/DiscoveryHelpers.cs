using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli;

internal static class DiscoveryHelpers
{
	public static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverAsync(DiscoverySettings settings, CancellationToken token)
	{
		int[] ports = ParsePorts(settings.Ports);
		DiscoveryOptions options = new DiscoveryOptions
		{
			Ports = ports,
			TcpTimeoutMs = (settings.TimeoutMs ?? 400),
			UseNapDiscovery = !settings.NoNap,
			UseTcpScan = !settings.NoTcp
		};
		try
		{
			return await ConsoleDiscovery.DiscoverAsync(options, token);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			throw;
		}
		catch (NotSupportedException)
		{
			return Array.Empty<DiscoveredConsole>();
		}
		catch (SocketException)
		{
			return Array.Empty<DiscoveredConsole>();
		}
		catch (AggregateException ex3)
		{
			ex3.InnerExceptions.All((Exception e) => e is NotSupportedException || e is SocketException);
			return Array.Empty<DiscoveredConsole>();
		}
		catch
		{
			return Array.Empty<DiscoveredConsole>();
		}
	}

	private static int[] ParsePorts(string? ports)
	{
		if (string.IsNullOrWhiteSpace(ports))
		{
			return new int[2] { 730, 731 };
		}
		int result;
		return (from p in ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			select int.TryParse(p, out result) ? result : 0 into p
			where p > 0
			select p).Distinct().ToArray();
	}
}
