using System;
using System.IO;
using NoDev.Common.IO;

namespace NoDev.Xdbf.Records
{
    public class SyncInfoRecord
    {
        public readonly ulong LastSync;
        public ulong NextSync;
        public readonly DateTime ServerSyncTime;

        public SyncInfoRecord()
        {
            this.NextSync = 1;
            ServerSyncTime = DateTime.FromFileTimeUtc(0);
        }

        public SyncInfoRecord(byte[] data)
        {
            var io = new EndianIO(data, EndianType.Big);
            this.NextSync = io.ReadUInt64();
            this.LastSync = io.ReadUInt64();
            this.ServerSyncTime = DateTime.FromFileTime(io.ReadInt64());
            io.Close();
        }

        public byte[] ToArray()
        {
            var io = new EndianIO(new MemoryStream(0x18), EndianType.Big);
            io.Write(this.NextSync);
            io.Write(this.LastSync);
            io.Write(this.ServerSyncTime.ToFileTime());
            io.Close();

            return io.ToArray();
        }
    }
}
