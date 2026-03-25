using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace Xbox360.Remote.Cli.Commands;

internal static class SaveHelpers
{
	internal sealed record SaveFileRecord(string Device, string ProfileId, string RemotePath, string RelativePath, long Size, DateTime Modified);

	internal sealed record SaveUploadRecord(string LocalPath, string RemotePath, long Size);

	private static readonly HashSet<string> SupportedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hdd1", "HddX", "Usb0", "Usb1", "Usb2", "UsbMu", "Mu", "IntMu", "MmcMu" };

	public static async Task<List<SaveFileRecord>> EnumerateSaveFilesAsync(string ip, int port, string user, string pass, int timeoutMs, uint titleId, string? profileId, string? devices)
	{
		string titleText = titleId.ToString("X8", CultureInfo.InvariantCulture);
		HashSet<string> filterProfiles = (string.IsNullOrWhiteSpace(profileId) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(new string[1] { profileId }, StringComparer.OrdinalIgnoreCase));
		HashSet<string> requestedRoots = ParseRootFilter(devices);
		List<SaveFileRecord> files = new List<SaveFileRecord>();
		foreach (string root in await GetAvailableRootsAsync(ip, port, user, pass, timeoutMs, requestedRoots))
		{
			string contentRoot = "/" + root + "/Content";
			Trace("content " + contentRoot);
			if (filterProfiles.Count > 0)
			{
				foreach (string item2 in filterProfiles)
				{
					string text = $"{contentRoot}/{item2}/{titleText}";
					Trace("direct " + text);
					await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, item2, text, files);
				}
				continue;
			}
			(FtpListItem[], bool)? tuple = await TryGetListingAsync(ip, port, user, pass, timeoutMs, contentRoot);
			if (!tuple.HasValue || tuple.Value.Item2)
			{
				continue;
			}
			FtpListItem[] item = tuple.Value.Item1;
			foreach (FtpListItem ftpListItem in item)
			{
				if (ftpListItem.Type == FtpObjectType.Directory && IsProfileId(ftpListItem.Name))
				{
					string text2 = $"{contentRoot}/{ftpListItem.Name}/{titleText}";
					Trace("walk " + text2);
					await CollectTitleFilesAsync(ip, port, user, pass, timeoutMs, root, ftpListItem.Name, text2, files);
				}
			}
		}
		return files;
	}

	public static async Task DownloadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath, string localPath, IProgress<long>? progress = null)
	{
		await WithFreshClientAsync(ip, port, user, pass, timeoutMs, async delegate(AsyncFtpClient client)
		{
			Progress<FtpProgress> progress2 = ((progress == null) ? null : new Progress<FtpProgress>(delegate(FtpProgress p)
			{
				if (p.TransferredBytes >= 0)
				{
					progress.Report(p.TransferredBytes);
				}
			}));
			await client.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, progress2);
		});
	}

	public static async Task UploadFileAsync(string ip, int port, string user, string pass, int timeoutMs, string localPath, string remotePath, IProgress<long>? progress = null)
	{
		Progress<FtpProgress> progress2 = ((progress == null) ? null : new Progress<FtpProgress>(delegate(FtpProgress p)
		{
			if (p.TransferredBytes >= 0)
			{
				progress.Report(p.TransferredBytes);
			}
		}));
		await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeoutMs, localPath, remotePath, ensureRemoteDirectory: true, progress2, CancellationToken.None);
	}

	public static async Task<bool> RemoteFileExistsAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath)
	{
		try
		{
			return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, (AsyncFtpClient client) => client.FileExists(remotePath));
		}
		catch
		{
			return false;
		}
	}

	public static string FormatTitleLabel(uint titleId)
	{
		if (TitleIdDatabase.Instance.TryResolve(titleId, null, out TitleIdEntry entry) && entry != null)
		{
			return $"{entry.Name} (0x{titleId:X8})";
		}
		return $"0x{titleId:X8}";
	}

	public static string BuildOutputFolderName(uint titleId)
	{
		string text = FormatTitleLabel(titleId);
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return text;
	}

	public static bool TryParseTitleId(string? text, out uint titleId)
	{
		titleId = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			text2 = text2.Substring(2);
		}
		return uint.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId);
	}

	public static bool IsProfileId(string name)
	{
		if (name.Length != 16)
		{
			return false;
		}
		return name.All((char ch) => Uri.IsHexDigit(ch));
	}

	public static string BuildTitleRoot(string device, string profileId, uint titleId)
	{
		string value = device.Trim().Trim(':', '\\', '/');
		return $"/{value}/Content/{profileId}/{titleId:X8}";
	}

	public static List<SaveUploadRecord> BuildUploadPlanFromDirectory(string localRoot, string remoteRoot)
	{
		List<SaveUploadRecord> list = new List<SaveUploadRecord>();
		string fileName = Path.GetFileName(localRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		foreach (string item in Directory.EnumerateFiles(localRoot, "*", SearchOption.AllDirectories))
		{
			string value = Path.GetRelativePath(localRoot, item).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
			string remotePath = NormalizeRemotePath($"{remoteRoot}/{fileName}/{value}");
			list.Add(new SaveUploadRecord(item, remotePath, new FileInfo(item).Length));
		}
		return list;
	}

	public static List<SaveUploadRecord> BuildUploadPlanFromFile(string localPath, string remoteRoot, string? relativeRemotePath)
	{
		string text = (string.IsNullOrWhiteSpace(relativeRemotePath) ? Path.GetFileName(localPath) : relativeRemotePath.Trim().Replace('\\', '/'));
		string remotePath = NormalizeRemotePath(remoteRoot + "/" + text);
		return new List<SaveUploadRecord>
		{
			new SaveUploadRecord(localPath, remotePath, new FileInfo(localPath).Length)
		};
	}

	private static HashSet<string> ParseRootFilter(string? devices)
	{
		if (string.IsNullOrWhiteSpace(devices))
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
		return new HashSet<string>(from root in devices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where SupportedRoots.Contains(root)
			select root, StringComparer.OrdinalIgnoreCase);
	}

	private static async Task<List<string>> GetAvailableRootsAsync(string ip, int port, string user, string pass, int timeoutMs, HashSet<string> requestedRoots)
	{
		List<string> roots = new List<string>();
		(FtpListItem[], bool)? tuple = await TryGetListingAsync(ip, port, user, pass, timeoutMs, "/");
		if (tuple.HasValue)
		{
			foreach (FtpListItem item in tuple.Value.Item1.Where((FtpListItem i) => i.Type == FtpObjectType.Directory))
			{
				if (SupportedRoots.Contains(item.Name) && (requestedRoots.Count <= 0 || requestedRoots.Contains(item.Name)))
				{
					roots.Add(item.Name);
				}
			}
		}
		if (roots.Count > 0)
		{
			return roots;
		}
		if (requestedRoots.Count > 0)
		{
			return requestedRoots.ToList();
		}
		return SupportedRoots.ToList();
	}

	private static async Task<(FtpListItem[] items, bool rootListing)?> TryGetListingAsync(string ip, int port, string user, string pass, int timeoutMs, string path)
	{
		try
		{
			Trace("list " + path);
			return await WithFreshClientAsync(ip, port, user, pass, timeoutMs, (AsyncFtpClient client) => FtpHelpers.GetListingWithFallbackAsync(client, path));
		}
		catch
		{
			Trace("fail " + path);
			return null;
		}
	}

	private static async Task CollectTitleFilesAsync(string ip, int port, string user, string pass, int timeoutMs, string root, string profileId, string titleRoot, List<SaveFileRecord> files)
	{
		Queue<(string Path, string Relative)> pending = new Queue<(string, string)>();
		pending.Enqueue((titleRoot, string.Empty));
		while (pending.Count > 0)
		{
			var (current, relative) = pending.Dequeue();
			Trace("open " + current);
			(FtpListItem[], bool)? tuple2 = await TryGetListingAsync(ip, port, user, pass, timeoutMs, current);
			if (!tuple2.HasValue || (tuple2.Value.Item2 && !string.Equals(current, titleRoot, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}
			FtpListItem[] item = tuple2.Value.Item1;
			foreach (FtpListItem ftpListItem in item)
			{
				string name = ftpListItem.Name;
				if ((!(name == ".") && !(name == "..")) || 1 == 0)
				{
					string text = (string.IsNullOrWhiteSpace(relative) ? ftpListItem.Name : (relative + "/" + ftpListItem.Name));
					string path = (string.IsNullOrWhiteSpace(ftpListItem.FullName) ? (current.TrimEnd('/') + "/" + ftpListItem.Name) : ftpListItem.FullName);
					path = NormalizeRemotePath(path);
					if (ftpListItem.Type == FtpObjectType.Directory)
					{
						pending.Enqueue((path, text));
						continue;
					}
					files.Add(new SaveFileRecord(root, profileId, path, text, ftpListItem.Size, ftpListItem.Modified));
					Trace("file " + path);
				}
			}
		}
	}

	private static async Task<T> WithFreshClientAsync<T>(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task<T>> action)
	{
		T result;
		await using (AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs))
		{
			await client.Connect();
			result = await action(client);
		}
		return result;
	}

	private static async Task WithFreshClientAsync(string ip, int port, string user, string pass, int timeoutMs, Func<AsyncFtpClient, Task> action)
	{
		await using AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeoutMs);
		await client.Connect();
		await action(client);
	}

	private static string NormalizeRemotePath(string path)
	{
		string text = path.Replace('\\', '/');
		while (text.Contains("/./", StringComparison.Ordinal))
		{
			text = text.Replace("/./", "/", StringComparison.Ordinal);
		}
		while (text.Contains("//", StringComparison.Ordinal))
		{
			text = text.Replace("//", "/", StringComparison.Ordinal);
		}
		return text.TrimEnd('/');
	}

	private static void Trace(string message)
	{
		if (string.Equals(Environment.GetEnvironmentVariable("XECLI_DEBUG_SAVE"), "1", StringComparison.Ordinal))
		{
			Console.Error.WriteLine("[save-debug] " + message);
		}
	}
}
