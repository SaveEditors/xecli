using NoDev.Common.IO;

namespace NoDev.Xdbf.Records
{
    public class DataFileRecord
    {
        public readonly Namespace Namespace;
        public readonly ulong ID;
        public int Offset;
        public int Size;

        public DataFileRecord(Namespace ns, ulong id, int offset, int size)
        {
            this.Namespace = ns;
            this.ID = id;
            this.Offset = offset;
            this.Size = size;
        }

        public DataFileRecord(EndianIO io)
        {
            this.Namespace = (Namespace)io.ReadInt16();
            this.ID = io.ReadUInt64();
            this.Offset = io.ReadInt32();
            this.Size = io.ReadInt32();
        }

        public void Write(EndianIO io)
        {
            io.Write((short)this.Namespace);
            io.Write(this.ID);
            io.Write(this.Offset);
            io.Write(this.Size);
        }
    }
}
