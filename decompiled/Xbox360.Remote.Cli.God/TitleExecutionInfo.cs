using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class TitleExecutionInfo
{
	public uint MediaId { get; }

	public uint Version { get; }

	public uint BaseVersion { get; }

	public uint TitleId { get; }

	public byte Platform { get; }

	public byte ExecutableType { get; }

	public byte DiscNumber { get; }

	public byte DiscCount { get; }

	private TitleExecutionInfo(uint mediaId, uint version, uint baseVersion, uint titleId, byte platform, byte executableType, byte discNumber, byte discCount)
	{
		MediaId = mediaId;
		Version = version;
		BaseVersion = baseVersion;
		TitleId = titleId;
		Platform = platform;
		ExecutableType = executableType;
		DiscNumber = discNumber;
		DiscCount = discCount;
	}

	public static TitleExecutionInfo FromXex(Stream stream)
	{
		uint mediaId = Endian.ReadUInt32BE(stream);
		uint version = Endian.ReadUInt32BE(stream);
		uint baseVersion = Endian.ReadUInt32BE(stream);
		uint titleId = Endian.ReadUInt32BE(stream);
		int num = stream.ReadByte();
		int num2 = stream.ReadByte();
		int num3 = stream.ReadByte();
		int num4 = stream.ReadByte();
		if (num < 0 || num2 < 0 || num3 < 0 || num4 < 0)
		{
			throw new EndOfStreamException();
		}
		return new TitleExecutionInfo(mediaId, version, baseVersion, titleId, (byte)num, (byte)num2, (byte)num3, (byte)num4);
	}

	public static TitleExecutionInfo FromXbe(Stream stream)
	{
		stream.Seek(8L, SeekOrigin.Current);
		uint titleId = Endian.ReadUInt32LE(stream);
		stream.Seek(164L, SeekOrigin.Current);
		uint version = Endian.ReadUInt32LE(stream);
		return new TitleExecutionInfo(0u, version, 0u, titleId, 0, 0, 1, 1);
	}
}
