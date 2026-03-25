using System.Security.Cryptography;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal enum ContentType : uint {
    GamesOnDemand = 0x7000,
    XboxOriginal = 0x5000
}

internal sealed class ConHeaderBuilder {
    private static readonly Lazy<byte[]> Template = new Lazy<byte[]>(LoadTemplate);
    private readonly byte[] buffer;

    public ConHeaderBuilder() {
        buffer = Template.Value.ToArray();
    }

    private static byte[] LoadTemplate() {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "empty_live.bin");
        return File.ReadAllBytes(path);
    }

    public ConHeaderBuilder WithBlockCounts(uint blocksAllocated, ushort blocksNotAllocated) {
        WriteUInt24BE(0x0392, blocksAllocated);
        Endian.WriteUInt16BE(buffer, 0x0395, blocksNotAllocated);
        return this;
    }

    public ConHeaderBuilder WithContentType(ContentType contentType) {
        Endian.WriteUInt32BE(buffer, 0x0344, (uint) contentType);
        return this;
    }

    public ConHeaderBuilder WithDataPartsInfo(uint partCount, ulong partsTotalSize) {
        Endian.WriteUInt32LE(buffer, 0x03A0, partCount);
        Endian.WriteUInt32BE(buffer, 0x03A4, (uint) (partsTotalSize / 0x0100));
        return this;
    }

    public ConHeaderBuilder WithExecutionInfo(TitleExecutionInfo info) {
        Endian.WriteUInt32BE(buffer, 0x0354, info.MediaId);
        Endian.WriteUInt32BE(buffer, 0x0360, info.TitleId);
        buffer[0x0364] = info.Platform;
        buffer[0x0365] = info.ExecutableType;
        buffer[0x0366] = info.DiscNumber;
        buffer[0x0367] = info.DiscCount;
        return this;
    }

    public ConHeaderBuilder WithGameTitle(string title) {
        WriteUtf16BE(0x0411, title);
        WriteUtf16BE(0x1691, title);
        return this;
    }

    public ConHeaderBuilder WithMhtHash(byte[] mhtHash) {
        if (mhtHash.Length != 20)
            throw new ArgumentException("MHT hash must be 20 bytes.", nameof(mhtHash));
        Buffer.BlockCopy(mhtHash, 0, buffer, 0x037D, 20);
        return this;
    }

    public byte[] FinalizeHeader() {
        buffer[0x035B] = 0;
        buffer[0x035F] = 0;
        buffer[0x0391] = 0;

        byte[] digest = SHA1.HashData(buffer.AsSpan(0x0344, 0xACBC));
        Buffer.BlockCopy(digest, 0, buffer, 0x032C, digest.Length);
        return buffer;
    }

    private void WriteUInt24BE(int offset, uint value) {
        buffer[offset] = (byte) ((value >> 16) & 0xFF);
        buffer[offset + 1] = (byte) ((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte) (value & 0xFF);
    }

    private void WriteUtf16BE(int offset, string text) {
        if (string.IsNullOrEmpty(text))
            return;
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(text + "\0");
        Buffer.BlockCopy(bytes, 0, buffer, offset, Math.Min(bytes.Length, buffer.Length - offset));
    }
}
