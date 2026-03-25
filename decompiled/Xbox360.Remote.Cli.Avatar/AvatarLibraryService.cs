using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarLibraryService
{
	public static readonly string DefaultCollectionFolderName = "Avatar-Item-Collection";

	private static readonly string[] DefaultRootCandidates = new string[2]
	{
		Path.Combine(AppContext.BaseDirectory, DefaultCollectionFolderName),
		"A:\\Downloads\\12\\em\\Avatar-Item-Collection"
	};

	public static string ResolveCollectionRoot(string? explicitRoot = null)
	{
		foreach (string item in EnumerateRootCandidates(explicitRoot))
		{
			if (!string.IsNullOrWhiteSpace(item))
			{
				string fullPath = Path.GetFullPath(item);
				if (Directory.Exists(fullPath))
				{
					return fullPath;
				}
			}
		}
		throw new DirectoryNotFoundException("Unable to locate " + DefaultCollectionFolderName + ". Provide an explicit root or set XECLI_AVATAR_COLLECTION_ROOT.");
	}

	public static AvatarCollectionFingerprint BuildFingerprint(string rootPath)
	{
		int num = 0;
		long num2 = 0L;
		DateTime lastWriteTimeUtc = Directory.GetLastWriteTimeUtc(rootPath);
		foreach (string item in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
		{
			FileInfo fileInfo = new FileInfo(item);
			num++;
			num2 += fileInfo.Length;
			if (fileInfo.LastWriteTimeUtc > lastWriteTimeUtc)
			{
				lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			}
		}
		return new AvatarCollectionFingerprint(Path.GetFullPath(rootPath), num, num2, lastWriteTimeUtc.Ticks);
	}

	public static AvatarLibraryIndex LoadIndex(string? explicitRoot = null, bool useCache = true, string? explicitCachePath = null)
	{
		string text = ResolveCollectionRoot(explicitRoot);
		AvatarCollectionFingerprint avatarCollectionFingerprint = BuildFingerprint(text);
		string cachePath = AvatarIndexCache.GetCachePath(explicitCachePath);
		if (useCache)
		{
			AvatarLibraryIndex avatarLibraryIndex = AvatarIndexCache.TryLoad(cachePath, avatarCollectionFingerprint);
			if (avatarLibraryIndex != null)
			{
				return avatarLibraryIndex;
			}
		}
		List<AvatarItemRecord> list = new List<AvatarItemRecord>();
		foreach (string item4 in Directory.EnumerateDirectories(text))
		{
			if (!uint.TryParse(Path.GetFileName(item4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
			{
				continue;
			}
			foreach (var item5 in EnumerateTitleFiles(item4))
			{
				string item = item5.FilePath;
				string item2 = item5.ContentType;
				string item3 = item5.RelativeStorePath;
				string fileName = Path.GetFileName(item);
				if (!string.IsNullOrWhiteSpace(fileName))
				{
					AvatarPackageMetadata avatarPackageMetadata = AvatarPackageReader.ReadMetadata(item);
					uint titleId = ((avatarPackageMetadata.TitleId != 0) ? avatarPackageMetadata.TitleId : result);
					string text2 = ResolveTitleName(titleId, avatarPackageMetadata.GameName);
					string displayName = ResolveItemDisplayName(fileName, avatarPackageMetadata.DisplayName, avatarPackageMetadata.GameName, text2);
					string relativePath = $"{result:X8}/{item3.Replace('\\', '/')}";
					IReadOnlyList<string> tags = BuildTags(displayName, text2, avatarPackageMetadata.Publisher);
					list.Add(new AvatarItemRecord(titleId, text2, item2, fileName, item3, relativePath, item, new FileInfo(item).Length, avatarPackageMetadata.Magic, displayName, avatarPackageMetadata.GameName, avatarPackageMetadata.Publisher, tags));
				}
			}
		}
		AvatarLibraryIndex avatarLibraryIndex2 = new AvatarLibraryIndex(avatarCollectionFingerprint, DateTimeOffset.UtcNow, list.OrderBy<AvatarItemRecord, string>((AvatarItemRecord avatarItemRecord) => avatarItemRecord.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord avatarItemRecord) => ResolveItemDisplayName(avatarItemRecord.ContentId, avatarItemRecord.DisplayName, avatarItemRecord.GameName, avatarItemRecord.TitleName), StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord avatarItemRecord) => avatarItemRecord.ContentId, StringComparer.OrdinalIgnoreCase)
			.ToArray());
		if (useCache)
		{
			AvatarIndexCache.Save(cachePath, avatarLibraryIndex2);
		}
		return avatarLibraryIndex2;
	}

	public static AvatarLibraryIndex MergeMetadata(AvatarLibraryIndex primary, AvatarLibraryIndex overlay)
	{
		if (primary.Items.Count == 0 || overlay.Items.Count == 0)
		{
			return primary;
		}
		Dictionary<(uint, string), AvatarItemRecord> dictionary = (from item in overlay.Items
			group item by (TitleId: item.TitleId, NormalizeRelativePath(item.RelativePath))).ToDictionary((IGrouping<(uint TitleId, string), AvatarItemRecord> group) => group.Key, (IGrouping<(uint TitleId, string), AvatarItemRecord> group) => group.First());
		Dictionary<(uint, string), AvatarItemRecord> dictionary2 = (from item in overlay.Items
			group item by (TitleId: item.TitleId, item.ContentId.Trim())).ToDictionary((IGrouping<(uint TitleId, string), AvatarItemRecord> group) => group.Key, (IGrouping<(uint TitleId, string), AvatarItemRecord> group) => group.First());
		List<AvatarItemRecord> list = new List<AvatarItemRecord>(primary.Items.Count);
		foreach (AvatarItemRecord item in primary.Items)
		{
			AvatarItemRecord value = null;
			dictionary.TryGetValue((item.TitleId, NormalizeRelativePath(item.RelativePath)), out value);
			if ((object)value == null)
			{
				value = (dictionary2.TryGetValue((item.TitleId, item.ContentId.Trim()), out var value2) ? value2 : null);
			}
			if (value == null)
			{
				list.Add(item);
			}
			else
			{
				list.Add(MergeItemMetadata(item, value));
			}
		}
		return new AvatarLibraryIndex(primary.Fingerprint, primary.GeneratedUtc, list.ToArray());
	}

	public static IReadOnlyList<AvatarTitleSummary> ListTitles(AvatarLibraryIndex index)
	{
		return index.Titles;
	}

	public static IReadOnlyList<AvatarItemRecord> SearchItems(AvatarLibraryIndex index, AvatarItemQuery query)
	{
		return AvatarLibraryQueries.Filter(index.Items, query);
	}

	public static AvatarItemRecord? FindByContentId(AvatarLibraryIndex index, string contentId, uint? titleId = null)
	{
		return index.Items.FirstOrDefault((AvatarItemRecord item) => item.ContentId.Equals(contentId, StringComparison.OrdinalIgnoreCase) && (!titleId.HasValue || item.TitleId == titleId.Value));
	}

	public static AvatarInstallPlan PrepareInstallPlan(AvatarLibraryIndex index, string contentId, AvatarOwnershipPatch ownership, string deviceRoot, uint? titleId = null, string? workingDirectory = null)
	{
		return AvatarInstallPlanner.PrepareInstallPlan(new AvatarInstallRequest(FindByContentId(index, contentId, titleId) ?? throw new FileNotFoundException("Unable to locate avatar item " + contentId + "."), deviceRoot, ownership, workingDirectory));
	}

	private static IEnumerable<(string FilePath, string? ContentType, string RelativeStorePath)> EnumerateTitleFiles(string titleDirectory)
	{
		string path = Path.Combine(titleDirectory, "00009000");
		if (Directory.Exists(path))
		{
			foreach (string item in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
			{
				string fileName = Path.GetFileName(item);
				if (!string.IsNullOrWhiteSpace(fileName))
				{
					yield return (FilePath: item, ContentType: "00009000", RelativeStorePath: "00009000/" + fileName);
				}
			}
		}
		foreach (string item2 in Directory.EnumerateFiles(titleDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			string fileName2 = Path.GetFileName(item2);
			if (!string.IsNullOrWhiteSpace(fileName2))
			{
				yield return (FilePath: item2, ContentType: null, RelativeStorePath: fileName2);
			}
		}
	}

	private static IEnumerable<string> EnumerateRootCandidates(string? explicitRoot)
	{
		if (!string.IsNullOrWhiteSpace(explicitRoot))
		{
			yield return explicitRoot;
		}
		string environmentVariable = Environment.GetEnvironmentVariable("XECLI_AVATAR_COLLECTION_ROOT");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			yield return environmentVariable;
		}
		string[] defaultRootCandidates = DefaultRootCandidates;
		for (int i = 0; i < defaultRootCandidates.Length; i++)
		{
			yield return defaultRootCandidates[i];
		}
	}

	private static string ResolveTitleName(uint titleId, string? metadataGameName)
	{
		if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry entry) && entry != null && !string.IsNullOrWhiteSpace(entry.Name) && !entry.Name.StartsWith("Title ", StringComparison.OrdinalIgnoreCase))
		{
			return entry.Name.Trim();
		}
		if (!string.IsNullOrWhiteSpace(metadataGameName))
		{
			return metadataGameName.Trim();
		}
		return $"Title 0x{titleId:X8}";
	}

	private static AvatarItemRecord MergeItemMetadata(AvatarItemRecord primary, AvatarItemRecord overlay)
	{
		string text = ChooseTitleName(primary.TitleName, overlay.TitleName);
		string displayName = ResolveItemDisplayName(primary.ContentId, overlay.DisplayName, overlay.GameName, primary.DisplayName, primary.GameName, text);
		string gameName = ChooseOptionalText(primary.GameName, overlay.GameName);
		string publisher = ChooseOptionalText(primary.Publisher, overlay.Publisher);
		IReadOnlyList<string> tags = (from tag in primary.Tags.Concat(overlay.Tags)
			where !string.IsNullOrWhiteSpace(tag)
			select tag).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string tag) => tag, StringComparer.OrdinalIgnoreCase).ToArray();
		return primary with
		{
			TitleName = text,
			DisplayName = displayName,
			GameName = gameName,
			Publisher = publisher,
			Tags = tags
		};
	}

	private static string ChooseTitleName(string primary, string overlay)
	{
		if (IsMeaningfulText(overlay) && !IsPlaceholderTitleName(overlay))
		{
			return overlay.Trim();
		}
		if (IsMeaningfulText(primary))
		{
			return primary.Trim();
		}
		return overlay.Trim();
	}

	internal static string ResolveItemDisplayName(string contentId, params string?[] candidates)
	{
		foreach (string text in candidates)
		{
			if (IsMeaningfulText(text))
			{
				string text2 = text.Trim();
				if (!IsPlaceholderDisplayName(text2, contentId))
				{
					return text2;
				}
			}
		}
		return contentId;
	}

	private static string? ChooseOptionalText(string? primary, string? overlay)
	{
		if (IsMeaningfulText(overlay))
		{
			return overlay.Trim();
		}
		if (IsMeaningfulText(primary))
		{
			return primary.Trim();
		}
		return null;
	}

	private static bool IsMeaningfulText(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 2)
		{
			return !LooksLikeRawContentId(value);
		}
		return false;
	}

	private static bool LooksLikeRawContentId(string value)
	{
		string text = value.Trim();
		if (text.Length >= 12 && text.Length <= 64)
		{
			return text.All((char ch) => char.IsDigit(ch) || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f'));
		}
		return false;
	}

	private static bool IsPlaceholderDisplayName(string? value, string contentId)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}
		string text = value.Trim();
		if (!LooksLikeRawContentId(text) && !text.Equals(contentId, StringComparison.OrdinalIgnoreCase))
		{
			return text.StartsWith("Title 0x", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsPlaceholderTitleName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}
		return value.Trim().StartsWith("Title 0x", StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeRelativePath(string relativePath)
	{
		return relativePath.Replace('\\', '/').TrimStart('/').Trim();
	}

	private static IReadOnlyList<string> BuildTags(string displayName, string titleName, string? publisher)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		AddTagIfContains(hashSet, displayName, "shirt");
		AddTagIfContains(hashSet, displayName, "hat");
		AddTagIfContains(hashSet, displayName, "helmet");
		AddTagIfContains(hashSet, displayName, "hoodie");
		AddTagIfContains(hashSet, displayName, "jacket");
		AddTagIfContains(hashSet, displayName, "glasses");
		AddTagIfContains(hashSet, displayName, "prop");
		AddTagIfContains(hashSet, displayName, "logo");
		AddTagIfContains(hashSet, displayName, "male");
		AddTagIfContains(hashSet, displayName, "female");
		string[] array = titleName.Split(new char[5] { ' ', ':', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			if (text.Length >= 3)
			{
				hashSet.Add(text.ToLowerInvariant());
			}
		}
		if (!string.IsNullOrWhiteSpace(publisher))
		{
			hashSet.Add(publisher.Trim().ToLowerInvariant());
		}
		return hashSet.OrderBy<string, string>((string tag) => tag, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static void AddTagIfContains(HashSet<string> tags, string source, string keyword)
	{
		if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
		{
			tags.Add(keyword);
		}
	}
}
