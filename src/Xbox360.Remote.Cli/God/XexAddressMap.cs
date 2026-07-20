namespace Xbox360.Remote.Cli.God;

internal sealed record XexAddressMapResult(
    uint Rva,
    uint? XexLoadBase,
    uint? XexImageBase,
    long? GhidraDelta,
    uint? MappedXexAddress,
    bool? InImage,
    string? Warning);

internal static class XexAddressMap {
    public static bool TryMap(
        XexMetadata metadata,
        uint ghidraBase,
        uint ghidraAddress,
        out XexAddressMapResult result,
        out string? errorMessage) {
        result = new XexAddressMapResult(0, null, null, 0, null, null, null);
        errorMessage = null;

        if (ghidraAddress < ghidraBase) {
            errorMessage = "--ghidra must be greater than or equal to --ghidra-base.";
            return false;
        }

        uint rva = ghidraAddress - ghidraBase;
        uint? loadBase = metadata.SecurityInfo?.LoadAddress ?? metadata.ImageBaseAddress ?? metadata.OriginalBaseAddress;
        uint? imageBase = metadata.ImageBaseAddress ?? metadata.ExportTable?.ImageBaseAddress ?? metadata.OriginalBaseAddress;
        uint? mappedAddress = null;
        string? warning = null;

        if (loadBase.HasValue) {
            ulong computed = (ulong)loadBase.Value + rva;
            if (computed > uint.MaxValue) {
                errorMessage = "Mapped XEX address exceeds 32-bit address space.";
                return false;
            }

            mappedAddress = (uint)computed;
        }
        else {
            warning = "XEX load base is unknown; mapped address unavailable.";
        }

        bool? inImage = null;
        if (metadata.SecurityInfo != null) {
            inImage = rva < metadata.SecurityInfo.ImageSize;
            if (inImage == false) {
                warning = $"RVA 0x{rva:X8} is outside XEX image size 0x{metadata.SecurityInfo.ImageSize:X8}.";
            }
        }

        result = new XexAddressMapResult(
            rva,
            loadBase,
            imageBase,
            loadBase.HasValue ? (long)loadBase.Value - ghidraBase : null,
            mappedAddress,
            inImage,
            warning);
        return true;
    }

    public static string Hex(uint value) => $"0x{value:X8}";

    public static string? Hex(uint? value) => value.HasValue ? Hex(value.Value) : null;

    public static string FormatSignedDelta(long value) {
        string sign = value < 0 ? "-" : "+";
        ulong magnitude = value < 0 ? (ulong)-value : (ulong)value;
        return $"{sign}0x{magnitude:X}";
    }
}
