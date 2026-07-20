using System.Buffers.Binary;
using System.Text;

namespace Xbox360.Remote.Cli.God;

internal static class XexMetadataParser {
    private const uint OriginalBaseAddressHeader = 0x00010001;
    private const uint EntryPointHeader = 0x00010100;
    private const uint ImageBaseAddressHeader = 0x00010201;
    private const uint ImportLibrariesHeader = 0x000103FF;
    private const uint BaseFileFormatHeader = 0x000003FF;
    private const uint OriginalPeNameHeader = 0x000183FF;
    private const uint SystemFlagsHeader = 0x00030000;
    private const uint ExecutionInfoHeader = 0x00040006;
    private const uint BoundingPathHeader = 0x000080FF;

    public static XexMetadata Parse(byte[] data) {
        if (data.Length < 0x18)
            throw new InvalidDataException("XEX file is too small.");

        if (!HasMagic(data))
            throw new InvalidDataException("Missing XEX2 magic bytes.");

        uint moduleFlags = ReadUInt32BE(data, 0x04);
        uint peDataOffset = ReadUInt32BE(data, 0x08);
        uint securityInfoOffset = ReadUInt32BE(data, 0x10);
        uint optionalHeaderCount = ReadUInt32BE(data, 0x14);

        uint headerSize = GetHeaderSize(data, optionalHeaderCount);
        Dictionary<uint, uint> optionalHeaders = ReadOptionalHeaders(data, optionalHeaderCount);

        XexExecutionInfo? executionInfo = null;
        if (optionalHeaders.TryGetValue(ExecutionInfoHeader, out uint executionInfoOffset))
            executionInfo = ReadExecutionInfo(data, executionInfoOffset);

        XexBaseFileFormatInfo? baseFileFormat = null;
        if (optionalHeaders.TryGetValue(BaseFileFormatHeader, out uint baseFileFormatOffset))
            baseFileFormat = ReadBaseFileFormat(data, baseFileFormatOffset);

        XexImportLibrariesInfo? importLibraries = null;
        if (optionalHeaders.TryGetValue(ImportLibrariesHeader, out uint importLibrariesOffset))
            importLibraries = ReadImportLibraries(data, importLibrariesOffset);

        XexHeaderStringInfo? originalPeName = null;
        if (optionalHeaders.TryGetValue(OriginalPeNameHeader, out uint originalPeNameOffset))
            originalPeName = ReadHeaderString(data, originalPeNameOffset);

        XexHeaderStringInfo? boundingPath = null;
        if (optionalHeaders.TryGetValue(BoundingPathHeader, out uint boundingPathOffset))
            boundingPath = ReadHeaderString(data, boundingPathOffset);

        XexSecurityInfo? securityInfo = ReadSecurityInfo(data, securityInfoOffset);
        XexExportTableInfo? exportTable = securityInfo != null ? ReadOptionalExportTable(data, securityInfo) : null;

        return new XexMetadata {
            Magic = "XEX2",
            ModuleFlags = new XexModuleFlags(moduleFlags),
            PeDataOffset = peDataOffset,
            SecurityInfoOffset = securityInfoOffset,
            OptionalHeaderCount = optionalHeaderCount,
            HeaderSize = headerSize,
            OriginalBaseAddress = optionalHeaders.TryGetValue(OriginalBaseAddressHeader, out uint originalBaseAddress) ? originalBaseAddress : null,
            EntryPoint = optionalHeaders.TryGetValue(EntryPointHeader, out uint entryPoint) ? entryPoint : null,
            ImageBaseAddress = optionalHeaders.TryGetValue(ImageBaseAddressHeader, out uint imageBaseAddress) ? imageBaseAddress : null,
            SystemFlags = optionalHeaders.TryGetValue(SystemFlagsHeader, out uint systemFlags) ? systemFlags : null,
            ExecutionInfo = executionInfo,
            BaseFileFormat = baseFileFormat,
            ImportLibraries = importLibraries,
            OriginalPeName = originalPeName,
            BoundingPath = boundingPath,
            SecurityInfo = securityInfo,
            ExportTable = exportTable
        };
    }

    private static uint GetHeaderSize(byte[] data, uint optionalHeaderCount) {
        int availableOptionalHeaders = (data.Length - 0x18) / 8;
        if (optionalHeaderCount > (uint) availableOptionalHeaders)
            throw new InvalidDataException("XEX header table is truncated.");

        return (uint) (0x18 + ((int) optionalHeaderCount * 8));
    }

