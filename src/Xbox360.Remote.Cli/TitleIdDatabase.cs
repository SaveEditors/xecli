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

internal sealed record TitleIdDatabaseWarning(string Path, int? Line, string Message) {
    public override string ToString() {
        string location = Line.HasValue ? $"{Path}:{Line.Value}" : Path;
        return $"{location}: {Message}";
    }
}

internal sealed record TitleIdDatabaseStatus(
    string SourceKind,
    string? SourcePath,
    int EntryCount,
    int UserEntryCount,
    int CacheEntryCount,
    IReadOnlyList<TitleIdDatabaseWarning> Warnings);

internal sealed class TitleIdDatabase {
    private const int MaxWarnings = 100;
    private static readonly object InstanceGate = new();
    private static readonly HashSet<string> GenericExecutableNames = new(StringComparer.OrdinalIgnoreCase) {
        "dash",
        "default",
        "game",
        "launch",
        "launcher",
        "xam",
        "xboxkrnl",
        "xshell"
    };

    private static TitleIdDatabase? instance;
    private readonly Dictionary<uint, List<TitleIdEntry>> byTitleId;
    private readonly HashSet<uint> userTitleIds;

    private TitleIdDatabase(
        Dictionary<uint, List<TitleIdEntry>> byTitleId,
        HashSet<uint> userTitleIds,
        TitleIdDatabaseStatus status) {
        this.byTitleId = byTitleId;
        this.userTitleIds = userTitleIds;
        Status = status;
    }

    public static TitleIdDatabase Instance {
        get {
            lock (InstanceGate) {
                return instance ??= LoadConfigured();
            }
        }
    }

    public TitleIdDatabaseStatus Status { get; }

    public IReadOnlyList<TitleIdDatabaseWarning> Warnings => Status.Warnings;

    public static void Reload() {
        lock (InstanceGate) {
            instance = LoadConfigured();
        }
    }

    internal static TitleIdDatabase LoadForCommandPath(string path) {
        string fullPath;
        try {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return CreateInvalidPathDatabase("command", path, ex.Message);
        }

        return LoadDatabase(new UserSource("command", fullPath), includeCache: true);
    }

    internal static TitleIdDatabaseStatus InspectUserFile(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return new TitleIdDatabaseStatus(
                "none",
                null,
                0,
                0,
                0,
                Array.Empty<TitleIdDatabaseWarning>());
        }

