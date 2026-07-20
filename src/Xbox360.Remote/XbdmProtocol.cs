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

public enum XbdmResponseBodyRequirement {
    Forbidden,
    Optional,
    RequiredNonEmpty
}

public delegate bool XbdmResponseMessageParser<T>(string message, out T value);

public delegate bool XbdmResponseLinesValidator(IReadOnlyList<string> lines);

public readonly record struct XbdmResponse(int StatusCode, XbdmResponseType ResponseType, string RawMessage, string Message) {
    public void Expect(
        string command,
        int expectedStatusCode,
        XbdmResponseType? expectedResponseType = null,
        XbdmResponseBodyRequirement bodyRequirement = XbdmResponseBodyRequirement.Optional) {
        Expect(command, new[] { expectedStatusCode }, expectedResponseType, bodyRequirement);
    }

    public void Expect(
        string command,
        IReadOnlyCollection<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType = null,
        XbdmResponseBodyRequirement bodyRequirement = XbdmResponseBodyRequirement.Optional) {
        if (expectedStatusCodes == null || expectedStatusCodes.Count == 0)
            throw new ArgumentException("At least one expected status code is required.", nameof(expectedStatusCodes));

        if (!expectedStatusCodes.Contains(StatusCode)) {
            throw XbdmProtocolViolationException.ForUnexpectedResponse(
                command,
                this,
                expectedStatusCodes,
                expectedResponseType,
                bodyRequirement);
        }

        if (expectedResponseType.HasValue && ResponseType != expectedResponseType.Value) {
            throw XbdmProtocolViolationException.ForUnexpectedResponse(
                command,
                this,
                expectedStatusCodes,
                expectedResponseType,
                bodyRequirement);
        }

        switch (bodyRequirement) {
            case XbdmResponseBodyRequirement.Forbidden:
                if (Message.Length != 0) {
                    throw XbdmProtocolViolationException.ForUnexpectedResponse(
                        command,
                        this,
                        expectedStatusCodes,
                        expectedResponseType,
                        bodyRequirement);
                }
                break;
            case XbdmResponseBodyRequirement.RequiredNonEmpty:
                if (string.IsNullOrWhiteSpace(Message)) {
                    throw XbdmProtocolViolationException.ForUnexpectedResponse(
                        command,
                        this,
                        expectedStatusCodes,
                        expectedResponseType,
                        bodyRequirement);
                }
                break;
            case XbdmResponseBodyRequirement.Optional:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(bodyRequirement), bodyRequirement, "Unsupported body requirement.");
        }
    }

    public string ExpectMessage(
        string command,
        int expectedStatusCode,
        XbdmResponseType? expectedResponseType = null,
        XbdmResponseBodyRequirement bodyRequirement = XbdmResponseBodyRequirement.RequiredNonEmpty) {
        Expect(command, expectedStatusCode, expectedResponseType, bodyRequirement);
        return Message;
    }

    public string ExpectMessage(
        string command,
        IReadOnlyCollection<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType = null,
        XbdmResponseBodyRequirement bodyRequirement = XbdmResponseBodyRequirement.RequiredNonEmpty) {
        Expect(command, expectedStatusCodes, expectedResponseType, bodyRequirement);
        return Message;
    }

    public T ExpectParsed<T>(
        string command,
        int expectedStatusCode,
        XbdmResponseMessageParser<T> parser,
        string expectedBodyDescription,
        XbdmResponseType? expectedResponseType = null) {
        Expect(
            command,
            expectedStatusCode,
            expectedResponseType,
            XbdmResponseBodyRequirement.Optional);

        if (string.IsNullOrWhiteSpace(Message)) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                this,
                new[] { expectedStatusCode },
                expectedResponseType,
                expectedBodyDescription);
        }

        if (parser(Message, out T value))
            return value;

        throw XbdmProtocolViolationException.ForInvalidBody(
            command,
            this,
            new[] { expectedStatusCode },
            expectedResponseType,
            expectedBodyDescription);
    }

    public IReadOnlyList<string> ExpectLines(
        string command,
        IReadOnlyList<string>? lines,
        string expectedBodyDescription,
        int minimumLineCount = 0,
        XbdmResponseLinesValidator? validator = null) {
        if (minimumLineCount < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumLineCount), minimumLineCount, "Minimum line count cannot be negative.");
        if (string.IsNullOrWhiteSpace(expectedBodyDescription))
            throw new ArgumentException("Expected body description is required.", nameof(expectedBodyDescription));

        Expect(
            command,
            (int) XbdmResponseType.MultiResponse,
            XbdmResponseType.MultiResponse,
            XbdmResponseBodyRequirement.Optional);

        if (lines == null || lines.Count < minimumLineCount) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                this,
                new[] { (int) XbdmResponseType.MultiResponse },
                XbdmResponseType.MultiResponse,
                expectedBodyDescription);
        }

        if (validator != null && !validator(lines)) {
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                this,
                new[] { (int) XbdmResponseType.MultiResponse },
                XbdmResponseType.MultiResponse,
                expectedBodyDescription);
        }

        return lines;
    }

    public void ExpectBinaryLength(
        string command,
        long expectedLength,
        long actualLength,
        string? expectedBodyDescription = null) {
        if (expectedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedLength), expectedLength, "Expected binary length cannot be negative.");
        if (actualLength < 0)
            throw new ArgumentOutOfRangeException(nameof(actualLength), actualLength, "Actual binary length cannot be negative.");

        Expect(
            command,
            (int) XbdmResponseType.BinaryResponse,
            XbdmResponseType.BinaryResponse,
            XbdmResponseBodyRequirement.Optional);

        if (actualLength != expectedLength) {
            string description = string.IsNullOrWhiteSpace(expectedBodyDescription)
                ? $"exact binary length of {expectedLength} bytes (received {actualLength})"
                : $"{expectedBodyDescription}; expected {expectedLength} bytes, received {actualLength}";
            throw XbdmProtocolViolationException.ForInvalidBody(
                command,
                this,
                new[] { (int) XbdmResponseType.BinaryResponse },
                XbdmResponseType.BinaryResponse,
                description);
        }
    }
}

