using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Xbox360.Remote;

internal static class XbdmParamUtils {
    public static bool TryGetString(string text, string key, [NotNullWhen(true)] out string? value) {
        int offset = GetOffsetToValue(text.AsSpan(), key, true);
        return (value = offset >= 0 ? GetValueAt(text.AsSpan(), offset) : null) != null;
    }

    public static bool TryGetUInt32(string text, string key, out uint value) {
        int offset = GetOffsetToValue(text.AsSpan(), key, true);
        if (offset >= 0) {
            string? txt = GetValueAt(text.AsSpan(), offset, 13, 13);
            NumberStyles ns = txt != null && txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.HexNumber : NumberStyles.Integer;
            return uint.TryParse(txt.AsSpan(ns == NumberStyles.HexNumber ? 2 : 0), ns, CultureInfo.InvariantCulture, out value);
        }

        value = 0;
        return false;
    }

    public static bool TryGetUInt64(string text, string key, out ulong value) {
        int offset = GetOffsetToValue(text.AsSpan(), key, true);
        if (offset >= 0) {
            string? txt = GetValueAt(text.AsSpan(), offset, 24, 24);
            NumberStyles ns = txt != null && (txt.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || txt.StartsWith("0q", StringComparison.OrdinalIgnoreCase))
                ? NumberStyles.HexNumber
                : NumberStyles.Integer;
            return ulong.TryParse(txt.AsSpan(ns == NumberStyles.HexNumber ? 2 : 0), ns, CultureInfo.InvariantCulture, out value);
        }

        value = 0;
        return false;
    }

    public static bool HasFlag(string text, string key) {
        return GetOffsetToValue(text.AsSpan(), key, false) >= 0;
    }

    private static int GetOffsetToValue(ReadOnlySpan<char> text, string key, bool valueRequired) {
        ReadOnlySpan<char> keySpan = key.AsSpan();
        bool inQuote = false;
        int i = 0, j, offset;

        while (i < text.Length) {
            while (i < text.Length && IsSpaceOrEol(text[i]))
                i++;
            if (i == text.Length)
                return -1;

            for (j = 0; (offset = i + j) < text.Length && !IsSpaceOrEol(text[offset]); j++) {
                if (text[offset] == '=') {
                    if (MatchRegion(keySpan, text, j, i))
                        return offset + 1;
                    break;
                }
            }

            if (!valueRequired && (offset >= text.Length || text[offset] != '=') && MatchRegion(keySpan, text, j, i)) {
                return offset;
            }

            for (i = offset; i < text.Length && (!IsSpaceOrEol(text[i]) || inQuote); i++) {
                if (text[i] == '"')
                    inQuote = !inQuote;
            }
        }

        return -1;
    }

    private static string? GetValueAt(ReadOnlySpan<char> text, int offset, int initialCapacity = 64, int maxChars = -1) {
        bool inQuote = false;
        StringBuilder sb = new StringBuilder(maxChars < 0 ? initialCapacity : Math.Min(initialCapacity, maxChars));
        while (offset < text.Length && (!IsSpaceOrEol(text[offset]) || inQuote)) {
            if (text[offset] == '"') {
                if (inQuote && offset != text.Length - 1 && text[offset + 1] == '"') {
                    if (maxChars >= 0 && sb.Length == maxChars)
                        return null;
                    sb.Append('"');
                    offset += 2;
                }
                else {
                    inQuote = !inQuote;
                    offset++;
                }
            }
            else {
                if (maxChars >= 0 && sb.Length == maxChars)
                    return null;
                sb.Append(text[offset++]);
            }
        }

        return sb.ToString();
    }

    private static bool MatchRegion(ReadOnlySpan<char> key, ReadOnlySpan<char> text, int count, int offset) {
        int i = 0;
        while (count-- != 0 && i < key.Length) {
            char ch1 = char.ToLowerInvariant(key[i++]);
            char ch2 = char.ToLowerInvariant(text[offset++]);
            if (ch1 != ch2)
                return false;
        }

        return count < 0 && i >= key.Length;
    }

    private static bool IsSpaceOrEol(char ch) => ch == ' ' || ch == '\r' || ch == 0;
}