    private static Dictionary<uint, uint> ReadOptionalHeaders(byte[] data, uint optionalHeaderCount) {
        int tableOffset = 0x18;
        Dictionary<uint, uint> headers = new Dictionary<uint, uint>();
        for (int i = 0; i < optionalHeaderCount; i++) {
            int offset = tableOffset + (i * 8);
            uint key = ReadUInt32BE(data, offset);
            uint value = ReadUInt32BE(data, offset + 4);
            if (headers.ContainsKey(key))
                throw new InvalidDataException($"Duplicate XEX optional header key 0x{key:X8}.");

            headers.Add(key, value);
        }

        return headers;
    }

    private static XexExecutionInfo? ReadExecutionInfo(byte[] data, uint offset) {
        if (!TryReadUInt32BE(data, offset, out uint mediaId) ||
            !TryReadUInt32BE(data, offset + 4, out uint version) ||
            !TryReadUInt32BE(data, offset + 8, out uint baseVersion) ||
            !TryReadUInt32BE(data, offset + 12, out uint titleId) ||
            !TryReadByte(data, offset + 16, out byte platform) ||
            !TryReadByte(data, offset + 17, out byte executableType) ||
            !TryReadByte(data, offset + 18, out byte discNumber) ||
            !TryReadByte(data, offset + 19, out byte discCount) ||
            !TryReadUInt32BE(data, offset + 20, out uint saveGameId))
            throw new InvalidDataException("XEX execution info is truncated.");

        return new XexExecutionInfo {
            MediaId = mediaId,
            Version = version,
            BaseVersion = baseVersion,
            TitleId = titleId,
            Platform = platform,
            ExecutableType = executableType,
            DiscNumber = discNumber,
            DiscCount = discCount,
            SaveGameId = saveGameId
        };
    }

    private static XexBaseFileFormatInfo? ReadBaseFileFormat(byte[] data, uint offset) {
        if (!TryReadUInt32BE(data, offset, out uint size) ||
            !TryReadUInt32BE(data, offset + 4, out uint encryption) ||
            !TryReadUInt32BE(data, offset + 8, out uint compression))
            throw new InvalidDataException("XEX base file format is truncated.");

        return new XexBaseFileFormatInfo {
            Size = size,
            Encryption = encryption,
            Compression = compression
        };
    }

    private static XexImportLibrariesInfo? ReadImportLibraries(byte[] data, uint offset) {
        if (!TryReadUInt32BE(data, offset, out uint size) ||
            !TryReadUInt32BE(data, offset + 4, out uint libraryEntriesSize) ||
            !TryReadUInt32BE(data, offset + 8, out uint libraryCount))
            throw new InvalidDataException("XEX import libraries are truncated.");

        return new XexImportLibrariesInfo {
            Size = size,
            LibraryEntriesSize = libraryEntriesSize,
            LibraryCount = libraryCount
        };
    }

    private static XexHeaderStringInfo? ReadHeaderString(byte[] data, uint offset) {
        if (!TryReadUInt32BE(data, offset, out uint size))
            throw new InvalidDataException("XEX header string is truncated.");

        if (size <= 4)
            return null;
        if (size > int.MaxValue)
            throw new InvalidDataException("XEX header string is invalid.");

        int start = checked((int) offset + 4);
        if (start >= data.Length)
            throw new InvalidDataException("XEX header string is truncated.");

        int declaredLength = checked((int) size - 4);
        if (data.Length - start < declaredLength)
            throw new InvalidDataException("XEX header string is truncated.");

        string text = DecodeHeaderString(data, start, declaredLength);
        return new XexHeaderStringInfo {
            Size = size,
            Text = text
        };
    }

