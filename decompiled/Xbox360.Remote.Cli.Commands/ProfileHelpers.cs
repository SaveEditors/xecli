using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProfileHelpers
{
	internal sealed class XamUserInfo
	{
		[JsonPropertyName("slot")]
		public int Slot { get; set; }

		[JsonPropertyName("gamertag")]
		public string? Gamertag { get; set; }

		[JsonPropertyName("xuid")]
		public string? Xuid { get; set; }

		[JsonPropertyName("signinstate")]
		public uint SignInState { get; set; }
	}

	internal sealed class F3ProfileInfo
	{
		[JsonPropertyName("index")]
		public int Index { get; set; }

		[JsonPropertyName("gamertag")]
		public string? Gamertag { get; set; }

		[JsonPropertyName("gamerscore")]
		public int Gamerscore { get; set; }

		[JsonPropertyName("signedin")]
		public int SignedIn { get; set; }

		[JsonPropertyName("xuid")]
		public string? Xuid { get; set; }
	}

	private static readonly string[] ProfileRoots = new string[7] { "Hdd1", "Usb0", "Usb1", "Usb2", "Mu", "IntMu", "MmcMu" };

	private const uint XamUserGetNameOrdinal = 526u;

	private const uint XamUserGetSigninStateOrdinal = 528u;

	private const uint XamUserGetSigninInfoOrdinal = 551u;

	internal static async Task<List<string>> TryGetFtpProfilesAsync(string ip)
	{
		List<string> results = new List<string>();
		await FtpHelpers.WithClientAsync(new FtpConnectionSettings
		{
			Ip = ip
		}, async delegate(AsyncFtpClient client)
		{
			string[] profileRoots = ProfileRoots;
			foreach (string root in profileRoots)
			{
				string path = "/" + root + "/Content";
				FtpListItem[] array;
				try
				{
					(FtpListItem[], bool) obj = await FtpHelpers.GetListingWithFallbackAsync(client, path);
					(array, _) = obj;
					if (obj.Item2)
					{
						continue;
					}
				}
				catch
				{
					continue;
				}
				FtpListItem[] array2 = array;
				foreach (FtpListItem ftpListItem in array2)
				{
					if (ftpListItem.Type == FtpObjectType.Directory && IsHex16(ftpListItem.Name) && !ftpListItem.Name.Equals("0000000000000000", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(root + ":" + ftpListItem.Name);
					}
				}
			}
			return 0;
		}, CancellationToken.None);
		return results;
	}

	internal static async Task<List<F3ProfileInfo>> TryGetF3ProfilesAsync(string ip)
	{
		using HttpClient client = new HttpClient();
		client.Timeout = TimeSpan.FromSeconds(2L);
		string requestUri = "http://" + ip + ":9999/getProfileInfo";
		using HttpResponseMessage response = await client.GetAsync(requestUri);
		if (!response.IsSuccessStatusCode)
		{
			return new List<F3ProfileInfo>();
		}
		string text = await response.Content.ReadAsStringAsync();
		if (string.IsNullOrWhiteSpace(text))
		{
			return new List<F3ProfileInfo>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<F3ProfileInfo>>(text, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			}) ?? new List<F3ProfileInfo>();
		}
		catch
		{
			return new List<F3ProfileInfo>();
		}
	}

	internal static Dictionary<string, List<string>> GroupFtpProfiles(IEnumerable<string> entries)
	{
		Dictionary<string, List<string>> dictionary = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string entry in entries)
		{
			int num = entry.IndexOf(':');
			if (num > 0 && num != entry.Length - 1)
			{
				string device = entry.Substring(0, num);
				string key = entry.Substring(num + 1);
				if (!dictionary.TryGetValue(key, out var value))
				{
					value = (dictionary[key] = new List<string>());
				}
				if (!value.Any((string existing) => existing.Equals(device, StringComparison.OrdinalIgnoreCase)))
				{
					value.Add(device);
				}
			}
		}
		return dictionary;
	}

	internal static async Task<XamUserInfo?> TryGetSignedInXamUserAsync(string ip, int port, int timeoutMs, CancellationToken cancellationToken)
	{
		int signedInSlot = -1;
		uint signedInState = 0u;
		await WithFreshClientAsync(ip, port, timeoutMs, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			for (int slot = 0; slot < 4; slot++)
			{
				if (TryParseRpcUInt32(await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 528, systemThread: false, vm: false, new RpcArgument[1]
				{
					new RpcArgument(RpcArgType.Int, slot)
				}, cancellationToken), out var value) && value != 0)
				{
					signedInSlot = slot;
					signedInState = value;
					break;
				}
			}
			return 0;
		}, cancellationToken);
		if (signedInSlot < 0)
		{
			return null;
		}
		uint? num = await WithFreshClientAsync(ip, port, timeoutMs, (XbdmClient client) => FindScratchBaseAsync(client, cancellationToken), cancellationToken);
		if (!num.HasValue)
		{
			return null;
		}
		uint nameBuffer = num.Value;
		uint infoBuffer = num.Value + 256;
		await WithFreshClientAsync(ip, port, timeoutMs, async delegate(XbdmClient client)
		{
			await ZeroMemoryRawAsync(client, nameBuffer, 256, cancellationToken);
			await ZeroMemoryRawAsync(client, infoBuffer, 256, cancellationToken);
			return 0;
		}, cancellationToken);
		await WithFreshClientAsync(ip, port, timeoutMs, async delegate(XbdmClient client)
		{
			Jrpc2Client jrpc = new Jrpc2Client(client);
			await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 526, systemThread: false, vm: false, new RpcArgument[3]
			{
				new RpcArgument(RpcArgType.Int, signedInSlot),
				new RpcArgument(RpcArgType.UInt, nameBuffer),
				new RpcArgument(RpcArgType.Int, 256)
			}, cancellationToken);
			await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 551, systemThread: false, vm: false, new RpcArgument[3]
			{
				new RpcArgument(RpcArgType.Int, signedInSlot),
				new RpcArgument(RpcArgType.Int, 1),
				new RpcArgument(RpcArgType.UInt, infoBuffer)
			}, cancellationToken);
			return 0;
		}, cancellationToken);
		byte[] nameBytes = await WithFreshClientAsync(ip, port, timeoutMs, (XbdmClient client) => ReadMemoryRawAsync(client, nameBuffer, 64, cancellationToken), cancellationToken);
		byte[] array = await WithFreshClientAsync(ip, port, timeoutMs, (XbdmClient client) => ReadMemoryRawAsync(client, infoBuffer, 64, cancellationToken), cancellationToken);
		string text = ReadAsciiString(nameBytes);
		ulong? num2 = null;
		if (array.Length >= 8)
		{
			ulong num3 = BitConverter.ToUInt64(array, 0);
			if (num3 != 0L)
			{
				num2 = num3;
			}
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		return new XamUserInfo
		{
			Slot = signedInSlot,
			Gamertag = text,
			Xuid = (num2.HasValue ? $"0x{num2.Value:X16}" : null),
			SignInState = signedInState
		};
	}

	internal static async Task<XamUserInfo?> TryGetSignedInXamUserAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		Jrpc2Client jrpc = new Jrpc2Client(client);
		int signedInSlot = -1;
		uint signedInState = 0u;
		for (int slot = 0; slot < 4; slot++)
		{
			if (TryParseRpcUInt32(await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 528, systemThread: false, vm: false, new RpcArgument[1]
			{
				new RpcArgument(RpcArgType.Int, slot)
			}, cancellationToken), out var value) && value != 0)
			{
				signedInSlot = slot;
				signedInState = value;
				break;
			}
		}
		if (signedInSlot < 0)
		{
			return null;
		}
		uint? num = await FindScratchBaseAsync(client, cancellationToken);
		if (!num.HasValue)
		{
			return null;
		}
		uint nameBuffer = num.Value;
		uint infoBuffer = num.Value + 256;
		try
		{
			await ZeroMemoryRawAsync(client, nameBuffer, 256, cancellationToken);
			await ZeroMemoryRawAsync(client, infoBuffer, 256, cancellationToken);
			await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 526, systemThread: false, vm: false, new RpcArgument[3]
			{
				new RpcArgument(RpcArgType.Int, signedInSlot),
				new RpcArgument(RpcArgType.UInt, nameBuffer),
				new RpcArgument(RpcArgType.Int, 256)
			}, cancellationToken);
			await jrpc.CallAsync(RpcDataType.Int, null, "xam.xex", 551, systemThread: false, vm: false, new RpcArgument[3]
			{
				new RpcArgument(RpcArgType.Int, signedInSlot),
				new RpcArgument(RpcArgType.Int, 1),
				new RpcArgument(RpcArgType.UInt, infoBuffer)
			}, cancellationToken);
			byte[] nameBytes = await ReadMemoryRawAsync(client, nameBuffer, 64, cancellationToken);
			byte[] array = await ReadMemoryRawAsync(client, infoBuffer, 64, cancellationToken);
			string text = ReadAsciiString(nameBytes);
			ulong? num2 = null;
			if (array.Length >= 8)
			{
				ulong num3 = BitConverter.ToUInt64(array, 0);
				if (num3 != 0L)
				{
					num2 = num3;
				}
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			return new XamUserInfo
			{
				Slot = signedInSlot,
				Gamertag = text,
				Xuid = (num2.HasValue ? $"0x{num2.Value:X16}" : null),
				SignInState = signedInState
			};
		}
		catch
		{
			return null;
		}
	}

	internal static string? TryGetTitleFallbackName(uint? titleId, string? runningXex, string? resolvedName)
	{
		if (!string.IsNullOrWhiteSpace(resolvedName) && !resolvedName.Equals("Xbox 360 Dashboard", StringComparison.OrdinalIgnoreCase))
		{
			return resolvedName;
		}
		if (string.IsNullOrWhiteSpace(runningXex))
		{
			return resolvedName;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(runningXex.Trim());
		if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
		{
			return resolvedName;
		}
		if (titleId == (uint?)4294838225u && !fileNameWithoutExtension.Equals("dash", StringComparison.OrdinalIgnoreCase) && !fileNameWithoutExtension.Equals("default", StringComparison.OrdinalIgnoreCase))
		{
			return fileNameWithoutExtension;
		}
		if (!string.IsNullOrWhiteSpace(resolvedName))
		{
			return resolvedName;
		}
		return fileNameWithoutExtension;
	}

	private static async Task<uint?> FindScratchBaseAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		IEnumerable<XbdmMemoryRegion> enumerable = from r in await client.GetMemoryRegionsAsync(cancellationToken)
			where r.Protect == 4 && r.Size >= 512
			orderby Math.Abs((long)r.BaseAddress - 805306368L)
			select r;
		foreach (XbdmMemoryRegion region in enumerable)
		{
			byte[] array = await ReadMemoryRawAsync(client, region.BaseAddress, 256, cancellationToken);
			if (array.Length >= 256 && array.Count((byte b) => b != 0) == 0)
			{
				return region.BaseAddress;
			}
		}
		return null;
	}

	private static async Task<T> WithFreshClientAsync<T>(string ip, int port, int timeoutMs, Func<XbdmClient, Task<T>> action, CancellationToken cancellationToken)
	{
		using XbdmClient client = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
		{
			Host = ip,
			Port = port,
			TimeoutMs = timeoutMs
		}, cancellationToken);
		return await action(client);
	}

	private static async Task ZeroMemoryRawAsync(XbdmClient client, uint address, int length, CancellationToken cancellationToken)
	{
		byte[] inArray = new byte[32];
		string hex = Convert.ToHexString(inArray);
		int writeLength;
		for (int offset = 0; offset < length; offset += writeLength)
		{
			writeLength = Math.Min(32, length - offset);
			string value = ((writeLength == 32) ? hex : Convert.ToHexString(new byte[writeLength]));
			XbdmResponse item = (await client.SendRawAsync($"setmem addr=0x{(int)address + offset:X8} data={value}", cancellationToken)).Item1;
			if (item.StatusCode != 200)
			{
				throw new IOException("setmem failed: " + item.RawMessage);
			}
		}
	}

	private static async Task<byte[]> ReadMemoryRawAsync(XbdmClient client, uint address, int length, CancellationToken cancellationToken)
	{
		var (xbdmResponse, readOnlyList) = await client.SendRawAsync($"getmem addr=0x{address:X8} length={length}", cancellationToken);
		if (xbdmResponse.StatusCode != 202 || readOnlyList == null || readOnlyList.Count == 0)
		{
			return Array.Empty<byte>();
		}
		char[] array = string.Concat(readOnlyList).Where(Uri.IsHexDigit).ToArray();
		if (array.Length == 0)
		{
			return Array.Empty<byte>();
		}
		string text = new string(array);
		int num = Math.Min(text.Length, length * 2);
		if ((num & 1) != 0)
		{
			num--;
		}
		if (num <= 0)
		{
			return Array.Empty<byte>();
		}
		return Convert.FromHexString(text.Substring(0, num));
	}

	private static string? ReadAsciiString(byte[] data)
	{
		if (data.Length == 0)
		{
			return null;
		}
		int num = Array.IndexOf(data, (byte)0);
		if (num < 0)
		{
			num = data.Length;
		}
		if (num == 0)
		{
			return null;
		}
		return Encoding.ASCII.GetString(data, 0, num).Trim();
	}

	private static bool TryParseRpcUInt32(string? text, out uint value)
	{
		value = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		text = text.Trim();
		if (CliHelpers.TryParseUInt32(text, out value))
		{
			return true;
		}
		return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
	}

	private static bool IsHex16(string name)
	{
		if (name.Length != 16)
		{
			return false;
		}
		foreach (char c in name)
		{
			if ((c < '0' || c > '9') && (c < 'a' || c > 'f') && (c < 'A' || c > 'F'))
			{
				return false;
			}
		}
		return true;
	}
}
