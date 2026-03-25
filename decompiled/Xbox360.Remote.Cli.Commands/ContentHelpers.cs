using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentFTP;

namespace Xbox360.Remote.Cli.Commands;

internal static class ContentHelpers
{
	internal sealed record ContentTypeSummary(string Code, string Kind, int Count);

	internal sealed record ContentEntry(uint TitleId, string Name, List<string> Devices, List<ContentTypeSummary> Types);

	internal sealed record ContentLocation(string Device, string Path);

	private static readonly HashSet<string> DefaultRoots = new HashSet<string>(new string[4] { "Hdd1", "Usb0", "Usb1", "HddX" }, StringComparer.OrdinalIgnoreCase);

	public static async Task<List<ContentEntry>> ListAsync(string ip, int port, string user, string pass, int timeoutMs, string? devices, uint? titleId, bool showTypes)
	{
		Dictionary<uint, ContentEntry> map = new Dictionary<uint, ContentEntry>();
		foreach (string root in ParseRoots(devices))
		{
			string basePath = "/" + root + "/Content/0000000000000000";
			(FtpListItem[], bool)? tuple = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, basePath);
			if (!tuple.HasValue || tuple.Value.Item2)
			{
				continue;
			}
			FtpListItem[] item = tuple.Value.Item1;
			foreach (FtpListItem ftpListItem in item)
			{
				if (ftpListItem.Type != FtpObjectType.Directory || !SaveHelpers.TryParseTitleId(ftpListItem.Name, out var titleId2) || (titleId.HasValue && titleId2 != titleId.Value))
				{
					continue;
				}
				if (!map.TryGetValue(titleId2, out ContentEntry entry))
				{
					string name = ResolveTitleName(titleId2);
					entry = (map[titleId2] = new ContentEntry(titleId2, name, new List<string>(), new List<ContentTypeSummary>()));
				}
				if (!entry.Devices.Contains<string>(root, StringComparer.OrdinalIgnoreCase))
				{
					entry.Devices.Add(root);
				}
				if (!showTypes)
				{
					continue;
				}
				string path = basePath + "/" + ftpListItem.Name;
				(FtpListItem[], bool)? tuple2 = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, path);
				if (!tuple2.HasValue || tuple2.Value.Item2)
				{
					continue;
				}
				foreach (FtpListItem type in tuple2.Value.Item1.Where((FtpListItem ftpListItem2) => ftpListItem2.Type == FtpObjectType.Directory))
				{
					string kind = DescribeContentType(type.Name);
					ContentTypeSummary contentTypeSummary = entry.Types.FirstOrDefault((ContentTypeSummary t) => t.Code.Equals(type.Name, StringComparison.OrdinalIgnoreCase));
					if (contentTypeSummary == null)
					{
						entry.Types.Add(new ContentTypeSummary(type.Name, kind, 1));
						continue;
					}
					entry.Types.Remove(contentTypeSummary);
					entry.Types.Add(contentTypeSummary with
					{
						Count = contentTypeSummary.Count + 1
					});
				}
				entry = null;
			}
		}
		return map.Values.OrderBy((ContentEntry e) => e.TitleId).ToList();
	}

	public static async Task<List<ContentLocation>> FindLocationsAsync(string ip, int port, string user, string pass, int timeoutMs, string? devices, uint titleId)
	{
		List<ContentLocation> locations = new List<ContentLocation>();
		foreach (string root in ParseRoots(devices))
		{
			string path = $"/{root}/Content/0000000000000000/{titleId:X8}";
			(FtpListItem[], bool)? tuple = await SaveHelpersTryGetListingAsync(ip, port, user, pass, timeoutMs, path);
			if (tuple.HasValue && !tuple.Value.Item2)
			{
				locations.Add(new ContentLocation(root, path));
			}
		}
		return locations;
	}

	public static async Task DeletePathAsync(string ip, int port, string user, string pass, int timeoutMs, string path)
	{
		await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
		client.Config.ConnectTimeout = timeoutMs;
		client.Config.ReadTimeout = timeoutMs;
		client.Config.DataConnectionConnectTimeout = timeoutMs;
		client.Config.DataConnectionReadTimeout = timeoutMs;
		await client.Connect();
		await client.DeleteDirectory(path);
	}

	private static async Task<(FtpListItem[] items, bool rootListing)?> SaveHelpersTryGetListingAsync(string ip, int port, string user, string pass, int timeoutMs, string path)
	{
		_ = 2;
		try
		{
			(FtpListItem[] items, bool rootListing)? result;
			await using (AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port))
			{
				client.Config.ConnectTimeout = timeoutMs;
				client.Config.ReadTimeout = timeoutMs;
				client.Config.DataConnectionConnectTimeout = timeoutMs;
				client.Config.DataConnectionReadTimeout = timeoutMs;
				await client.Connect();
				result = await FtpHelpers.GetListingWithFallbackAsync(client, path);
			}
			return result;
		}
		catch
		{
			return null;
		}
	}

	private static IEnumerable<string> ParseRoots(string? devices)
	{
		if (string.IsNullOrWhiteSpace(devices))
		{
			return DefaultRoots;
		}
		return devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase);
	}

	private static string ResolveTitleName(uint titleId)
	{
		if (!TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry entry) || !(entry != null))
		{
			return $"Title 0x{titleId:X8}";
		}
		return entry.Name;
	}

	private static string DescribeContentType(string code)
	{
		return code.ToUpperInvariant() switch
		{
			"00007000" => "Game", 
			"000D0000" => "XBLA", 
			"000B0000" => "Title Update", 
			"00000002" => "DLC", 
			"00010000" => "Profile Data", 
			"00004000" => "Installed Game", 
			"00000001" => "Save", 
			_ => code, 
		};
	}
}