    private static XexSecurityInfo? ReadSecurityInfo(byte[] data, uint offset) {
        const int minimumSize = 0x188;
        if (offset < 0x18u || offset > int.MaxValue || data.Length - (int) offset < minimumSize)
            throw new InvalidDataException("XEX security info is truncated.");

        int start = (int) offset;
        if (!TryReadUInt32BE(data, offset, out uint size) ||
            !TryReadUInt32BE(data, offset + 4, out uint imageSize))
            throw new InvalidDataException("XEX security info is truncated.");

        byte[] signature = data.AsSpan(start + 8, 256).ToArray();
        if (!TryReadUInt32BE(data, offset + 0x10C, out uint infoSize) ||
            !TryReadUInt32BE(data, offset + 0x110, out uint imageFlags) ||
            !TryReadUInt32BE(data, offset + 0x114, out uint loadAddress) ||
            !TryReadBytes(data, offset + 0x118, 20, out byte[] imageHash) ||
            !TryReadUInt32BE(data, offset + 0x12C, out uint importTableCount) ||
            !TryReadBytes(data, offset + 0x130, 20, out byte[] importDigest) ||
            !TryReadBytes(data, offset + 0x144, 16, out byte[] mediaId) ||
            !TryReadBytes(data, offset + 0x154, 16, out byte[] imageKey) ||
            !TryReadUInt32BE(data, offset + 0x164, out uint exportTableAddress) ||
            !TryReadBytes(data, offset + 0x168, 20, out byte[] headerHash) ||
            !TryReadUInt32BE(data, offset + 0x17C, out uint gameRegion) ||
            !TryReadUInt32BE(data, offset + 0x180, out uint allowedMediaTypes) ||
            !TryReadUInt32BE(data, offset + 0x184, out uint pageDescriptorCount))
            throw new InvalidDataException("XEX security info is truncated.");

        return new XexSecurityInfo {
            Size = size,
            ImageSize = imageSize,
            Signature = signature,
            InfoSize = infoSize,
            ImageFlags = imageFlags,
            LoadAddress = loadAddress,
            ImageHash = imageHash,
            ImportTableCount = importTableCount,
            ImportDigest = importDigest,
            MediaId = mediaId,
            ImageKey = imageKey,
            ExportTableAddress = exportTableAddress,
            HeaderHash = headerHash,
            GameRegion = gameRegion,
            AllowedMediaTypes = allowedMediaTypes,
            PageDescriptorCount = pageDescriptorCount
        };
    }

    private static XexExportTableInfo? ReadExportTable(byte[] data, XexSecurityInfo securityInfo) {
        if (securityInfo.ExportTableAddress < securityInfo.LoadAddress)
            throw new InvalidDataException("XEX export table is invalid.");

        uint offset = securityInfo.ExportTableAddress - securityInfo.LoadAddress;
        if (!TryReadUInt32BE(data, offset, out uint magic0) ||
            !TryReadUInt32BE(data, offset + 4, out uint magic1) ||
            !TryReadUInt32BE(data, offset + 8, out uint magic2) ||
            !TryReadUInt32BE(data, offset + 12, out uint moduleNumber0) ||
            !TryReadUInt32BE(data, offset + 16, out uint moduleNumber1) ||
            !TryReadUInt32BE(data, offset + 20, out uint version0) ||
            !TryReadUInt32BE(data, offset + 24, out uint version1) ||
            !TryReadUInt32BE(data, offset + 28, out uint version2) ||
            !TryReadUInt32BE(data, offset + 32, out uint imageBaseAddress) ||
            !TryReadUInt32BE(data, offset + 36, out uint count) ||
            !TryReadUInt32BE(data, offset + 40, out uint baseOrdinal))
            throw new InvalidDataException("XEX export table is truncated.");

        return new XexExportTableInfo {
            Address = securityInfo.ExportTableAddress,
            Magic0 = magic0,
            Magic1 = magic1,
            Magic2 = magic2,
            ModuleNumber0 = moduleNumber0,
            ModuleNumber1 = moduleNumber1,
            Version0 = version0,
            Version1 = version1,
            Version2 = version2,
            ImageBaseAddress = imageBaseAddress,
            Count = count,
            BaseOrdinal = baseOrdinal
        };
    }

