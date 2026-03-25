using System.Text;

namespace Xbox360.Remote.Cli.God;

internal enum IsoType {
    Xgd3,
    Xgd2,
    Xgd1,
    Xsf
}

internal sealed class IsoReader {
    public const uint SectorSize = 0x800;

    public VolumeDescriptor Volume { get; }
    public DirectoryTable DirectoryTable { get; }
    private readonly Stream reader;

    private IsoReader(Stream reader, VolumeDescriptor volume, DirectoryTable table) {
        this.reader = reader;
        Volume = volume;
        DirectoryTable = table;
    }

    public static IsoReader Read(Stream stream) {
        VolumeDescriptor volume = VolumeDescriptor.Read(stream);
        DirectoryTable table = DirectoryTable.ReadRoot(stream, volume);
        return new IsoReader(stream, volume, table);
    }

    public Stream? GetEntry(WindowsPath path) {
        DirectoryEntry? entry = null;
        DirectoryTable? dir = DirectoryTable;

        foreach (string name in path.Components) {
            entry = dir?.GetEntry(name);
            dir = entry?.Subdirectory;
        }

        if (entry == null)
            return null;

        ulong position = Volume.RootOffset + (ulong) entry.Sector * Volume.SectorSize;
        reader.Seek((long) position, SeekOrigin.Begin);
        return reader;
    }

    public ulong GetMaxUsedPrefixSize() {
        return DirectoryTable.GetMaxUsedPrefixSize();
    }
}

internal sealed class WindowsPath {
    public IReadOnlyList<string> Components { get; }

    public WindowsPath(string path) {
        Components = path
            .Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }
}

internal sealed class VolumeDescriptor {
    public ulong RootOffset { get; }
    public ulong SectorSize { get; }
    public byte[] Identifier { get; }
    public uint RootDirectorySector { get; }
    public uint RootDirectorySize { get; }
    public byte[] ImageCreationTime { get; }
    public ulong VolumeSize { get; }
    public ulong VolumeSectors { get; }

    private VolumeDescriptor(
        ulong rootOffset,
        ulong sectorSize,
        byte[] identifier,
        uint rootDirectorySector,
        uint rootDirectorySize,
        byte[] imageCreationTime,
        ulong volumeSize,
        ulong volumeSectors) {
        RootOffset = rootOffset;
        SectorSize = sectorSize;
        Identifier = identifier;
        RootDirectorySector = rootDirectorySector;
        RootDirectorySize = rootDirectorySize;
        ImageCreationTime = imageCreationTime;
        VolumeSize = volumeSize;
        VolumeSectors = volumeSectors;
    }

    public static VolumeDescriptor Read(Stream stream) {
        IsoType isoType = DetectIsoType(stream) ?? throw new InvalidDataException("Unrecognized ISO format.");
        ulong rootOffset = GetRootOffset(isoType);
        stream.Seek((long) (0x20 * IsoReader.SectorSize + rootOffset), SeekOrigin.Begin);

        byte[] identifier = new byte[20];
        stream.ReadExactly(identifier);

        uint rootDirSector = Endian.ReadUInt32LE(stream);
        uint rootDirSize = Endian.ReadUInt32LE(stream);

        byte[] imageCreationTime = new byte[8];
        stream.ReadExactly(imageCreationTime);

        ulong length = (ulong) stream.Length;
        ulong volumeSize = length - rootOffset;
        ulong volumeSectors = volumeSize / IsoReader.SectorSize;

        return new VolumeDescriptor(rootOffset, IsoReader.SectorSize, identifier, rootDirSector, rootDirSize, imageCreationTime, volumeSize, volumeSectors);
    }

    private static IsoType? DetectIsoType(Stream stream) {
        if (Check(stream, IsoType.Xsf))
            return IsoType.Xsf;
        if (Check(stream, IsoType.Xgd2))
            return IsoType.Xgd2;
        if (Check(stream, IsoType.Xgd1))
            return IsoType.Xgd1;
        if (Check(stream, IsoType.Xgd3))
            return IsoType.Xgd3;
        return null;
    }

    private static bool Check(Stream stream, IsoType isoType) {
        ulong rootOffset = GetRootOffset(isoType);
        ulong pos = 0x20 * IsoReader.SectorSize + rootOffset;
        if ((ulong) stream.Length < pos + 20)
            return false;

        stream.Seek((long) pos, SeekOrigin.Begin);
        byte[] buf = new byte[20];
        stream.ReadExactly(buf);
        return Encoding.ASCII.GetString(buf) == "MICROSOFT*XBOX*MEDIA";
    }

