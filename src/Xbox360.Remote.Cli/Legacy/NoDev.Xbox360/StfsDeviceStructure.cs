using System.IO;
using System.Reflection;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public struct StfsCreatePacket
    {
        public string DeviceName;
        public long BackingFileOffset;
        public ulong BackingMaximumVolumeSize;
        public StfsVolumeDescriptor VolumeDescriptor;
        public uint DeviceExtensionSize;
        public byte BlockCacheElementCount;
        public byte TitleOwned;
        public byte BackingFilePresized;
        public byte DeviceCharacteristics;
    }

    public class StfsVolumeDescriptor
    {
        public byte DescriptorLength;
        public byte Version;

        [Obfuscation]
        public readonly byte ReadOnlyFormat;

        public byte RootActiveIndex;
        public byte DirectoryOverallocated;
        public byte DirectoryIndexBoundsValid;
        public ushort DirectoryAllocationBlocks;
        public uint DirectoryFirstBlockNumber;
        public byte[] RootHash; // 20
        public uint NumberOfTotalBlocks;
        public uint NumberOfFreeBlocks;
        public byte Flags;

        public StfsVolumeDescriptor()
        {
            this.DescriptorLength = 0x24;
            this.RootHash = new byte[0x14];
        }

        public StfsVolumeDescriptor(EndianIO io)
        {
            DescriptorLength = io.ReadByte();
            Version = io.ReadByte();

            Flags = io.ReadByte();
            ReadOnlyFormat = (byte)(Flags & 1);
            RootActiveIndex = (byte)((Flags >> 1) & 1);
            DirectoryOverallocated = (byte)((Flags >> 2) & 1);
            DirectoryIndexBoundsValid = (byte)((Flags >> 3) & 1);

            io.Endianness = EndianType.Little;
            DirectoryAllocationBlocks = io.ReadUInt16();
            DirectoryFirstBlockNumber = io.ReadUInt24();
            io.Endianness = EndianType.Big;

            RootHash = io.ReadByteArray(20);
            NumberOfTotalBlocks = io.ReadUInt32();
            NumberOfFreeBlocks = io.ReadUInt32();
        }

        [Obfuscation]
        public byte[] ToArray()
        {
            var io = new EndianIO(new MemoryStream(), EndianType.Big);

            io.Write(DescriptorLength);
            io.Write(Version);

            Flags = (byte)((Flags & 0xf0) | ((ReadOnlyFormat & 1) | ((RootActiveIndex & 1) << 1)
                | ((DirectoryOverallocated & 1) << 2) | ((DirectoryIndexBoundsValid & 1) << 3)));

            io.Write(Flags);

            io.Endianness = EndianType.Little;
            io.Write(DirectoryAllocationBlocks);
            io.WriteUInt24(DirectoryFirstBlockNumber);
            io.Endianness = EndianType.Big;

            io.Write(RootHash);
            io.Write(NumberOfTotalBlocks);
            io.Write(NumberOfFreeBlocks);

            io.Close();
            return io.ToArray();
        }
    }
}
