using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Xbox360.Remote.Cli.Avatar;

namespace Xbox360.Remote.Cli.Commands;

internal static class AvatarCommandHelpers
{
	internal sealed record AvatarResolvedPaths(string? ConfiguredLibraryRoot, string? EffectiveLibraryRoot, string? ConfiguredCachePath, string EffectiveCachePath, string? ConfiguredManifestUrl, string EffectiveManifestUrl, string? ConfiguredTitleMapUrl, string EffectiveTitleMapUrl, string? ConfiguredContentBaseUrl, string EffectiveContentBaseUrl, string? ConfiguredDownloadCachePath, string EffectiveDownloadCachePath, bool RemoteMode);

	internal sealed record AvatarResolvedOwnership(string? Gamertag, ulong Xuid, string XuidText, string SignInState, bool FromCurrentUser);

	public static AvatarResolvedPaths ResolvePaths(string? explicitLibraryRoot, string? explicitCachePath, bool forceRemote = false, string? explicitManifestUrl = null, string? explicitTitleMapUrl = null, string? explicitContentBaseUrl = null, string? explicitDownloadCachePath = null)
	{
		CliConfig cliConfig = CliConfig.Load();
		string text = (string.IsNullOrWhiteSpace(cliConfig.AvatarLibraryRoot) ? null : cliConfig.AvatarLibraryRoot);
		string text2 = (string.IsNullOrWhiteSpace(cliConfig.AvatarCachePath) ? null : cliConfig.AvatarCachePath);
		string text3 = (string.IsNullOrWhiteSpace(cliConfig.AvatarManifestUrl) ? null : cliConfig.AvatarManifestUrl);
		string text4 = (string.IsNullOrWhiteSpace(cliConfig.AvatarTitleMapUrl) ? null : cliConfig.AvatarTitleMapUrl);
		string text5 = (string.IsNullOrWhiteSpace(cliConfig.AvatarContentBaseUrl) ? null : cliConfig.AvatarContentBaseUrl);
		string text6 = (string.IsNullOrWhiteSpace(cliConfig.AvatarDownloadCachePath) ? null : cliConfig.AvatarDownloadCachePath);
		string explicitRoot = ((!string.IsNullOrWhiteSpace(explicitLibraryRoot)) ? explicitLibraryRoot : text);
		string explicitCachePath2 = ((!string.IsNullOrWhiteSpace(explicitCachePath)) ? explicitCachePath : text2);
		string effectiveManifestUrl = ((!string.IsNullOrWhiteSpace(explicitManifestUrl)) ? explicitManifestUrl : (text3 ?? AvatarRemoteService.GetDefaultManifestUrl()));
		string effectiveTitleMapUrl = ((!string.IsNullOrWhiteSpace(explicitTitleMapUrl)) ? explicitTitleMapUrl : (text4 ?? AvatarRemoteService.GetDefaultTitleMapUrl()));
		string effectiveContentBaseUrl = ((!string.IsNullOrWhiteSpace(explicitContentBaseUrl)) ? explicitContentBaseUrl : (text5 ?? AvatarRemoteService.GetDefaultContentBaseUrl()));
		string downloadCacheDirectory = AvatarRemoteService.GetDownloadCacheDirectory((!string.IsNullOrWhiteSpace(explicitDownloadCachePath)) ? explicitDownloadCachePath : text6);
		bool flag = forceRemote;
		string effectiveLibraryRoot = null;
		if (!flag)
		{
			if (!string.IsNullOrWhiteSpace(explicitLibraryRoot))
			{
				effectiveLibraryRoot = AvatarLibraryService.ResolveCollectionRoot(explicitLibraryRoot);
			}
			else
			{
				try
				{
					effectiveLibraryRoot = AvatarLibraryService.ResolveCollectionRoot(explicitRoot);
				}
				catch
				{
					flag = true;
				}
			}
		}
		string effectiveCachePath = (flag ? AvatarRemoteService.GetCachePath(explicitCachePath2) : AvatarIndexCache.GetCachePath(explicitCachePath2));
		return new AvatarResolvedPaths(text, effectiveLibraryRoot, text2, effectiveCachePath, text3, effectiveManifestUrl, text4, effectiveTitleMapUrl, text5, effectiveContentBaseUrl, text6, downloadCacheDirectory, flag);
	}