public sealed class XbdmProtocolViolationException : IOException {
    private XbdmProtocolViolationException(
        string command,
        XbdmResponse response,
        IReadOnlyList<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType,
        XbdmResponseBodyRequirement bodyRequirement,
        string expectedBodyDescription,
        string message)
        : base(message) {
        Command = command;
        StatusCode = response.StatusCode;
        ResponseType = response.ResponseType;
        RawResponse = response.RawMessage;
        BodyLength = response.Message.Length;
        BodyPreview = BuildBodyPreview(response.Message);
        ExpectedStatusCodes = expectedStatusCodes;
        ExpectedResponseType = expectedResponseType;
        BodyRequirement = bodyRequirement;
        ExpectedBodyDescription = expectedBodyDescription;
    }

    public string Command { get; }

    public int StatusCode { get; }

    public XbdmResponseType ResponseType { get; }

    public string RawResponse { get; }

    public int BodyLength { get; }

    public string BodyPreview { get; }

    public IReadOnlyList<int> ExpectedStatusCodes { get; }

    public XbdmResponseType? ExpectedResponseType { get; }

    public XbdmResponseBodyRequirement BodyRequirement { get; }

    public string ExpectedBodyDescription { get; }

    public string ExpectedDescription {
        get {
            string statusText = ExpectedStatusCodes.Count == 1
                ? $"status {ExpectedStatusCodes[0]}"
                : $"one of statuses {string.Join(", ", ExpectedStatusCodes)}";
            string typeText = ExpectedResponseType.HasValue
                ? $", response type {ExpectedResponseType.Value}"
                : string.Empty;
            return $"{statusText}{typeText}, body {ExpectedBodyDescription}";
        }
    }

