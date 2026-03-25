using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace System
{
    public static class Extensions
    {
        public static string RemoveNullBytes(this string str)
        {
            return str.Replace("\0", "");
        }

        public static Rectangle ToRectangle(this RectangleF rect)
        {
            return new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }

        public static bool IsNull(this IEnumerable<byte> data)
        {
            return data == null || data.All(t => t == 0);
        }

        public static byte[] ComputeSHA1(this byte[] data)
        {
            return SHA1.Create().ComputeHash(data);
        }

        public static byte[] GetNullTerminatedBigEndianUnicodeArray(this string str)
        {
            return Encoding.BigEndianUnicode.GetBytes(str + "\0");
        }

        public static void Memset(this byte[] data, byte value, int index = 0, int length = -1)
        {
            if (data == null)
                throw new Exception("Memset: Input data cannot be empty.");

            if (length == -1)
                length = data.Length;

            if (index + length > data.Length)
                throw new Exception("Memset: Index and length exceed the size of the data.");

            for (int x = index; x < length; x++)
                data[x] = value;
        }

        public static byte[] Read(this Stream io, int length)
        {
            var buffer = new byte[length];
            io.Read(buffer, 0, length);
            return buffer;
        }

        public static void Write(this Stream io, byte[] buffer)
        {
            io.Write(buffer, 0, buffer.Length);
        }

        public static byte[] Read(this byte[] input, int index, int length = -1)
        {
            if (length == -1)
                length = input.Length - index;

            var output = new byte[length];
            Array.Copy(input, index, output, 0, length);
            return output;
        }

        public static byte[] Read(this byte[] input, long index, long length = -1)
        {
            if (length == -1)
                length = input.Length - index;

            var output = new byte[length];
            Array.Copy(input, index, output, 0, length);
            return output;
        }

        public static void Write(this byte[] input, byte[] buffer, long index = 0, long length = -1)
        {
            if (length == -1)
                length = buffer.Length - index;

            Array.Copy(buffer, 0, input, index, length);
        }

        public static short ReadInt16(this byte[] data, int offset = 0)
        {
            return (short)((data[offset] << 8) | data[offset + 1]);
        }

        public static int ReadInt32(this byte[] data, int offset = 0)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        public static long ReadInt64(this byte[] data, int offset = 0)
        {
            var n1 = (data[offset + 3] | (data[offset + 2] << 8) | (data[offset + 1] << 16) | (data[offset] << 24)) & 0xFFFFFFFF;
            var n2 = (data[offset+7] | (data[offset + 6] << 8) | (data[offset + 5] << 16) | (data[offset + 4] << 24)) & 0xFFFFFFFF;
            return n2 | (n1 << 32);
        }

        public static ushort ReadUInt16(this byte[] data, int offset = 0) { return (ushort)data.ReadInt16(offset); }
        public static uint ReadUInt32(this byte[] data, int offset) { return (uint)data.ReadInt32(offset); }
        public static ulong ReadUInt64(this byte[] data, int offset) { return (ulong)data.ReadInt64(offset); }

        public static void WriteInt16(this byte[] data, int offset, short value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)(value & 0x00FF);
        }

        public static void WriteInt32(this byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)((value & 0x00FF0000) >> 16);
            data[offset + 2] = (byte)((value & 0x0000FF00) >> 8);
            data[offset + 3] = (byte)(value & 0x000000FF);
        }

        public static void WriteInt64(this byte[] data, int offset, long value)
        {
            Array.Copy(BitConverter.GetBytes(value), data, data.Length);
            var t = data[offset];
            data[offset] = data[offset+7];
            data[offset+7] = t;
            t = data[offset+1];
            data[offset + 1] = data[offset+6];
            data[offset+6] = t;
            t = data[offset+2];
            data[offset+2] = data[offset+5];
            data[offset+5] = t;
            t = data[offset+3];
            data[offset+3] = data[offset+4];
            data[offset+4] = t;
        }

        public static int RotateLeft(this int value, int bits)
        {
            return (value << bits | value >> 32 - bits) & -1;
        }

        public static long RotateLeft(this long value, int bits)
        {
            return (value << bits | value >> 4 - bits) & -1;
        }

        public static long RotateLeft(this uint value, int bits)
        {
            long longValue = value;
            return (longValue << bits | longValue >> 64 - bits) & -1L;
        }

        public static int CountLeadingZerosWord(this int value) { return (int)(CountLeadingZeros(value, 32)); }
        public static int CountLeadingZerosWord(this uint value) { return (int)(CountLeadingZeros(value, 32)); }
        public static int CountLeadingZerosWord(this long value) { return (int)(CountLeadingZeros(value, 32)); }

        public static long CountLeadingZerosDouble(this long value) { return CountLeadingZeros(value, 64); }

        public static long CountLeadingZeros(this long value, int bitLength)
        {
            long leadingZeros = 0;
            for (var x = 0; x < bitLength; x++)
            {
                if ((value & 1L << x) == 0)
                    leadingZeros++;
                else
                    leadingZeros = 0;
            }
            return leadingZeros;
        }
    }
}