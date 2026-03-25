using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal static class ModuleCommandHelpers
{
	public static uint ParseRpcUInt32(string value)
	{
		string text = value.Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return 0u;
		}
		if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(2);
		}
		if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
		{
			return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}
		return result;
	}

	public static async Task<uint> ResolveModuleHandleAsync(XbdmClient client, Jrpc2Client jrpc, string moduleName, CancellationToken cancellationToken)
	{
		if (TryGetStoredHandle(moduleName, out var handle))
		{
			return handle;
		}
		uint num = ParseRpcUInt32(await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 1102, systemThread: false, vm: false, new RpcArgument[1]
		{
			new RpcArgument(RpcArgType.Bytes, CreateNullTerminatedAscii(moduleName))
		}, cancellationToken));
		if (num != 0)
		{
			return num;
		}
		XbdmModuleInfo xbdmModuleInfo = (await client.GetModulesAsync(includeSections: false, cancellationToken)).FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
		if (xbdmModuleInfo == null)
		{
			return 0u;
		}
		if (TryGetStoredHandle(xbdmModuleInfo.Name, out handle))
		{
			return handle;
		}
		return 0u;
	}

	public static async Task<uint> UnloadModuleAsync(Jrpc2Client jrpc, uint handle, CancellationToken cancellationToken)
	{
		return ParseRpcUInt32(await jrpc.CallAsync(RpcDataType.Int, null, "xboxkrnl.exe", 417, systemThread: true, vm: false, new RpcArgument[1]
		{
			new RpcArgument(RpcArgType.UInt, handle)
		}, cancellationToken));
	}

	public static byte[] CreateNullTerminatedAscii(string value)
	{
		byte[] bytes = Encoding.ASCII.GetBytes(value);
		byte[] array = new byte[bytes.Length + 1];
		Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
		array[^1] = 0;
		return array;
	}

	public static bool IsAmbiguousRpcCompletion(Exception ex)
	{
		if (ex is TimeoutException)
		{
			return true;
		}
		if (ex is IOException ex2 && ex2.Message.Contains("potential infinite loop", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	public static async Task<XbdmModuleInfo?> SafeWaitForModuleStateAsync(XbdmClient client, string moduleName, bool shouldExist, CancellationToken cancellationToken)
	{
		try
		{
			return await WaitForModuleStateAsync(client, moduleName, shouldExist, cancellationToken);
		}
		catch (Exception)
		{
			return shouldExist ? null : new XbdmModuleInfo
			{
				Name = moduleName
			};
		}
	}

	public static async Task<XbdmModuleInfo?> WaitForModuleStateAsync(XbdmClient client, string moduleName, bool shouldExist, CancellationToken cancellationToken)
	{
		for (int attempt = 0; attempt < 5; attempt++)
		{
			XbdmModuleInfo xbdmModuleInfo = (await client.GetModulesAsync(includeSections: false, cancellationToken)).FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
			if (shouldExist)
			{
				if (xbdmModuleInfo != null)
				{
					return xbdmModuleInfo;
				}
			}
			else if (xbdmModuleInfo == null)
			{
				return null;
			}
			if (attempt + 1 < 5)
			{
				await Task.Delay(400, cancellationToken);
			}
		}
		return (await client.GetModulesAsync(includeSections: false, cancellationToken)).FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
	}

	public static async Task<XbdmModuleInfo?> WaitForModuleStateWithReconnectAsync(string ip, int port, int timeoutMs, string moduleName, bool shouldExist, CancellationToken cancellationToken)
	{
		for (int attempt = 0; attempt < 72; attempt++)
		{
			try
			{
				if (attempt > 0)
				{
					int value = attempt * 5;
					AnsiConsole.MarkupLine($"[yellow]Waiting for console reconnect {attempt + 1}/{72} ({value}s elapsed)...[/]");
				}
				XbdmModuleInfo result;
				await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
				{
					Host = ip,
					Port = port,
					TimeoutMs = timeoutMs
				}, cancellationToken))
				{
					result = await WaitForModuleStateAsync(client, moduleName, shouldExist, cancellationToken);
				}
				return result;
			}
			catch when (attempt + 1 < 72)
			{
				await Task.Delay(5000, cancellationToken);
			}
		}
		return shouldExist ? null : new XbdmModuleInfo
		{
			Name = moduleName
		};
	}

	public static async Task<uint> TryResolveHandleAfterReconnectAsync(string ip, int port, int timeoutMs, string moduleName, CancellationToken cancellationToken)
	{
		for (int attempt = 0; attempt < 24; attempt++)
		{
			try
			{
				uint result;
				await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
				{
					Host = ip,
					Port = port,
					TimeoutMs = timeoutMs
				}, cancellationToken))
				{
					Jrpc2Client jrpc = new Jrpc2Client(client);
					result = await ResolveModuleHandleAsync(client, jrpc, moduleName, cancellationToken);
				}
				return result;
			}
			catch when (attempt + 1 < 24)
			{
				await Task.Delay(5000, cancellationToken);
			}
		}
		return 0u;
	}

	public static async Task<XbdmModuleInfo?> ConfirmModulePresenceFreshAsync(string ip, int port, int timeoutMs, string moduleName, CancellationToken cancellationToken)
	{
		for (int attempt = 0; attempt < 4; attempt++)
		{
			if (attempt > 0)
			{
				await Task.Delay(1500, cancellationToken);
			}
			try
			{
				await using (XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
				{
					Host = ip,
					Port = port,
					TimeoutMs = timeoutMs
				}, cancellationToken))
				{
					XbdmModuleInfo xbdmModuleInfo = (await client.GetModulesAsync(includeSections: false, cancellationToken)).FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
					if (xbdmModuleInfo != null)
					{
						return xbdmModuleInfo;
					}
				}
				XbdmModuleInfo xbdmModuleInfo2 = null;
			}
			catch when (attempt + 1 < 4)
			{
			}
		}
		return null;
	}

	public static void StoreHandle(string moduleName, uint handle)
	{
		CliConfig cliConfig2;
		CliConfig cliConfig = (cliConfig2 = CliConfig.Load());
		if (cliConfig2.ModuleHandles == null)
		{
			Dictionary<string, string> dictionary = (cliConfig2.ModuleHandles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}
		cliConfig.ModuleHandles[moduleName] = $"0x{handle:X8}";
		cliConfig.Save();
	}

	public static void RemoveHandle(string moduleName)
	{
		CliConfig cliConfig = CliConfig.Load();
		CliConfig cliConfig2 = cliConfig;
		if (cliConfig2.ModuleHandles == null)
		{
			Dictionary<string, string> dictionary = (cliConfig2.ModuleHandles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		}
		if (cliConfig.ModuleHandles.Remove(moduleName))
		{
			cliConfig.Save();
		}
	}

	public static void StorePendingLoad(string moduleName, string modulePath, bool systemThread)
	{
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.PendingModuleOperation = new CliConfig.PendingModuleOperationInfo
		{
			Action = "load",
			ModuleName = moduleName,
			ModulePath = modulePath,
			SystemThread = systemThread,
			CreatedUtc = DateTimeOffset.UtcNow
		};
		cliConfig.Save();
	}

	public static void ClearPendingIfMatch(string action, string moduleName)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.PendingModuleOperation != null && string.Equals(cliConfig.PendingModuleOperation.Action, action, StringComparison.OrdinalIgnoreCase) && string.Equals(cliConfig.PendingModuleOperation.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
		{
			cliConfig.PendingModuleOperation = null;
			cliConfig.Save();
		}
	}

	private static bool TryGetStoredHandle(string moduleName, out uint handle)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.ModuleHandles != null && cliConfig.ModuleHandles.TryGetValue(moduleName, out string value) && CliHelpers.TryParseUInt32(value, out handle))
		{
			return true;
		}
		handle = 0u;
		return false;
	}
}
