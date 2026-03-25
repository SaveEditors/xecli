using System.Text.Json.Serialization;

namespace Xbox360.Remote.Cli.Avatar;

internal enum AvatarPackageMagic {
    Unknown = 0,
    Live,
    Pirs,
    Con
}

internal sealed record AvatarCollectionFingerprint(
    string RootPath,
    int FileCount,
    long TotalBytes,
    long LatestWriteTimeUtcTicks);

internal sealed record AvatarPackageMetadata(
    AvatarPackageMagic Magic,
    uint TitleId,
    string? DisplayName,
    string? Publisher,
    string? GameName);

internal sealed record AvatarItemRecord(
    uint TitleId,
    string TitleName,
    string? ContentType,
    string ContentId,
    string RelativeStorePath,
    string RelativePath,
    string SourcePath,
    long SizeBytes,
    AvatarPackageMagic Magic,
    string DisplayName,
    string? GameName,
    string? Publisher,
    IReadOnlyList<string> Tags,
    string? Sha256 = null,
    string? DownloadUrl = null);

internal sealed record AvatarTitleSummary(
    uint TitleId,
    string TitleName,
    int ItemCount,
    long TotalBytes,
    IReadOnlyList<string> Publishers);

internal sealed record AvatarLibraryIndex(
    AvatarCollectionFingerprint Fingerprint,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<AvatarItemRecord> Items) {

    [JsonIgnore]
    public IReadOnlyList<AvatarTitleSummary> Titles => AvatarLibraryQueries.SummarizeTitles(Items);
}

internal sealed record AvatarItemQuery(
    uint? TitleId = null,
    string? Search = null,
    string? Publisher = null,
    string? Tag = null,
    int? Limit = null);

internal sealed record AvatarOwnershipPatch(
    ulong PrimaryXuid,
    IReadOnlyList<ulong>? AdditionalXuids = null);

internal sealed record AvatarInstallRequest(
    AvatarItemRecord Item,
    string DeviceRoot,
    AvatarOwnershipPatch Ownership,
    string? WorkingDirectory = null);

internal sealed record AvatarInstallPlan(
    AvatarItemRecord Item,
    string PatchedLocalPath,
    string RemoteDirectory,
    string RemoteFilePath,
    ulong PrimaryXuid,
    IReadOnlyList<ulong> OwnershipTable);

internal static class AvatarLibraryQueries {
    public static IReadOnlyList<AvatarTitleSummary> SummarizeTitles(IReadOnlyList<AvatarItemRecord> items) {
        return items
            .GroupBy(item => item.TitleId)
            .Select(group => new AvatarTitleSummary(
                group.Key,
                group.First().TitleName,
                group.Count(),
                group.Sum(item => item.SizeBytes),
                group.Select(item => item.Publisher)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToArray()))
            .OrderBy(summary => summary.TitleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.TitleId)
            .ToArray();
    }

    public static IReadOnlyList<AvatarItemRecord> Filter(IReadOnlyList<AvatarItemRecord> items, AvatarItemQuery query) {
        IEnumerable<AvatarItemRecord> filtered = items;

        if (query.TitleId.HasValue)
            filtered = filtered.Where(item => item.TitleId == query.TitleId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search)) {
            string search = query.Search.Trim();
            filtered = filtered.Where(item =>
                AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.TitleName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                item.ContentId.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Publisher)) {
            string publisher = query.Publisher.Trim();
            filtered = filtered.Where(item => string.Equals(item.Publisher, publisher, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag)) {
            string tag = query.Tag.Trim();
            filtered = filtered.Where(item => item.Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = filtered
            .OrderBy(item => item.TitleName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase);

        if (query.Limit.HasValue && query.Limit.Value > 0)
            filtered = filtered.Take(query.Limit.Value);

        return filtered.ToArray();
    }
}
