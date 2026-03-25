using System.IO;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal sealed class VolumeDescriptor
{
	public ulong RootOffset { get; }

	public ulong SectorSize { get; }

	public byte[] Identifier { get; }

	public uint RootDirectorySector { get; }

	public uint RootDirectorySize { get; }

	public byte[] ImageCreationTime { get; }

	public ulong VolumeSize { get; }

	public ulong VolumeSectors { get; }

	private VolumeDescriptor(ulong rootOffset, ulong sectorSize, byte[] identifier, uint rootDirectorySector, uint rootDirectorySize, byte[] imageCreationTime, ulong volumeSize, ulong volumeSectors)
	{
		RootOffset = rootOffset;
		SectorSize = sectorSize;
		Identifier = identifier;
		RootDirectorySector = rootDirectorySector;
		RootDirectorySize = rootDirectorySize;
		ImageCreationTime = imageCreationTime;
		VolumeSize = volumeSize;
		VolumeSectors = volumeSectors;
	}

	public static VolumeDescriptor Read(Stream stream)
	{
		ulong rootOffset = GetRootOffset(DetectIsoType(stream) ?? throw new InvalidDataException("Unrecognized ISO format."));
		stream.Seek((long)(65536 + rootOffset), SeekOrigin.Begin);
		byte[] array = new byte[20];
		stream.ReadExactly(array);
		uint rootDirectorySector = Endian.ReadUInt32LE(stream);
		uint rootDirectorySize = Endian.ReadUInt32LE(stream);
		byte[] array2 = new byte[8];
		stream.ReadExactly(array2);
		ulong num = (ulong)stream.Length - rootOffset;
		ulong volumeSectors = num / 2048;
		return new VolumeDescriptor(rootOffset, 2048uL, array, rootDirectorySector, rootDirectorySize, array2, num, volumeSectors);
	}

	private static IsoType? DetectIsoType(Stream stream)
	{
		if (Check(stream, IsoType.Xsf))
		{
			return IsoType.Xsf;
		}
		if (Check(stream, IsoType.Xgd2))
		{
			return IsoType.Xgd2;
		}
		if (Check(stream, IsoType.Xgd1))
		{
			return IsoType.Xgd1;
		}
		if (Check(stream, IsoType.Xgd3))
		{
			return IsoType.Xgd3;
		}
		return null;
	}

	private static bool Check(Stream stream, IsoType isoType)
	{
		ulong rootOffset = GetRootOffset(isoType);
		ulong num = 65536 + rootOffset;
		if ((ulong)stream.Length < num + 20)
		{
			return false;
		}
		stream.Seek((long)num, SeekOrigin.Begin);
		byte[] array = new byte[20];
		stream.ReadExactly(array);
		return Encoding.ASCII.GetString(array) == "MICROSOFT*XBOX*MEDIA";
	}

	private static ulong GetRootOffset(IsoType isoType)
	{
		return isoType switch
		{
			IsoType.Xgd3 => 34078720uL, 
			IsoType.Xgd2 => 265879552uL, 
			IsoType.Xgd1 => 405798912uL, 
			_ => 0uL, 
		};
	}
}
