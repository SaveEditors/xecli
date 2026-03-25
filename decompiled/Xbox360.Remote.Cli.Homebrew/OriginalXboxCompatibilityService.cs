using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Homebrew;

internal static class OriginalXboxCompatibilityService
{
	private static readonly IReadOnlyList<OriginalXboxCompatibilityDefinition> XeFuDefinitions = new OriginalXboxCompatibilityDefinition[3]
	{
		new OriginalXboxCompatibilityDefinition("hacked", "Hacked XeFu Pack", "Best overall choice for modded consoles. Removes the stock whitelist and restriction checks so more original Xbox titles can boot.", "Includes the standard XeFu set, extra emulator files from newer Xbox builds, and external config support. Requires an exploited console.", "https://consolemods.org/wiki/images/9/9d/Hacked_Xefu_Pack.zip", "https://consolemods.org/wiki/File:Hacked_Xefu_Pack.zip"),
		new OriginalXboxCompatibilityDefinition("hud", "Hacked XeFu Pack with HUD", "Same compatibility-focused hacked pack, but also keeps the full Xbox 360 guide available while you are inside original Xbox titles.", "The guide and game-chat support use more resources, so some titles may run worse or behave less reliably than the standard hacked pack.", "https://consolemods.org/wiki/images/c/c4/Hacked_Xefu_Pack_with_HUD.zip", "https://consolemods.org/wiki/File:Hacked_Xefu_Pack_with_HUD.zip"),
		new OriginalXboxCompatibilityDefinition("retail", "Unmodified Retail XeFu Pack", "Closest to the stock Microsoft emulator files. Use this if you want the original behavior instead of the hacked compatibility set.", "This keeps the official restrictions and whitelist behavior. It does not add the wider hacked-console compatibility options.", "https://consolemods.org/wiki/images/2/28/Unmodified_Retail_Xefu_Pack.zip", "https://consolemods.org/wiki/File:Unmodified_Retail_Xefu_Pack.zip")
	};

	private static readonly OriginalXboxCompatibilityDefinition PartitionFixerDefinition = new OriginalXboxCompatibilityDefinition("fixer", "HDD Compatibility Partition Fixer", "Creates the HddX compatibility partition required for original Xbox emulator files on non-standard drives.", "Run it on the console, press A to create the partition, then reboot before installing a XeFu set.", "https://consolemods.org/wiki/images/b/b2/Hdd_compat_partition_fixer_v1.zip", "https://consolemods.org/wiki/File:Hdd_compat_partition_fixer_v1.zip");

	public static IReadOnlyList<OriginalXboxCompatibilityDefinition> Catalog => XeFuDefinitions;

	public static bool IsKnownSetId(string? setId)
	{
		if (string.IsNullOrWhiteSpace(setId))
		{
			return false;
		}
		return XeFuDefinitions.Any((OriginalXboxCompatibilityDefinition definition) => string.Equals(definition.Id, setId.Trim(), StringComparison.OrdinalIgnoreCase));
	}

	public static OriginalXboxCompatibilityDefinition ResolveSet(string setId)
	{
		OriginalXboxCompatibilityDefinition? originalXboxCompatibilityDefinition = XeFuDefinitions.FirstOrDefault((OriginalXboxCompatibilityDefinition definition) => string.Equals(definition.Id, setId, StringComparison.OrdinalIgnoreCase));
		if (originalXboxCompatibilityDefinition == null)
		{
			throw new InvalidOperationException($"Unknown Original Xbox compatibility set '{setId}'. Expected one of: {string.Join(", ", XeFuDefinitions.Select((OriginalXboxCompatibilityDefinition definition) => definition.Id))}.");
		}
		return originalXboxCompatibilityDefinition;
	}

	public static string GetCacheRoot(string? explicitDirectory)
	{
		if (!string.IsNullOrWhiteSpace(explicitDirectory))
		{
			return Path.GetFullPath(explicitDirectory);
		}
		return Path.Combine(CliPaths.CachePath, "ogxbox");
	}

	public static string DescribeSet(OriginalXboxCompatibilityDefinition definition)
	{
		string sourceLabel = HomebrewPackageService.GetSourceLabel(definition.PrimaryUrl);
		return $"{definition.DisplayName} - {definition.Description} {definition.Notes} (source: {sourceLabel})";
	}

