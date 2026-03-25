using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class IsoReader
{
	public const uint SectorSize = 2048u;

	private readonly Stream reader;

	public VolumeDescriptor Volume { get; }

	public DirectoryTable DirectoryTable { get; }

	private IsoReader(Stream reader, VolumeDescriptor volume, DirectoryTable table)
	{
		this.reader = reader;
		Volume = volume;
		DirectoryTable = table;
	}

	public static IsoReader Read(Stream stream)
	{
		VolumeDescriptor volume = VolumeDescriptor.Read(stream);
		DirectoryTable table = Xbox360.Remote.Cli.God.DirectoryTable.ReadRoot(stream, volume);
		return new IsoReader(stream, volume, table);
	}

	public Stream? GetEntry(WindowsPath path)
	{
		DirectoryEntry directoryEntry = null;
		DirectoryTable directoryTable = DirectoryTable;
		foreach (string component in path.Components)
		{
			directoryEntry = directoryTable?.GetEntry(component);
			directoryTable = directoryEntry?.Subdirectory;
		}
		if (directoryEntry == null)
		{
			return null;
		}
		ulong offset = Volume.RootOffset + directoryEntry.Sector * Volume.SectorSize;
		reader.Seek((long)offset, SeekOrigin.Begin);
		return reader;
	}

	public ulong GetMaxUsedPrefixSize()
	{
		return DirectoryTable.GetMaxUsedPrefixSize();
	}
}
