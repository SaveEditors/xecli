using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NoDev.Common.IO;
using NoDev.Xdbf.Records;

namespace NoDev.Xdbf
{
    using SyncRecord = Tuple<SyncInfoRecord, SyncIndexRecord>;

    public enum Namespace : ushort
    {
        Achievements = 0x0001,
        Images = 0x0002,
        Settings = 0x0003,
        Titles = 0x0004,
        Strings = 0x0005,
        Avatar = 0x0006
    }

    public enum DataFileOrigin
    {
        Profile,
        PEC
    }

    public class DataFile
    {
        private EndianIO _io;
        private readonly string _fileName;

        private readonly List<DataFileRecord> _records;
        private readonly List<FreeRecord> _freeRecords;

        private readonly Dictionary<Namespace, SyncRecord> _syncRecords;
        private readonly ulong _syncInfoId, _syncIndexId;
        private readonly int _expansionRecords;

        private readonly ushort _version, _reserved;

        private int _maxRecords, _maxFreeRecords, _freeRecordsOffset, _dataOffset;

        private bool _modified;

        private const uint
            Magic = 0x58444246;

        private const ushort
            DefaultVersion = 0x0001,
            DefaultReserved = 0x0000;

        private const int
            HeaderSize = 0x18,
            RecordSize = 0x12,
            FreeRecordSize = 0x08;

        private DataFile(DataFileOrigin origin)
        {
            switch (origin)
            {
                case DataFileOrigin.Profile:
                    this._syncInfoId = 0x0200000000;
                    this._syncIndexId = 0x0100000000;
                    this._expansionRecords = 0x200;
                    break;
                case DataFileOrigin.PEC:
                    this._syncInfoId = 0x0000000002;
                    this._syncIndexId = 0x0000000001;
                    this._expansionRecords = 0x24;
                    break;
            }

            this._syncRecords = new Dictionary<Namespace, SyncRecord>();
        }

        public DataFile(string outPath, int maxRecords, DataFileOrigin origin)
            : this(origin)
        {
            this._fileName = outPath;

            this._version = DefaultVersion;
            this._reserved = DefaultReserved;

            this._maxRecords = maxRecords;
            this._maxFreeRecords = maxRecords;

            this._records = new List<DataFileRecord>(maxRecords);
            this._freeRecords = new List<FreeRecord>(1);

            this._freeRecords.Add(new FreeRecord(0, 0));

            this.RecalculateOffsets();

            this._io = new EndianIO(new MemoryStream(this._dataOffset), EndianType.Big);
            this._io.SetLength(this._dataOffset);

            this._modified = true;
        }

        public DataFile(string inPath, DataFileOrigin origin)
            : this(origin)
        {
            this._fileName = inPath;

            byte[] fileArr = File.ReadAllBytes(inPath);

            var io = new EndianIO(new MemoryStream(fileArr.Length), EndianType.Big);

            io.Write(fileArr);

            io.Position = 0;

            if (io.ReadUInt32() != Magic)
                throw new XdbfException("Invalid data file signature.");

            _version = io.ReadUInt16();
            if (_version != 0x01)
                throw new XdbfException(string.Format("Unsupported data file version [0x{0:X4}].", _version));

            _reserved = io.ReadUInt16();

            this._maxRecords = io.ReadInt32();
            int entryCount = io.ReadInt32();

            this._maxFreeRecords = io.ReadInt32();
            int freeEntryCount = io.ReadInt32();

            if (entryCount > _maxRecords)
                throw new XdbfException("Entry count exceeds limit in data file.");

            if (freeEntryCount > _maxFreeRecords)
                throw new XdbfException("Free entry count exceeds limit in data file.");

            this.RecalculateOffsets();


            // Read records
            this._records = new List<DataFileRecord>(_maxRecords);

            for (uint x = 0; x < entryCount; x++)
                this._records.Add(new DataFileRecord(io));


            // Read free records
            io.Position = _freeRecordsOffset;

            this._freeRecords = new List<FreeRecord>(_maxFreeRecords);

            for (var x = 0; x < freeEntryCount; x++)
                this._freeRecords.Add(new FreeRecord(io));

            this._io = io;
        }

