using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace Xbox360.Remote.Cli;

internal static class CliHelpers
{
	private const int ReconnectAttempts = 3;

	private const int ReconnectTimeoutMs = 10000;

	private static Task<string?>? stopReadTask;

	public static async Task<XbdmClient> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken)
	{
		var (host, port, timeoutMs) = await ResolveTargetAsync(settings, cancellationToken);
		return await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = host,
			Port = port,
			TimeoutMs = timeoutMs
		}, cancellationToken);
	}

	public static async Task<(string Ip, int Port, int TimeoutMs)> ResolveTargetAsync(ConnectionSettings settings, CancellationToken cancellationToken)
	{
		CliConfig config = CliConfig.Load();
		string text = settings.Ip ?? config.DefaultIp;
		if (string.IsNullOrWhiteSpace(text))
		{
			if (Console.IsInputRedirected)
			{
				throw new InvalidOperationException("No IP provided. Use --ip or run `rgh connect`.");
			}
			IReadOnlyList<DiscoveredConsole> readOnlyList = await DiscoveryHelpers.DiscoverAsync(new DiscoverySettings(), cancellationToken);
			if (readOnlyList.Count == 0)
			{
				throw new InvalidOperationException("No consoles discovered. Use --ip or run `rgh connect`.");
			}
			text = ((readOnlyList.Count == 1) ? readOnlyList[0].Ip.ToString() : CliOutput.PromptForConsole(readOnlyList));
			if (string.IsNullOrWhiteSpace(text))
			{
				throw new InvalidOperationException("No console selected.");
			}
			config.DefaultIp = text;
			CliConfig cliConfig = config;
			int? defaultPort = cliConfig.DefaultPort;
			defaultPort.GetValueOrDefault();
			if (!defaultPort.HasValue)
			{
				int value = 730;
				cliConfig.DefaultPort = value;
			}
			config.Save();
			AnsiConsole.MarkupLine("[green]Default console set to[/] " + text);
		}
		int item = settings.Port ?? config.DefaultPort ?? 730;
		int item2 = settings.TimeoutMs ?? 5000;
		return (Ip: text, Port: item, TimeoutMs: item2);
	}

	public static async Task<int> WithClientAsync(ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken)
	{
		var (item, item2, item3) = await ResolveTargetAsync(settings, cancellationToken);
		return await WithClientAsync((Ip: item, Port: item2, TimeoutMs: item3), settings, action, cancellationToken);
	}

	public static async Task<int> WithClientOnceAsync(ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken)
	{
		var (item, item2, item3) = await ResolveTargetAsync(settings, cancellationToken);
		return await WithClientOnceAsync((Ip: item, Port: item2, TimeoutMs: item3), settings, action, cancellationToken);
	}

	public static async Task<int> WithClientAsync((string Ip, int Port, int TimeoutMs) target, ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken)
	{
		Exception lastError = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			int timeoutMs = ((attempt == 1) ? target.TimeoutMs : 10000);
			try
			{
				using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
				{
					Host = target.Ip,
					Port = target.Port,
					TimeoutMs = timeoutMs
				}, cancellationToken);
				EnsureDefaultTarget(settings, target.Ip, target.Port);
				return await action(client);
			}
			catch (Exception ex) when (IsTransient(ex))
			{
				lastError = ex;
				if (attempt < 3)
				{
					if (await ShowReconnectCountdownAsync(attempt, 3, 10, cancellationToken))
					{
						throw new OperationCanceledException("Reconnection cancelled by user.");
					}
					continue;
				}
			}
			break;
		}
		throw lastError ?? new IOException("Unable to connect to console.");
	}

	public static async Task<int> WithClientOnceAsync((string Ip, int Port, int TimeoutMs) target, ConnectionSettings settings, Func<XbdmClient, Task<int>> action, CancellationToken cancellationToken)
	{
		XbdmConnectionOptions xbdmConnectionOptions = new XbdmConnectionOptions
		{
			Host = target.Ip,
			Port = target.Port,
			TimeoutMs = target.TimeoutMs
		};
		using XbdmClient client = await XbdmClient.ConnectAsync(xbdmConnectionOptions, cancellationToken);
		EnsureDefaultTarget(settings, target.Ip, target.Port);
		return await action(client);
	}

	private static void EnsureDefaultTarget(ConnectionSettings settings, string ip, int port)
	{
		if (!string.IsNullOrWhiteSpace(settings.Ip))
		{
			CliConfig cliConfig = CliConfig.Load();
			if (!string.Equals(cliConfig.DefaultIp, ip, StringComparison.OrdinalIgnoreCase) || cliConfig.DefaultPort != port)
			{
				cliConfig.DefaultIp = ip;
				cliConfig.DefaultPort = port;
				cliConfig.Save();
			}
		}
	}

	private static bool IsTransient(Exception ex)
	{
		if (!(ex is IOException) && !(ex is SocketException))
		{
			return ex is TimeoutException;
		}
		return true;
	}

	private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, CancellationToken cancellationToken)
	{
		Task<string?> stopTask = EnsureStopTask();
		for (int remaining = seconds; remaining > 0; remaining--)
		{
			AnsiConsole.MarkupLine($"[yellow]Attempting reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");
			if (await Task.WhenAny(Task.Delay(1000, cancellationToken), stopTask) == stopTask)
			{
				string text = await stopTask;
				stopTask = EnsureStopTask();
				if (text != null && text.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static Task<string?> EnsureStopTask()
	{
		if (Console.IsInputRedirected)
		{
			return Task.FromResult<string>(null);
		}
		if (stopReadTask == null || stopReadTask.IsCompleted)
		{
			stopReadTask = Task.Run(() => Console.ReadLine());
		}
		return stopReadTask;
	}

	public static CancellationTokenSource CreateTimeoutTokenSource(ConnectionSettings settings, int? overrideMs = null)
	{
		return new CancellationTokenSource(overrideMs ?? settings.TimeoutMs ?? 5000);
	}

	public static bool TryParseUInt32(string? text, out uint value)
	{
		value = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
		}
		return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	public static bool TryParseThreadId(string? text, out uint value)
	{
		value = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		if (TryParseUInt32(text, out value))
		{
			return true;
		}
		if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			value = (uint)result;
			return true;
		}
		return false;
	}

	public static bool TryParseRpcArgument(string text, out RpcArgument arg)
	{
		arg = new RpcArgument(RpcArgType.Int, 0);
		int num = text.IndexOf(':');
		if (num <= 0)
		{
			return false;
		}
		string text2 = text.Substring(0, num).ToLowerInvariant();
		string text3 = text.Substring(num + 1);
		switch (text2)
		{
		case "int":
		case "i32":
		{
			if (int.TryParse(text3, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
			{
				arg = new RpcArgument(RpcArgType.Int, result2);
				return true;
			}
			return false;
		}
		case "u32":
		case "uint":
		{
			if (uint.TryParse(text3.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text3.AsSpan(2) : text3.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result7))
			{
				arg = new RpcArgument(RpcArgType.UInt, result7);
				return true;
			}
			if (uint.TryParse(text3, NumberStyles.Integer, CultureInfo.InvariantCulture, out result7))
			{
				arg = new RpcArgument(RpcArgType.UInt, result7);
				return true;
			}
			return false;
		}
		case "bool":
		{
			if (bool.TryParse(text3, out var result3))
			{
				arg = new RpcArgument(RpcArgType.Bool, result3);
				return true;
			}
			return false;
		}
		case "byte":
		{
			if (byte.TryParse(text3, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result6))
			{
				arg = new RpcArgument(RpcArgType.Byte, result6);
				return true;
			}
			return false;
		}
		case "float":
		{
			if (float.TryParse(text3, NumberStyles.Float, CultureInfo.InvariantCulture, out var result8))
			{
				arg = new RpcArgument(RpcArgType.Float, result8);
				return true;
			}
			return false;
		}
		case "double":
		{
			if (double.TryParse(text3, NumberStyles.Float, CultureInfo.InvariantCulture, out var result5))
			{
				arg = new RpcArgument(RpcArgType.Double, result5);
				return true;
			}
			return false;
		}
		case "string":
			arg = new RpcArgument(RpcArgType.String, text3);
			return true;
		case "u64":
		case "ulong":
		case "uint64":
		{
			if (ulong.TryParse(text3.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text3.AsSpan(2) : text3.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result4))
			{
				arg = new RpcArgument(RpcArgType.UInt64, result4);
				return true;
			}
			if (ulong.TryParse(text3, NumberStyles.Integer, CultureInfo.InvariantCulture, out result4))
			{
				arg = new RpcArgument(RpcArgType.UInt64, result4);
				return true;
			}
			return false;
		}
		case "i64":
		case "long":
		case "int64":
		{
			if (long.TryParse(text3, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				arg = new RpcArgument(RpcArgType.Int64, result);
				return true;
			}
			return false;
		}
		case "hex":
		case "bytes":
			arg = new RpcArgument(RpcArgType.Bytes, ConvertHexToBytes(text3));
			return true;
		default:
			return false;
		}
	}

	private static byte[] ConvertHexToBytes(string hex)
	{
		if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			hex = hex.Substring(2);
		}
		if (hex.Length % 2 != 0)
		{
			throw new InvalidOperationException("Hex string must have even length.");
		}
		byte[] array = new byte[hex.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
		}
		return array;
	}
}