	public static async Task<AvatarLibraryIndex> LoadIndexAsync(AvatarResolvedPaths paths, bool useCache, CancellationToken cancellationToken)
	{
		if (paths.RemoteMode)
		{
			AvatarLibraryIndex avatarLibraryIndex = await AvatarRemoteService.LoadIndexAsync(paths.EffectiveManifestUrl, paths.EffectiveTitleMapUrl, paths.EffectiveContentBaseUrl, useCache, paths.EffectiveCachePath, cancellationToken);
			string text = null;
			if (!string.IsNullOrWhiteSpace(paths.ConfiguredLibraryRoot))
			{
				try
				{
					text = AvatarLibraryService.ResolveCollectionRoot(paths.ConfiguredLibraryRoot);
				}
				catch
				{
					text = null;
				}
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				try
				{
					text = AvatarLibraryService.ResolveCollectionRoot();
				}
				catch
				{
					text = null;
				}
			}
			if (!string.IsNullOrWhiteSpace(text))
			{
				string explicitCachePath = Path.Combine(CliPaths.CachePath, "avatar", "avatar-local-overlay-index.v2.json");
				AvatarLibraryIndex overlay = AvatarLibraryService.LoadIndex(text, useCache, explicitCachePath);
				avatarLibraryIndex = AvatarLibraryService.MergeMetadata(avatarLibraryIndex, overlay);
			}
			return avatarLibraryIndex;
		}
		return AvatarLibraryService.LoadIndex(paths.EffectiveLibraryRoot, useCache, paths.EffectiveCachePath);
	}

