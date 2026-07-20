using System.Security.Cryptography;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal enum ContentType : uint {
    GamesOnDemand = 0x7000,
    XboxOriginal = 0x5000
}

internal sealed class ConHeaderBuilder {
    private const string ExpectedTemplateSha256 = "0B6D8388E38270291C0899FA19B12773EB724BC3930B0E1624C030D64C517A66";
    private static readonly Lazy<byte[]> Template = new Lazy<byte[]>(CreateTemplate);
    private readonly byte[] buffer;

    public ConHeaderBuilder() {
        buffer = Template.Value.ToArray();
    }

    private static byte[] CreateTemplate() {
        byte[] template = new byte[0xB000];
        WriteHex(template, 0x0000, "4C495645");
        template.AsSpan(0x022C, 8).Fill(0xFF);
        WriteHex(template, 0x032C, "C06530D687EB083F3055ADF6F17F434AFFFC4895");
        WriteHex(template, 0x0342, "AD0E");
        template[0x0346] = 0x70;
        template[0x034B] = 0x02;
        template[0x035B] = 0x0A;
        template[0x035F] = 0x0A;
        WriteHex(template, 0x0366, "0101");
        WriteHex(template, 0x0379, "24050511AA369F3AD52AA7A28EC4853990B5895B65B52F85405442");
        WriteHex(template, 0x0395, "4E41");
        WriteHex(template, 0x03A0, "444E");
        template[0x03AC] = 0x01;
        Encoding.BigEndianUnicode.GetBytes("This is an installed game. To play, insert the original game disc.")
            .CopyTo(template, 0x0D11);
        WriteHex(template, 0x1714, "3841");
        WriteHex(template, 0x1718, "3841");

        string actualSha256 = Convert.ToHexString(SHA256.HashData(template));
        if (!string.Equals(actualSha256, ExpectedTemplateSha256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Generated CON header template invariant failed: expected {ExpectedTemplateSha256}, found {actualSha256}.");

        return template;
    }

    private static void WriteHex(byte[] destination, int offset, string hex) =>
        Convert.FromHexString(hex).CopyTo(destination, offset);

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
