using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NoDev.Common.IO
{
    public enum EndianType
    {
        Big,
        Little
    }

    public class EndianIO
    {
        private EndianType _endianness;
        private bool _requiresReverse;

        public EndianType Endianness
        {
            get { return this._endianness; }
            set
            {
                this._endianness = value;

                if (IsLittleEndian)
                    this._requiresReverse = this.Endianness == EndianType.Big;
                else
                    this._requiresReverse = this.Endianness == EndianType.Little;
            }
        }

        private static readonly bool IsLittleEndian = BitConverter.IsLittleEndian;

        private readonly byte[] _buffer;

        public Stream Stream;

        public EndianIO(Stream stream, EndianType endianType)
            : this(endianType)
        {
            this.Stream = stream;
        }

        protected EndianIO(EndianType endianType)
        {
            this.Endianness = endianType;
            this._buffer = new byte[8];
        }

        public EndianIO(byte[] buffer, EndianType endianType)
            : this(new MemoryStream(buffer), endianType)
        {
            
        }

        public EndianIO(string fileName, EndianType endianType, FileMode fileMode = FileMode.Open, 
            FileAccess fileAccess = FileAccess.ReadWrite, FileShare fileShare = FileShare.Read, int bufferSize = 0x08, bool isAsync = false)
            : this(new FileStream(fileName, fileMode, fileAccess, fileShare, bufferSize, isAsync), endianType)
        {

        }

        public virtual long Length
        {
            get
            {
                return this.Stream.Length;
            }
        }

        public virtual long Position
        {
            get
            {
                return this.Stream.Position;
            }
            set
            {
                this.Stream.Position = value;
            }
        }

        public virtual void Seek(long position, SeekOrigin origin)
        {
            this.Stream.Seek(position, origin);
        }

        public virtual void Flush()
        {
            this.Stream.Flush();
        }

        public virtual void SetLength(long value)
        {
            this.Stream.SetLength(value);
        }

        public byte[] ToArray()
        {
            var ms = this.Stream as MemoryStream;

            if (ms != null)
                return ms.ToArray();

            if (this.Stream is FileStream)
            {
                this.Position = 0;
                var buffer = new byte[this.Length];
                this.Read(buffer, 0, buffer.Length);
                return buffer;
            }

            return ((dynamic)this.Stream).ToArray();
        }

        public void Close()
        {
            this.Stream.Close();
        }

        public virtual int Read(byte[] buffer, int offset, int count)
        {
            return this.Stream.Read(buffer, offset, count);
        }

        public virtual async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            return await this.Stream.ReadAsync(buffer, offset, count);
        }

        public byte[] ReadByteArray(long count)
        {
            var buffer = new byte[count];
            this.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public async Task<byte[]> ReadByteArrayAsync(long count)
        {
            var buffer = new byte[count];
            await this.ReadAsync(buffer, 0, buffer.Length);
            return buffer;
        }

        public byte[] ReadToEnd()
        {
            return this.ReadByteArray((int)(this.Length - this.Position));
        }

        public byte ReadByte()
        {
            this.Read(_buffer, 0, 1);
            return _buffer[0];
        }

        public bool ReadBoolean()
        {
            return this.ReadByte() != 0x00;
        }

        public short ReadInt16()
        {
            this.Read(_buffer, 0, 2);
            if (this._requiresReverse)
                return (short)(_buffer[1] | (_buffer[0] << 8));
            return (short)(_buffer[0] | (_buffer[1] << 8));
        }

        public short ReadInt16(long address)
        {
            Position = address;
            return ReadInt16();
        }

        public int ReadInt32()
        {
            this.Read(_buffer, 0, 4);
            if (this._requiresReverse)
                return _buffer[3] | (_buffer[2] << 8) | (_buffer[1] << 16) | (_buffer[0] << 24);
            return _buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24);
        }

        public long ReadInt64()
        {
            this.Read(_buffer, 0, 8);
            if (this._requiresReverse)
            {
                long n1 = (_buffer[3] | (_buffer[2] << 8) | (_buffer[1] << 16) | (_buffer[0] << 24)) & 0xFFFFFFFF;
                long n2 = (_buffer[7] | (_buffer[6] << 8) | (_buffer[5] << 16) | (_buffer[4] << 24)) & 0xFFFFFFFF;
                return n2 | (n1 << 32);
            }
            long n3 = (_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16) | (_buffer[3] << 24)) & 0xFFFFFFFF;
            long n4 = (_buffer[4] | (_buffer[5] << 8) | (_buffer[6] << 16) | (_buffer[7] << 24)) & 0xFFFFFFFF;
            return n3 | (n4 << 32);
        }
        
        public ushort ReadUInt16()
        {
            return (ushort)this.ReadInt16();
        }

        public uint ReadUInt32()
        {
            return (uint)this.ReadInt32();
        }

        public ulong ReadUInt64()
        {
            return (ulong)this.ReadInt64();
        }

        public uint ReadUInt24()
        {
            this.Read(_buffer, 0, 3);
            if (this._requiresReverse)
                return (uint)(_buffer[2] | (_buffer[1] << 8) | (_buffer[0] << 16));
            return (uint)(_buffer[0] | (_buffer[1] << 8) | (_buffer[2] << 16));
        }

        public float ReadSingle()
        {
            this.Read(_buffer, 0, 4);
            if (this._requiresReverse)
            {
                byte t = _buffer[0];
                _buffer[0] = _buffer[3];
                _buffer[3] = t;
                t = _buffer[1];
                _buffer[1] = _buffer[2];
                _buffer[2] = t;
            }
            return BitConverter.ToSingle(_buffer, 0);
        }

        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble(this.ReadInt64());
        }

        public string ReadAsciiString(int length)
        {
            return Encoding.ASCII.GetString(this.ReadByteArray(length));
        }

        public string ReadAsciiString(long position, int length)
        {
            Position = position;
            return Encoding.ASCII.GetString(this.ReadByteArray(length));
        }

        public string ReadNullTerminatedAsciiString()
        {
            int stringLength = 0;
            long startPosition = this.Position;
            while (this.ReadByte() != 0x00)
                stringLength++;
            this.Position = startPosition;
            var str = ReadAsciiString(stringLength);
            this.Position++;
            return str;
        }

        public string ReadUnicodeString(int length)
        {
            return Encoding.BigEndianUnicode.GetString(this.ReadByteArray(length * 2));
        }

        public string ReadNullTerminatedUnicodeString()
        {
            int stringLength = 0;
            long startPosition = this.Position;
            while (this.ReadUInt16() != 0x00)
                stringLength++;
            this.Position = startPosition;
            var str = ReadUnicodeString(stringLength);
            this.Position += 0x02;
            return str;
        }

        [Obfuscation]
        public virtual void Write(byte[] buffer, int offset, int count)
        {
            this.Stream.Write(buffer, offset, count);
        }

        [Obfuscation]
        public virtual async Task WriteAsync(byte[] buffer, int offset, int count)
        {
            await this.Stream.WriteAsync(buffer, offset, count);
        }

        [Obfuscation]
        public void Write(byte[] buffer)
        {
            this.Write(buffer, 0, buffer.Length);
        }

        [Obfuscation]
        public async void WriteAsync(byte[] buffer)
        {
            await this.WriteAsync(buffer, 0, buffer.Length);
        }

        [Obfuscation]
        public void Write(byte value)
        {
            this.Write(new[] { value });
        }

        [Obfuscation]
        public void Write(bool value)
        {
            this.Write(value ? (byte)1 : (byte)0);
        }

        [Obfuscation]
        public void Write(short value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[1];
                buffer[1] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(int value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[3];
                buffer[3] = t;
                t = buffer[1];
                buffer[1] = buffer[2];
                buffer[2] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(long value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[7];
                buffer[7] = t;
                t = buffer[1];
                buffer[1] = buffer[6];
                buffer[6] = t;
                t = buffer[2];
                buffer[2] = buffer[5];
                buffer[5] = t;
                t = buffer[3];
                buffer[3] = buffer[4];
                buffer[4] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(ushort value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[1];
                buffer[1] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(uint value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[3];
                buffer[3] = t;
                t = buffer[1];
                buffer[1] = buffer[2];
                buffer[2] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(ulong value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[7];
                buffer[7] = t;
                t = buffer[1];
                buffer[1] = buffer[6];
                buffer[6] = t;
                t = buffer[2];
                buffer[2] = buffer[5];
                buffer[5] = t;
                t = buffer[3];
                buffer[3] = buffer[4];
                buffer[4] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(float value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[3];
                buffer[3] = t;
                t = buffer[1];
                buffer[1] = buffer[2];
                buffer[2] = t;
            }
            this.Write(buffer);
        }

        [Obfuscation]
        public void Write(double value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[7];
                buffer[7] = t;
                t = buffer[1];
                buffer[1] = buffer[6];
                buffer[6] = t;
                t = buffer[2];
                buffer[2] = buffer[5];
                buffer[5] = t;
                t = buffer[3];
                buffer[3] = buffer[4];
                buffer[4] = t;
            }
            this.Write(buffer);
        }

        public void WriteUInt24(uint value)
        {
            byte[] buffer = BitConverter.GetBytes(value);
            Array.Resize(ref buffer, 3);
            if (this._requiresReverse)
            {
                byte t = buffer[0];
                buffer[0] = buffer[2];
                buffer[2] = t;
            }
            this.Write(buffer);
        }

        public void WriteAsciiString(string value)
        {
            this.Write(Encoding.ASCII.GetBytes(value));
        }

        public void WriteAsciiString(string value, int length)
        {
            var buffer = new byte[length];
            byte[] stringBuffer = Encoding.ASCII.GetBytes(value);
            Array.Copy(stringBuffer, buffer, (stringBuffer.Length > length) ? length : stringBuffer.Length);
            this.Write(buffer);
        }

        public void WriteNullTerminatedAsciiString(string value)
        {
            this.WriteAsciiString(value);
            this.Write((byte)0x00);
        }

        public void WriteUnicodeString(string value)
        {
            this.Write(this.Endianness == EndianType.Big ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.Unicode.GetBytes(value));
        }

        public void WriteUnicodeString(string value, int length)
        {
            length *= 2;
            var buffer = new byte[length];
            byte[] stringBuffer = this.Endianness == EndianType.Big ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.Unicode.GetBytes(value);
            Array.Copy(stringBuffer, buffer, (stringBuffer.Length > length) ? length : stringBuffer.Length);
            this.Write(buffer);
        }

        public void WriteNullTerminatedUnicodeString(string value)
        {
            this.WriteUnicodeString(value);
            this.Write((short)0x00);
        }
    }
}