	public static void SaveConfiguredPaths(string? libraryRoot, string? cachePath, string? manifestUrl, string? titleMapUrl, string? contentBaseUrl, string? downloadCachePath, bool clear)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (clear)
		{
			cliConfig.AvatarLibraryRoot = null;
			cliConfig.AvatarCachePath = null;
			cliConfig.AvatarManifestUrl = null;
			cliConfig.AvatarTitleMapUrl = null;
			cliConfig.AvatarContentBaseUrl = null;
			cliConfig.AvatarDownloadCachePath = null;
		}
		else
		{
			if (!string.IsNullOrWhiteSpace(libraryRoot))
			{
				cliConfig.AvatarLibraryRoot = Path.GetFullPath(libraryRoot);
			}
			if (!string.IsNullOrWhiteSpace(cachePath))
			{
				cliConfig.AvatarCachePath = AvatarIndexCache.GetCachePath(cachePath);
			}
			if (!string.IsNullOrWhiteSpace(manifestUrl))
			{
				cliConfig.AvatarManifestUrl = manifestUrl.Trim();
			}
			if (!string.IsNullOrWhiteSpace(titleMapUrl))
			{
				cliConfig.AvatarTitleMapUrl = titleMapUrl.Trim();
			}
			if (!string.IsNullOrWhiteSpace(contentBaseUrl))
			{
				cliConfig.AvatarContentBaseUrl = contentBaseUrl.Trim().TrimEnd('/') + "/";
			}
			if (!string.IsNullOrWhiteSpace(downloadCachePath))
			{
				cliConfig.AvatarDownloadCachePath = AvatarRemoteService.GetDownloadCacheDirectory(downloadCachePath);
			}
		}
		cliConfig.Save();
	}

	public static bool TryParseXuid(string? text, out ulong xuid)
	{
		xuid = 0uL;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			string text3 = text2;
			text2 = text3.Substring(2, text3.Length - 2);
		}
		if (!ulong.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out xuid))
		{
			return ulong.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out xuid);
		}
		return true;
	}

	public static int ParseLimit(int? limit, int defaultValue = 100)
	{
		if (!limit.HasValue)
		{
			return defaultValue;
		}
		if (limit.Value > 0)
		{
			return limit.Value;
		}
		return int.MaxValue;
	}

	public static string DescribeLayout(AvatarItemRecord item)
	{
		if (!string.IsNullOrWhiteSpace(item.ContentType))
		{
			return item.ContentType;
		}
		return "root";
	}

	public static string ResolveItemDisplayName(AvatarItemRecord item)
	{
		return AvatarLibraryService.ResolveItemDisplayName(item.ContentId, item.DisplayName, item.GameName, item.TitleName);
	}

	public static string DescribeItem(AvatarItemRecord item)
	{
		return item.TitleName + " - " + ResolveItemDisplayName(item);
	}

	public static async Task<AvatarResolvedOwnership> ResolveOwnershipAsync(AvatarInstallSettingsBase settings, string targetIp, CancellationToken cancellationToken)
	{
		string? text = GetOptionText(settings, "Xuid") ?? GetCommandLineOptionText("xuid");
		string gamertag = GetOptionText(settings, "Gamertag") ?? GetCommandLineOptionText("gamertag");
		if (TryParseXuid(text, out var xuid))
		{
			return new AvatarResolvedOwnership(gamertag, xuid, $"0x{xuid:X16}", "Explicit XUID", FromCurrentUser: false);
		}
		int port = settings.XbdmPort ?? CliConfig.Load().DefaultPort ?? 730;
		int timeoutMs = settings.TimeoutMs ?? 5000;
		ProfileHelpers.XamUserInfo xamUserInfo = await ProfileHelpers.TryGetSignedInXamUserAsync(targetIp, port, timeoutMs, cancellationToken);
		if (xamUserInfo == null || string.IsNullOrWhiteSpace(xamUserInfo.Xuid) || !TryParseXuid(xamUserInfo.Xuid, out var xuid2))
		{
			string text2 = (string.IsNullOrWhiteSpace(xamUserInfo?.Gamertag) ? "none" : xamUserInfo.Gamertag);
			if (string.Equals(text2, "none", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("It seems you are not signed in. Please sign in and run `rgh signin state` to verify.");
			}
			throw new InvalidOperationException("It seems no usable signed-in profile was detected. Current user: " + text2 + ". Please sign in again and run `rgh signin state` to verify.");
		}
		return new AvatarResolvedOwnership(xamUserInfo.Gamertag, xuid2, $"0x{xuid2:X16}", HardwareHelpers.DescribeSignInState(xamUserInfo.SignInState), FromCurrentUser: true);
	}

	private static string? GetOptionText(object settings, string propertyName)
	{
		PropertyInfo property = settings.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property == null)
		{
			return null;
		}
		return property.GetValue(settings)?.ToString();
	}

	private static string? GetCommandLineOptionText(string optionName)
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		for (int i = 0; i < commandLineArgs.Length; i++)
		{
			string text = commandLineArgs[i];
			if (text.Equals("--" + optionName, StringComparison.OrdinalIgnoreCase) || text.Equals("-" + optionName, StringComparison.OrdinalIgnoreCase))
			{
				if (i + 1 < commandLineArgs.Length)
				{
					return commandLineArgs[i + 1];
				}
				return null;
			}
			string text2 = "--" + optionName + "=";
			if (text.StartsWith(text2, StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text;
				int length = text2.Length;
				return text3.Substring(length, text3.Length - length);
			}
		}
		return null;
	}

	public static IEnumerable<AvatarTitleSummary> FilterTitles(IEnumerable<AvatarTitleSummary> titles, string? search, int limit)
	{
		IEnumerable<AvatarTitleSummary> source = titles;
		if (!string.IsNullOrWhiteSpace(search))
		{
			string needle = search.Trim();
			source = source.Where((AvatarTitleSummary title) => title.TitleName.Contains(needle, StringComparison.OrdinalIgnoreCase) || title.TitleId.ToString("X8", CultureInfo.InvariantCulture).Contains(needle, StringComparison.OrdinalIgnoreCase) || title.Publishers.Any((string p) => p.Contains(needle, StringComparison.OrdinalIgnoreCase)));
		}
		return source.OrderBy<AvatarTitleSummary, string>((AvatarTitleSummary title) => title.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy((AvatarTitleSummary title) => title.TitleId).Take(limit);
	}

	public static IReadOnlyList<AvatarItemRecord> FilterItems(AvatarLibraryIndex index, AvatarItemsCommand.Settings settings)
	{
		uint? titleId = null;
		if (!string.IsNullOrWhiteSpace(settings.TitleId))
		{
			if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId2))
			{
				throw new InvalidOperationException("Invalid --titleid.");
			}
			titleId = titleId2;
		}
		AvatarItemQuery query = new AvatarItemQuery(titleId, settings.Search, settings.Publisher, settings.Tag, ParseLimit(settings.Limit));
		IEnumerable<AvatarItemRecord> source = AvatarLibraryService.SearchItems(index, query);
		if (!string.IsNullOrWhiteSpace(settings.Game))
		{
			string game = settings.Game.Trim();
			source = source.Where((AvatarItemRecord item) => item.TitleName.Contains(game, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(game, StringComparison.OrdinalIgnoreCase)));
		}
		return source.ToArray();
	}

	public static IReadOnlyList<AvatarItemRecord> SelectInstallItems(AvatarLibraryIndex index, AvatarInstallCommand.Settings settings)
	{
		if (!string.IsNullOrWhiteSpace(settings.ContentId))
		{
			uint? titleId = null;
			if (!string.IsNullOrWhiteSpace(settings.TitleId))
			{
				if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId2))
				{
					throw new InvalidOperationException("Invalid --titleid.");
				}
				titleId = titleId2;
			}
			AvatarItemRecord avatarItemRecord = AvatarLibraryService.FindByContentId(index, settings.ContentId, titleId);
			if (avatarItemRecord == null)
			{
				throw new FileNotFoundException("Avatar item " + settings.ContentId + " was not found in the library.");
			}
			return new AvatarItemRecord[1] { avatarItemRecord };
		}
		if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId3))
		{
			throw new InvalidOperationException("Provide --contentid or --titleid.");
		}
		if (!settings.All)
		{
			throw new InvalidOperationException("Use --all with --titleid to install every item for a title.");
		}
		IReadOnlyList<AvatarItemRecord> readOnlyList = AvatarLibraryService.SearchItems(index, new AvatarItemQuery(titleId3, null, null, null, int.MaxValue));
		if (readOnlyList.Count == 0)
		{
			throw new FileNotFoundException($"No avatar items were found for title 0x{titleId3:X8}.");
		}
		return readOnlyList;
	}

	public static async Task<IReadOnlyList<AvatarItemRecord>> MaterializeInstallItemsAsync(IReadOnlyList<AvatarItemRecord> items, AvatarResolvedPaths paths, CancellationToken cancellationToken)
	{
		if (!paths.RemoteMode)
		{
			return items;
		}
		List<AvatarItemRecord> materialized = new List<AvatarItemRecord>(items.Count);
		await CliOutput.RunBatchProgressAsync($"Avatar download {items.Count} item(s)", items.Select((AvatarItemRecord item) => new CliOutput.TransferBatchItem(DescribeItem(item), item.SizeBytes)).ToList(), async delegate(TransferBatchScope batch)
		{
			foreach (AvatarItemRecord item in items)
			{
				batch.StartFile(DescribeItem(item), item.SizeBytes);
				Progress<long> progress = new Progress<long>(delegate(long value)
				{
					batch.ReportFileProgress(value);
				});
				string sourcePath = await AvatarRemoteService.EnsureLocalPackageAsync(item, paths.EffectiveDownloadCachePath, progress, cancellationToken);
				materialized.Add(item with
				{
					SourcePath = sourcePath
				});
				batch.CompleteFile();
			}
		});
		return materialized;
	}

	public static async Task<string?> TryGetRemoteFileSizeTextAsync(AvatarInstallSettingsBase settings, string remotePath, CancellationToken cancellationToken)
	{
		try
		{
			var (host, port, user, pass, num) = await FtpHelpers.ResolveAsync(CreateFtpSettings(settings), cancellationToken);
			string result;
			await using (AsyncFtpClient client = new AsyncFtpClient(host, user, pass, port))
			{
				client.Config.ConnectTimeout = num;
				client.Config.ReadTimeout = num;
				client.Config.DataConnectionConnectTimeout = num;
				client.Config.DataConnectionReadTimeout = num;
				await client.Connect(cancellationToken);
				long? num2 = await FtpHelpers.TryGetFileSizeAsync(client, remotePath);
				result = (num2.HasValue ? FtpHelpers.FormatBytes(num2.Value) : null);
			}
			return result;
		}
		catch
		{
			try
			{
				var (host2, port2, timeoutMs) = await ResolveXbdmTargetAsync(settings, cancellationToken);
				using XbdmClient client2 = await XbdmClient.ConnectAsync(new XbdmConnectionOptions
				{
					Host = host2,
					Port = port2,
					TimeoutMs = timeoutMs
				}, cancellationToken);
				return await TryGetRemoteFileSizeTextViaXbdmAsync(client2, remotePath, cancellationToken);
			}
			catch
			{
				return null;
			}
		}
	}

	public static async Task<(string Ip, int Port, int TimeoutMs)> ResolveXbdmTargetAsync(AvatarInstallSettingsBase settings, CancellationToken cancellationToken)
	{
		return await CliHelpers.ResolveTargetAsync(new ConnectionSettings
		{
			Ip = settings.Ip,
			Port = settings.XbdmPort,
			TimeoutMs = settings.TimeoutMs
		}, cancellationToken);
	}

	public static FtpConnectionSettings CreateFtpSettings(AvatarInstallSettingsBase settings)
	{
		return new FtpConnectionSettings
		{
			Ip = settings.Ip,
			Port = settings.Port,
			User = settings.User,
			Pass = settings.Pass,
			TimeoutMs = settings.TimeoutMs,
			Json = settings.Json
		};
	}

	public static string ToXbdmPath(string remotePath)
	{
		string text = remotePath.Replace('/', '\\').Trim();
		text = text.TrimStart('\\');
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("Remote path cannot be empty.");
		}
		int num = text.IndexOf('\\');
		if (num < 0)
		{
			if (!text.EndsWith(":", StringComparison.Ordinal))
			{
				return text + ":";
			}
			return text;
		}
		string text2 = text.Substring(0, num);
		if (!text2.EndsWith(":", StringComparison.Ordinal))
		{
			text2 += ":";
		}
		string text3 = text;
		int num2 = num + 1;
		string text4 = text3.Substring(num2, text3.Length - num2).TrimStart('\\');
		if (!string.IsNullOrWhiteSpace(text4))
		{
			return text2 + "\\" + text4;
		}
		return text2;
	}

	public static async Task EnsureRemoteDirectoryViaXbdmAsync(XbdmClient client, string remoteDirectory, CancellationToken cancellationToken)
	{
		string text = ToXbdmPath(remoteDirectory).TrimEnd('\\');
		int num = text.IndexOf('\\');
		if (num < 0)
		{
			return;
		}
		string current = text.Substring(0, num);
		string text2 = text;
		int num2 = num + 1;
		string text3 = text2.Substring(num2, text2.Length - num2);
		string[] array = text3.Split(new char[1] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text4 in array)
		{
			current = current + "\\" + text4;
			try
			{
				await client.CreateDirectoryAsync(current, cancellationToken);
			}
			catch
			{
			}
		}
	}

	public static async Task<bool> RemoteFileExistsViaXbdmAsync(XbdmClient client, string remotePath, CancellationToken cancellationToken)
	{
		string path = ToXbdmPath(remotePath);
		string directoryName = Path.GetDirectoryName(path);
		string fileName = Path.GetFileName(path);
		if (string.IsNullOrWhiteSpace(directoryName) || string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}
		try
		{
			return (await client.GetDirectoryAsync(directoryName.EndsWith("\\", StringComparison.Ordinal) ? directoryName : (directoryName + "\\"), cancellationToken)).Any((XbdmFileEntry entry) => !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return false;
		}
	}

	public static async Task<string?> TryGetRemoteFileSizeTextViaXbdmAsync(XbdmClient client, string remotePath, CancellationToken cancellationToken)
	{
		string path = ToXbdmPath(remotePath);
		string directoryName = Path.GetDirectoryName(path);
		string fileName = Path.GetFileName(path);
		if (string.IsNullOrWhiteSpace(directoryName) || string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		try
		{
			XbdmFileEntry xbdmFileEntry = (await client.GetDirectoryAsync(directoryName.EndsWith("\\", StringComparison.Ordinal) ? directoryName : (directoryName + "\\"), cancellationToken)).FirstOrDefault((XbdmFileEntry entry) => !entry.IsDirectory && string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase));
			return (xbdmFileEntry != null) ? FtpHelpers.FormatBytes(checked((long)xbdmFileEntry.Size)) : null;
		}
		catch
		{
			return null;
		}
	}
}
