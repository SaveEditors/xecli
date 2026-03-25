using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal static class FtpHelpers
{
	private const int ReconnectAttempts = 3;

	private const int ReconnectDelaySeconds = 10;

	private const int UploadReconnectDelayMs = 500;

	private static readonly HashSet<string> RootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Hdd1", "HddX", "Usb0", "Usb1", "Usb2", "System", "SysExt", "Game", "Cache", "Flash",
		"D"
	};

	public static async Task<(string Ip, int Port, string User, string Pass, int TimeoutMs)> ResolveAsync(FtpConnectionSettings settings, CancellationToken cancellationToken)
	{
		CliConfig config = CliConfig.Load();
		string text = settings.Ip ?? config.DefaultIp;
		if (string.IsNullOrWhiteSpace(text))
		{
			string item = (await CliHelpers.ResolveTargetAsync(new ConnectionSettings(), cancellationToken)).Item1;
			text = item;
		}
		int num = settings.Port ?? config.DefaultFtpPort ?? 21;
		string text2 = settings.User ?? config.DefaultFtpUser ?? "xboxftp";
		string text3 = settings.Pass ?? config.DefaultFtpPassword ?? "xboxftp";
		int item2 = settings.TimeoutMs ?? 5000;
		config.DefaultIp = text;
		config.DefaultFtpPort = num;
		config.DefaultFtpUser = text2;
		config.DefaultFtpPassword = text3;
		config.Save();
		return (Ip: text, Port: num, User: text2, Pass: text3, TimeoutMs: item2);
	}

	public static AsyncFtpClient CreateClient(string ip, int port, string user, string pass, int timeoutMs)
	{
		AsyncFtpClient asyncFtpClient = new AsyncFtpClient(ip, user, pass, port);
		ConfigureClient(asyncFtpClient, timeoutMs);
		return asyncFtpClient;
	}

	public static void ConfigureClient(AsyncFtpClient client, int timeoutMs)
	{
		client.Config.ConnectTimeout = timeoutMs;
		client.Config.ReadTimeout = timeoutMs;
		client.Config.DataConnectionConnectTimeout = timeoutMs;
		client.Config.DataConnectionReadTimeout = timeoutMs;
		client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
		client.Config.UploadDataType = FtpDataType.Binary;
		client.Config.DownloadDataType = FtpDataType.Binary;
		client.Config.RetryAttempts = 3;
		client.Config.SocketKeepAlive = true;
	}

	public static async Task<int> WithClientAsync(FtpConnectionSettings settings, Func<AsyncFtpClient, Task<int>> action, CancellationToken cancellationToken)
	{
		(string, int, string, string, int) tuple = await ResolveAsync(settings, cancellationToken);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		Exception lastError = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			try
			{
				int result;
				await using (AsyncFtpClient client = CreateClient(ip, port, user, pass, timeout))
				{
					await client.Connect(cancellationToken);
					result = await action(client);
				}
				return result;
			}
			catch (Exception ex) when (IsTransient(ex))
			{
				lastError = ex;
				if (attempt < 3)
				{
					if (await ShowReconnectCountdownAsync(attempt, 3, 10, cancellationToken))
					{
						throw new OperationCanceledException("Reconnection cancelled by user.");
					}
					continue;
				}
			}
			break;
		}
		throw lastError ?? new IOException("Unable to connect to FTP server.");
	}

	private static bool IsTransient(Exception ex)
	{
		if (!(ex is IOException) && !(ex is SocketException))
		{
			return ex is TimeoutException;
		}
		return true;
	}

	private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, CancellationToken cancellationToken)
	{
		Task<string?> stopTask = EnsureStopTask();
		for (int remaining = seconds; remaining > 0; remaining--)
		{
			AnsiConsole.MarkupLine($"[yellow]Attempting FTP reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");
			if (await Task.WhenAny(Task.Delay(1000, cancellationToken), stopTask) == stopTask)
			{
				string text = await stopTask;
				stopTask = EnsureStopTask();
				if (text != null && text.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static Task<string?> EnsureStopTask()
	{
		if (Console.IsInputRedirected)
		{
			return Task.FromResult<string>(null);
		}
		return Task.Run(() => Console.ReadLine());
	}

	public static string NormalizePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "/";
		}
		string text = path.Trim().Replace('\\', '/');
		int num = text.IndexOf(':');
		if (num > 0)
		{
			string text2 = text.Substring(0, num);
			string text3 = text.Substring(num + 1).TrimStart('/');
			if (text3.Length != 0)
			{
				return "/" + text2 + "/" + text3;
			}
			return "/" + text2;
		}
		if (!text.StartsWith("/", StringComparison.Ordinal))
		{
			text = "/" + text.TrimStart('/');
		}
		return text;
	}

	public static bool IsLikelyRootListing(string requestedPath, IReadOnlyList<FtpListItem> items)
	{
		if (string.Equals(requestedPath, "/", StringComparison.Ordinal))
		{
			return false;
		}
		if (items.Count == 0)
		{
			return false;
		}
		int num = items.Count((FtpListItem i) => RootNames.Contains(i.Name));
		int num2 = Math.Min(3, items.Count);
		return num >= num2;
	}

	public static async Task<(FtpListItem[] Items, bool RootListing)> GetListingWithFallbackAsync(AsyncFtpClient client, string path)
	{
		FtpListItem[] items = await client.GetListing(path, FtpListOption.AllFiles);
		bool rootListing = IsLikelyRootListing(path, items);
		if (rootListing && (await client.Execute("CWD " + path)).Success)
		{
			FtpListItem[] array = await client.GetListing(".", FtpListOption.AllFiles);
			if (!IsLikelyRootListing(path, array))
			{
				items = array;
				rootListing = false;
			}
		}
		return (Items: items, RootListing: rootListing);
	}

	public static async Task<long?> TryGetFileSizeAsync(AsyncFtpClient client, string path)
	{
		try
		{
			long num = await client.GetFileSize(path, -1L);
			if (num >= 0)
			{
				return num;
			}
		}
		catch
		{
		}
		try
		{
			FtpListItem ftpListItem = await client.GetObjectInfo(path);
			if (ftpListItem != null && ftpListItem.Type == FtpObjectType.File && ftpListItem.Size >= 0)
			{
				return ftpListItem.Size;
			}
		}
		catch
		{
		}
		try
		{
			string text = NormalizePath(path).TrimEnd('/');
			int num2 = text.LastIndexOf('/');
			if (num2 >= 0)
			{
				string path2 = ((num2 == 0) ? "/" : text.Substring(0, num2));
				string name = text.Substring(num2 + 1);
				FtpListItem ftpListItem2 = (await GetListingWithFallbackAsync(client, path2)).Item1.FirstOrDefault((FtpListItem item) => item.Type == FtpObjectType.File && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
				if (ftpListItem2 != null && ftpListItem2.Size >= 0)
				{
					return ftpListItem2.Size;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public static async Task EnsureRemoteDirectoryAsync(AsyncFtpClient client, string path)
	{
		string text = NormalizePath(path).Trim('/');
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string[] array = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
		StringBuilder current = new StringBuilder();
		string[] array2 = array;
		foreach (string value in array2)
		{
			current.Append('/').Append(value);
			string currentPath = current.ToString();
			if (!(await client.DirectoryExists(currentPath)))
			{
				await client.CreateDirectory(currentPath, force: true);
			}
		}
	}

	public static async Task UploadFileVerifiedAsync(string ip, int port, string user, string pass, int timeoutMs, string localPath, string remotePath, bool ensureRemoteDirectory, IProgress<FtpProgress>? progress, CancellationToken cancellationToken)
	{
		string normalizedRemotePath = NormalizePath(remotePath);
		string parentDirectory = GetParentDirectory(normalizedRemotePath);
		Exception lastError = null;
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				await using AsyncFtpClient client = CreateClient(ip, port, user, pass, timeoutMs);
				await client.Connect(cancellationToken);
				if (ensureRemoteDirectory && parentDirectory != null)
				{
					await EnsureRemoteDirectoryAsync(client, parentDirectory);
				}
				long expectedSize = new FileInfo(localPath).Length;
				FtpStatus ftpStatus = await client.UploadFile(localPath, normalizedRemotePath, FtpRemoteExists.Overwrite, createRemoteDir: false, FtpVerify.None, progress, cancellationToken);
				if (ftpStatus != FtpStatus.Success)
				{
					throw new IOException($"FTP upload did not complete for {normalizedRemotePath} (status: {ftpStatus}).");
				}
				long? num = await TryGetFileSizeAsync(client, normalizedRemotePath);
				if (!num.HasValue || num.Value != expectedSize)
				{
					throw new IOException($"FTP upload size mismatch for {normalizedRemotePath} (expected {expectedSize} bytes, got {(num.HasValue ? num.Value.ToString(CultureInfo.InvariantCulture) : "unknown")}).");
				}
				return;
			}
			catch (Exception ex) when (attempt < 3 && IsTransient(ex))
			{
				lastError = ex;
				await TryDeleteRemoteFileAsync(ip, port, user, pass, timeoutMs, normalizedRemotePath, cancellationToken);
				await Task.Delay(500, cancellationToken);
			}
			catch (Exception ex2)
			{
				lastError = ex2;
				break;
			}
		}
		throw lastError ?? new IOException("Unable to upload " + Path.GetFileName(localPath) + ".");
	}

	public static async Task UploadBytesVerifiedAsync(string ip, int port, string user, string pass, int timeoutMs, byte[] data, string remotePath, bool ensureRemoteDirectory, IProgress<FtpProgress>? progress, CancellationToken cancellationToken)
	{
		string tempPath = Path.Combine(Path.GetTempPath(), $"xecli-ftp-bytes-{Guid.NewGuid():N}.tmp");
		try
		{
			await File.WriteAllBytesAsync(tempPath, data, cancellationToken);
			await UploadFileVerifiedAsync(ip, port, user, pass, timeoutMs, tempPath, remotePath, ensureRemoteDirectory, progress, cancellationToken);
		}
		finally
		{
			try
			{
				if (File.Exists(tempPath))
				{
					File.Delete(tempPath);
				}
			}
			catch
			{
			}
		}
	}

	public static string FormatBytes(long bytes)
	{
		double num = bytes;
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.##} {array[num2]}";
	}

	private static async Task TryDeleteRemoteFileAsync(string ip, int port, string user, string pass, int timeoutMs, string remotePath, CancellationToken cancellationToken)
	{
		_ = 3;
		try
		{
			await using AsyncFtpClient cleanupClient = CreateClient(ip, port, user, pass, timeoutMs);
			await cleanupClient.Connect(cancellationToken);
			if (await cleanupClient.FileExists(remotePath))
			{
				await cleanupClient.DeleteFile(remotePath);
			}
		}
		catch
		{
		}
	}

	private static string? GetParentDirectory(string remotePath)
	{
		string text = NormalizePath(remotePath).TrimEnd('/');
		int num = text.LastIndexOf('/');
		if (num < 0)
		{
			return null;
		}
		if (num != 0)
		{
			return text.Substring(0, num);
		}
		return "/";
	}
}
