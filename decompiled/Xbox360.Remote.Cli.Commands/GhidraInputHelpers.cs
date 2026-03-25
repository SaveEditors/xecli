using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal static class GhidraInputHelpers
{
	public static async Task<string?> ResolveRunningFtpPathAsync()
	{
		_ = 1;
		try
		{
			using XbdmClient client = await CliHelpers.ConnectAsync(new ConnectionSettings(), CancellationToken.None);
			return MapDevicePathToFtpPath(await client.GetRunningXexPathAsync(null, CancellationToken.None));
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine("[red]Failed to resolve running XEX:[/] " + Markup.Escape(ex.Message));
			return null;
		}
	}

	public static async Task<string?> DownloadViaFtpAsync(string ftpPath)
	{
		string normalized = FtpHelpers.NormalizePath(ftpPath);
		string text = Path.GetFileName(normalized);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "downloaded.xex";
		}
		string cachePath = CliPaths.CachePath;
		Directory.CreateDirectory(cachePath);
		string localPath = Path.Combine(cachePath, text);
		await FtpHelpers.WithClientAsync(new FtpConnectionSettings(), async delegate(AsyncFtpClient client)
		{
			long valueOrDefault = (await FtpHelpers.TryGetFileSizeAsync(client, normalized)).GetValueOrDefault();
			await CliOutput.RunWithProgressAsync("FTP fetch " + Markup.Escape(normalized), (valueOrDefault > 0) ? new long?(valueOrDefault) : ((long?)null), async delegate(IProgress<CliOutput.TransferProgressUpdate> progress)
			{
				Progress<FtpProgress> progress2 = new Progress<FtpProgress>(delegate(FtpProgress p)
				{
					if (p.TransferredBytes >= 0)
					{
						progress.Report(new CliOutput.TransferProgressUpdate(p.TransferredBytes, "receiving"));
					}
				});
				await client.DownloadFile(localPath, normalized, FtpLocalExists.Overwrite, FtpVerify.None, progress2);
			});
			return 0;
		}, CancellationToken.None);
		OperationFeedback.WriteSuccess("FTP fetch complete", $"[cyan]{Markup.Escape(normalized)}[/] -> [white]{Markup.Escape(localPath)}[/]");
		return localPath;
	}

	public static string? MapDevicePathToFtpPath(string? devicePath)
	{
		if (string.IsNullOrWhiteSpace(devicePath))
		{
			return null;
		}
		string text = devicePath.Trim().Replace('\\', '/');
		if (!text.StartsWith("/", StringComparison.Ordinal))
		{
			text = "/" + text;
		}
		if (TryMapPrefix(text, "/Device/Harddisk0/Partition1/", "/Hdd1/", out string mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Harddisk0/Partition2/", "/HddX/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Harddisk0/Partition0/", "/Hdd0/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Usb0/", "/Usb0/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Usb1/", "/Usb1/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Usb2/", "/Usb2/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Mu/", "/Mu/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/IntMu/", "/IntMu/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/MmcMu/", "/MmcMu/", out mapped))
		{
			return mapped;
		}
		if (TryMapPrefix(text, "/Device/Cdrom0/", "/D/", out mapped))
		{
			return mapped;
		}
		return null;
	}

	private static bool TryMapPrefix(string path, string prefix, string target, out string mapped)
	{
		if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		{
			string text = path.Substring(prefix.Length);
			mapped = target + text;
			return true;
		}
		mapped = string.Empty;
		return false;
	}
}
