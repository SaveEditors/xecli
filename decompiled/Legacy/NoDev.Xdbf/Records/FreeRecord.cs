using NoDev.Common.IO;

namespace NoDev.Xdbf.Records
{
    public class FreeRecord
    {
        public int Offset;
        public int Size;

        public FreeRecord(int offset, int size)
        {
            this.Offset = offset;
            this.Size = size;
        }

        public FreeRecord(EndianIO io)
        {
            this.Offset = io.ReadInt32();
            this.Size = io.ReadInt32();
        }

        public void Write(EndianIO io)
        {
            io.Write(this.Offset);
            io.Write(this.Size);
        }
    }
}