        public static byte[] GetSingleRecord(string inFile, Namespace ns, ulong id)
        {
            var io = new EndianIO(inFile, EndianType.Big);

            io.Position = 0x08;

            var maxRecords = io.ReadInt32();
            var entryCount = io.ReadInt32();

            var tIo = new EndianIO(io.ReadByteArray(entryCount * RecordSize), EndianType.Big);

            var maxFreeRecords = tIo.ReadInt32();

            tIo.Position += 0x04;

            while (entryCount-- > -1)
            {
                var tNs = (Namespace)tIo.ReadInt16();
                var tId = tIo.ReadUInt64();

                if (tNs != ns || tId != id)
                {
                    tIo.Position += 0x08;
                    continue;
                }

                io.Position = CalculateDataOffset(maxRecords, maxFreeRecords) + tIo.ReadInt32();

                var data = io.ReadByteArray(tIo.ReadInt32());

                io.Close();
                tIo.Close();

                return data;
            }

            tIo.Close();
            io.Close();

            return null;
        }

        public void Close()
        {
            this.Flush();
            this._io.Close();
        }

        public void Flush()
        {
            if (!this._modified)
                return;

            foreach (var syncRec in this._syncRecords)
            {
                this.UpdateData(syncRec.Key, _syncInfoId, syncRec.Value.Item1.ToArray());
                this.UpdateData(syncRec.Key, _syncIndexId, syncRec.Value.Item2.ToArray());
            }

            this.WriteHeader();

            this._io.Flush();

            File.WriteAllBytes(this._fileName, this._io.ToArray());

            this._modified = false;
        }

        private void RecalculateOffsets()
        {
            _freeRecordsOffset = CalculateFreeRecordsOffset(_maxRecords);
            _dataOffset = CalculateDataOffset(_maxRecords, _maxFreeRecords);
        }

        private static int CalculateFreeRecordsOffset(int maxRecords)
        {
            return HeaderSize + (maxRecords * RecordSize);
        }

        private static int CalculateDataOffset(int maxRecords, int maxFreeRecords)
        {
            return (maxRecords * RecordSize) + ((maxFreeRecords + 3) << 3);
        }

        private void WriteHeader()
        {
            var io = this._io;

            io.Position = 0;

            io.Write(Magic);
            io.Write(this._version);
            io.Write(this._reserved);
            io.Write(this._maxRecords);
            io.Write(this._records.Count);
            io.Write(this._maxFreeRecords);
            io.Write(this._freeRecords.Count);


            // Write records.
            this._records.Sort((x, y) => x.Namespace == y.Namespace ? x.ID.CompareTo(y.ID) : x.Namespace.CompareTo(y.Namespace));

            foreach (var record in this._records)
                record.Write(io);

            io.Write(new byte[(_maxRecords - _records.Count) * RecordSize]);


            // Write free records
            this._freeRecords.Sort((x, y) => x.Offset.CompareTo(y.Offset));

            var freeOffset = (int)(io.Length - this._dataOffset);

            var lastRecord = this._freeRecords.Last();
            lastRecord.Offset = freeOffset;
            lastRecord.Size = ~freeOffset;

            foreach (var freeRecord in this._freeRecords)
                freeRecord.Write(io);

            io.Write(new byte[(_maxFreeRecords - _freeRecords.Count)*FreeRecordSize]);
        }

        public IEnumerable<DataFileRecord> GetRecords(Namespace ns)
        {
            return this._records.FindAll(r => r.Namespace == ns && r.ID != this._syncInfoId && r.ID != this._syncIndexId);
        }

        public DataFileRecord GetRecord(Namespace ns, ulong id)
        {
            int recordIndex = this.GetRecordIndex(ns, id);

            if (recordIndex == -1)
                return null;

            return this._records[recordIndex];
        }

        public bool RecordExists(Namespace ns, ulong id)
        {
            return this.GetRecordIndex(ns, id) != -1;
        }

        private int GetRecordIndex(Namespace ns, ulong id)
        {
            return this._records.FindIndex(r => r.Namespace == ns && r.ID == id);
        }

        private FreeRecord FindFreeDataBlock(int dataSize)
        {
            return this._freeRecords.Find(r => dataSize <= r.Size);
        }

        private EndianIO SeekToData(DataFileRecord record)
        {
            this._io.Position = this._dataOffset + record.Offset;

            return this._io;
        }

        public byte[] GetData(Namespace ns, ulong id)
        {
            return this.GetData(this.GetRecord(ns, id));
        }

        public byte[] GetData(DataFileRecord record)
        {
            return this.SeekToData(record).ReadByteArray(record.Size);
        }