    private static ulong GetRootOffset(IsoType isoType) {
        return isoType switch {
            IsoType.Xgd3 => 0x2080000,
            IsoType.Xgd2 => 0xFD90000,
            IsoType.Xgd1 => 0x18300000,
            _ => 0
        };
    }
}

internal sealed class DirectoryTable {
    public uint Sector { get; }
    public uint Size { get; }
    public List<DirectoryEntry> Entries { get; }

    private DirectoryTable(uint sector, uint size, List<DirectoryEntry> entries) {
        Sector = sector;
        Size = size;
        Entries = entries;
    }

    public static DirectoryTable ReadRoot(Stream stream, VolumeDescriptor volume) {
        return Read(stream, volume, volume.RootDirectorySector, volume.RootDirectorySize);
    }

    internal static DirectoryTable Read(Stream stream, VolumeDescriptor volume, uint sector, uint size) {
        List<DirectoryEntry> entries = new List<DirectoryEntry>();
        uint sectorCount = (size + IsoReader.SectorSize - 1) / IsoReader.SectorSize;
        for (uint sectorIndex = 0; sectorIndex < sectorCount; sectorIndex++) {
            ulong sectorPosition = ((ulong) (sector + sectorIndex) * volume.SectorSize) + volume.RootOffset;
            stream.Seek((long) sectorPosition, SeekOrigin.Begin);
            while (true) {
                DirectoryEntry? entry = DirectoryEntry.Read(stream, volume);
                if (entry == null)
                    break;
                entries.Add(entry);
            }
        }

        return new DirectoryTable(sector, size, entries);
    }

    public DirectoryEntry? GetEntry(string name) {
        return Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public ulong GetMaxUsedPrefixSize() {
        ulong max = 0;
        foreach (DirectoryEntry entry in Entries) {
            ulong value = (ulong) entry.Sector * IsoReader.SectorSize + entry.Size;
            if (entry.Subdirectory != null) {
                value = Math.Max(value, entry.Subdirectory.GetMaxUsedPrefixSize());
            }
            if (value > max)
                max = value;
        }
        return max;
    }
}

internal sealed class DirectoryEntry {
    public string Name { get; }
    public uint Sector { get; }
    public uint Size { get; }
    public DirectoryEntryAttributes Attributes { get; }
    public DirectoryTable? Subdirectory { get; }

    private DirectoryEntry(string name, uint sector, uint size, DirectoryEntryAttributes attributes, DirectoryTable? subdirectory) {
        Name = name;
        Sector = sector;
        Size = size;
        Attributes = attributes;
        Subdirectory = subdirectory;
    }

    public static DirectoryEntry? Read(Stream stream, VolumeDescriptor volume) {
        ushort subtreeLeft = Endian.ReadUInt16LE(stream);
        ushort subtreeRight = Endian.ReadUInt16LE(stream);
        if (subtreeLeft == 0xFFFF || subtreeRight == 0xFFFF)
            return null;

        uint sector = Endian.ReadUInt32LE(stream);
        uint size = Endian.ReadUInt32LE(stream);
        if (size == 0)
            return null;

        int attr = stream.ReadByte();
        if (attr < 0)
            return null;
        DirectoryEntryAttributes attributes = (DirectoryEntryAttributes) attr;
        int nameLength = stream.ReadByte();

        byte[] nameBytes = new byte[nameLength];
        stream.ReadExactly(nameBytes);
        string name = Encoding.ASCII.GetString(nameBytes);

        long alignment = (4 - (stream.Position % 4)) % 4;
        if (alignment > 0)
            stream.Seek(alignment, SeekOrigin.Current);

        DirectoryTable? subdirectory = null;
        if (attributes.HasFlag(DirectoryEntryAttributes.Directory)) {
            long returnPos = stream.Position;
            subdirectory = DirectoryTable.Read(stream, volume, sector, size);
            stream.Seek(returnPos, SeekOrigin.Begin);
        }

        return new DirectoryEntry(name, sector, size, attributes, subdirectory);
    }
}

[Flags]
internal enum DirectoryEntryAttributes : byte {
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    Directory = 0x10,
    Archive = 0x20,
    Normal = 0x80
}
