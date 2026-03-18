using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Xbox360.Remote;

public enum XbdmResponseType {
    SingleResponse = 200,
    Connected = 201,
    MultiResponse = 202,
    BinaryResponse = 203,
    ReadyForBinary = 204
}

public readonly record struct XbdmResponse(int StatusCode, XbdmResponseType ResponseType, string RawMessage, string Message);

internal sealed class XbdmLineReader {
    private readonly Stream stream;
    private readonly byte[] buffer;
    private int bufferStart;
    private int bufferEnd;

    public XbdmLineReader(Stream stream, int bufferSize = 8192) {
        this.stream = stream;
        this.buffer = new byte[Math.Max(1024, bufferSize)];
    }

    public async Task<string> ReadLineAsync(CancellationToken cancellationToken) {
        while (true) {
            int newlineIndex = IndexOfByte(buffer, bufferStart, bufferEnd - bufferStart, (byte) '\n');
            if (newlineIndex >= 0) {
                int length = newlineIndex - bufferStart;
                string line = Encoding.ASCII.GetString(buffer, bufferStart, length);
                bufferStart = newlineIndex + 1;
                if (line.EndsWith("\r", StringComparison.Ordinal))
                    line = line[..^1];
                return line;
            }

            if (bufferEnd == buffer.Length) {
                if (bufferStart > 0) {
                    Buffer.BlockCopy(buffer, bufferStart, buffer, 0, bufferEnd - bufferStart);
                    bufferEnd -= bufferStart;
                    bufferStart = 0;
                }
                else {
                    throw new IOException("XBDM line exceeds buffer capacity.");
                }
            }

            int read = await stream.ReadAsync(buffer.AsMemory(bufferEnd, buffer.Length - bufferEnd), cancellationToken);
            if (read == 0)
                throw new IOException("XBDM connection closed.");
            bufferEnd += read;
        }
    }

    public async Task ReadExactAsync(byte[] destination, int offset, int count, CancellationToken cancellationToken) {
        int readTotal = 0;
        if (bufferStart < bufferEnd) {
            int buffered = Math.Min(count, bufferEnd - bufferStart);
            Buffer.BlockCopy(buffer, bufferStart, destination, offset, buffered);
            bufferStart += buffered;
            readTotal += buffered;
        }

        while (readTotal < count) {
            int read = await stream.ReadAsync(destination.AsMemory(offset + readTotal, count - readTotal), cancellationToken);
            if (read == 0)
                throw new IOException("XBDM connection closed while reading binary data.");
            readTotal += read;
        }
    }

    public async Task<byte> ReadByteAsync(CancellationToken cancellationToken) {
        if (bufferStart < bufferEnd) {
            return buffer[bufferStart++];
        }

        int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        if (read == 0)
            throw new IOException("XBDM connection closed while reading binary data.");
        bufferStart = 0;
        bufferEnd = read;
        return buffer[bufferStart++];
    }

    public async Task<byte[]> ReadUntilAsync(byte terminator, int maxBytes, CancellationToken cancellationToken) {
        using MemoryStream ms = new MemoryStream();
        for (int i = 0; i < maxBytes; i++) {
            byte b = await ReadByteAsync(cancellationToken);
            if (b == terminator)
                break;
            ms.WriteByte(b);
        }
        return ms.ToArray();
    }

    public async Task<ushort> ReadUInt16LEAsync(CancellationToken cancellationToken) {
        byte[] tmp = ArrayPool<byte>.Shared.Rent(2);
        try {
            await ReadExactAsync(tmp, 0, 2, cancellationToken);
            return BinaryPrimitives.ReadUInt16LittleEndian(tmp.AsSpan(0, 2));
        }
        finally {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    public async Task<int> ReadInt32LEAsync(CancellationToken cancellationToken) {
        byte[] tmp = ArrayPool<byte>.Shared.Rent(4);
        try {
            await ReadExactAsync(tmp, 0, 4, cancellationToken);
            return BinaryPrimitives.ReadInt32LittleEndian(tmp.AsSpan(0, 4));
        }
        finally {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    private static int IndexOfByte(byte[] array, int start, int count, byte value) {
        for (int i = start; i < start + count; i++) {
            if (array[i] == value)
                return i;
        }

        return -1;
    }
}

internal static class XbdmResponseParser {
    public static XbdmResponse Parse(string line) {
        if (line.Length < 3 || !int.TryParse(line.AsSpan(0, 3), out int code)) {
            throw new IOException($"Invalid XBDM response: {line}");
        }

        XbdmResponseType type = Enum.IsDefined(typeof(XbdmResponseType), code)
            ? (XbdmResponseType) code
            : XbdmResponseType.SingleResponse;

        string message = line.Length > 4 ? line.Substring(4) : string.Empty;
        return new XbdmResponse(code, type, line, message);
    }
}