        private void Rebuild(int maxRecords, int maxFreeRecords)
        {
            int dataOffset = CalculateDataOffset(maxRecords, maxFreeRecords);

            var tempData = new EndianIO(new MemoryStream(dataOffset), EndianType.Big);

            tempData.Position = dataOffset;

            foreach (var rec in this._records)
            {
                var tempPosition = (int)tempData.Position - dataOffset;
                tempData.Write(this.GetData(rec));
                rec.Offset = tempPosition;
            }

            this._io.Close();

            this._io = tempData;

            this._records.Capacity = maxRecords;

            this._freeRecords.Clear();
            this._freeRecords.Capacity = maxFreeRecords;
            this._freeRecords.Add(new FreeRecord(0, 0));

            this._maxRecords = maxRecords;
            this._maxFreeRecords = maxFreeRecords;

            this.RecalculateOffsets();

            this._modified = true;
        }

        public void RemoveRecord(Namespace ns, ulong id)
        {
            this.RemoveRecord(this.GetRecord(ns, id));
        }

        public void RemoveRecord(DataFileRecord rec)
        {
            this._records.Remove(rec);

            int dataEnd = rec.Offset + rec.Size;

            if (dataEnd == this._io.Length)
                this._io.SetLength(dataEnd);
            else
            {
                if (this._freeRecords.Count + 1 > _maxFreeRecords)
                    this.Rebuild(_maxRecords, _maxFreeRecords);

                this._freeRecords.Add(new FreeRecord(rec.Offset, rec.Size));
            }
        }

        public void RemoveSyncRecord(Namespace ns, ulong id)
        {
            if (!this.IsPendingSync(ns, id))
                return;

            SyncInfoRecord syncInfo = this._syncRecords[ns].Item1;

            syncInfo.NextSync--;

            this.UpdateData(ns, _syncInfoId, syncInfo.ToArray());

            SyncIndexRecord syncIndex = this._syncRecords[ns].Item2;

            ulong syncId = syncIndex.Entries.First(e => e.ID == id).SyncID;

            foreach (var e in syncIndex.Entries.Where(e => e.SyncID > syncId))
                e.SyncID--;

            this.UpdateData(ns, _syncIndexId, syncIndex.ToArray());
        }

        public bool IsPendingSync(Namespace ns, ulong id)
        {
            SyncInfoRecord syncInfo;
            SyncIndexRecord syncIndex;

            if (this._syncRecords.ContainsKey(ns))
            {
                syncInfo = this._syncRecords[ns].Item1;
                syncIndex = this._syncRecords[ns].Item2;
            }
            else
            {
                if (!this.RecordExists(ns, _syncInfoId) || !this.RecordExists(ns, _syncIndexId))
                    return false;

                syncInfo = new SyncInfoRecord(this.GetData(ns, _syncInfoId));
                syncIndex = new SyncIndexRecord(this.GetData(ns, _syncIndexId));

                this._syncRecords.Add(ns, new SyncRecord(syncInfo, syncIndex));
            }

            var entry = syncIndex.Entries.FirstOrDefault(e => e.ID == id);

            if (entry == null)
                return false;

            return entry.SyncID > syncInfo.LastSync;
        }

        private void SyncRecord(Namespace ns, ulong id)
        {
            if (id == _syncInfoId || id == _syncIndexId)
                return;

            if (ns == Namespace.Images || ns == Namespace.Strings)
            {
                this._modified = true;
                return;
            }

            SyncInfoRecord syncInfo;
            SyncIndexRecord syncIndex;

            if (this._syncRecords.ContainsKey(ns))
            {
                syncInfo = this._syncRecords[ns].Item1;
                syncIndex = this._syncRecords[ns].Item2;
            }
            else
            {
                if (this.RecordExists(ns, _syncInfoId))
                    syncInfo = new SyncInfoRecord(this.GetData(ns, _syncInfoId));
                else
                {
                    syncInfo = new SyncInfoRecord();
                    this.InsertRecord(ns, _syncInfoId, syncInfo.ToArray());
                }


                if (this.RecordExists(ns, _syncIndexId))
                    syncIndex = new SyncIndexRecord(this.GetData(ns, _syncIndexId));
                else
                {
                    syncIndex = new SyncIndexRecord(id, 0);
                    this.InsertRecord(ns, _syncIndexId, syncIndex.ToArray());
                }

                this._syncRecords.Add(ns, new SyncRecord(syncInfo, syncIndex));
            }

            syncIndex.SetEntry(id, syncInfo.NextSync);

            if (syncInfo.NextSync < syncInfo.LastSync)
                syncInfo.NextSync = syncInfo.LastSync + 1;
            else
                syncInfo.NextSync++;

            this._modified = true;
        }

