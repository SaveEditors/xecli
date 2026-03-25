using System;
using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class XexHeader
{
	private const uint ExecutionIdField = 262150u;

	public TitleExecutionInfo? ExecutionInfo { get; }

	private XexHeader(TitleExecutionInfo? executionInfo)
	{
		ExecutionInfo = executionInfo;
	}

	public static XexHeader Read(Stream stream)
	{
		Span<byte> buffer = stackalloc byte[4];
		stream.ReadExactly(buffer);
		if (buffer[0] != 88 || buffer[1] != 69 || buffer[2] != 88 || buffer[3] != 50)
		{
			throw new InvalidDataException("Missing XEX2 magic bytes.");
		}
		long num = stream.Position - 4;
		Endian.ReadUInt32BE(stream);
		Endian.ReadUInt32BE(stream);
		Endian.ReadUInt32BE(stream);
		Endian.ReadUInt32BE(stream);
		uint num2 = Endian.ReadUInt32BE(stream);
		TitleExecutionInfo executionInfo = null;
		for (uint num3 = 0u; num3 < num2; num3++)
		{
			uint num4 = Endian.ReadUInt32BE(stream);
			uint num5 = Endian.ReadUInt32BE(stream);
			if (num4 == 262150)
			{
				long position = stream.Position;
				stream.Seek(num + num5, SeekOrigin.Begin);
				executionInfo = TitleExecutionInfo.FromXex(stream);
				stream.Seek(position, SeekOrigin.Begin);
			}
		}
		return new XexHeader(executionInfo);
	}
}