    public static XbdmProtocolViolationException ForUnexpectedResponse(
        string command,
        XbdmResponse response,
        IReadOnlyCollection<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType,
        XbdmResponseBodyRequirement bodyRequirement) {
        string expectedBodyDescription = DescribeBodyRequirement(bodyRequirement);
        IReadOnlyList<int> statuses = expectedStatusCodes.ToArray();
        return new XbdmProtocolViolationException(
            command,
            response,
            statuses,
            expectedResponseType,
            bodyRequirement,
            expectedBodyDescription,
            BuildMessage(command, response, statuses, expectedResponseType, expectedBodyDescription));
    }

    public static XbdmProtocolViolationException ForInvalidBody(
        string command,
        XbdmResponse response,
        IReadOnlyCollection<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType,
        string expectedBodyDescription) {
        IReadOnlyList<int> statuses = expectedStatusCodes.ToArray();
        return new XbdmProtocolViolationException(
            command,
            response,
            statuses,
            expectedResponseType,
            XbdmResponseBodyRequirement.RequiredNonEmpty,
            expectedBodyDescription,
            BuildMessage(command, response, statuses, expectedResponseType, expectedBodyDescription));
    }

    private static string BuildMessage(
        string command,
        XbdmResponse response,
        IReadOnlyList<int> expectedStatusCodes,
        XbdmResponseType? expectedResponseType,
        string expectedBodyDescription) {
        string statusText = expectedStatusCodes.Count == 1
            ? $"status {expectedStatusCodes[0]}"
            : $"one of statuses {string.Join(", ", expectedStatusCodes)}";
        string typeText = expectedResponseType.HasValue
            ? $" and response type {expectedResponseType.Value}"
            : string.Empty;
        string raw = string.IsNullOrEmpty(response.RawMessage) ? "<empty>" : response.RawMessage;
        return $"{command} failed: XBDM protocol violation; expected {statusText}{typeText} with body {expectedBodyDescription}, " +
               $"but got status {response.StatusCode} ({response.ResponseType}) with body length {response.Message.Length}. Raw response: {raw}";
    }

    private static string DescribeBodyRequirement(XbdmResponseBodyRequirement bodyRequirement) {
        return bodyRequirement switch {
            XbdmResponseBodyRequirement.Forbidden => "forbidden",
            XbdmResponseBodyRequirement.Optional => "optional",
            XbdmResponseBodyRequirement.RequiredNonEmpty => "required non-empty",
            _ => bodyRequirement.ToString()
        };
    }

    private static string BuildBodyPreview(string message) {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        ReadOnlySpan<char> source = message.AsSpan(0, Math.Min(message.Length, 80));
        char[] chars = new char[source.Length];
        for (int i = 0; i < source.Length; i++) {
            char c = source[i];
            chars[i] = char.IsControl(c) ? '?' : c;
        }

        return new string(chars);
    }
}

internal sealed class XbdmLineReader {
    private readonly Stream stream;
    private readonly byte[] buffer;
    private readonly int readTimeoutMs;
    private int bufferStart;
    private int bufferEnd;

    public XbdmLineReader(Stream stream, int bufferSize = 8192, int readTimeoutMs = 5000) {
        this.stream = stream;
        this.buffer = new byte[Math.Max(1024, bufferSize)];
        this.readTimeoutMs = Math.Max(250, readTimeoutMs);
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

            int read = await ReadAsync(buffer.AsMemory(bufferEnd, buffer.Length - bufferEnd), cancellationToken);
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
            int read = await ReadAsync(destination.AsMemory(offset + readTotal, count - readTotal), cancellationToken);
            if (read == 0)
                throw new IOException("XBDM connection closed while reading binary data.");
            readTotal += read;
        }
    }

    public async Task<byte> ReadByteAsync(CancellationToken cancellationToken) {
        if (bufferStart < bufferEnd) {
            return buffer[bufferStart++];
        }

        int read = await ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
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

    private async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
        using CancellationTokenSource timeoutCts = new CancellationTokenSource(readTimeoutMs);
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try {
            return await stream.ReadAsync(buffer, cts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
            throw new TimeoutException($"XBDM read timed out after {readTimeoutMs} ms.");
        }
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
