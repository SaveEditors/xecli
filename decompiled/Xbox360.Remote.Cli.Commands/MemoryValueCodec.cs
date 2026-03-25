using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Xbox360.Remote.Cli.Commands;

internal static class MemoryValueCodec
{
	public static string NormalizeType(string? type)
	{
		return type?.Trim().ToLowerInvariant() switch
		{
			"byte" => "u8", 
			"short" => "s16", 
			"ushort" => "u16", 
			"int" => "s32", 
			"uint" => "u32", 
			"long" => "s64", 
			"ulong" => "u64", 
			"float" => "f32", 
			"double" => "f64", 
			"string" => "ascii", 
			"bytes" => "hex", 
			null => string.Empty, 
			_ => type.Trim().ToLowerInvariant(), 
		};
	}

	public static int GetSize(string type)
	{
		switch (NormalizeType(type))
		{
		case "s8":
		case "u8":
			return 1;
		case "s16":
		case "u16":
			return 2;
		case "s32":
		case "u32":
		case "f32":
			return 4;
		case "s64":
		case "u64":
		case "f64":
			return 8;
		case "hex":
		case "ascii":
			return 0;
		default:
			return -1;
		}
	}

	public static object ParseValue(string type, ReadOnlySpan<byte> bytes, bool littleEndian)
	{
		return NormalizeType(type) switch
		{
			"u8" => bytes[0], 
			"s8" => (sbyte)bytes[0], 
			"u16" => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes), 
			"s16" => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes), 
			"u32" => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes), 
			"s32" => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes), 
			"u64" => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes), 
			"s64" => littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes), 
			"f32" => BitConverter.Int32BitsToSingle(littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes)), 
			"f64" => BitConverter.Int64BitsToDouble(littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes)), 
			"ascii" => Encoding.ASCII.GetString(bytes).TrimEnd('\0'), 
			"hex" => ConvertToHex(bytes), 
			_ => "unknown", 
		};
	}

	public static byte[] BuildBytes(string type, string value, bool littleEndian)
	{
		switch (NormalizeType(type))
		{
		case "u8":
			return new byte[1] { byte.Parse(value, CultureInfo.InvariantCulture) };
		case "s8":
			return new byte[1] { (byte)sbyte.Parse(value, CultureInfo.InvariantCulture) };
		case "u16":
		{
			Span<byte> destination6 = stackalloc byte[2];
			ushort value7 = ParseUInt16(value);
			if (littleEndian)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(destination6, value7);
			}
			else
			{
				BinaryPrimitives.WriteUInt16BigEndian(destination6, value7);
			}
			return destination6.ToArray();
		}
		case "s16":
		{
			Span<byte> destination4 = stackalloc byte[2];
			short value5 = short.Parse(value, CultureInfo.InvariantCulture);
			if (littleEndian)
			{
				BinaryPrimitives.WriteInt16LittleEndian(destination4, value5);
			}
			else
			{
				BinaryPrimitives.WriteInt16BigEndian(destination4, value5);
			}
			return destination4.ToArray();
		}
		case "u32":
		{
			Span<byte> destination7 = stackalloc byte[4];
			uint value8 = ParseUInt32(value);
			if (littleEndian)
			{
				BinaryPrimitives.WriteUInt32LittleEndian(destination7, value8);
			}
			else
			{
				BinaryPrimitives.WriteUInt32BigEndian(destination7, value8);
			}
			return destination7.ToArray();
		}
		case "s32":
		{
			Span<byte> destination2 = stackalloc byte[4];
			int value3 = int.Parse(value, CultureInfo.InvariantCulture);
			if (littleEndian)
			{
				BinaryPrimitives.WriteInt32LittleEndian(destination2, value3);
			}
			else
			{
				BinaryPrimitives.WriteInt32BigEndian(destination2, value3);
			}
			return destination2.ToArray();
		}
		case "u64":
		{
			Span<byte> destination3 = stackalloc byte[8];
			ulong value4 = ParseUInt64(value);
			if (littleEndian)
			{
				BinaryPrimitives.WriteUInt64LittleEndian(destination3, value4);
			}
			else
			{
				BinaryPrimitives.WriteUInt64BigEndian(destination3, value4);
			}
			return destination3.ToArray();
		}
		case "s64":
		{
			Span<byte> destination8 = stackalloc byte[8];
			long value9 = long.Parse(value, CultureInfo.InvariantCulture);
			if (littleEndian)
			{
				BinaryPrimitives.WriteInt64LittleEndian(destination8, value9);
			}
			else
			{
				BinaryPrimitives.WriteInt64BigEndian(destination8, value9);
			}
			return destination8.ToArray();
		}
		case "f32":
		{
			Span<byte> destination5 = stackalloc byte[4];
			int value6 = BitConverter.SingleToInt32Bits(float.Parse(value, CultureInfo.InvariantCulture));
			if (littleEndian)
			{
				BinaryPrimitives.WriteInt32LittleEndian(destination5, value6);
			}
			else
			{
				BinaryPrimitives.WriteInt32BigEndian(destination5, value6);
			}
			return destination5.ToArray();
		}
		case "f64":
		{
			Span<byte> destination = stackalloc byte[8];
			long value2 = BitConverter.DoubleToInt64Bits(double.Parse(value, CultureInfo.InvariantCulture));
			if (littleEndian)
			{
				BinaryPrimitives.WriteInt64LittleEndian(destination, value2);
			}
			else
			{
				BinaryPrimitives.WriteInt64BigEndian(destination, value2);
			}
			return destination.ToArray();
		}
		case "ascii":
			return Encoding.ASCII.GetBytes(value);
		case "hex":
			return ParseHex(value);
		default:
			throw new InvalidOperationException("Unknown type.");
		}
	}

	public static byte[] ParseHexPattern(string text)
	{
		string text2 = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace("-", "")
			.Replace("_", "");
		if (text2.Length % 2 != 0)
		{
			throw new InvalidOperationException("Hex pattern must have even length.");
		}
		byte[] array = new byte[text2.Length / 2];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = Convert.ToByte(text2.Substring(i * 2, 2), 16);
		}
		return array;
	}

	private static ushort ParseUInt16(string value)
	{
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return ushort.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}
		return ushort.Parse(value, CultureInfo.InvariantCulture);
	}

	private static uint ParseUInt32(string value)
	{
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}
		return uint.Parse(value, CultureInfo.InvariantCulture);
	}

	private static ulong ParseUInt64(string value)
	{
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			return ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}
		return ulong.Parse(value, CultureInfo.InvariantCulture);
	}

	private static byte[] ParseHex(string hex)
	{
		if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			hex = hex.Substring(2);
		}
		hex = hex.Replace(" ", "").Replace("-", "").Replace("_", "");
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

	private static string ConvertToHex(ReadOnlySpan<byte> data)
	{
		char[] array = new char[data.Length * 2];
		for (int i = 0; i < data.Length; i++)
		{
			byte b = data[i];
			array[i * 2] = GetHex((byte)(b >> 4));
			array[i * 2 + 1] = GetHex((byte)(b & 0xF));
		}
		return new string(array);
	}

	private static char GetHex(byte value)
	{
		return (char)((value < 10) ? (48 + value) : (65 + (value - 10)));
	}
}
