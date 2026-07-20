using System.Buffers.Binary;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal sealed class TitleExecutionInfo {
    public uint MediaId { get; }
    public uint Version { get; }
    public uint BaseVersion { get; }
    public uint TitleId { get; }
    public byte Platform { get; }
    public byte ExecutableType { get; }
    public byte DiscNumber { get; }
    public byte DiscCount { get; }
    public uint SaveGameId { get; }

    private TitleExecutionInfo(
        uint mediaId,
        uint version,
        uint baseVersion,
        uint titleId,
        byte platform,
        byte executableType,
        byte discNumber,
        byte discCount,
        uint saveGameId) {
        MediaId = mediaId;
        Version = version;
        BaseVersion = baseVersion;
        TitleId = titleId;
        Platform = platform;
        ExecutableType = executableType;
        DiscNumber = discNumber;
        DiscCount = discCount;
        SaveGameId = saveGameId;
    }

    public static TitleExecutionInfo FromXex(Stream stream) {
        uint mediaId = Endian.ReadUInt32BE(stream);
        uint version = Endian.ReadUInt32BE(stream);
        uint baseVersion = Endian.ReadUInt32BE(stream);
        uint titleId = Endian.ReadUInt32BE(stream);
        int platform = stream.ReadByte();
        int executableType = stream.ReadByte();
        int discNumber = stream.ReadByte();
        int discCount = stream.ReadByte();
        if (platform < 0 || executableType < 0 || discNumber < 0 || discCount < 0)
            throw new EndOfStreamException();
        uint saveGameId = Endian.ReadUInt32BE(stream);

        return new TitleExecutionInfo(
            mediaId,
            version,
            baseVersion,
            titleId,
            (byte) platform,
            (byte) executableType,
            (byte) discNumber,
            (byte) discCount,
            saveGameId);
    }

    public static TitleExecutionInfo FromXbe(Stream stream) {
        stream.Seek(8, SeekOrigin.Current);
        uint titleId = Endian.ReadUInt32LE(stream);

        stream.Seek(164, SeekOrigin.Current);
        uint version = Endian.ReadUInt32LE(stream);

        return new TitleExecutionInfo(
            0,
            version,
            0,
            titleId,
            0,
            0,
            1,
            1,
            0);
    }
}

internal sealed class TitleInfo {
    public ContentType ContentType { get; }
    public TitleExecutionInfo ExecutionInfo { get; }

    private TitleInfo(ContentType contentType, TitleExecutionInfo info) {
        ContentType = contentType;
        ExecutionInfo = info;
    }

    public static TitleInfo FromImage(IsoReader iso) {
        Stream? entry = iso.GetEntry(new WindowsPath("\\default.xex"));
        if (entry != null) {
            XexHeader header = XexHeader.Read(entry);
            if (header.ExecutionInfo == null)
                throw new InvalidDataException("No execution info in default.xex.");
            return new TitleInfo(ContentType.GamesOnDemand, header.ExecutionInfo);
        }

        entry = iso.GetEntry(new WindowsPath("\\default.xbe"));
        if (entry != null) {
            XbeHeader header = XbeHeader.Read(entry);
            if (header.ExecutionInfo == null)
                throw new InvalidDataException("No execution info in default.xbe.");
            return new TitleInfo(ContentType.XboxOriginal, header.ExecutionInfo);
        }

        throw new InvalidDataException("No executable found in this image.");
    }
}

internal sealed class XexHeader {
    private const uint ExecutionIdField = 0x00040006;
    public TitleExecutionInfo? ExecutionInfo { get; }

    private XexHeader(TitleExecutionInfo? executionInfo) {
        ExecutionInfo = executionInfo;
    }

    public static XexHeader Read(Stream stream) {
        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        if (magic[0] != (byte) 'X' || magic[1] != (byte) 'E' || magic[2] != (byte) 'X' || magic[3] != (byte) '2')
            throw new InvalidDataException("Missing XEX2 magic bytes.");

        long headerOffset = stream.Position - 4;
        _ = Endian.ReadUInt32BE(stream);
        _ = Endian.ReadUInt32BE(stream);
        _ = Endian.ReadUInt32BE(stream);
        _ = Endian.ReadUInt32BE(stream);

        uint fieldCount = Endian.ReadUInt32BE(stream);
        TitleExecutionInfo? executionInfo = null;

        for (uint i = 0; i < fieldCount; i++) {
            uint key = Endian.ReadUInt32BE(stream);
            uint value = Endian.ReadUInt32BE(stream);
            if (key != ExecutionIdField)
                continue;
            long returnPos = stream.Position;
            stream.Seek(headerOffset + value, SeekOrigin.Begin);
            executionInfo = TitleExecutionInfo.FromXex(stream);
            stream.Seek(returnPos, SeekOrigin.Begin);
        }

        return new XexHeader(executionInfo);
    }
}

internal sealed class XbeHeader {
    public TitleExecutionInfo? ExecutionInfo { get; }

    private XbeHeader(TitleExecutionInfo? executionInfo) {
        ExecutionInfo = executionInfo;
    }

    public static XbeHeader Read(Stream stream) {
        Span<byte> magic = stackalloc byte[4];
        stream.ReadExactly(magic);
        if (magic[0] != (byte) 'X' || magic[1] != (byte) 'B' || magic[2] != (byte) 'E' || magic[3] != (byte) 'H')
            throw new InvalidDataException("Missing XBEH magic bytes.");

        stream.Seek(256, SeekOrigin.Current);
        uint baseAddr = Endian.ReadUInt32LE(stream);

        stream.Seek(16, SeekOrigin.Current);
        uint certAddr = Endian.ReadUInt32LE(stream);

        long offset = stream.Position - 284;
        uint certOffset = certAddr - baseAddr;
        stream.Seek(offset + certOffset, SeekOrigin.Begin);

        TitleExecutionInfo info = TitleExecutionInfo.FromXbe(stream);
        return new XbeHeader(info);
    }
}
