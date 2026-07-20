using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarPackageReader {
    private const int HeaderReadLength = 0x1800;
    private const int TitleIdOffset = 0x360;
    private const int DisplayNameRegionStart = 0x410;
    private const int DisplayNameRegionLength = 0x400;
    private const int PublisherRegionStart = 0x1610;
    private const int PublisherRegionLength = 0x80;
    private const int GameNameRegionStart = 0x1690;
    private const int GameNameRegionLength = 0x80;

    public static AvatarPackageMetadata ReadMetadata(string filePath) {
        using FileStream stream = File.OpenRead(filePath);
        int readLength = (int)Math.Min(HeaderReadLength, stream.Length);
        byte[] buffer = new byte[readLength];
        int read = 0;
        while (read < buffer.Length) {
            int bytesRead = stream.Read(buffer, read, buffer.Length - read);
            if (bytesRead == 0)
                break;
            read += bytesRead;
        }

        if (read < 4)
            return new AvatarPackageMetadata(AvatarPackageMagic.Unknown, 0, null, null, null);

        AvatarPackageMagic magic = ParseMagic(buffer);
        uint titleId = read >= TitleIdOffset + 4
            ? ReadUInt32BigEndian(buffer, TitleIdOffset)
            : 0;

        string? displayName = NormalizeDisplayName(ReadUtf16LeRegion(buffer, DisplayNameRegionStart, DisplayNameRegionLength));
        string? publisher = NormalizePublisher(ReadUtf16LeRegion(buffer, PublisherRegionStart, PublisherRegionLength));
        string? gameName = NormalizeGameName(ReadUtf16LeRegion(buffer, GameNameRegionStart, GameNameRegionLength));

        return new AvatarPackageMetadata(magic, titleId, displayName, publisher, gameName);
    }

    public static bool TryParseTitleIdFromPath(string path, out uint titleId) {
        titleId = 0;
        DirectoryInfo? current = Directory.GetParent(path);
        while (current != null) {
            if (uint.TryParse(current.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId))
                return true;
            current = current.Parent;
        }

        return false;
    }

    public static bool IsSupportedPackage(string filePath) {
        using FileStream stream = File.OpenRead(filePath);
        Span<byte> magic = stackalloc byte[4];
        if (stream.Read(magic) != magic.Length)
            return false;
        return magic.SequenceEqual("LIVE"u8) || magic.SequenceEqual("PIRS"u8) || magic.SequenceEqual("CON "u8);
    }

    private static AvatarPackageMagic ParseMagic(ReadOnlySpan<byte> buffer) {
        ReadOnlySpan<byte> magic = buffer[..4];
        if (magic.SequenceEqual("LIVE"u8))
            return AvatarPackageMagic.Live;
        if (magic.SequenceEqual("PIRS"u8))
            return AvatarPackageMagic.Pirs;
        if (magic.SequenceEqual("CON "u8))
            return AvatarPackageMagic.Con;
        return AvatarPackageMagic.Unknown;
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset) {
        return (uint)(
            (buffer[offset] << 24) |
            (buffer[offset + 1] << 16) |
            (buffer[offset + 2] << 8) |
            buffer[offset + 3]);
    }

    private static string? ReadUtf16LeRegion(byte[] buffer, int start, int length) {
        if (start >= buffer.Length)
            return null;

        int end = Math.Min(buffer.Length, start + length);
        int actualStart = start;
        while (actualStart + 1 < end && buffer[actualStart] == 0 && buffer[actualStart + 1] == 0) {
            actualStart += 2;
        }

        if (actualStart + 1 >= end)
            return null;

        int actualEnd = actualStart;
        while (actualEnd + 1 < end) {
            if (buffer[actualEnd] == 0 && buffer[actualEnd + 1] == 0)
                break;
            actualEnd += 2;
        }

        int count = actualEnd - actualStart;
        if (count <= 0)
            return null;

        string value = Encoding.Unicode.GetString(buffer, actualStart, count).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? NormalizeDisplayName(string? value) {
        return NormalizeCommon(value, stripLeadingNoisePrefix: false);
    }

    private static string? NormalizePublisher(string? value) {
        string? normalized = NormalizeCommon(value, stripLeadingNoisePrefix: true);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        if (normalized.Equals("Xbox Live", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("XBOX LIVE", StringComparison.OrdinalIgnoreCase)) {
            return "Xbox LIVE";
        }

        return normalized;
    }

    private static string? NormalizeGameName(string? value) {
        return NormalizeCommon(value, stripLeadingNoisePrefix: true);
    }

    private static string? NormalizeCommon(string? value, bool stripLeadingNoisePrefix) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Normalize(NormalizationForm.FormKC)
            .Replace('\u0000', ' ')
            .Replace('\uFEFF', ' ')
            .Trim();

        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = RepairCommonMojibake(normalized);

        if (stripLeadingNoisePrefix) {
            while (normalized.Length >= 2 && (IsNoisePrefix(normalized[0]) || normalized[0] > 127) && IsLikelyWordStart(normalized[1])) {
                normalized = normalized[1..].TrimStart();
            }

            while (normalized.Length >= 3 &&
                   char.IsUpper(normalized[0]) &&
                   normalized[0] == normalized[1] &&
                   char.IsUpper(normalized[1]) &&
                   char.IsLetter(normalized[2])) {
                normalized = normalized[1..].TrimStart();
            }
        }

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string RepairCommonMojibake(string value) {
        return value
            .Replace("\u84E2\u00A2", "™", StringComparison.Ordinal)
            .Replace("\u80E2\u0099", "'", StringComparison.Ordinal)
            .Replace("蓢¢", "™", StringComparison.Ordinal)
            .Replace("胢", "'", StringComparison.Ordinal)
            .Replace("Xbox Live", "Xbox LIVE", StringComparison.Ordinal);
    }

    private static bool IsNoisePrefix(char value) {
        return value is '.' or '-' or '_' or '+' || char.IsLower(value);
    }

    private static bool IsLikelyWordStart(char value) {
        return char.IsUpper(value) || char.IsDigit(value) || value == '#';
    }
}
