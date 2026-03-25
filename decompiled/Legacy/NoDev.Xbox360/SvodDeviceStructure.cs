using System;
using System.IO;
using NoDev.Common.IO;

namespace NoDev.Xbox360
{
    public class SvodDeviceDescriptor
    {
        public readonly byte DescriptorLength;
        public readonly byte BlockCacheElementCount;
        public readonly byte WorkerThreadProcessor;
        public readonly byte WorkerThreadPriority;
        public SvodHashEntry FirstFragmentHashEntry;
        public readonly SvodDeviceFeatures Features;
        public readonly uint NumberOfDataBlocks;
        public readonly uint StartingDataBlock;
        private readonly byte[] _reserved;

        public SvodDeviceDescriptor(EndianIO io)
        {
            this.DescriptorLength = io.ReadByte();
            this.BlockCacheElementCount = io.ReadByte();
            this.WorkerThreadProcessor = io.ReadByte();
            this.WorkerThreadPriority = io.ReadByte();
            this.FirstFragmentHashEntry.Hash = io.ReadByteArray(0x14);
            this.Features = new SvodDeviceFeatures(io.ReadByte());
            this.NumberOfDataBlocks = io.ReadUInt24();

            io.Endianness = EndianType.Little;
            this.StartingDataBlock = io.ReadUInt24();
            io.Endianness = EndianType.Big;

            this._reserved = io.ReadByteArray(5);
        }

        public byte[] ToArray()
        {
            var io = new EndianIO(new MemoryStream(), EndianType.Big);
            io.Write(this.DescriptorLength);
            io.Write(this.BlockCacheElementCount);
            io.Write(this.WorkerThreadProcessor);
            io.Write(this.WorkerThreadPriority);
            io.Write(this.FirstFragmentHashEntry.Hash);
            io.Write(this.Features.Flags);
            io.Write(this.NumberOfDataBlocks);
            io.Write(this.StartingDataBlock);
            io.Write(this._reserved);
            io.Close();

            return io.ToArray();
        }
    }

    public struct SvodCreatePacket
    {
        public byte[] FsCache;
        public SvodDeviceDescriptor Descriptor;
    }

    public struct SvodHashEntry
    {
        public byte[] Hash;

        public bool IsBlockLoaded
        {
            get { return !Hash.IsNull(); }
        }
    }

    public class SvodDeviceFeatures
    {
        public readonly byte MustBeZeroForFutureUsage;
        public readonly bool HasEnhancedGDFLayout;
        public readonly byte ShouldBeZeroForDownLevelClients;

        public readonly byte Flags;

        public SvodDeviceFeatures(byte flags)
        {
            this.Flags = flags;
            this.MustBeZeroForFutureUsage = (byte)(flags & 0x3f);
            this.HasEnhancedGDFLayout = ((flags >> 6) & 0x01) != 0;
            this.ShouldBeZeroForDownLevelClients = (byte)((flags >> 7) & 0x01);
        }
    }
}
