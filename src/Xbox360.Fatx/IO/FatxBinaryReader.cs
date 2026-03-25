using System.Buffers.Binary;
using System.Text;

namespace Xbox360.Fatx.IO;

public static class FatxBinaryReader
{
    public static async Task<byte[]> ReadBytesAsync(Stream stream, int count, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        if (read == count)
        {
            return buffer;
        }

        return buffer[..read];
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> data, FatxEndian endian = FatxEndian.Little)
        => endian == FatxEndian.Big ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);

    public static ushort ReadUInt16(ReadOnlySpan<byte> data, FatxEndian endian = FatxEndian.Little)
        => endian == FatxEndian.Big ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);

    public static void WriteUInt32(Span<byte> data, uint value, FatxEndian endian = FatxEndian.Little)
    {
        if (endian == FatxEndian.Big)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, value);
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
    }

    public static void WriteUInt16(Span<byte> data, ushort value, FatxEndian endian = FatxEndian.Little)
    {
        if (endian == FatxEndian.Big)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data, value);
            return;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(data, value);
    }

    public static string ReadAscii(ReadOnlySpan<byte> data)
        => Encoding.ASCII.GetString(data).TrimEnd('\0', ' ');
}
