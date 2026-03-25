using System.Globalization;
using System.Text;

namespace Xbox360.Remote.Cli;

internal sealed record TitleIdEntry(
    uint TitleId,
    uint? MediaId,
    string Name,
    string? Serial,
    string? Type,
    string? Region,
    string? XexCrc,
    string? Wave);

internal sealed class TitleIdDatabase {
    private static readonly Lazy<TitleIdDatabase> LazyInstance = new Lazy<TitleIdDatabase>(Load);
    private readonly Dictionary<uint, List<TitleIdEntry>> byTitleId;

    private TitleIdDatabase(Dictionary<uint, List<TitleIdEntry>> byTitleId) {
        this.byTitleId = byTitleId;
    }

    public static TitleIdDatabase Instance => LazyInstance.Value;

    public bool TryResolve(uint titleId, uint? mediaId, out TitleIdEntry? entry) {
        entry = null;
        if (!byTitleId.TryGetValue(titleId, out List<TitleIdEntry>? entries) || entries.Count == 0)
            return false;

        if (mediaId.HasValue) {
            entry = entries.FirstOrDefault(e => e.MediaId == mediaId.Value) ?? entries.FirstOrDefault();
            return entry != null;
        }

        entry = entries[0];
        return true;
    }

    public IReadOnlyList<TitleIdEntry> FindAll(uint titleId) {
        return byTitleId.TryGetValue(titleId, out List<TitleIdEntry>? entries)
            ? entries
            : Array.Empty<TitleIdEntry>();
    }

    private static TitleIdDatabase Load() {
        Dictionary<uint, List<TitleIdEntry>> data = new Dictionary<uint, List<TitleIdEntry>>();
        foreach (string path in GetSourcePaths()) {
            if (!File.Exists(path))
                continue;
            try {
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) {
                    LoadCsv(path, data);
                }
                else if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
                    LoadTildeList(path, data);
                }
            }
            catch {
                // ignored
            }
        }

        return new TitleIdDatabase(data);
    }

    private static IEnumerable<string> GetSourcePaths() {
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Assets", "xbox360_gamelist.csv");
        yield return Path.Combine(baseDir, "Assets", "xbox360_titleids.txt");

        string local = Path.Combine(CliPaths.ConfigDirectory, "titleids.local.csv");
        yield return local;
    }

    private static void LoadCsv(string path, Dictionary<uint, List<TitleIdEntry>> data) {
        using StreamReader reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        bool headerSkipped = false;
        while (!reader.EndOfStream) {
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> fields = ParseCsvLine(line);
            if (fields.Count < 2)
                continue;

            if (!headerSkipped) {
                headerSkipped = true;
                if (fields[0].Contains("Game", StringComparison.OrdinalIgnoreCase) &&
                    fields.Any(f => f.Contains("Title", StringComparison.OrdinalIgnoreCase))) {
                    continue;
                }
            }

            string name = fields.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
            string titleIdText = fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
            string? serial = fields.ElementAtOrDefault(2)?.Trim();
            string? type = fields.ElementAtOrDefault(3)?.Trim();
            string? region = fields.ElementAtOrDefault(4)?.Trim();
            string? xexCrc = fields.ElementAtOrDefault(5)?.Trim();
            string? mediaIdText = fields.ElementAtOrDefault(6)?.Trim();
            string? wave = fields.ElementAtOrDefault(7)?.Trim();

            if (!TryParseHex(titleIdText, out uint titleId))
                continue;

            uint? mediaId = TryParseHex(mediaIdText, out uint mediaParsed) ? mediaParsed : null;
            if (string.IsNullOrWhiteSpace(name))
                name = $"Title {titleId:X8}";

            TitleIdEntry entry = new TitleIdEntry(
                titleId,
                mediaId,
                name,
                serial,
                type,
                region,
                xexCrc,
                wave);

            if (!data.TryGetValue(titleId, out List<TitleIdEntry>? list)) {
                list = new List<TitleIdEntry>();
                data[titleId] = list;
            }

            list.Add(entry);
        }
    }

    private static void LoadTildeList(string path, Dictionary<uint, List<TitleIdEntry>> data) {
        foreach (string rawLine in File.ReadLines(path)) {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            int split = line.IndexOf('~');
            if (split <= 0 || split == line.Length - 1)
                continue;

            string titleIdText = line.Substring(0, split).Trim();
            string name = line.Substring(split + 1).Trim();
            if (!TryParseHex(titleIdText, out uint titleId))
                continue;
            if (string.IsNullOrWhiteSpace(name))
                name = $"Title {titleId:X8}";

            TitleIdEntry entry = new TitleIdEntry(
                titleId,
                null,
                name,
                null,
                null,
                null,
                null,
                null);

            if (!data.TryGetValue(titleId, out List<TitleIdEntry>? list)) {
                list = new List<TitleIdEntry>();
                data[titleId] = list;
            }

            if (list.Any(e => e.MediaId == null && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add(entry);
        }
    }

    private static bool TryParseHex(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Substring(2);
        return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static List<string> ParseCsvLine(string line) {
        List<string> fields = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++) {
            char ch = line[i];
            if (inQuotes) {
                if (ch == '"') {
                    if (i + 1 < line.Length && line[i + 1] == '"') {
                        current.Append('"');
                        i++;
                    }
                    else {
                        inQuotes = false;
                    }
                }
                else {
                    current.Append(ch);
                }
                continue;
            }

            if (ch == '"') {
                inQuotes = true;
                continue;
            }

            if (ch == ',') {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        fields.Add(current.ToString());
        return fields;
    }
}
