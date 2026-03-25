using System.Buffers.Binary;

namespace Xbox360.Remote.Cli.God;

internal static class Endian {
    public static ushort ReadUInt16BE(Stream stream) {
        Span<byte> buf = stackalloc byte[2];
        stream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt16BigEndian(buf);
    }

    public static ushort ReadUInt16LE(Stream stream) {
        Span<byte> buf = stackalloc byte[2];
        stream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt16LittleEndian(buf);
    }

    public static uint ReadUInt32BE(Stream stream) {
        Span<byte> buf = stackalloc byte[4];
        stream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt32BigEndian(buf);
    }

    public static uint ReadUInt32LE(Stream stream) {
        Span<byte> buf = stackalloc byte[4];
        stream.ReadExactly(buf);
        return BinaryPrimitives.ReadUInt32LittleEndian(buf);
    }

    public static void WriteUInt16BE(byte[] buffer, int offset, ushort value) {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), value);
    }

    public static void WriteUInt32BE(byte[] buffer, int offset, uint value) {
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), value);
    }

    public static void WriteUInt32LE(byte[] buffer, int offset, uint value) {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
    }
}