        private void InsertRecord(Namespace ns, ulong id, byte[] data)
        {
            if (data == null)
                throw new XdbfException("Cannot insert null buffer into data file.");

            if (RecordExists(ns, id))
                throw new XdbfException("Attempted to insert an existing record.");

            if (this._records.Count + 1 > _maxRecords)
                this.Rebuild(_maxRecords + _expansionRecords, _maxFreeRecords);

            var freeRecord = FindFreeDataBlock(data.Length);

            int dataOffset;

            if (freeRecord == null)
                dataOffset = (int)(this._io.Length - this._dataOffset);
            else
            {
                dataOffset = freeRecord.Offset;

                if (freeRecord.Size == data.Length)
                    this._freeRecords.Remove(freeRecord);
                else
                {
                    freeRecord.Offset += data.Length;
                    freeRecord.Size -= data.Length;
                }
            }

            var rec = new DataFileRecord(ns, id, dataOffset, data.Length);

            this.SeekToData(rec);
            this._io.Write(data);

            this._records.Add(rec);

            this.SyncRecord(ns, id);
        }

        public void UpdateData(DataFileRecord record, byte[] newData)
        {
            if (newData == null)
                throw new XdbfException("Null buffer passed to data file record.");

            bool isLastRecord = record.Offset + record.Size == this._io.Length;

            if (newData.Length > record.Size && !isLastRecord)
            {
                if (this._freeRecords.Count + 1 > _maxFreeRecords)
                    this.Rebuild(_maxRecords, _maxFreeRecords);
                else
                    this._freeRecords.Add(new FreeRecord(record.Offset, record.Size));

                this._records.Remove(record);

                this.InsertRecord(record.Namespace, record.ID, newData);

                return;
            }

            int originalSize = record.Size;

            this.SeekToData(record);

            this._io.Write(newData);

            record.Size = newData.Length;

            if (newData.Length < originalSize)
            {
                if (this._freeRecords.Count + 1 > _maxFreeRecords)
                    this.Rebuild(_maxRecords, _maxFreeRecords);
                else
                {
                    int freeSpace = originalSize - newData.Length;
                    if (isLastRecord)
                        this._io.SetLength(this._io.Length - freeSpace);
                    else
                        this._freeRecords.Add(new FreeRecord(record.Offset + newData.Length, originalSize - newData.Length));
                }
                    
            }

            this.SyncRecord(record.Namespace, record.ID);
        }

        public void UpdateData(Namespace ns, ulong id, byte[] newData)
        {
            var rec = GetRecord(ns, id);

            if (rec == null)
                throw new XdbfException(string.Format("Record not found in data file (0x{0:X2}, 0x{1:X16}).", (ushort)ns, id));

            this.UpdateData(rec, newData);
        }

        public void UpdateOrInsertRecord(Namespace ns, ulong id, byte[] newData)
        {
            if (RecordExists(ns, id))
                this.UpdateData(ns, id, newData);
            else
                this.InsertRecord(ns, id, newData);
        }

        [Conditional("DEBUG")]
        public void ExtractRecords(string folder, Namespace ns)
        {
            string format = string.Format(@"{0}\{1} - ", folder, ns);

            var recs = this.GetRecords(ns);

            foreach (var rec in recs)
                this.ExtractRecord(rec, format + rec.ID.ToString("X16"));
        }

        [Conditional("DEBUG")]
        public void ExtractRecords(string folder)
        {
            foreach (var rec in _records)
                this.ExtractRecord(rec, folder + "\\" + rec.Namespace + " - " + rec.ID.ToString("X16"));
        }

        [Conditional("DEBUG")]
        public void ExtractRecord(Namespace ns, ulong id, string filename)
        {
            this.ExtractRecord(this.GetRecord(ns, id), filename);
        }

        [Conditional("DEBUG")]
        public void ExtractRecord(DataFileRecord rec, string filename)
        {
            string ext = "";

            switch (rec.Namespace)
            {
                case Namespace.Images:
                    ext = ".png";
                    break;
                case Namespace.Strings:
                    ext = ".txt";
                    break;
            }

            File.WriteAllBytes(filename + ext, this.GetData(rec));
        }
    }
}
