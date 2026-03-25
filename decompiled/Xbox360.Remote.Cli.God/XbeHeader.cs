using System;
using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class XbeHeader
{
	public TitleExecutionInfo? ExecutionInfo { get; }

	private XbeHeader(TitleExecutionInfo? executionInfo)
	{
		ExecutionInfo = executionInfo;
	}

	public static XbeHeader Read(Stream stream)
	{
		Span<byte> buffer = stackalloc byte[4];
		stream.ReadExactly(buffer);
		if (buffer[0] != 88 || buffer[1] != 66 || buffer[2] != 69 || buffer[3] != 72)
		{
			throw new InvalidDataException("Missing XBEH magic bytes.");
		}
		stream.Seek(256L, SeekOrigin.Current);
		uint num = Endian.ReadUInt32LE(stream);
		stream.Seek(16L, SeekOrigin.Current);
		uint num2 = Endian.ReadUInt32LE(stream);
		long num3 = stream.Position - 284;
		uint num4 = num2 - num;
		stream.Seek(num3 + num4, SeekOrigin.Begin);
		return new XbeHeader(TitleExecutionInfo.FromXbe(stream));
	}
}
