using System.Globalization;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarLibraryService {
    public static readonly string DefaultCollectionFolderName = "Avatar-Item-Collection";
    private static readonly string[] DefaultRootCandidates = {
        Path.Combine(AppContext.BaseDirectory, DefaultCollectionFolderName),
        @"A:\Downloads\12\em\Avatar-Item-Collection"
    };

    public static string ResolveCollectionRoot(string? explicitRoot = null) {
        IEnumerable<string> candidates = EnumerateRootCandidates(explicitRoot);
        foreach (string candidate in candidates) {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            string fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath))
                return fullPath;
        }

        throw new DirectoryNotFoundException($"Unable to locate {DefaultCollectionFolderName}. Provide an explicit root or set XECLI_AVATAR_COLLECTION_ROOT.");
    }

    public static AvatarCollectionFingerprint BuildFingerprint(string rootPath) {
        int fileCount = 0;
        long totalBytes = 0;
        DateTime latest = Directory.GetLastWriteTimeUtc(rootPath);

        foreach (string file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)) {
            FileInfo info = new FileInfo(file);
            fileCount++;
            totalBytes += info.Length;
            if (info.LastWriteTimeUtc > latest)
                latest = info.LastWriteTimeUtc;
        }

        return new AvatarCollectionFingerprint(
            Path.GetFullPath(rootPath),
            fileCount,
            totalBytes,
            latest.Ticks);
    }

    public static AvatarLibraryIndex LoadIndex(string? explicitRoot = null, bool useCache = true, string? explicitCachePath = null) {
        string rootPath = ResolveCollectionRoot(explicitRoot);
        AvatarCollectionFingerprint fingerprint = BuildFingerprint(rootPath);
        string cachePath = AvatarIndexCache.GetCachePath(explicitCachePath);

        if (useCache) {
            AvatarLibraryIndex? cached = AvatarIndexCache.TryLoad(cachePath, fingerprint);
            if (cached != null)
                return cached;
        }

        List<AvatarItemRecord> items = new List<AvatarItemRecord>();
        foreach (string titleDirectory in Directory.EnumerateDirectories(rootPath)) {
            string titleFolder = Path.GetFileName(titleDirectory);
            if (!uint.TryParse(titleFolder, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint titleIdFromFolder))
                continue;

            foreach ((string filePath, string? contentType, string relativeStorePath) in EnumerateTitleFiles(titleDirectory)) {
                string contentId = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(contentId))
                    continue;

                AvatarPackageMetadata metadata = AvatarPackageReader.ReadMetadata(filePath);
                uint titleId = metadata.TitleId != 0 ? metadata.TitleId : titleIdFromFolder;
                string titleName = ResolveTitleName(titleId, metadata.GameName);
                string displayName = metadata.DisplayName ?? contentId;
                string relativePath = $"{titleIdFromFolder:X8}/{relativeStorePath.Replace('\\', '/')}";
                IReadOnlyList<string> tags = BuildTags(displayName, titleName, metadata.Publisher);

                items.Add(new AvatarItemRecord(
                    titleId,
                    titleName,
                    contentType,
                    contentId,
                    relativeStorePath,
                    relativePath,
                    filePath,
                    new FileInfo(filePath).Length,
                    metadata.Magic,
                    displayName,
                    metadata.GameName,
                    metadata.Publisher,
                    tags));
            }
        }

        AvatarLibraryIndex index = new AvatarLibraryIndex(
            fingerprint,
            DateTimeOffset.UtcNow,
            items
                .OrderBy(item => item.TitleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        if (useCache)
            AvatarIndexCache.Save(cachePath, index);

        return index;
    }

    public static IReadOnlyList<AvatarTitleSummary> ListTitles(AvatarLibraryIndex index) {
        return index.Titles;
    }

    public static IReadOnlyList<AvatarItemRecord> SearchItems(AvatarLibraryIndex index, AvatarItemQuery query) {
        return AvatarLibraryQueries.Filter(index.Items, query);
    }

    public static AvatarItemRecord? FindByContentId(AvatarLibraryIndex index, string contentId, uint? titleId = null) {
        return index.Items.FirstOrDefault(item =>
            item.ContentId.Equals(contentId, StringComparison.OrdinalIgnoreCase) &&
            (!titleId.HasValue || item.TitleId == titleId.Value));
    }

    public static AvatarInstallPlan PrepareInstallPlan(AvatarLibraryIndex index, string contentId, AvatarOwnershipPatch ownership, string deviceRoot, uint? titleId = null, string? workingDirectory = null) {
        AvatarItemRecord item = FindByContentId(index, contentId, titleId)
            ?? throw new FileNotFoundException($"Unable to locate avatar item {contentId}.");

        return AvatarInstallPlanner.PrepareInstallPlan(new AvatarInstallRequest(item, deviceRoot, ownership, workingDirectory));
    }

    private static IEnumerable<(string FilePath, string? ContentType, string RelativeStorePath)> EnumerateTitleFiles(string titleDirectory) {
        string contentTypeDirectory = Path.Combine(titleDirectory, "00009000");
        if (Directory.Exists(contentTypeDirectory)) {
            foreach (string filePath in Directory.EnumerateFiles(contentTypeDirectory, "*", SearchOption.TopDirectoryOnly)) {
                string contentId = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(contentId))
                    continue;
                yield return (filePath, "00009000", $"00009000/{contentId}");
            }
        }

        foreach (string filePath in Directory.EnumerateFiles(titleDirectory, "*", SearchOption.TopDirectoryOnly)) {
            string contentId = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(contentId))
                continue;
            yield return (filePath, null, contentId);
        }
    }

    private static IEnumerable<string> EnumerateRootCandidates(string? explicitRoot) {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
            yield return explicitRoot;

        string? envRoot = Environment.GetEnvironmentVariable("XECLI_AVATAR_COLLECTION_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
            yield return envRoot;

        foreach (string candidate in DefaultRootCandidates)
            yield return candidate;
    }

    private static string ResolveTitleName(uint titleId, string? metadataGameName) {
        if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry? entry) &&
            entry != null &&
            !string.IsNullOrWhiteSpace(entry.Name) &&
            !entry.Name.StartsWith("Title ", StringComparison.OrdinalIgnoreCase)) {
            return entry.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(metadataGameName))
            return metadataGameName.Trim();

        return $"Title 0x{titleId:X8}";
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
}
