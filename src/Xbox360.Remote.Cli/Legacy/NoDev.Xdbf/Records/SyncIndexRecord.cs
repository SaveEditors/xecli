using System.Collections.Generic;
using System.IO;
using NoDev.Common.IO;

namespace NoDev.Xdbf.Records
{
    public sealed class SyncIndexRecord
    {
        public class SyncEntry
        {
            public ulong ID;
            public ulong SyncID;

            public SyncEntry(ulong id, ulong syncId)
            {
                this.ID = id;
                this.SyncID = syncId;
            }
        }

        private const int SyncEntrySize = 0x10;
        public readonly List<SyncEntry> Entries;

        public SyncIndexRecord(ulong id, ulong syncId)
        {
            this.Entries = new List<SyncEntry>(1);
            this.Entries.Add(new SyncEntry(id, syncId));
        }

        public SyncIndexRecord(byte[] data)
        {
            if (data.Length % SyncEntrySize != 0)
                throw new XdbfException(string.Format("Invalid index record length ({0:X8}).", data.Length));

            var io = new EndianIO(data, EndianType.Big);

            int entryCount = data.Length / SyncEntrySize;

            this.Entries = new List<SyncEntry>(entryCount);

            for (int x = 0; x < entryCount; x++)
                this.Entries.Add(new SyncEntry(io.ReadUInt64(), io.ReadUInt64()));

            io.Close();
        }

        private SyncEntry FindFreeEntry()
        {
            return this.Entries.Find(e => e.ID == 0);
        }

        private SyncEntry FindEntry(ulong id)
        {
            return this.Entries.Find(e => e.ID == id);
        }

        public void SetEntry(ulong id, ulong syncId)
        {
            var e = FindEntry(id);

            if (e == null)
                this.AddEntry(id, syncId);
            else
            {
                e.SyncID = syncId;

                this.Entries.Remove(e);
                this.Entries.Add(e);
            }
        }

        /*public void SetEntry(ulong id, SyncInfoRecord syncInfo)
        {
            var e = FindEntry(id);

            if (e == null)
                this.AddEntry(id, syncInfo.NextSync++);
            else
            {
                if (e.SyncID != syncInfo.NextSync - 1)
                    e.SyncID = syncInfo.NextSync++;

                this.Entries.Remove(e);
                this.Entries.Add(e);
            }
        }*/

        private void AddEntry(ulong id, ulong syncId)
        {
            var e = FindFreeEntry();

            if (e == null)
                this.Entries.Add(new SyncEntry(id, syncId));
            else
            {
                e.ID = id;
                e.SyncID = syncId;
            }
        }

        public byte[] ToArray()
        {
            var io = new EndianIO(new MemoryStream(this.Entries.Count * SyncEntrySize), EndianType.Big);
            foreach (var rec in this.Entries)
            {
                io.Write(rec.ID);
                io.Write(rec.SyncID);
            }
            io.Close();

            return io.ToArray();
        }
    }
}
