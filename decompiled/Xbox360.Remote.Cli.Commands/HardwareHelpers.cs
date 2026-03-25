using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli.Commands;

internal static class HardwareHelpers
{
	private sealed class RemoteUiBuffer
	{
		private readonly List<(uint Address, byte[] Data)> segments = new List<(uint, byte[])>();

		private uint cursor;

		public RemoteUiBuffer(uint baseAddress)
		{
			cursor = baseAddress;
		}

		public uint WriteUtf16String(string value)
		{
			byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value + "\0");
			return WriteBlock(bytes, 2);
		}

		public uint WritePointerArray(IReadOnlyList<uint> pointers)
		{
			byte[] array = new byte[pointers.Count * 4];
			for (int i = 0; i < pointers.Count; i++)
			{
				WriteUInt32BigEndian(array, i * 4, pointers[i]);
			}
			return WriteBlock(array, 4);
		}

		public uint WriteZeroBlock(int size, int alignment)
		{
			return WriteBlock(new byte[size], alignment);
		}

		public IReadOnlyList<(uint Address, byte[] Data)> GetSegments()
		{
			return segments;
		}

		private uint WriteBlock(byte[] data, int alignment)
		{
			cursor = Align(cursor, (uint)alignment);
			uint num = cursor;
			segments.Add((num, data));
			cursor += (uint)data.Length;
			return num;
		}

		private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
		{
			buffer[offset] = (byte)(value >> 24);
			buffer[offset + 1] = (byte)(value >> 16);
			buffer[offset + 2] = (byte)(value >> 8);
			buffer[offset + 3] = (byte)value;
		}

