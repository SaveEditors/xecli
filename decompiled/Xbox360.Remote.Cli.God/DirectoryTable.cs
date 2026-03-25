using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Xbox360.Remote.Cli.God;

internal sealed class DirectoryTable
{
	public uint Sector { get; }

	public uint Size { get; }

	public List<DirectoryEntry> Entries { get; }

	private DirectoryTable(uint sector, uint size, List<DirectoryEntry> entries)
	{
		Sector = sector;
		Size = size;
		Entries = entries;
	}

	public static DirectoryTable ReadRoot(Stream stream, VolumeDescriptor volume)
	{
		return Read(stream, volume, volume.RootDirectorySector, volume.RootDirectorySize);
	}

	internal static DirectoryTable Read(Stream stream, VolumeDescriptor volume, uint sector, uint size)
	{
		List<DirectoryEntry> list = new List<DirectoryEntry>();
		uint num = (size + 2048 - 1) / 2048;
		for (uint num2 = 0u; num2 < num; num2++)
		{
			ulong offset = (sector + num2) * volume.SectorSize + volume.RootOffset;
			stream.Seek((long)offset, SeekOrigin.Begin);
			while (true)
			{
				DirectoryEntry directoryEntry = DirectoryEntry.Read(stream, volume);
				if (directoryEntry == null)
				{
					break;
				}
				list.Add(directoryEntry);
			}
		}
		return new DirectoryTable(sector, size, list);
	}

	public DirectoryEntry? GetEntry(string name)
	{
		return Entries.FirstOrDefault((DirectoryEntry e) => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	public ulong GetMaxUsedPrefixSize()
	{
		ulong num = 0uL;
		foreach (DirectoryEntry entry in Entries)
		{
			ulong num2 = (ulong)((long)entry.Sector * 2048L + entry.Size);
			if (entry.Subdirectory != null)
			{
				num2 = Math.Max(num2, entry.Subdirectory.GetMaxUsedPrefixSize());
			}
			if (num2 > num)
			{
				num = num2;
			}
		}
		return num;
	}
}
