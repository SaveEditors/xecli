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

    public static string ReadAscii(ReadOnlySpan<byte> data)
        => Encoding.ASCII.GetString(data).TrimEnd('\0', ' ');
}
