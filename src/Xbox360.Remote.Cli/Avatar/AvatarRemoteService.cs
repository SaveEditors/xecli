using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarRemoteService {
    private const string DefaultRepoOwner = "SaveEditors";
    private const string DefaultRepoName = "Avatar-Item-Collection";
    private const string DefaultBranch = "main";

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string GetDefaultManifestUrl() {
        return $"https://raw.githubusercontent.com/{DefaultRepoOwner}/{DefaultRepoName}/{DefaultBranch}/avatar-manifest.json";
    }

    public static string GetDefaultTitleMapUrl() {
        return $"https://raw.githubusercontent.com/{DefaultRepoOwner}/{DefaultRepoName}/{DefaultBranch}/avatar-title-map.json";
    }

    public static string GetDefaultContentBaseUrl() {
        return $"https://raw.githubusercontent.com/{DefaultRepoOwner}/{DefaultRepoName}/{DefaultBranch}/";
    }

    public static string GetCachePath(string? explicitCachePath) {
        if (string.IsNullOrWhiteSpace(explicitCachePath))
            return Path.Combine(CliPaths.CachePath, "avatar", "avatar-remote-index.v2.json");

        string fullPath = Path.GetFullPath(explicitCachePath);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
            return Path.Combine(fullPath, "avatar-remote-index.v2.json");

        string directory = Path.GetDirectoryName(fullPath) ?? CliPaths.ConfigDirectory;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}.remote{Path.GetExtension(fullPath)}");
    }

    public static string GetDownloadCacheDirectory(string? explicitDirectory) {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            return Path.GetFullPath(explicitDirectory);

        return Path.Combine(CliPaths.CachePath, "avatar", "packages");
    }

    public static async Task<AvatarLibraryIndex> LoadIndexAsync(
        string manifestUrl,
        string titleMapUrl,
        string contentBaseUrl,
        bool useCache,
        string? explicitCachePath,
        CancellationToken cancellationToken) {
        string cachePath = GetCachePath(explicitCachePath);
        if (useCache) {
            AvatarLibraryIndex? cached = TryLoadCache(cachePath, manifestUrl, titleMapUrl, contentBaseUrl);
            if (cached != null)
                return cached;
        }

        using HttpClient client = CreateHttpClient();
        string manifestJson = await client.GetStringAsync(manifestUrl, cancellationToken);
        string titleMapJson = await client.GetStringAsync(titleMapUrl, cancellationToken);

        List<AvatarManifestEntry> manifest = JsonSerializer.Deserialize<List<AvatarManifestEntry>>(manifestJson, SerializerOptions)
            ?? throw new InvalidDataException("Avatar manifest is empty.");
        List<AvatarTitleMapEntry> titleMapEntries = JsonSerializer.Deserialize<List<AvatarTitleMapEntry>>(titleMapJson, SerializerOptions)
            ?? new List<AvatarTitleMapEntry>();

        Dictionary<string, AvatarTitleMapEntry> titleMap = titleMapEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.TitleId))
            .GroupBy(entry => entry.TitleId!.Trim().ToUpperInvariant())
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<AvatarItemRecord> items = new List<AvatarItemRecord>(manifest.Count);
        foreach (AvatarManifestEntry entry in manifest) {
            if (string.IsNullOrWhiteSpace(entry.TitleId) || string.IsNullOrWhiteSpace(entry.Relative) || string.IsNullOrWhiteSpace(entry.FileName))
                continue;
            if (!uint.TryParse(entry.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint titleId))
                continue;

            string normalizedRelative = entry.Relative.Replace('\\', '/').TrimStart('/');
            string relativeStorePath = normalizedRelative.StartsWith($"{entry.TitleId}/", StringComparison.OrdinalIgnoreCase)
                ? normalizedRelative[(entry.TitleId.Length + 1)..]
                : normalizedRelative;
            string? contentType = entry.Layout.Equals("root", StringComparison.OrdinalIgnoreCase) ? null : entry.Layout;
            string titleName = ResolveRemoteTitleName(titleId, entry.TitleId, titleMap);
            string? publisher = ResolveRemotePublisher(entry.TitleId, titleMap);
            string displayName = AvatarLibraryService.ResolveItemDisplayName(
                entry.FileName,
                entry.ItemName,
                titleName);
            string downloadUrl = CombineContentUrl(contentBaseUrl, normalizedRelative);
            IReadOnlyList<string> tags = BuildTags(displayName, titleName, publisher);

            items.Add(new AvatarItemRecord(
                titleId,
                titleName,
                contentType,
                entry.FileName,
                relativeStorePath.Replace('/', Path.DirectorySeparatorChar),
                normalizedRelative,
                string.Empty,
                entry.SizeBytes,
                AvatarPackageMagic.Unknown,
                displayName,
                titleName,
                publisher,
                tags,
                entry.Sha256,
                downloadUrl));
        }

        AvatarLibraryIndex index = new AvatarLibraryIndex(
            new AvatarCollectionFingerprint(
                manifestUrl,
                items.Count,
                items.Sum(item => item.SizeBytes),
                DateTime.UtcNow.Ticks),
            DateTimeOffset.UtcNow,
            items
                .OrderBy(item => item.TitleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        SaveCache(cachePath, manifestUrl, titleMapUrl, contentBaseUrl, index);
        return index;
    }

    public static AvatarCacheStatus DescribeStatus(string cachePath, string manifestUrl, string titleMapUrl, string contentBaseUrl) {
        string expectedSignature = FormatRemoteSignature(manifestUrl, titleMapUrl, contentBaseUrl);
        if (!File.Exists(cachePath)) {
            return new AvatarCacheStatus(
                cachePath,
                "missing",
                "No remote avatar cache file was found.",
                expectedSignature,
                null,
                null,
                null,
                null);
        }

        try {
            AvatarRemoteCacheEnvelope? envelope = JsonSerializer.Deserialize<AvatarRemoteCacheEnvelope>(File.ReadAllText(cachePath), SerializerOptions);
            if (envelope?.Index == null) {
                return new AvatarCacheStatus(
                    cachePath,
                    "corrupt",
                    "The remote cache file could not be parsed.",
                    expectedSignature,
                    null,
                    null,
                    null,
                    null);
            }

            string cachedSignature = FormatRemoteSignature(envelope.ManifestUrl, envelope.TitleMapUrl, envelope.ContentBaseUrl);
            if (!string.Equals(envelope.ManifestUrl, manifestUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.TitleMapUrl, titleMapUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.ContentBaseUrl, contentBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                return new AvatarCacheStatus(
                    cachePath,
                    "stale",
                    "The cache URLs do not match the current remote settings.",
                    expectedSignature,
                    cachedSignature,
                    envelope.Index.GeneratedUtc,
                    envelope.Index.Items.Count,
                    envelope.Index.Items.Sum(item => item.SizeBytes));
            }

            return new AvatarCacheStatus(
                cachePath,
                "ready",
                "The cache matches the current remote settings.",
                expectedSignature,
                cachedSignature,
                envelope.Index.GeneratedUtc,
                envelope.Index.Items.Count,
                envelope.Index.Items.Sum(item => item.SizeBytes));
        }
        catch {
            return new AvatarCacheStatus(
                cachePath,
                "corrupt",
                "The remote cache file could not be read.",
                expectedSignature,
                null,
                null,
                null,
                null);
        }
    }

    public static async Task<string> EnsureLocalPackageAsync(
        AvatarItemRecord item,
        string downloadCacheDirectory,
        IProgress<long>? progress,
        CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
            return item.SourcePath;
        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
            throw new FileNotFoundException($"Avatar item {item.ContentId} does not have a local source or a download URL.");

        string relativePath = item.RelativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        string localPath = Path.Combine(downloadCacheDirectory, relativePath);
        string? localDirectory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(localDirectory))
            Directory.CreateDirectory(localDirectory);

        if (File.Exists(localPath)) {
            if (await VerifyFileAsync(localPath, item.SizeBytes, item.Sha256, cancellationToken))
                return localPath;
            File.Delete(localPath);
        }

        using HttpClient client = CreateHttpClient();
        using HttpResponseMessage response = await client.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (FileStream fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
            byte[] buffer = new byte[1024 * 64];
            long total = 0;
            while (true) {
                int read = await responseStream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report(total);
            }

            await fileStream.FlushAsync(cancellationToken);
        }

        if (!await VerifyFileAsync(localPath, item.SizeBytes, item.Sha256, cancellationToken)) {
            File.Delete(localPath);
            throw new InvalidDataException($"Downloaded avatar item {item.ContentId} failed integrity validation.");
        }

        return localPath;
    }

    private static HttpClient CreateHttpClient() {
        HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(10);
        CliHttpUserAgent.Apply(client);
        return client;
    }

    private static string ResolveRemoteTitleName(uint titleId, string rawTitleId, IReadOnlyDictionary<string, AvatarTitleMapEntry> titleMap) {
        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? titleEntry) &&
            titleEntry != null &&
            !string.IsNullOrWhiteSpace(titleEntry.Name)) {
            return titleEntry.Name.Trim();
        }

        if (titleMap.TryGetValue(rawTitleId.Trim().ToUpperInvariant(), out AvatarTitleMapEntry? entry) &&
            !string.IsNullOrWhiteSpace(entry.TitleName)) {
            return entry.TitleName.Trim();
        }

        return TitleIdDatabase.FormatTitleId(titleId);
    }

    private static string? ResolveRemotePublisher(string rawTitleId, IReadOnlyDictionary<string, AvatarTitleMapEntry> titleMap) {
        if (!titleMap.TryGetValue(rawTitleId.Trim().ToUpperInvariant(), out AvatarTitleMapEntry? entry) ||
            entry.Publishers == null ||
            entry.Publishers.Count == 0) {
            return null;
        }

        return string.Join(", ", entry.Publishers);
    }

    private static string CombineContentUrl(string baseUrl, string relativePath) {
        string trimmedBase = baseUrl.TrimEnd('/');
        string[] segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        string encodedPath = string.Join("/", segments.Select(Uri.EscapeDataString));
        return $"{trimmedBase}/{encodedPath}";
    }

    private static IReadOnlyList<string> BuildTags(string displayName, string titleName, string? publisher) {
        HashSet<string> tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTagIfContains(tags, displayName, "shirt");
        AddTagIfContains(tags, displayName, "hat");
        AddTagIfContains(tags, displayName, "helmet");
        AddTagIfContains(tags, displayName, "hoodie");
        AddTagIfContains(tags, displayName, "jacket");
        AddTagIfContains(tags, displayName, "glasses");
        AddTagIfContains(tags, displayName, "prop");
        AddTagIfContains(tags, displayName, "logo");
        AddTagIfContains(tags, displayName, "male");
        AddTagIfContains(tags, displayName, "female");

        foreach (string token in titleName.Split(new[] { ' ', ':', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (token.Length >= 3)
                tags.Add(token.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(publisher))
            tags.Add(publisher.Trim().ToLowerInvariant());

        return tags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddTagIfContains(HashSet<string> tags, string source, string keyword) {
        if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            tags.Add(keyword);
    }

    private static AvatarLibraryIndex? TryLoadCache(string cachePath, string manifestUrl, string titleMapUrl, string contentBaseUrl) {
        try {
            if (!File.Exists(cachePath))
                return null;

            AvatarRemoteCacheEnvelope? envelope = JsonSerializer.Deserialize<AvatarRemoteCacheEnvelope>(File.ReadAllText(cachePath), SerializerOptions);
            if (envelope?.Index == null)
                return null;

            if (!string.Equals(envelope.ManifestUrl, manifestUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.TitleMapUrl, titleMapUrl, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.ContentBaseUrl, contentBaseUrl, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            return envelope.Index;
        }
        catch {
            return null;
        }
    }

    private static void SaveCache(string cachePath, string manifestUrl, string titleMapUrl, string contentBaseUrl, AvatarLibraryIndex index) {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        AvatarRemoteCacheEnvelope envelope = new AvatarRemoteCacheEnvelope(manifestUrl, titleMapUrl, contentBaseUrl, index);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(envelope, SerializerOptions));
    }

    private static string FormatRemoteSignature(string manifestUrl, string titleMapUrl, string contentBaseUrl) {
        return $"manifest={manifestUrl};titleMap={titleMapUrl};contentBase={contentBaseUrl}";
    }

    private static async Task<bool> VerifyFileAsync(string localPath, long expectedSize, string? expectedSha256, CancellationToken cancellationToken) {
        FileInfo info = new FileInfo(localPath);
        if (expectedSize > 0 && info.Length != expectedSize)
            return false;

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;

        await using FileStream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        string actual = Convert.ToHexString(hash).ToLowerInvariant();
        return actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record AvatarManifestEntry(
        string TitleId,
        string Layout,
        string ItemName,
        string FileName,
        long SizeBytes,
        string Sha256,
        string Relative);

    private sealed record AvatarTitleMapEntry(
        string? TitleId,
        string? TitleName,
        IReadOnlyList<string>? Publishers);

    private sealed record AvatarRemoteCacheEnvelope(
        string ManifestUrl,
        string TitleMapUrl,
        string ContentBaseUrl,
        AvatarLibraryIndex Index);
}
