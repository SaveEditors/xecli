using System;
using System.Buffers.Binary;
using System.IO;

namespace Xbox360.Remote.Cli.God;

internal static class Endian
{
	public static ushort ReadUInt16BE(Stream stream)
	{
		Span<byte> span = stackalloc byte[2];
		stream.ReadExactly(span);
		return BinaryPrimitives.ReadUInt16BigEndian(span);
	}

	public static ushort ReadUInt16LE(Stream stream)
	{
		Span<byte> span = stackalloc byte[2];
		stream.ReadExactly(span);
		return BinaryPrimitives.ReadUInt16LittleEndian(span);
	}

	public static uint ReadUInt32BE(Stream stream)
	{
		Span<byte> span = stackalloc byte[4];
		stream.ReadExactly(span);
		return BinaryPrimitives.ReadUInt32BigEndian(span);
	}

	public static uint ReadUInt32LE(Stream stream)
	{
		Span<byte> span = stackalloc byte[4];
		stream.ReadExactly(span);
		return BinaryPrimitives.ReadUInt32LittleEndian(span);
	}

	public static void WriteUInt16BE(byte[] buffer, int offset, ushort value)
	{
		BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), value);
	}

	public static void WriteUInt32BE(byte[] buffer, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), value);
	}

	public static void WriteUInt32LE(byte[] buffer, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
	}
}