		private static uint Align(uint value, uint alignment)
		{
			uint num = value % alignment;
			if (num != 0)
			{
				return value + (alignment - num);
			}
			return value;
		}
	}

	private const int HalSendSmcMessageOrdinal = 41;

	private const int XamAllocOrdinal = 490;

	private const int XamShowMessageBoxUiOrdinal = 714;

	public static string DescribeSignInState(uint? state)
	{
		return state switch
		{
			null => "unknown", 
			0u => "Not signed in", 
			1u => "Signed in locally", 
			2u => "Signed in to Xbox Live", 
			_ => $"Unknown (0x{state.Value:X})", 
		};
	}

	public static async Task<string> GetSmcVersionAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		byte[] array = await SendSmcMessageAsync(client, new byte[1] { 18 }, cancellationToken);
		if (array.Length < 4)
		{
			throw new IOException("SMC did not return a version payload.");
		}
		return $"{array[2]}.{array[3]}";
	}

	public static async Task SetFanSpeedAsync(XbdmClient client, string channel, int speedPercent, CancellationToken cancellationToken)
	{
		int num = Math.Clamp(speedPercent, 10, 100);
		byte encoded = (byte)((num < 45) ? 127 : ((byte)(num | 0x80)));
		if (channel.Equals("both", StringComparison.OrdinalIgnoreCase) || channel.Equals("primary", StringComparison.OrdinalIgnoreCase))
		{
			await DispatchSmcMessageAsync(client, new byte[2] { 137, encoded }, cancellationToken);
		}
		if (channel.Equals("both", StringComparison.OrdinalIgnoreCase) || channel.Equals("secondary", StringComparison.OrdinalIgnoreCase))
		{
			await DispatchSmcMessageAsync(client, new byte[2] { 148, encoded }, cancellationToken);
		}
	}

	public static async Task SetLedsAsync(XbdmClient client, RingLedColor topLeft, RingLedColor topRight, RingLedColor bottomLeft, RingLedColor bottomRight, CancellationToken cancellationToken)
	{
		await new Jrpc2Client(client).SetLedsAsync((int)topLeft, (int)topRight, (int)bottomLeft, (int)bottomRight, cancellationToken);
	}

	public static async Task ExecuteXamShortcutAsync(XbdmClient client, XamShortcutOrdinal ordinal, CancellationToken cancellationToken)
	{
		await new Jrpc2Client(client).DispatchAsync(RpcDataType.Void, null, "xam.xex", (int)ordinal, systemThread: false, vm: false, new RpcArgument[4]
		{
			new RpcArgument(RpcArgType.Int, 0),
			new RpcArgument(RpcArgType.Int, 0),
			new RpcArgument(RpcArgType.Int, 0),
			new RpcArgument(RpcArgType.Int, 0)
		}, cancellationToken);
	}

	public static string FormatLedSummary(CliConfig.LedStateInfo state)
	{
		return $"TL={state.TopLeft}, TR={state.TopRight}, BL={state.BottomLeft}, BR={state.BottomRight}";
	}

	public static bool TryParsePopupStylePreset(string? value, out PopupStylePreset preset)
	{
		preset = PopupStylePreset.None;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		switch (value.Trim().ToLowerInvariant())
		{
		case "none":
		case "plain":
		case "clean":
		case "no-icon":
		case "noicon":
			preset = PopupStylePreset.None;
			return true;
		case "error":
		case "alert":
		case "x":
			preset = PopupStylePreset.Error;
			return true;
		case "question":
		case "ask":
			preset = PopupStylePreset.Question;
			return true;
		case "warn":
		case "caution":
		case "warning":
			preset = PopupStylePreset.Warning;
			return true;
		default:
			return false;
		}
	}

	public static bool TryParseLedColor(string? value, out RingLedColor color)
	{
		color = RingLedColor.Off;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		switch (value.Trim().ToLowerInvariant())
		{
		case "off":
		case "black":
			color = RingLedColor.Off;
			return true;
		case "red":
			color = RingLedColor.Red;
			return true;
		case "green":
			color = RingLedColor.Green;
			return true;
		case "amber":
		case "orange":
		case "yellow":
			color = RingLedColor.Orange;
			return true;
		default:
			return false;
		}
	}

	public static bool TryResolveLedState(CliConfig.LedStateInfo? cached, string? preset, string? topLeft, string? topRight, string? bottomLeft, string? bottomRight, out CliConfig.LedStateInfo state, out string? error)
	{
		error = null;
		state = new CliConfig.LedStateInfo();
		if (!string.IsNullOrWhiteSpace(preset))
		{
			if (!TryGetPreset(preset, out state))
			{
				error = "Unknown preset. Use all-green, all-red, all-orange, all-off, quadrant1, quadrant2, quadrant3, or quadrant4.";
				return false;
			}
		}
		else
		{
			state = new CliConfig.LedStateInfo
			{
				TopLeft = (cached?.TopLeft ?? "off"),
				TopRight = (cached?.TopRight ?? "off"),
				BottomLeft = (cached?.BottomLeft ?? "off"),
				BottomRight = (cached?.BottomRight ?? "off")
			};
		}
		if (!string.IsNullOrWhiteSpace(topLeft))
		{
			state.TopLeft = topLeft;
		}
		if (!string.IsNullOrWhiteSpace(topRight))
		{
			state.TopRight = topRight;
		}
		if (!string.IsNullOrWhiteSpace(bottomLeft))
		{
			state.BottomLeft = bottomLeft;
		}
		if (!string.IsNullOrWhiteSpace(bottomRight))
		{
			state.BottomRight = bottomRight;
		}
		if (!TryParseLedColor(state.TopLeft, out var color) || !TryParseLedColor(state.TopRight, out color) || !TryParseLedColor(state.BottomLeft, out color) || !TryParseLedColor(state.BottomRight, out color))
		{
			error = "Invalid LED color. Use off, green, red, or orange.";
			return false;
		}
		state.UpdatedUtc = DateTimeOffset.UtcNow;
		return true;
	}

	public static void SaveLedState(CliConfig.LedStateInfo state)
	{
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.LastLedState = state;
		cliConfig.Save();
	}

	public static void SaveFanState(int speedPercent, string channel)
	{
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.LastFanState = new CliConfig.FanStateInfo
		{
			SpeedPercent = speedPercent,
			Channel = channel,
			UpdatedUtc = DateTimeOffset.UtcNow
		};
		cliConfig.Save();
	}

	public static async Task<ProfileHelpers.XamUserInfo?> TryGetSignedInUserAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		return await ProfileHelpers.TryGetSignedInXamUserAsync(client, cancellationToken);
	}

	public static async Task ShowMessageBoxAsync(XbdmClient client, string title, string body, IReadOnlyList<string> buttons, uint messageBoxType, uint focusedButtonIndex, CancellationToken cancellationToken)
	{
		uint? num = await TryAllocateXamMemoryAsync(client, EstimateMessageBoxBufferSize(title, body, buttons), cancellationToken);
		if (!num.HasValue)
		{
			uint? num2 = await FindScratchBaseAsync(client, cancellationToken);
			if (!num2.HasValue)
			{
				throw new IOException("Could not locate writable memory for popup UI.");
			}
			num = num2.Value + 8192;
		}
		RemoteUiBuffer remoteUiBuffer = new RemoteUiBuffer(num.Value);
		uint titleAddress = remoteUiBuffer.WriteUtf16String(title);
		uint bodyAddress = remoteUiBuffer.WriteUtf16String(body);
		List<uint> list = new List<uint>(buttons.Count);
		foreach (string button in buttons)
		{
			list.Add(remoteUiBuffer.WriteUtf16String(button));
		}
		uint buttonArrayAddress = remoteUiBuffer.WritePointerArray(list);
		uint resultAddress = remoteUiBuffer.WriteZeroBlock(32, 16);
		uint overlappedAddress = remoteUiBuffer.WriteZeroBlock(32, 16);
		foreach (var (address, array) in remoteUiBuffer.GetSegments())
		{
			await client.WriteMemoryAsync(address, array, cancellationToken);
		}
		await new Jrpc2Client(client).DispatchAsync(RpcDataType.Int, null, "xam.xex", 714, systemThread: false, vm: false, new RpcArgument[9]
		{
			new RpcArgument(RpcArgType.UInt, 0u),
			new RpcArgument(RpcArgType.UInt, titleAddress),
			new RpcArgument(RpcArgType.UInt, bodyAddress),
			new RpcArgument(RpcArgType.UInt, (uint)buttons.Count),
			new RpcArgument(RpcArgType.UInt, buttonArrayAddress),
			new RpcArgument(RpcArgType.UInt, focusedButtonIndex),
			new RpcArgument(RpcArgType.UInt, messageBoxType),
			new RpcArgument(RpcArgType.UInt, resultAddress),
			new RpcArgument(RpcArgType.UInt, overlappedAddress)
		}, cancellationToken);
	}

	private static bool TryGetPreset(string preset, out CliConfig.LedStateInfo state)
	{
		CliConfig.LedStateInfo ledStateInfo;
		switch (preset.Trim().ToLowerInvariant())
		{
		case "all-green":
			ledStateInfo = NewLedState("all-green", "green", "green", "green", "green");
			break;
		case "all-red":
			ledStateInfo = NewLedState("all-red", "red", "red", "red", "red");
			break;
		case "all-orange":
			ledStateInfo = NewLedState("all-orange", "orange", "orange", "orange", "orange");
			break;
		case "all-off":
			ledStateInfo = NewLedState("all-off", "off", "off", "off", "off");
			break;
		case "quadrant1":
		case "player1":
			ledStateInfo = NewLedState("quadrant1", "green", "off", "off", "off");
			break;
		case "quadrant2":
		case "player2":
			ledStateInfo = NewLedState("quadrant2", "off", "green", "off", "off");
			break;
		case "quadrant3":
		case "player3":
			ledStateInfo = NewLedState("quadrant3", "off", "off", "green", "off");
			break;
		case "quadrant4":
		case "player4":
			ledStateInfo = NewLedState("quadrant4", "off", "off", "off", "green");
			break;
		default:
			ledStateInfo = new CliConfig.LedStateInfo();
			break;
		}
		state = ledStateInfo;
		return !string.IsNullOrWhiteSpace(state.TopLeft);
	}

	private static CliConfig.LedStateInfo NewLedState(string preset, string tl, string tr, string bl, string br)
	{
		return new CliConfig.LedStateInfo
		{
			Preset = preset,
			TopLeft = tl,
			TopRight = tr,
			BottomLeft = bl,
			BottomRight = br,
			UpdatedUtc = DateTimeOffset.UtcNow
		};
	}

	private static async Task<byte[]> SendSmcMessageAsync(XbdmClient client, byte[] message, CancellationToken cancellationToken)
	{
		uint? num = await FindScratchBaseAsync(client, cancellationToken);
		if (!num.HasValue)
		{
			throw new IOException("Could not locate a writable scratch region for SMC RPC.");
		}
		uint inputAddress = num.Value;
		uint outputAddress = num.Value + 32;
		byte[] array = new byte[16];
		byte[] output = new byte[16];
		Buffer.BlockCopy(message, 0, array, 0, Math.Min(16, message.Length));
		await client.WriteMemoryAsync(inputAddress, array, cancellationToken);
		await client.WriteMemoryAsync(outputAddress, output, cancellationToken);
		await new Jrpc2Client(client).CallAsync(RpcDataType.Void, null, "xboxkrnl.exe", 41, systemThread: true, vm: false, new RpcArgument[2]
		{
			new RpcArgument(RpcArgType.UInt, inputAddress),
			new RpcArgument(RpcArgType.UInt, outputAddress)
		}, 120, 250, cancellationToken);
		return await client.ReadMemoryBytesAsync(outputAddress, 16, cancellationToken);
	}

	private static async Task DispatchSmcMessageAsync(XbdmClient client, byte[] message, CancellationToken cancellationToken)
	{
		byte[] array = new byte[16];
		Buffer.BlockCopy(message, 0, array, 0, Math.Min(16, message.Length));
		await new Jrpc2Client(client).DispatchAsync(RpcDataType.Void, null, "xboxkrnl.exe", 41, systemThread: true, vm: false, new RpcArgument[1]
		{
			new RpcArgument(RpcArgType.Bytes, array)
		}, cancellationToken);
	}

	private static async Task<uint?> FindScratchBaseAsync(XbdmClient client, CancellationToken cancellationToken)
	{
		uint[] array = new uint[3] { 805306368u, 805306624u, 805310464u };
		uint[] array2 = array;
		foreach (uint candidate in array2)
		{
			try
			{
				if ((await client.ReadMemoryBytesAsync(candidate, 32, cancellationToken)).Length == 32)
				{
					return candidate;
				}
			}
			catch
			{
			}
		}
		IEnumerable<XbdmMemoryRegion> enumerable = from r in await client.GetMemoryRegionsAsync(cancellationToken)
			where r.Protect == 4 && r.Size >= 64
			orderby Math.Abs((long)r.BaseAddress - 805306368L)
			select r;
		foreach (XbdmMemoryRegion region in enumerable)
		{
			try
			{
				if ((await client.ReadMemoryBytesAsync(region.BaseAddress, 32, cancellationToken)).Length == 32)
				{
					return region.BaseAddress;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static int EstimateMessageBoxBufferSize(string title, string body, IReadOnlyList<string> buttons)
	{
		int num = 128;
		num += AlignInt((title.Length + 1) * 2, 4);
		num += AlignInt((body.Length + 1) * 2, 4);
		num += AlignInt(buttons.Count * 4, 4);
		foreach (string button in buttons)
		{
			num += AlignInt((button.Length + 1) * 2, 4);
		}
		return num;
	}

	private static async Task<uint?> TryAllocateXamMemoryAsync(XbdmClient client, int size, CancellationToken cancellationToken)
	{
		Jrpc2Client jrpc2Client = new Jrpc2Client(client);
		try
		{
			return ParseRpcUInt32(await jrpc2Client.CallAsync(RpcDataType.Int, null, "xam.xex", 490, systemThread: true, vm: false, new RpcArgument[1]
			{
				new RpcArgument(RpcArgType.Int, size)
			}, cancellationToken));
		}
		catch
		{
			return null;
		}
	}

	private static uint ParseRpcUInt32(string value)
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
			if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
			{
				return 0u;
			}
			return result2;
		}
		return result;
	}

	private static int AlignInt(int value, int alignment)
	{
		int num = value % alignment;
		if (num != 0)
		{
			return value + (alignment - num);
		}
		return value;
	}
}