	public static string DescribeAction(bool consoleInstall, bool includeFixer)
	{
		string text = (consoleInstall ? "XeCLI will download the selected XeFu pack, extract it, and upload the compatibility files to HddX:\\Compatibility on the console." : "XeCLI will download the selected XeFu pack, extract it, and stage the compatibility files into a ready-to-copy Compatibility folder.");
		if (!includeFixer)
		{
			return text;
		}
		return text + " XeCLI will also prepare the HDD Compatibility Partition Fixer so you can create HddX first if your drive is missing that partition.";
	}

	public static async Task<OriginalXboxCompatibilityInstallResult> InstallToHostAsync(string setId, string targetRoot, string? explicitCacheDirectory, bool includeFixer, bool forceDownload, bool autoConfirm, CancellationToken cancellationToken)
	{
		OriginalXboxCompatibilityDefinition definition = ResolveSet(setId);
		string cacheRoot = GetCacheRoot(explicitCacheDirectory);
		string archiveRoot = Path.Combine(cacheRoot, "archives");
		string stagingRoot = HomebrewPackageService.ResolveStagingRoot(targetRoot, explicitCacheDirectory);
		Directory.CreateDirectory(targetRoot);
		Directory.CreateDirectory(archiveRoot);
		Directory.CreateDirectory(stagingRoot);
		try
		{
			if (!autoConfirm && !Console.IsInputRedirected)
			{
				Table table = CliOutput.CreateTable();
				table.AddColumn("[white]Field[/]");
				table.AddColumn("[white]Value[/]");
				table.AddRow("[white]Target[/]", "[springgreen3_1]" + Markup.Escape(targetRoot) + "[/]");
				table.AddRow("[white]XeFu Set[/]", "[deepskyblue1]" + Markup.Escape(definition.DisplayName) + "[/]");
				table.AddRow("[white]Details[/]", "[grey]" + Markup.Escape(definition.Description) + "[/]");
				table.AddRow("[white]Notes[/]", "[grey]" + Markup.Escape(definition.Notes) + "[/]");
				table.AddRow("[white]Partition Fixer[/]", includeFixer ? "[gold1]Included[/]" : "[grey]Not included[/]");
				table.AddRow("[white]Action[/]", "[grey]" + Markup.Escape(DescribeAction(consoleInstall: false, includeFixer)) + "[/]");
				AnsiConsole.Write(table);
				if (!AnsiConsole.Confirm("Continue with the Original Xbox compatibility staging?"))
				{
					throw new OperationCanceledException("Original Xbox compatibility staging cancelled by user.");
				}
			}
			string xefuArchive = await DownloadArchiveAsync(definition, archiveRoot, forceDownload, cancellationToken);
			string xefuExtract = Path.Combine(stagingRoot, definition.Id);
			Status status = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
			Color? foreground = Color.SpringGreen3_1;
			Decoration? decoration = Decoration.Bold;
			status.SpinnerStyle(new Style(foreground, null, decoration)).Start("Extracting " + definition.DisplayName + "...", delegate
			{
				HomebrewPackageService.ExtractArchive(xefuArchive, xefuExtract);
			});
			string sourceRoot = ResolveCompatibilityPayloadRoot(xefuExtract);
			string compatibilityTarget = Path.Combine(targetRoot, "Compatibility");
			(int, long) tuple = await HomebrewPackageService.CopyDirectoryAsync(sourceRoot, compatibilityTarget, definition.DisplayName + " files", cancellationToken);
			int fileCount = tuple.Item1;
			long totalBytes = tuple.Item2;
			bool fixerInstalled = false;
			string fixerPath = null;
			if (includeFixer)
			{
				string fixerArchive = await DownloadArchiveAsync(PartitionFixerDefinition, archiveRoot, forceDownload, cancellationToken);
				string fixerExtract = Path.Combine(stagingRoot, "fixer");
				Status status2 = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
				Color? foreground2 = Color.SpringGreen3_1;
				decoration = Decoration.Bold;
				status2.SpinnerStyle(new Style(foreground2, null, decoration)).Start("Extracting HDD Compatibility Partition Fixer...", delegate
				{
					HomebrewPackageService.ExtractArchive(fixerArchive, fixerExtract);
				});
				string sourceRoot2 = HomebrewPackageService.CollapseRootDirectory(fixerExtract);
				fixerPath = Path.Combine(targetRoot, "HddCompatibilityPartitionFixer");
				await HomebrewPackageService.CopyDirectoryAsync(sourceRoot2, fixerPath, "HDD Compatibility Partition Fixer", cancellationToken);
				fixerInstalled = true;
			}
			WriteInstructionsFile(targetRoot, definition, includeFixer);
			return new OriginalXboxCompatibilityInstallResult("host", definition.Id, definition.DisplayName, targetRoot, compatibilityTarget, CompatibilityInstalled: true, includeFixer, fixerInstalled, fixerPath, fileCount, totalBytes, BuildInstructions(definition, includeFixer, consoleInstall: false));
		}
		finally
		{
			HomebrewPackageService.TryDeleteDirectory(stagingRoot);
		}
	}

