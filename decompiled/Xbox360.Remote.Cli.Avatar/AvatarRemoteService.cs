using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Xbox360.Remote.Cli.Avatar;

internal static class AvatarRemoteService
{
	private sealed record AvatarManifestEntry(string TitleId, string Layout, string ItemName, string FileName, long SizeBytes, string Sha256, string Relative);

	private sealed record AvatarTitleMapEntry(string? TitleId, string? TitleName, IReadOnlyList<string>? Publishers);

	private sealed record AvatarRemoteCacheEnvelope(string ManifestUrl, string TitleMapUrl, string ContentBaseUrl, AvatarLibraryIndex Index);

	private const string DefaultRepoOwner = "SaveEditors";

	private const string DefaultRepoName = "Avatar-Item-Collection";

	private const string DefaultBranch = "main";

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = false
	};

	public static string GetDefaultManifestUrl()
	{
		return "https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-manifest.json";
	}

	public static string GetDefaultTitleMapUrl()
	{
		return "https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/avatar-title-map.json";
	}

	public static string GetDefaultContentBaseUrl()
	{
		return "https://raw.githubusercontent.com/SaveEditors/Avatar-Item-Collection/main/";
	}

	public static string GetCachePath(string? explicitCachePath)
	{
		if (string.IsNullOrWhiteSpace(explicitCachePath))
		{
			return Path.Combine(CliPaths.CachePath, "avatar", "avatar-remote-index.v2.json");
		}
		string fullPath = Path.GetFullPath(explicitCachePath);
		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
		{
			return Path.Combine(fullPath, "avatar-remote-index.v2.json");
		}
		string? path = Path.GetDirectoryName(fullPath) ?? CliPaths.ConfigDirectory;
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
		return Path.Combine(path, fileNameWithoutExtension + ".remote" + Path.GetExtension(fullPath));
	}

	public static string GetDownloadCacheDirectory(string? explicitDirectory)
	{
		if (!string.IsNullOrWhiteSpace(explicitDirectory))
		{
			return Path.GetFullPath(explicitDirectory);
		}
		return Path.Combine(CliPaths.CachePath, "avatar", "packages");
	}

	public static async Task<AvatarLibraryIndex> LoadIndexAsync(string manifestUrl, string titleMapUrl, string contentBaseUrl, bool useCache, string? explicitCachePath, CancellationToken cancellationToken)
	{
		string cachePath = GetCachePath(explicitCachePath);
		if (useCache)
		{
			AvatarLibraryIndex avatarLibraryIndex = TryLoadCache(cachePath, manifestUrl, titleMapUrl, contentBaseUrl);
			if (avatarLibraryIndex != null)
			{
				return avatarLibraryIndex;
			}
		}
		using HttpClient client = CreateHttpClient();
		string manifestJson = await client.GetStringAsync(manifestUrl, cancellationToken);
		string json = await client.GetStringAsync(titleMapUrl, cancellationToken);
		List<AvatarManifestEntry> list = JsonSerializer.Deserialize<List<AvatarManifestEntry>>(manifestJson, SerializerOptions) ?? throw new InvalidDataException("Avatar manifest is empty.");
		Dictionary<string, AvatarTitleMapEntry> titleMap = (from entry in JsonSerializer.Deserialize<List<AvatarTitleMapEntry>>(json, SerializerOptions) ?? new List<AvatarTitleMapEntry>()
			where !string.IsNullOrWhiteSpace(entry.TitleId)
			group entry by entry.TitleId.Trim().ToUpperInvariant()).ToDictionary<IGrouping<string, AvatarTitleMapEntry>, string, AvatarTitleMapEntry>((IGrouping<string, AvatarTitleMapEntry> group) => group.Key, (IGrouping<string, AvatarTitleMapEntry> group) => group.First(), StringComparer.OrdinalIgnoreCase);
		List<AvatarItemRecord> list2 = new List<AvatarItemRecord>(list.Count);
		foreach (AvatarManifestEntry item in list)
		{
			if (!string.IsNullOrWhiteSpace(item.TitleId) && !string.IsNullOrWhiteSpace(item.Relative) && !string.IsNullOrWhiteSpace(item.FileName) && uint.TryParse(item.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
			{
				string text = item.Relative.Replace('\\', '/').TrimStart('/');
				string text2;
				if (!text.StartsWith(item.TitleId + "/", StringComparison.OrdinalIgnoreCase))
				{
					text2 = text;
				}
				else
				{
					string text3 = text;
					int num = item.TitleId.Length + 1;
					text2 = text3.Substring(num, text3.Length - num);
				}
				string text4 = text2;
				string contentType = (item.Layout.Equals("root", StringComparison.OrdinalIgnoreCase) ? null : item.Layout);
				string text5 = ResolveRemoteTitleName(result, item.TitleId, titleMap);
				string publisher = ResolveRemotePublisher(item.TitleId, titleMap);
				string displayName = AvatarLibraryService.ResolveItemDisplayName(item.FileName, item.ItemName, text5);
				string downloadUrl = CombineContentUrl(contentBaseUrl, text);
				IReadOnlyList<string> tags = BuildTags(displayName, text5, publisher);
				list2.Add(new AvatarItemRecord(result, text5, contentType, item.FileName, text4.Replace('/', Path.DirectorySeparatorChar), text, string.Empty, item.SizeBytes, AvatarPackageMagic.Unknown, displayName, text5, publisher, tags, item.Sha256, downloadUrl));
			}
		}
		AvatarLibraryIndex avatarLibraryIndex2 = new AvatarLibraryIndex(new AvatarCollectionFingerprint(manifestUrl, list2.Count, list2.Sum((AvatarItemRecord item) => item.SizeBytes), DateTime.UtcNow.Ticks), DateTimeOffset.UtcNow, list2.OrderBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName), StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.ContentId, StringComparer.OrdinalIgnoreCase)
			.ToArray());
		SaveCache(cachePath, manifestUrl, titleMapUrl, contentBaseUrl, avatarLibraryIndex2);
		return avatarLibraryIndex2;
	}

	public static async Task<string> EnsureLocalPackageAsync(AvatarItemRecord item, string downloadCacheDirectory, IProgress<long>? progress, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
		{
			return item.SourcePath;
		}
		if (string.IsNullOrWhiteSpace(item.DownloadUrl))
		{
			throw new FileNotFoundException("Avatar item " + item.ContentId + " does not have a local source or a download URL.");
		}
		string path = item.RelativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		string localPath = Path.Combine(downloadCacheDirectory, path);
		string directoryName = Path.GetDirectoryName(localPath);
		if (!string.IsNullOrWhiteSpace(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (File.Exists(localPath))
		{
			if (await VerifyFileAsync(localPath, item.SizeBytes, item.Sha256, cancellationToken))
			{
				return localPath;
			}
			File.Delete(localPath);
		}
		using HttpClient client = CreateHttpClient();
		using HttpResponseMessage response = await client.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await using (Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
		{
			await using FileStream fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
			byte[] buffer = new byte[65536];
			long total = 0L;
			while (true)
			{
				int read = await responseStream.ReadAsync(buffer, cancellationToken);
				if (read <= 0)
				{
					break;
				}
				await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				total += read;
				progress?.Report(total);
			}
			await fileStream.FlushAsync(cancellationToken);
		}
		if (!(await VerifyFileAsync(localPath, item.SizeBytes, item.Sha256, cancellationToken)))
		{
			File.Delete(localPath);
			throw new InvalidDataException("Downloaded avatar item " + item.ContentId + " failed integrity validation.");
		}
		return localPath;
	}

	private static HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromMinutes(10L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XeCLI/1.0");
		return httpClient;
	}

	private static string ResolveRemoteTitleName(uint titleId, string rawTitleId, IReadOnlyDictionary<string, AvatarTitleMapEntry> titleMap)
	{
		if (titleMap.TryGetValue(rawTitleId.Trim().ToUpperInvariant(), out AvatarTitleMapEntry value) && !string.IsNullOrWhiteSpace(value.TitleName))
		{
			return value.TitleName.Trim();
		}
		if (!TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry entry) || !(entry != null) || string.IsNullOrWhiteSpace(entry.Name))
		{
			return $"Title 0x{titleId:X8}";
		}
		return entry.Name.Trim();
	}

	private static string? ResolveRemotePublisher(string rawTitleId, IReadOnlyDictionary<string, AvatarTitleMapEntry> titleMap)
	{
		if (!titleMap.TryGetValue(rawTitleId.Trim().ToUpperInvariant(), out AvatarTitleMapEntry value) || value.Publishers == null || value.Publishers.Count == 0)
		{
			return null;
		}
		return string.Join(", ", value.Publishers);
	}

	private static string CombineContentUrl(string baseUrl, string relativePath)
	{
		string text = baseUrl.TrimEnd('/');
		string[] source = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		string text2 = string.Join("/", source.Select(Uri.EscapeDataString));
		return text + "/" + text2;
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

	private static AvatarLibraryIndex? TryLoadCache(string cachePath, string manifestUrl, string titleMapUrl, string contentBaseUrl)
	{
		try
		{
			if (!File.Exists(cachePath))
			{
				return null;
			}
			AvatarRemoteCacheEnvelope avatarRemoteCacheEnvelope = JsonSerializer.Deserialize<AvatarRemoteCacheEnvelope>(File.ReadAllText(cachePath), SerializerOptions);
			if (avatarRemoteCacheEnvelope?.Index == null)
			{
				return null;
			}
			if (!string.Equals(avatarRemoteCacheEnvelope.ManifestUrl, manifestUrl, StringComparison.OrdinalIgnoreCase) || !string.Equals(avatarRemoteCacheEnvelope.TitleMapUrl, titleMapUrl, StringComparison.OrdinalIgnoreCase) || !string.Equals(avatarRemoteCacheEnvelope.ContentBaseUrl, contentBaseUrl, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return avatarRemoteCacheEnvelope.Index;
		}
		catch
		{
			return null;
		}
	}

	private static void SaveCache(string cachePath, string manifestUrl, string titleMapUrl, string contentBaseUrl, AvatarLibraryIndex index)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
		AvatarRemoteCacheEnvelope value = new AvatarRemoteCacheEnvelope(manifestUrl, titleMapUrl, contentBaseUrl, index);
		File.WriteAllText(cachePath, JsonSerializer.Serialize(value, SerializerOptions));
	}

	private static async Task<bool> VerifyFileAsync(string localPath, long expectedSize, string? expectedSha256, CancellationToken cancellationToken)
	{
		FileInfo fileInfo = new FileInfo(localPath);
		if (expectedSize > 0 && fileInfo.Length != expectedSize)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(expectedSha256))
		{
			return true;
		}
		bool result;
		await using (FileStream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			using SHA256 sha256 = SHA256.Create();
			string text = Convert.ToHexString(await sha256.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
			result = text.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
		}
		return result;
	}
}
