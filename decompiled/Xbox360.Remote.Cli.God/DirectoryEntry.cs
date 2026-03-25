using System.IO;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal sealed class DirectoryEntry
{
	public string Name { get; }

	public uint Sector { get; }

	public uint Size { get; }

	public DirectoryEntryAttributes Attributes { get; }

	public DirectoryTable? Subdirectory { get; }

	private DirectoryEntry(string name, uint sector, uint size, DirectoryEntryAttributes attributes, DirectoryTable? subdirectory)
	{
		Name = name;
		Sector = sector;
		Size = size;
		Attributes = attributes;
		Subdirectory = subdirectory;
	}

	public static DirectoryEntry? Read(Stream stream, VolumeDescriptor volume)
	{
		ushort num = Endian.ReadUInt16LE(stream);
		ushort num2 = Endian.ReadUInt16LE(stream);
		if (num == ushort.MaxValue || num2 == ushort.MaxValue)
		{
			return null;
		}
		uint sector = Endian.ReadUInt32LE(stream);
		uint num3 = Endian.ReadUInt32LE(stream);
		if (num3 == 0)
		{
			return null;
		}
		int num4 = stream.ReadByte();
		if (num4 < 0)
		{
			return null;
		}
		DirectoryEntryAttributes directoryEntryAttributes = (DirectoryEntryAttributes)num4;
		byte[] array = new byte[stream.ReadByte()];
		stream.ReadExactly(array);
		string name = Encoding.ASCII.GetString(array);
		long num5 = (4 - stream.Position % 4) % 4;
		if (num5 > 0)
		{
			stream.Seek(num5, SeekOrigin.Current);
		}
		DirectoryTable subdirectory = null;
		if (directoryEntryAttributes.HasFlag(DirectoryEntryAttributes.Directory))
		{
			long position = stream.Position;
			subdirectory = DirectoryTable.Read(stream, volume, sector, num3);
			stream.Seek(position, SeekOrigin.Begin);
		}
		return new DirectoryEntry(name, sector, num3, directoryEntryAttributes, subdirectory);
	}
}