    private static XexExportTableInfo? ReadOptionalExportTable(byte[] data, XexSecurityInfo securityInfo) {
        try {
            return ReadExportTable(data, securityInfo);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("export table is truncated", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
    }

    private static bool HasMagic(byte[] data) {
        return data[0] == (byte) 'X' && data[1] == (byte) 'E' && data[2] == (byte) 'X' && data[3] == (byte) '2';
    }

    private static uint ReadUInt32BE(byte[] data, int offset) {
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }

    private static bool TryReadUInt32BE(byte[] data, uint offset, out uint value) {
        if (offset > int.MaxValue || data.Length - (int) offset < 4) {
            value = 0;
            return false;
        }

        value = ReadUInt32BE(data, (int) offset);
        return true;
    }

    private static bool TryReadByte(byte[] data, uint offset, out byte value) {
        if (offset > int.MaxValue || data.Length - (int) offset < 1) {
            value = 0;
            return false;
        }

        value = data[(int) offset];
        return true;
    }

    private static bool TryReadBytes(byte[] data, uint offset, int length, out byte[] bytes) {
        if (offset > int.MaxValue || length < 0 || data.Length - (int) offset < length) {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = data.AsSpan((int) offset, length).ToArray();
        return true;
    }

    private static string DecodeHeaderString(byte[] data, int offset, int length) {
        int end = offset;
        int max = Math.Min(data.Length, offset + length);
        while (end < max && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }
}

internal sealed class XexMetadata {
    public string Magic { get; init; } = string.Empty;
    public XexModuleFlags ModuleFlags { get; init; } = new XexModuleFlags(0);
    public uint PeDataOffset { get; init; }
    public uint SecurityInfoOffset { get; init; }
    public uint OptionalHeaderCount { get; init; }
    public uint HeaderSize { get; init; }
    public uint? OriginalBaseAddress { get; init; }
    public uint? EntryPoint { get; init; }
    public uint? ImageBaseAddress { get; init; }
    public uint? SystemFlags { get; init; }
    public XexExecutionInfo? ExecutionInfo { get; init; }
    public XexBaseFileFormatInfo? BaseFileFormat { get; init; }
    public XexImportLibrariesInfo? ImportLibraries { get; init; }
    public XexHeaderStringInfo? OriginalPeName { get; init; }
    public XexHeaderStringInfo? BoundingPath { get; init; }
    public XexSecurityInfo? SecurityInfo { get; init; }
    public XexExportTableInfo? ExportTable { get; init; }
}

internal sealed record XexModuleFlags(uint Raw) {
    public bool TitleModule => (Raw & 0x00000001) != 0;
    public bool ExportsToTitle => (Raw & 0x00000002) != 0;
    public bool SystemDebugger => (Raw & 0x00000004) != 0;
    public bool DllModule => (Raw & 0x00000008) != 0;
    public bool ModulePatch => (Raw & 0x00000010) != 0;
    public bool FullPatch => (Raw & 0x00000020) != 0;
    public bool DeltaPatch => (Raw & 0x00000040) != 0;
    public bool UserMode => (Raw & 0x00000080) != 0;

    public IReadOnlyList<string> Names {
        get {
            List<string> names = new List<string>();
            if (TitleModule) names.Add("title module");
            if (ExportsToTitle) names.Add("exports to title");
            if (SystemDebugger) names.Add("system debugger");
            if (DllModule) names.Add("dll module");
            if (ModulePatch) names.Add("module patch");
            if (FullPatch) names.Add("full patch");
            if (DeltaPatch) names.Add("delta patch");
            if (UserMode) names.Add("user mode");
            return names;
        }
    }
}

internal sealed class XexExecutionInfo {
    public uint MediaId { get; init; }
    public uint Version { get; init; }
    public uint BaseVersion { get; init; }
    public uint TitleId { get; init; }
    public byte Platform { get; init; }
    public byte ExecutableType { get; init; }
    public byte DiscNumber { get; init; }
    public byte DiscCount { get; init; }
    public uint SaveGameId { get; init; }
}

internal sealed class XexBaseFileFormatInfo {
    public uint Size { get; init; }
    public uint Encryption { get; init; }
    public uint Compression { get; init; }

    public bool IsEncrypted => Encryption != 0;
    public string CompressionName => Compression switch {
        0x00000001 => "not compressed",
        0x00000002 => "compressed",
        0x00000003 => "delta compressed",
        _ => "unknown"
    };
}

internal sealed class XexImportLibrariesInfo {
    public uint Size { get; init; }
    public uint LibraryEntriesSize { get; init; }
    public uint LibraryCount { get; init; }
}

internal sealed class XexHeaderStringInfo {
    public uint Size { get; init; }
    public string Text { get; init; } = string.Empty;
}

internal sealed class XexSecurityInfo {
    public uint Size { get; init; }
    public uint ImageSize { get; init; }
    public byte[] Signature { get; init; } = Array.Empty<byte>();
    public uint InfoSize { get; init; }
    public uint ImageFlags { get; init; }
    public uint LoadAddress { get; init; }
    public byte[] ImageHash { get; init; } = Array.Empty<byte>();
    public uint ImportTableCount { get; init; }
    public byte[] ImportDigest { get; init; } = Array.Empty<byte>();
    public byte[] MediaId { get; init; } = Array.Empty<byte>();
    public byte[] ImageKey { get; init; } = Array.Empty<byte>();
    public uint ExportTableAddress { get; init; }
    public byte[] HeaderHash { get; init; } = Array.Empty<byte>();
    public uint GameRegion { get; init; }
    public uint AllowedMediaTypes { get; init; }
    public uint PageDescriptorCount { get; init; }

    public bool SignaturePresent => Signature.Any(b => b != 0);
}

internal sealed class XexExportTableInfo {
    public uint Address { get; init; }
    public uint Magic0 { get; init; }
    public uint Magic1 { get; init; }
    public uint Magic2 { get; init; }
    public uint ModuleNumber0 { get; init; }
    public uint ModuleNumber1 { get; init; }
    public uint Version0 { get; init; }
    public uint Version1 { get; init; }
    public uint Version2 { get; init; }
    public uint ImageBaseAddress { get; init; }
    public uint Count { get; init; }
    public uint BaseOrdinal { get; init; }
}
