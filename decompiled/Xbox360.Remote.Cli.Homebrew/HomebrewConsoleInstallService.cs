using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;

namespace Xbox360.Remote.Cli.Homebrew;

internal static class HomebrewConsoleInstallService
{
	private static readonly string[] CandidateRoots = new string[4] { "Hdd1", "Usb0", "Usb1", "Usb2" };

	public static async Task<HomebrewConsoleInstallResult> InstallAsync(HomebrewInstallCommand.Settings settings, CancellationToken cancellationToken)
	{
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, cancellationToken);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		timeout = Math.Max(timeout, 15000);
		HomebrewConsoleInstallResult result;
		await using (AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeout))
		{
			await client.Connect(cancellationToken);
			IReadOnlyList<HomebrewConsoleDevice> readOnlyList = await DetectDevicesAsync(client);
			if (readOnlyList.Count == 0)
			{
				throw new InvalidOperationException("No supported console install targets were detected. XeCLI only installs to Hdd1, Usb0, Usb1, or Usb2.");
			}
			HomebrewConsoleDevice device = ResolveDevice(settings.Device, readOnlyList, settings.AutoConfirm);
			HomebrewLaunchIniMode iniMode = ResolveLaunchIniMode(settings.IniMode, settings.AutoConfirm);
			string iniPath = FtpHelpers.NormalizePath(string.IsNullOrWhiteSpace(settings.IniPath) ? "/Hdd1/launch.ini" : settings.IniPath);
			if (!settings.AutoConfirm && !Console.IsInputRedirected)
			{
				Table table = CliOutput.CreateTable();
				table.AddColumn(new TableColumn("[white]Field[/]"));
				table.AddColumn(new TableColumn("[white]Value[/]"));
				table.AddRow("[white]Console[/]", "[springgreen3_1]" + Markup.Escape(ip) + "[/]");
				table.AddRow("[white]Packages[/]", "[deepskyblue1]" + Markup.Escape(HomebrewPackageService.DescribePackageSelection(HomebrewPackageService.ResolveSelection(settings.Package))) + "[/]");
				table.AddRow("[white]Action[/]", "[grey]" + Markup.Escape(HomebrewPackageService.DescribeInstallAction(consoleInstall: true)) + "[/]");
				table.AddRow("[white]Target Device[/]", "[gold1]" + Markup.Escape(device.DisplayName) + "[/]");
				table.AddRow("[white]Install Root[/]", "[cyan]" + Markup.Escape("/" + device.RootName) + "[/]");
				table.AddRow("[white]launch.ini[/]", "[mediumpurple3]" + Markup.Escape(DescribeIniMode(iniMode)) + "[/]");
				table.AddRow("[white]launch.ini Path[/]", "[grey]" + Markup.Escape(iniPath) + "[/]");
				AnsiConsole.Write(table);
				if (!AnsiConsole.Confirm("Continue with the console install?"))
				{
					throw new OperationCanceledException("Console install cancelled by user.");
				}
			}
			string stagingRoot = Path.Combine(HomebrewPackageService.GetPackageCacheRoot(settings.CacheDirectory), "console-stage", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
			Directory.CreateDirectory(stagingRoot);
			try
			{
				HomebrewInstallResult staged = await HomebrewPackageService.StagePackagesAsync(settings.Package, stagingRoot, settings.CacheDirectory, settings.ForceDownload, cancellationToken);
				foreach (InstalledHomebrewPackage package in staged.Packages)
				{
					string remoteRoot = "/" + device.RootName + "/" + package.InstallFolderName;
					await UploadDirectoryAsync(client, ip, port, user, pass, timeout, package.InstallPath, remoteRoot, "Upload " + package.DisplayName, cancellationToken);
				}
				bool pluginsUploaded = false;
				string text = Path.Combine(stagingRoot, "Plugins");
				if (Directory.Exists(text))
				{
					await UploadDirectoryAsync(client, ip, port, user, pass, timeout, text, "/" + device.RootName + "/Plugins", "Upload bundled plugins", cancellationToken);
					pluginsUploaded = true;
				}
				bool launchIniBackedUp = false;
				bool launchIniWritten = false;
				switch (iniMode)
				{
				case HomebrewLaunchIniMode.Generated:
				{
					launchIniBackedUp = await BackupFileIfPresentAsync(ip, port, user, pass, timeout, client, iniPath, cancellationToken);
					string s = HomebrewPackageService.CreateLaunchIniText(device.LaunchAlias);
					await FtpHelpers.UploadBytesVerifiedAsync(ip, port, user, pass, timeout, Encoding.ASCII.GetBytes(s), iniPath, ensureRemoteDirectory: true, null, cancellationToken);
					launchIniWritten = true;
					break;
				}
				case HomebrewLaunchIniMode.Merge:
					(launchIniWritten, launchIniBackedUp) = await MergeLaunchIniAsync(client, ip, port, user, pass, timeout, iniPath, device.LaunchAlias, cancellationToken);
					break;
				}
				result = new HomebrewConsoleInstallResult(ip, device.RootName, device.LaunchAlias, iniPath, iniMode, staged.Packages, pluginsUploaded, launchIniWritten, launchIniBackedUp);
			}
			finally
			{
				TryDeleteDirectory(stagingRoot);
			}
		}
		return result;
	}

	public static void RenderInstallResult(HomebrewConsoleInstallResult result)
	{
		long bytes = result.Packages.Sum((InstalledHomebrewPackage package) => package.TotalBytes);
		OperationFeedback.WriteSuccess("Console homebrew install complete", $"[green]{result.Packages.Count}[/] package(s) [silver]{FormatBytes(bytes)}[/] -> [cyan]{Markup.Escape(result.DeviceRoot)}[/] on [springgreen3_1]{Markup.Escape(result.Ip)}[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[green]Package[/]"));
		table.AddColumn(new TableColumn("[cyan]Console Path[/]"));
		table.AddColumn(new TableColumn("[gold1]Files[/]"));
		table.AddColumn(new TableColumn("[grey]Size[/]"));
		foreach (InstalledHomebrewPackage package in result.Packages)
		{
			table.AddRow("[green]" + Markup.Escape(package.DisplayName) + "[/]", $"[cyan]/{Markup.Escape(result.DeviceRoot)}/{Markup.Escape(package.InstallFolderName)}[/]", $"[gold1]{package.FileCount}[/]", "[grey]" + FormatBytes(package.TotalBytes) + "[/]");
		}
		AnsiConsole.Write(table);
		if (result.PluginsUploaded)
		{
			AnsiConsole.MarkupLine("[grey]Bundled plugins copied to[/] [springgreen3_1]/" + Markup.Escape(result.DeviceRoot) + "/Plugins[/]");
		}
		string text = result.LaunchIniMode switch
		{
			HomebrewLaunchIniMode.Generated => "Generated XeCLI launch.ini", 
			HomebrewLaunchIniMode.Merge => "Updated existing launch.ini plugin entries", 
			_ => "launch.ini left unchanged", 
		};
		AnsiConsole.MarkupLine($"[grey]{Markup.Escape(text)}[/] [mediumpurple3]{Markup.Escape(result.LaunchIniPath)}[/]");
		if (result.LaunchIniBackedUp)
		{
			AnsiConsole.MarkupLine("[grey]Backup written to[/] [mediumpurple3]" + Markup.Escape(result.LaunchIniPath + ".bak") + "[/]");
		}
	}

	private static async Task<IReadOnlyList<HomebrewConsoleDevice>> DetectDevicesAsync(AsyncFtpClient client)
	{
		List<HomebrewConsoleDevice> devices = new List<HomebrewConsoleDevice>();
		string[] candidateRoots = CandidateRoots;
		foreach (string root in candidateRoots)
		{
			try
			{
				if (!(await client.DirectoryExists("/" + root)))
				{
					continue;
				}
			}
			catch
			{
				continue;
			}
			string text = (string.Equals(root, "Hdd1", StringComparison.OrdinalIgnoreCase) ? "Hdd" : root);
			devices.Add(new HomebrewConsoleDevice(root, text, text));
		}
		return devices;
	}

	private static HomebrewConsoleDevice ResolveDevice(string? requestedDevice, IReadOnlyList<HomebrewConsoleDevice> devices, bool autoConfirm)
	{
		if (!string.IsNullOrWhiteSpace(requestedDevice))
		{
			HomebrewConsoleDevice? homebrewConsoleDevice = devices.FirstOrDefault((HomebrewConsoleDevice device) => string.Equals(device.RootName, requestedDevice, StringComparison.OrdinalIgnoreCase) || string.Equals(device.DisplayName, requestedDevice, StringComparison.OrdinalIgnoreCase));
			if (homebrewConsoleDevice == null)
			{
				throw new InvalidOperationException($"Console device '{requestedDevice}' was not detected. Available devices: {string.Join(", ", devices.Select((HomebrewConsoleDevice d) => d.DisplayName))}.");
			}
			return homebrewConsoleDevice;
		}
		if (autoConfirm || Console.IsInputRedirected)
		{
			HomebrewConsoleDevice homebrewConsoleDevice2 = devices.FirstOrDefault((HomebrewConsoleDevice device) => string.Equals(device.RootName, "Hdd1", StringComparison.OrdinalIgnoreCase));
			if (homebrewConsoleDevice2 != null)
			{
				return homebrewConsoleDevice2;
			}
			if (devices.Count == 1)
			{
				return devices[0];
			}
			throw new InvalidOperationException("Multiple console devices were detected. Specify one with --device Hdd1, --device Usb0, --device Usb1, or --device Usb2.");
		}
		if (devices.Count == 1)
		{
			AnsiConsole.MarkupLine("[green]Detected console install target:[/] [springgreen3_1]" + Markup.Escape(devices[0].DisplayName) + "[/]");
			return devices[0];
		}
		AnsiConsole.MarkupLine("[bold white]Choose which console drive to install the homebrew to.[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]#[/]"));
		table.AddColumn(new TableColumn("[bold springgreen3_1]Drive[/]"));
		for (int num = 0; num < devices.Count; num++)
		{
			table.AddRow($"[white]{num + 1}[/]", "[springgreen3_1]" + Markup.Escape(devices[num].DisplayName) + "[/]");
		}
		AnsiConsole.Write(table);
		int result;
		while (true)
		{
			AnsiConsole.Markup("[white]Choose a drive number:[/] ");
			if (int.TryParse(Console.ReadLine(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 1 && result <= devices.Count)
			{
				break;
			}
			AnsiConsole.MarkupLine("[red]Enter a valid drive number.[/]");
		}
		return devices[result - 1];
	}

	private static HomebrewLaunchIniMode ResolveLaunchIniMode(string? requestedMode, bool autoConfirm)
	{
		if (!string.IsNullOrWhiteSpace(requestedMode))
		{
			if (TryParseIniMode(requestedMode, out var mode))
			{
				return mode;
			}
			throw new InvalidOperationException("Invalid --ini-mode. Expected generated, merge, or skip.");
		}
		if (autoConfirm || Console.IsInputRedirected)
		{
			return HomebrewLaunchIniMode.Skip;
		}
		AnsiConsole.MarkupLine("[bold white]Choose how XeCLI should handle launch.ini on the console.[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]#[/]"));
		table.AddColumn(new TableColumn("[bold springgreen3_1]Mode[/]"));
		table.AddColumn(new TableColumn("[bold grey]Behavior[/]"));
		table.AddRow("[white]1[/]", "[springgreen3_1]generated[/]", "[grey]Write a new XeCLI launch.ini with Aurora and bundled plugins.[/]");
		table.AddRow("[white]2[/]", "[springgreen3_1]merge[/]", "[grey]Keep the existing launch.ini and only add/update bundled plugin entries.[/]");
		table.AddRow("[white]3[/]", "[springgreen3_1]skip[/]", "[grey]Do not touch launch.ini. Only install the homebrew files.[/]");
		AnsiConsole.Write(table);
		while (true)
		{
			AnsiConsole.Markup("[white]Choose a launch.ini mode [2]:[/] ");
			string text = Console.ReadLine();
			switch (string.IsNullOrWhiteSpace(text) ? "2" : text.Trim())
			{
			case "1":
				return HomebrewLaunchIniMode.Generated;
			case "2":
				return HomebrewLaunchIniMode.Merge;
			case "3":
				return HomebrewLaunchIniMode.Skip;
			}
			AnsiConsole.MarkupLine("[red]Enter 1, 2, or 3.[/]");
		}
	}

	private static bool TryParseIniMode(string value, out HomebrewLaunchIniMode mode)
	{
		switch (value.Trim().ToLowerInvariant())
		{
		case "create":
		case "generated":
		case "new":
			mode = HomebrewLaunchIniMode.Generated;
			return true;
		case "modify":
		case "merge":
			mode = HomebrewLaunchIniMode.Merge;
			return true;
		case "none":
		case "skip":
		case "download":
			mode = HomebrewLaunchIniMode.Skip;
			return true;
		default:
			mode = HomebrewLaunchIniMode.Generated;
			return false;
		}
	}

	private static string DescribeIniMode(HomebrewLaunchIniMode mode)
	{
		return mode switch
		{
			HomebrewLaunchIniMode.Generated => "Create a new XeCLI launch.ini", 
			HomebrewLaunchIniMode.Merge => "Modify the existing launch.ini and add bundled plugins", 
			_ => "Install homebrew only and leave launch.ini unchanged", 
		};
	}

	private static async Task UploadDirectoryAsync(AsyncFtpClient client, string ip, int port, string user, string pass, int timeout, string localRoot, string remoteRoot, string title, CancellationToken cancellationToken)
	{
		string[] files = Directory.GetFiles(localRoot, "*", SearchOption.AllDirectories);
		IReadOnlyList<CliOutput.TransferBatchItem> items = files.Select((string file) => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length)).ToArray();
		await client.CreateDirectory(FtpHelpers.NormalizePath(remoteRoot), cancellationToken);
		await CliOutput.RunBatchProgressAsync(title, items, async delegate(TransferBatchScope batch)
		{
			string[] array = files;
			foreach (string text in array)
			{
				string text2 = Path.GetRelativePath(localRoot, text).Replace('\\', '/');
				string remoteFile = CombineRemotePath(remoteRoot, text2);
				long length = new FileInfo(text).Length;
				batch.StartFile(text2, length);
				Progress<FtpProgress> progress = new Progress<FtpProgress>(delegate(FtpProgress ftpProgress)
				{
					if (ftpProgress.TransferredBytes > 0)
					{
						batch.ReportFileProgress(ftpProgress.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, items.Count)}");
					}
				});
				await UploadFileWithRetryAsync(ip, port, user, pass, timeout, text, remoteFile, progress, cancellationToken);
				batch.CompleteFile();
			}
		});
	}

	private static async Task<bool> BackupFileIfPresentAsync(string ip, int port, string user, string pass, int timeout, AsyncFtpClient client, string remotePath, CancellationToken cancellationToken)
	{
		string tempFile = Path.Combine(Path.GetTempPath(), $"xecli-launch-backup-{Guid.NewGuid():N}.tmp");
		try
		{
			if (await client.DownloadFile(tempFile, remotePath, FtpLocalExists.Overwrite) != FtpStatus.Success)
			{
				return false;
			}
			await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, tempFile, remotePath + ".bak", ensureRemoteDirectory: false, null, cancellationToken);
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			try
			{
				if (File.Exists(tempFile))
				{
					File.Delete(tempFile);
				}
			}
			catch
			{
			}
		}
	}

	private static async Task<(bool Written, bool BackedUp)> MergeLaunchIniAsync(AsyncFtpClient client, string ip, int port, string user, string pass, int timeout, string iniPath, string launchAlias, CancellationToken cancellationToken)
	{
		string[] pluginPaths = new string[3]
		{
			launchAlias + ":\\Plugins\\xbdm.xex",
			launchAlias + ":\\Plugins\\JRPC2.xex",
			launchAlias + ":\\Plugins\\XDRPC.xex"
		};
		if (!(await client.FileExists(iniPath)))
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("[Plugins]");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
			handler.AppendLiteral("plugin1 = ");
			handler.AppendFormatted(pluginPaths[0]);
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
			handler.AppendLiteral("plugin2 = ");
			handler.AppendFormatted(pluginPaths[1]);
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(10, 1, stringBuilder2);
			handler.AppendLiteral("plugin3 = ");
			handler.AppendFormatted(pluginPaths[2]);
			stringBuilder5.AppendLine(ref handler);
			await FtpHelpers.UploadBytesVerifiedAsync(ip, port, user, pass, timeout, Encoding.ASCII.GetBytes(stringBuilder.ToString()), iniPath, ensureRemoteDirectory: true, null, cancellationToken);
			return (Written: true, BackedUp: false);
		}
		PluginHelpers.PluginConfig config = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, iniPath);
		string tempBackupPath = Path.Combine(Path.GetTempPath(), $"xecli-ini-backup-{Guid.NewGuid():N}.ini");
		try
		{
			await File.WriteAllTextAsync(tempBackupPath, string.Join("\r\n", config.Lines), Encoding.UTF8);
			await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, tempBackupPath, iniPath + ".bak", ensureRemoteDirectory: false, null, cancellationToken);
		}
		finally
		{
			try
			{
				if (File.Exists(tempBackupPath))
				{
					File.Delete(tempBackupPath);
				}
			}
			catch
			{
			}
		}
		string[] array = pluginPaths;
		foreach (string text in array)
		{
			string fileName = Path.GetFileName(text);
			int num = (from pair in config.Slots
				orderby pair.Key
				where !string.IsNullOrWhiteSpace(pair.Value)
				select pair).FirstOrDefault((KeyValuePair<int, string> pair) => string.Equals(Path.GetFileName(pair.Value), fileName, StringComparison.OrdinalIgnoreCase)).Key;
			if (num <= 0)
			{
				num = config.Slots.OrderBy<KeyValuePair<int, string>, int>((KeyValuePair<int, string> pair) => pair.Key).FirstOrDefault((KeyValuePair<int, string> pair) => string.IsNullOrWhiteSpace(pair.Value)).Key;
			}
			if (num <= 0)
			{
				num = config.Slots.Keys.DefaultIfEmpty(0).Max() + 1;
			}
			config.SetSlot(num, text);
		}
		await PluginHelpers.SaveAsync(ip, port, user, pass, timeout, config, backup: false, cancellationToken);
		return (Written: true, BackedUp: true);
	}

	private static string CombineRemotePath(string root, string relative)
	{
		string text = FtpHelpers.NormalizePath(root).TrimEnd('/');
		string text2 = relative.Replace('\\', '/').TrimStart('/');
		return text + "/" + text2;
	}

	private static async Task UploadFileWithRetryAsync(string ip, int port, string user, string pass, int timeout, string localFile, string remoteFile, IProgress<FtpProgress> progress, CancellationToken cancellationToken)
	{
		await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, localFile, remoteFile, ensureRemoteDirectory: true, progress, cancellationToken);
	}

	private static string GetRemoteDirectory(string remoteFile)
	{
		int num = remoteFile.LastIndexOf('/');
		if (num <= 0)
		{
			return "/";
		}
		return remoteFile.Substring(0, num);
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static string FormatBytes(long bytes)
	{
		return FtpHelpers.FormatBytes(bytes);
	}
}