	public static async Task<OriginalXboxCompatibilityInstallResult> InstallToConsoleAsync(OriginalXboxCompatibilityInstallCommand.Settings settings, CancellationToken cancellationToken)
	{
		OriginalXboxCompatibilityDefinition definition = ResolveSet(settings.SetId);
		string cacheRoot = GetCacheRoot(settings.CacheDirectory);
		string archiveRoot = Path.Combine(cacheRoot, "archives");
		string stagingRoot = Path.Combine(cacheRoot, "console-stage", $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(archiveRoot);
		Directory.CreateDirectory(stagingRoot);
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, cancellationToken);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		timeout = Math.Max(timeout, 15000);
		OriginalXboxCompatibilityInstallResult result;
		await using (AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeout))
		{
			await client.Connect(cancellationToken);
			bool hasHddX = await SafeDirectoryExistsAsync(client, "/HddX");
			string fixerDevice = ResolveFixerDevice(await SafeDirectoryExistsAsync(client, "/Hdd1"), await SafeDirectoryExistsAsync(client, "/Usb0"), await SafeDirectoryExistsAsync(client, "/Usb1"), await SafeDirectoryExistsAsync(client, "/Usb2"));
			if (!settings.AutoConfirm && !Console.IsInputRedirected)
			{
				Table table = CliOutput.CreateTable();
				table.AddColumn("[white]Field[/]");
				table.AddColumn("[white]Value[/]");
				table.AddRow("[white]Console[/]", "[springgreen3_1]" + Markup.Escape(ip) + "[/]");
				table.AddRow("[white]XeFu Set[/]", "[deepskyblue1]" + Markup.Escape(definition.DisplayName) + "[/]");
				table.AddRow("[white]Details[/]", "[grey]" + Markup.Escape(definition.Description) + "[/]");
				table.AddRow("[white]Notes[/]", "[grey]" + Markup.Escape(definition.Notes) + "[/]");
				table.AddRow("[white]HddX Present[/]", hasHddX ? "[green]Yes[/]" : "[red]No[/]");
				table.AddRow("[white]Partition Fixer[/]", settings.IncludeFixer ? "[gold1]Included[/]" : "[grey]Not included[/]");
				table.AddRow("[white]Action[/]", "[grey]" + Markup.Escape(DescribeAction(consoleInstall: true, settings.IncludeFixer)) + "[/]");
				if (settings.IncludeFixer && !string.IsNullOrWhiteSpace(fixerDevice))
				{
					table.AddRow("[white]Fixer Target[/]", "[cyan]" + Markup.Escape(fixerDevice) + "[/]");
				}
				AnsiConsole.Write(table);
				if (!AnsiConsole.Confirm("Continue with the Original Xbox compatibility console install?"))
				{
					throw new OperationCanceledException("Original Xbox compatibility console install cancelled by user.");
				}
			}
			try
			{
				string xefuArchive = await DownloadArchiveAsync(definition, archiveRoot, settings.ForceDownload, cancellationToken);
				string xefuExtract = Path.Combine(stagingRoot, definition.Id);
				Status status = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
				Color? foreground = Color.SpringGreen3_1;
				Decoration? decoration = Decoration.Bold;
				status.SpinnerStyle(new Style(foreground, null, decoration)).Start("Extracting " + definition.DisplayName + "...", delegate
				{
					HomebrewPackageService.ExtractArchive(xefuArchive, xefuExtract);
				});
				string localRoot = ResolveCompatibilityPayloadRoot(xefuExtract);
				int fileCount = 0;
				long totalBytes = 0L;
				bool compatibilityInstalled = false;
				string compatibilityPath = "/HddX/Compatibility";
				if (hasHddX)
				{
					(int, long) tuple2 = await UploadDirectoryAsync(ip, port, user, pass, timeout, localRoot, compatibilityPath, definition.DisplayName + " upload", cancellationToken);
					fileCount = tuple2.Item1;
					totalBytes = tuple2.Item2;
					compatibilityInstalled = true;
				}
				else if (!settings.IncludeFixer)
				{
					throw new InvalidOperationException("HddX was not detected on the console. Re-run with --include-fixer to stage the HDD Compatibility Partition Fixer first.");
				}
				bool fixerInstalled = false;
				string fixerPath = null;
				if (settings.IncludeFixer)
				{
					if (string.IsNullOrWhiteSpace(fixerDevice))
					{
						throw new InvalidOperationException("No writable Hdd1/Usb0/Usb1/Usb2 target was detected for the HDD Compatibility Partition Fixer.");
					}
					string fixerArchive = await DownloadArchiveAsync(PartitionFixerDefinition, archiveRoot, settings.ForceDownload, cancellationToken);
					string fixerExtract = Path.Combine(stagingRoot, "fixer");
					Status status2 = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
					Color? foreground2 = Color.SpringGreen3_1;
					decoration = Decoration.Bold;
					status2.SpinnerStyle(new Style(foreground2, null, decoration)).Start("Extracting HDD Compatibility Partition Fixer...", delegate
					{
						HomebrewPackageService.ExtractArchive(fixerArchive, fixerExtract);
					});
					string localRoot2 = HomebrewPackageService.CollapseRootDirectory(fixerExtract);
					fixerPath = "/" + fixerDevice + "/HddCompatibilityPartitionFixer";
					await UploadDirectoryAsync(ip, port, user, pass, timeout, localRoot2, fixerPath, "HDD Compatibility Partition Fixer upload", cancellationToken);
					fixerInstalled = true;
				}
				result = new OriginalXboxCompatibilityInstallResult("console", definition.Id, definition.DisplayName, ip, compatibilityPath, compatibilityInstalled, settings.IncludeFixer, fixerInstalled, fixerPath, fileCount, totalBytes, BuildInstructions(definition, settings.IncludeFixer, consoleInstall: true));
			}
			finally
			{
				HomebrewPackageService.TryDeleteDirectory(stagingRoot);
			}
		}
		return result;
	}

	public static void RenderList()
	{
		Table table = CliOutput.CreateTable();
		table.AddColumn("[white]Set[/]");
		table.AddColumn("[springgreen3_1]Name[/]");
		table.AddColumn("[cyan]Best For[/]");
		table.AddColumn("[grey]Notes[/]");
		foreach (OriginalXboxCompatibilityDefinition xeFuDefinition in XeFuDefinitions)
		{
			table.AddRow("[white]" + Markup.Escape(xeFuDefinition.Id) + "[/]", "[springgreen3_1]" + Markup.Escape(xeFuDefinition.DisplayName) + "[/]", "[cyan]" + Markup.Escape(xeFuDefinition.Description) + "[/]", "[grey]" + Markup.Escape(xeFuDefinition.Notes) + "[/]");
		}
		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine("[grey]Optional helper:[/] [gold1]HDD Compatibility Partition Fixer[/] [grey]creates the HddX partition required on non-standard drives.[/]");
	}

	public static void RenderInstallResult(OriginalXboxCompatibilityInstallResult result)
	{
		if (result.CompatibilityInstalled)
		{
			OperationFeedback.WriteSuccess("Original Xbox compatibility install complete", $"[green]{Markup.Escape(result.SetName)}[/] -> [cyan]{Markup.Escape(result.CompatibilityPath)}[/]");
		}
		else
		{
			OperationFeedback.WriteSuccess("Partition fixer staged", "[gold1]HddX[/] was not present, so XeCLI staged the fixer and did not write compatibility files yet.");
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn("[white]Field[/]");
		table.AddColumn("[white]Value[/]");
		table.AddRow("[white]Mode[/]", "[springgreen3_1]" + Markup.Escape(result.Mode) + "[/]");
		table.AddRow("[white]XeFu Set[/]", "[deepskyblue1]" + Markup.Escape(result.SetName) + "[/]");
		table.AddRow("[white]Target[/]", "[cyan]" + Markup.Escape(result.Target) + "[/]");
		table.AddRow("[white]Compatibility Path[/]", "[gold1]" + Markup.Escape(result.CompatibilityPath) + "[/]");
		table.AddRow("[white]Compatibility Files[/]", result.CompatibilityInstalled ? "[green]Installed[/]" : "[yellow]Pending HddX creation[/]");
		table.AddRow("[white]Fixer Included[/]", result.FixerIncluded ? "[green]Yes[/]" : "[grey]No[/]");
		if (result.FixerIncluded)
		{
			table.AddRow("[white]Fixer Path[/]", string.IsNullOrWhiteSpace(result.FixerPath) ? "[grey]n/a[/]" : ("[mediumpurple3]" + Markup.Escape(result.FixerPath) + "[/]"));
		}
		if (result.CompatibilityInstalled)
		{
			table.AddRow("[white]Files[/]", $"[gold1]{result.FileCount}[/]");
			table.AddRow("[white]Size[/]", "[grey]" + Markup.Escape(HomebrewPackageService.FormatBytes(result.TotalBytes)) + "[/]");
		}
		AnsiConsole.Write(table);
		AnsiConsole.Write(new Panel("[grey]" + Markup.Escape(result.Instructions) + "[/]").Header("[bold deepskyblue1]Next Step[/]").BorderColor(Color.Grey));
	}

	private static async Task<string> DownloadArchiveAsync(OriginalXboxCompatibilityDefinition definition, string archiveRoot, bool forceDownload, CancellationToken cancellationToken)
	{
		string archiveExtension = HomebrewPackageService.GetArchiveExtension(definition.PrimaryUrl, definition.MirrorUrl);
		string archivePath = Path.Combine(archiveRoot, definition.Id + archiveExtension);
		if (!forceDownload && File.Exists(archivePath) && new FileInfo(archivePath).Length > 0 && HomebrewPackageService.IsRecognizedArchive(archivePath))
		{
			return archivePath;
		}
		if (File.Exists(archivePath))
		{
			File.Delete(archivePath);
		}
		Exception ex = null;
		foreach (string item in new string[2] { definition.PrimaryUrl, definition.MirrorUrl }.Where((string candidate) => !string.IsNullOrWhiteSpace(candidate)))
		{
			try
			{
				using HttpClient client = HomebrewPackageService.CreateHttpClient();
				HttpResponseMessage response = await client.GetAsync(await HomebrewPackageService.ResolveDownloadUrlAsync(client, item, cancellationToken), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				try
				{
					response.EnsureSuccessStatusCode();
					long? contentLength = response.Content.Headers.ContentLength;
					await CliOutput.RunWithProgressAsync("Download " + definition.DisplayName, contentLength, async delegate(IProgress<CliOutput.TransferProgressUpdate> progress)
					{
						await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
						await using FileStream fileStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
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
							progress.Report(new CliOutput.TransferProgressUpdate(total, "downloading"));
						}
						await fileStream.FlushAsync(cancellationToken);
					});
					if (!HomebrewPackageService.IsRecognizedArchive(archivePath))
					{
						File.Delete(archivePath);
						throw new InvalidDataException("Downloaded content for " + definition.DisplayName + " was not a supported archive.");
					}
					return archivePath;
				}
				finally
				{
					if (response != null)
					{
						((IDisposable)response).Dispose();
					}
				}
			}
			catch (Exception ex2)
			{
				ex = ex2;
				if (File.Exists(archivePath))
				{
					File.Delete(archivePath);
				}
			}
		}
		throw new InvalidOperationException("Failed to download " + definition.DisplayName + ": " + ex?.Message, ex);
	}

	private static string ResolveCompatibilityPayloadRoot(string extractRoot)
	{
		string text = HomebrewPackageService.CollapseRootDirectory(extractRoot);
		string text2 = Path.Combine(text, "Compatibility");
		if (Directory.Exists(text2))
		{
			return text2;
		}
		return text;
	}

	private static void WriteInstructionsFile(string targetRoot, OriginalXboxCompatibilityDefinition definition, bool includeFixer)
	{
		File.WriteAllText(Path.Combine(targetRoot, "XeCLI-OriginalXbox-Compatibility.txt"), BuildInstructions(definition, includeFixer, consoleInstall: false), Encoding.UTF8);
	}

	private static string BuildInstructions(OriginalXboxCompatibilityDefinition definition, bool includeFixer, bool consoleInstall)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (includeFixer)
		{
			stringBuilder.Append("If your hard drive does not already have HddX, run HDD Compatibility Partition Fixer first, press A to create the partition, then reboot the console. ");
		}
		stringBuilder.Append(consoleInstall ? ("XeCLI has prepared " + definition.DisplayName + ". Original Xbox emulator files belong in HddX:\\Compatibility. Disable stealth plugins before testing original Xbox games if they interfere with the emulator.") : ("Copy the Compatibility folder to HddX:\\Compatibility on the Xbox 360 hard drive. " + definition.DisplayName + " is staged and ready. Disable stealth plugins before testing original Xbox games if they interfere with the emulator."));
		return stringBuilder.ToString();
	}

	private static async Task<bool> SafeDirectoryExistsAsync(AsyncFtpClient client, string path)
	{
		try
		{
			return await client.DirectoryExists(path);
		}
		catch
		{
			return false;
		}
	}

	private static string? ResolveFixerDevice(bool hasHdd1, bool hasUsb0, bool hasUsb1, bool hasUsb2)
	{
		if (hasHdd1)
		{
			return "Hdd1";
		}
		if (hasUsb0)
		{
			return "Usb0";
		}
		if (hasUsb1)
		{
			return "Usb1";
		}
		if (hasUsb2)
		{
			return "Usb2";
		}
		return null;
	}

	private static async Task<(int FileCount, long TotalBytes)> UploadDirectoryAsync(string ip, int port, string user, string pass, int timeout, string localRoot, string remoteRoot, string title, CancellationToken cancellationToken)
	{
		string[] files = Directory.GetFiles(localRoot, "*", SearchOption.AllDirectories);
		IReadOnlyList<CliOutput.TransferBatchItem> items = files.Select((string file) => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length)).ToArray();
		(int FileCount, long TotalBytes) result;
		await using (AsyncFtpClient client = FtpHelpers.CreateClient(ip, port, user, pass, timeout))
		{
			await client.Connect(cancellationToken);
			await client.CreateDirectory(FtpHelpers.NormalizePath(remoteRoot), cancellationToken);
			await CliOutput.RunBatchProgressAsync(title, items, async delegate(TransferBatchScope batch)
			{
				string[] array = files;
				foreach (string text in array)
				{
					string text2 = Path.GetRelativePath(localRoot, text).Replace('\\', '/');
					string remotePath = CombineRemotePath(remoteRoot, text2);
					long length = new FileInfo(text).Length;
					batch.StartFile(text2, length);
					Progress<FtpProgress> progress = new Progress<FtpProgress>(delegate(FtpProgress ftpProgress)
					{
						if (ftpProgress.TransferredBytes > 0)
						{
							batch.ReportFileProgress(ftpProgress.TransferredBytes, $"file {batch.CompletedFiles + 1}/{Math.Max(1, items.Count)}");
						}
					});
					await FtpHelpers.UploadFileVerifiedAsync(ip, port, user, pass, timeout, text, remotePath, ensureRemoteDirectory: true, progress, cancellationToken);
					batch.CompleteFile();
				}
			});
			result = (FileCount: files.Length, TotalBytes: files.Sum((string file) => new FileInfo(file).Length));
		}
		return result;
	}

	private static string CombineRemotePath(string root, string relative)
	{
		string text = FtpHelpers.NormalizePath(root).TrimEnd('/');
		string text2 = relative.Replace('\\', '/').TrimStart('/');
		return text + "/" + text2;
	}
}