        try {
            string fullPath = CliPaths.ResolveTitleDatabasePath(path);
            return LoadDatabase(new UserSource("preview", fullPath), includeCache: false).Status;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return CreateInvalidPathDatabase("preview", path, ex.Message).Status;
        }
    }

    internal static string GetSettingsPath(CliConfig config) {
        if (!string.IsNullOrWhiteSpace(config.TitleDatabasePath)) {
            try {
                return CliPaths.ResolveTitleDatabasePath(config.TitleDatabasePath);
            }
            catch (Exception) {
                return config.TitleDatabasePath;
            }
        }

        if (File.Exists(CliPaths.DefaultTitleDatabasePath))
            return CliPaths.DefaultTitleDatabasePath;
        if (File.Exists(CliPaths.LegacyTitleDatabasePath))
            return CliPaths.LegacyTitleDatabasePath;
        return string.Empty;
    }

    public bool TryResolve(uint titleId, uint? mediaId, out TitleIdEntry? entry) {
        entry = null;
        if (!byTitleId.TryGetValue(titleId, out List<TitleIdEntry>? entries) || entries.Count == 0)
            return false;

        if (mediaId.HasValue) {
            entry = entries.FirstOrDefault(candidate => candidate.MediaId == mediaId.Value) ?? entries[0];
            return true;
        }

        entry = entries[0];
        return true;
    }

    public string ResolveNameOrHex(uint titleId, uint? mediaId = null) {
        return TryResolve(titleId, mediaId, out TitleIdEntry? entry) && entry != null
            ? entry.Name
            : FormatTitleId(titleId);
    }

    public IReadOnlyList<TitleIdEntry> FindAll(uint titleId) {
        return byTitleId.TryGetValue(titleId, out List<TitleIdEntry>? entries)
            ? entries
            : Array.Empty<TitleIdEntry>();
    }

    public IReadOnlyList<TitleIdEntry> FindByName(string? query) {
        string normalizedQuery = NormalizeSearchText(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return Array.Empty<TitleIdEntry>();

        return byTitleId.Values
            .SelectMany(entries => entries)
            .Select(entry => new SearchCandidate(entry, GetSearchRank(entry, normalizedQuery)))
            .Where(candidate => candidate.Rank >= 0)
            .OrderBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Entry.TitleId)
            .ThenBy(candidate => candidate.Entry.MediaId ?? uint.MaxValue)
            .Select(candidate => candidate.Entry)
            .ToArray();
    }

    public bool TryRememberDiscoveredTitle(uint titleId, string? runningXex, out string? discoveredName) {
        discoveredName = TryGetTrustedExecutableName(titleId, runningXex);
        if (discoveredName == null || userTitleIds.Contains(titleId))
            return false;

        try {
            lock (InstanceGate) {
                Dictionary<uint, List<TitleIdEntry>> cache = new();
                WarningCollector ignoredWarnings = new();
                if (File.Exists(CliPaths.DiscoveredTitleDatabasePath))
                    cache = LoadSource(CliPaths.DiscoveredTitleDatabasePath, ignoredWarnings);

                TitleIdEntry entry = new(titleId, null, discoveredName, null, "live-discovery", null, null, null);
                AddOrReplaceEntry(cache, entry, CliPaths.DiscoveredTitleDatabasePath, null, ignoredWarnings, warnOnDuplicate: false);
                WriteDiscoveryCache(cache);
                instance = LoadConfigured();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
            return false;
        }

        return true;
    }

    public static string FormatTitleId(uint titleId) => $"0x{titleId:X8}";

    private static TitleIdDatabase LoadConfigured() {
        UserSource source = ResolveConfiguredUserSource();
        return LoadDatabase(source, includeCache: true);
    }

    private static UserSource ResolveConfiguredUserSource() {
        string? environmentPath = Environment.GetEnvironmentVariable(CliPaths.TitleDatabaseEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentPath))
            return ResolveStoredSource("environment", environmentPath);

        CliConfig.TryLoad(out CliConfig config);
        if (!string.IsNullOrWhiteSpace(config.TitleDatabasePath))
            return ResolveStoredSource("config", config.TitleDatabasePath);
        if (File.Exists(CliPaths.DefaultTitleDatabasePath))
            return new UserSource("default", CliPaths.DefaultTitleDatabasePath);
        if (File.Exists(CliPaths.LegacyTitleDatabasePath))
            return new UserSource("legacy", CliPaths.LegacyTitleDatabasePath);
        return new UserSource("none", null);
    }

    private static UserSource ResolveStoredSource(string kind, string path) {
        try {
            return new UserSource(kind, CliPaths.ResolveTitleDatabasePath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return new UserSource(kind, path, ex.Message);
        }
    }

    private static TitleIdDatabase LoadDatabase(UserSource source, bool includeCache) {
        Dictionary<uint, List<TitleIdEntry>> data = new();
        HashSet<uint> userTitles = new();
        WarningCollector warnings = new();
        int cacheEntryCount = 0;
        int userEntryCount = 0;

        if (includeCache && File.Exists(CliPaths.DiscoveredTitleDatabasePath)) {
            Dictionary<uint, List<TitleIdEntry>> cache = LoadSource(CliPaths.DiscoveredTitleDatabasePath, warnings);
            ReplaceTitles(data, cache);
            cacheEntryCount = CountEntries(cache);
        }

        if (source.InvalidReason != null) {
            warnings.Add(source.Path ?? "Title Database", null, $"Invalid path: {source.InvalidReason}");
        }
        else if (!string.IsNullOrWhiteSpace(source.Path)) {
            Dictionary<uint, List<TitleIdEntry>> userData = LoadSource(source.Path, warnings, warnIfMissing: true);
            ReplaceTitles(data, userData);
            userTitles.UnionWith(userData.Keys);
            userEntryCount = CountEntries(userData);
        }

        TitleIdDatabaseStatus status = new(
            source.Kind,
            source.Path,
            CountEntries(data),
            userEntryCount,
            cacheEntryCount,
            warnings.ToArray());
        return new TitleIdDatabase(data, userTitles, status);
    }

    private static TitleIdDatabase CreateInvalidPathDatabase(string kind, string path, string message) {
        WarningCollector warnings = new();
        warnings.Add(path, null, $"Invalid path: {message}");
        return new TitleIdDatabase(
            new Dictionary<uint, List<TitleIdEntry>>(),
            new HashSet<uint>(),
            new TitleIdDatabaseStatus(kind, path, 0, 0, 0, warnings.ToArray()));
    }

    private static Dictionary<uint, List<TitleIdEntry>> LoadSource(
        string path,
        WarningCollector warnings,
        bool warnIfMissing = false) {
        Dictionary<uint, List<TitleIdEntry>> data = new();
        if (!File.Exists(path)) {
            if (warnIfMissing)
                warnings.Add(path, null, "File was not found.");
            return data;
        }

        try {
            string extension = Path.GetExtension(path);
            if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                LoadCsv(path, data, warnings);
            else if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                LoadText(path, data, warnings);
            else
                warnings.Add(path, null, "Unsupported file type. Use a .csv or .txt file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) {
            warnings.Add(path, null, $"Could not read file: {ex.Message}");
        }

        return data;
    }

    private static void LoadCsv(
        string path,
        Dictionary<uint, List<TitleIdEntry>> data,
        WarningCollector warnings) {
        using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        List<CsvRecord> records = ReadCsvRecords(reader, path, warnings);
        CsvColumns? columns = null;

        foreach (CsvRecord record in records) {
            if (IsCommentOrEmpty(record.Fields))
                continue;

            if (columns == null && TryCreateHeaderColumns(record.Fields, out CsvColumns? headerColumns)) {
                columns = headerColumns;
                continue;
            }

            CsvColumns effectiveColumns = columns ?? InferColumns(record.Fields);
            if (!TryCreateCsvEntry(record, effectiveColumns, path, warnings, out TitleIdEntry? entry))
                continue;
            AddOrReplaceEntry(data, entry!, path, record.Line, warnings, warnOnDuplicate: true);
        }
    }

    private static void LoadText(
        string path,
        Dictionary<uint, List<TitleIdEntry>> data,
        WarningCollector warnings) {
        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(path, Encoding.UTF8)) {
            lineNumber++;
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            int split = FindTextSeparator(line);
            if (split <= 0 || split == line.Length - 1) {
                warnings.Add(path, lineNumber, "Expected 'Title ID~Name', tab-separated, or 'Title ID=Name' data.");
                continue;
            }

            string left = line[..split].Trim();
            string right = line[(split + 1)..].Trim();
            string titleIdText;
            string name;
            if (TryParseHex(left, out _)) {
                titleIdText = left;
                name = right;
            }
            else if (TryParseHex(right, out _)) {
                titleIdText = right;
                name = left;
            }
            else {
                warnings.Add(path, lineNumber, "Title ID is not valid hexadecimal.");
                continue;
            }

            if (!TryParseHex(titleIdText, out uint titleId)) {
                warnings.Add(path, lineNumber, "Title ID is not valid hexadecimal.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(name)) {
                warnings.Add(path, lineNumber, "Title name is empty; the raw hexadecimal ID will be used.");
                name = FormatTitleId(titleId);
            }

            TitleIdEntry entry = new(titleId, null, name, null, null, null, null, null);
            AddOrReplaceEntry(data, entry, path, lineNumber, warnings, warnOnDuplicate: true);
        }
    }

    private static bool TryCreateCsvEntry(
        CsvRecord record,
        CsvColumns columns,
        string path,
        WarningCollector warnings,
        out TitleIdEntry? entry) {
        entry = null;
        string titleIdText = GetField(record.Fields, columns.TitleId);
        if (!TryParseHex(titleIdText, out uint titleId)) {
            warnings.Add(path, record.Line, "Title ID is missing or is not valid hexadecimal.");
            return false;
        }

        string name = GetField(record.Fields, columns.Name);
        if (string.IsNullOrWhiteSpace(name)) {
            warnings.Add(path, record.Line, "Title name is empty; the raw hexadecimal ID will be used.");
            name = FormatTitleId(titleId);
        }

        string mediaIdText = GetField(record.Fields, columns.MediaId);
        uint? mediaId = null;
        if (!string.IsNullOrWhiteSpace(mediaIdText)) {
            if (TryParseHex(mediaIdText, out uint parsedMediaId))
                mediaId = parsedMediaId;
            else
                warnings.Add(path, record.Line, "Media ID is not valid hexadecimal and was ignored.");
        }

        entry = new TitleIdEntry(
            titleId,
            mediaId,
            name.Trim(),
            NullIfWhiteSpace(GetField(record.Fields, columns.Serial)),
            NullIfWhiteSpace(GetField(record.Fields, columns.Type)),
            NullIfWhiteSpace(GetField(record.Fields, columns.Region)),
            NullIfWhiteSpace(GetField(record.Fields, columns.XexCrc)),
            NullIfWhiteSpace(GetField(record.Fields, columns.Wave)));
        return true;
    }

    private static bool TryCreateHeaderColumns(IReadOnlyList<string> fields, out CsvColumns? columns) {
        Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < fields.Count; index++) {
            string normalized = NormalizeHeader(fields[index]);
            if (!string.IsNullOrWhiteSpace(normalized))
                map.TryAdd(normalized, index);
        }

        int titleId = FindHeader(map, "titleid", "titleidentifier");
        if (titleId < 0) {
            columns = null;
            return false;
        }

        columns = new CsvColumns(
            titleId,
            FindHeader(map, "name", "gamename", "title", "titlename", "displayname"),
            FindHeader(map, "mediaid", "mediaidentifier"),
            FindHeader(map, "serial", "serialnumber"),
            FindHeader(map, "type", "titletype"),
            FindHeader(map, "region"),
            FindHeader(map, "xexcrc", "crc"),
            FindHeader(map, "wave"));
        return true;
    }

    private static CsvColumns InferColumns(IReadOnlyList<string> fields) {
        bool titleIdFirst = fields.Count > 1 && TryParseHex(fields[0], out _) && !TryParseHex(fields[1], out _);
        return titleIdFirst
            ? new CsvColumns(0, 1, -1, -1, -1, -1, -1, -1)
            : new CsvColumns(1, 0, 6, 2, 3, 4, 5, 7);
    }

    private static List<CsvRecord> ReadCsvRecords(TextReader reader, string path, WarningCollector warnings) {
        List<CsvRecord> records = new();
        List<string> fields = new();
        StringBuilder field = new();
        bool inQuotes = false;
        bool afterQuote = false;
        bool malformedTailReported = false;
        int line = 1;
        int recordLine = 1;

        while (reader.Read() is int value && value >= 0) {
            char current = (char)value;
            if (inQuotes) {
                if (current == '"') {
                    if (reader.Peek() == '"') {
                        reader.Read();
                        field.Append('"');
                    }
                    else {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                else {
                    field.Append(current);
                    if (current == '\n')
                        line++;
                }
                continue;
            }

            if (current == '"' && field.Length == 0 && !afterQuote) {
                inQuotes = true;
                continue;
            }
            if (current == '"' && !afterQuote) {
                warnings.Add(path, recordLine, "Unexpected quote in an unquoted CSV field.");
                field.Append(current);
                continue;
            }
            if (current == ',') {
                fields.Add(field.ToString());
                field.Clear();
                afterQuote = false;
                malformedTailReported = false;
                continue;
            }
            if (current is '\r' or '\n') {
                if (current == '\r' && reader.Peek() == '\n')
                    reader.Read();
                fields.Add(field.ToString());
                AddCsvRecord(records, fields, recordLine);
                fields = new List<string>();
                field.Clear();
                afterQuote = false;
                malformedTailReported = false;
                line++;
                recordLine = line;
                continue;
            }
            if (afterQuote && !char.IsWhiteSpace(current) && !malformedTailReported) {
                warnings.Add(path, recordLine, "Unexpected characters after a closing CSV quote.");
                malformedTailReported = true;
            }
            if (!afterQuote || !char.IsWhiteSpace(current))
                field.Append(current);
        }

        if (inQuotes)
            warnings.Add(path, recordLine, "Unterminated quoted CSV field; the partial record was still parsed.");
        if (field.Length > 0 || fields.Count > 0) {
            fields.Add(field.ToString());
            AddCsvRecord(records, fields, recordLine);
        }
        return records;
    }

    private static void AddCsvRecord(List<CsvRecord> records, List<string> fields, int line) {
        if (fields.Any(field => !string.IsNullOrWhiteSpace(field)))
            records.Add(new CsvRecord(line, fields.ToArray()));
    }

    private static bool IsCommentOrEmpty(IReadOnlyList<string> fields) {
        if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
            return true;
        string first = fields[0].TrimStart();
        return first.StartsWith('#') || first.StartsWith(';') || first.StartsWith("//", StringComparison.Ordinal);
    }

    private static void AddOrReplaceEntry(
        Dictionary<uint, List<TitleIdEntry>> data,
        TitleIdEntry entry,
        string path,
        int? line,
        WarningCollector warnings,
        bool warnOnDuplicate) {
        if (!data.TryGetValue(entry.TitleId, out List<TitleIdEntry>? entries)) {
            entries = new List<TitleIdEntry>();
            data[entry.TitleId] = entries;
        }

        int duplicateIndex = entries.FindIndex(candidate => candidate.MediaId == entry.MediaId);
        if (duplicateIndex >= 0) {
            if (warnOnDuplicate)
                warnings.Add(path, line, $"Duplicate {FormatTitleId(entry.TitleId)} entry replaced the earlier value (last entry wins).");
            entries[duplicateIndex] = entry;
        }
        else {
            entries.Add(entry);
        }
    }

    private static void ReplaceTitles(
        Dictionary<uint, List<TitleIdEntry>> target,
        Dictionary<uint, List<TitleIdEntry>> source) {
        foreach ((uint titleId, List<TitleIdEntry> entries) in source)
            target[titleId] = new List<TitleIdEntry>(entries);
    }

    private static void WriteDiscoveryCache(Dictionary<uint, List<TitleIdEntry>> cache) {
        StringBuilder output = new();
        output.AppendLine("Title ID,Name,Source");
        foreach (TitleIdEntry entry in cache.Values.SelectMany(entries => entries).OrderBy(entry => entry.TitleId)) {
            output.Append(FormatTitleId(entry.TitleId)).Append(',')
                .Append(EscapeCsv(entry.Name)).Append(',')
                .AppendLine("live-console");
        }
        AtomicFileWriter.WriteAllText(CliPaths.DiscoveredTitleDatabasePath, output.ToString());
    }

    private static string? TryGetTrustedExecutableName(uint titleId, string? runningXex) {
        if (titleId == 0 || string.IsNullOrWhiteSpace(runningXex))
            return null;
        string normalizedPath = runningXex.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (!Path.GetExtension(normalizedPath).Equals(".xex", StringComparison.OrdinalIgnoreCase))
            return null;

        string name = Path.GetFileNameWithoutExtension(normalizedPath).Trim().Replace('_', ' ');
        if (name.Length is < 2 or > 80 || GenericExecutableNames.Contains(name) || !name.Any(char.IsLetter))
            return null;
        if (TryParseHex(name, out uint parsed) && parsed == titleId)
            return null;
        return name;
    }

    private static int FindTextSeparator(string line) {
        foreach (char separator in new[] { '~', '\t', '=' }) {
            int index = line.IndexOf(separator);
            if (index >= 0)
                return index;
        }
        return -1;
    }

    private static int CountEntries(Dictionary<uint, List<TitleIdEntry>> data) => data.Values.Sum(entries => entries.Count);

    private static int FindHeader(IReadOnlyDictionary<string, int> map, params string[] names) {
        foreach (string name in names) {
            if (map.TryGetValue(name, out int index))
                return index;
        }
        return -1;
    }

    private static string NormalizeHeader(string value) {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string GetField(IReadOnlyList<string> fields, int index) {
        return index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeCsv(string value) {
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
    }

    private static int GetSearchRank(TitleIdEntry entry, string normalizedQuery) {
        string normalizedName = NormalizeSearchText(entry.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return -1;
        if (string.Equals(normalizedName, normalizedQuery, StringComparison.Ordinal))
            return 0;
        if (normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal))
            return 1;
        if (normalizedName.Contains(normalizedQuery, StringComparison.Ordinal))
            return 2;
        return ContainsTokenSubsequence(normalizedName, normalizedQuery) ? 3 : -1;
    }

    private static bool ContainsTokenSubsequence(string normalizedName, string normalizedQuery) {
        string[] nameTokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.Length == 0)
            return false;

        int nameIndex = 0;
        foreach (string queryToken in queryTokens) {
            bool matched = false;
            for (; nameIndex < nameTokens.Length; nameIndex++) {
                if (nameTokens[nameIndex].Contains(queryToken, StringComparison.Ordinal)) {
                    matched = true;
                    nameIndex++;
                    break;
                }
            }
            if (!matched)
                return false;
        }
        return true;
    }

    private static string NormalizeSearchText(string? text) {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;
        foreach (char rawChar in text.Normalize(NormalizationForm.FormKC)) {
            if (char.IsLetterOrDigit(rawChar)) {
                if (pendingSpace && builder.Length > 0)
                    builder.Append(' ');
                builder.Append(char.ToLowerInvariant(rawChar));
                pendingSpace = false;
            }
            else if (builder.Length > 0) {
                pendingSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static bool TryParseHex(string? text, out uint value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string cleaned = text.Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];
        return cleaned.Length is > 0 and <= 8 &&
            uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private sealed class WarningCollector {
        private readonly List<TitleIdDatabaseWarning> warnings = new();
        private int suppressed;

        internal void Add(string path, int? line, string message) {
            if (warnings.Count < MaxWarnings) {
                TitleIdDatabaseWarning warning = new(path, line, message);
                if (!warnings.Contains(warning))
                    warnings.Add(warning);
            }
            else {
                suppressed++;
            }
        }

        internal IReadOnlyList<TitleIdDatabaseWarning> ToArray() {
            if (suppressed == 0)
                return warnings.ToArray();
            return warnings.Append(new TitleIdDatabaseWarning(
                "Title Database",
                null,
                $"{suppressed.ToString(CultureInfo.InvariantCulture)} additional warning(s) were suppressed.")).ToArray();
        }
    }

    private sealed record UserSource(string Kind, string? Path, string? InvalidReason = null);
    private sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);
    private sealed record CsvColumns(int TitleId, int Name, int MediaId, int Serial, int Type, int Region, int XexCrc, int Wave);
    private sealed record SearchCandidate(TitleIdEntry Entry, int Rank);
}
