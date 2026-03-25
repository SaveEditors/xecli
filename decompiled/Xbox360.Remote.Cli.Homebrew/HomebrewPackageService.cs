using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Spectre.Console;
using Xbox360.Remote.Cli.Commands;
using Color = Spectre.Console.Color;

namespace Xbox360.Remote.Cli.Homebrew;

internal static class HomebrewPackageService
{
	private static readonly IReadOnlyList<HomebrewPackageDefinition> Definitions = new HomebrewPackageDefinition[8]
	{
		new HomebrewPackageDefinition("aurora", "Aurora 0.7b.2", "Dashboard package for Aurora 0.7b.2.", "Aurora", "http://phoenix.xboxunity.net/downloads/Aurora%200.7b.2%20-%20Release%20Package.rar", "https://consolemods.org/wiki/images/d/dd/Aurora_0.7b.2_-_Release_Package.rar"),
		new HomebrewPackageDefinition("dashlaunch", "DashLaunch 3.21", "Launch.ini configuration tool with a built-in FTP server.", "DashLaunch", "https://consolemods.org/wiki/File:DashLaunch_v3.21.7z", null),
		new HomebrewPackageDefinition("xexmenu", "XeXMenu 1.2", "File manager and launcher for XEX content.", "XeXMenu", "https://consolemods.org/wiki/images/5/5c/XeXmenu_1.2.7z", null),
		new HomebrewPackageDefinition("fsd", "Freestyle Dash 3", "Freestyle Dash 3 dashboard package.", "FreestyleDash", "https://consolemods.org/wiki/images/a/a0/Fsd3.zip", "https://consolemods.org/wiki/images/7/76/TeamFSD.Freestyle3.0.775.7z"),
		new HomebrewPackageDefinition("xm360", "XM360", "Unlocks STFS content such as XBLA, DLC, and title updates.", "XM360", "https://consolemods.org/wiki/images/5/5f/XM360.7z", "https://consolemods.org/wiki/File:XM360.7z"),
		new HomebrewPackageDefinition("timefixer", "TimeFixer", "Sets the Xbox 360 clock past 2025 and up to 9/17/2036.", "TimeFixer", "https://github.com/DerfJagged/TimeFixer/releases/download/v1/TimeFixer_by_Derf.zip", null),
		new HomebrewPackageDefinition("simple360", "Simple 360 NAND Flasher", "Flashes or dumps Xbox 360 NAND images.", "Simple360NANDFlasher", "https://consolemods.org/wiki/images/f/ff/Simple_360_NAND_Flasher.7z", "https://consolemods.org/wiki/File:Simple_360_NAND_Flasher.7z"),
		new HomebrewPackageDefinition("xelllaunch", "XellLaunch", "Launches XeLL from the dashboard or console flash.", "XellLaunch", "https://consolemods.org/wiki/images/4/41/XellLaunch.7z", "https://consolemods.org/wiki/File:XellLaunch.7z")
	};

	public static IReadOnlyList<HomebrewPackageDefinition> Catalog => Definitions;

	public static IReadOnlyList<string> KnownPackageIds => Definitions.Select((HomebrewPackageDefinition definition) => definition.Id).Append("all").ToArray();

	public static bool IsKnownPackageId(string? packageId)
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return false;
		}
		return KnownPackageIds.Contains<string>(packageId.Trim(), StringComparer.OrdinalIgnoreCase);
	}

	public static IReadOnlyList<HomebrewPackageDefinition> ResolveSelection(string packageId)
	{
		if (string.Equals(packageId, "all", StringComparison.OrdinalIgnoreCase))
		{
			return Definitions;
		}
		HomebrewPackageDefinition homebrewPackageDefinition = Definitions.FirstOrDefault((HomebrewPackageDefinition definition) => string.Equals(definition.Id, packageId, StringComparison.OrdinalIgnoreCase));
		if (homebrewPackageDefinition == null)
		{
			throw new InvalidOperationException($"Unknown package '{packageId}'. Expected one of: {string.Join(", ", KnownPackageIds)}.");
		}
		return new HomebrewPackageDefinition[1] { homebrewPackageDefinition };
	}

	public static string GetPackageCacheRoot(string? explicitDirectory)
	{
		if (!string.IsNullOrWhiteSpace(explicitDirectory))
		{
			return Path.GetFullPath(explicitDirectory);
		}
		return Path.Combine(CliPaths.CachePath, "packages");
	}

	public static string DescribePackage(HomebrewPackageDefinition package)
	{
		string sourceLabel = GetSourceLabel(package.PrimaryUrl);
		return $"{package.DisplayName} - {package.Description} (source: {sourceLabel})";
	}

	public static string DescribePackageSelection(IReadOnlyList<HomebrewPackageDefinition> packages)
	{
		return string.Join(Environment.NewLine, packages.Select(DescribePackage));
	}

	public static string DescribeInstallAction(bool consoleInstall)
	{
		if (!consoleInstall)
		{
			return "XeCLI will download the selected public package archives, extract them into a staging folder, and copy the files to the chosen USB drive or folder. If a package includes plugins or launch.ini support, XeCLI will write those as part of the staged install.";
		}
		return "XeCLI will download the selected public package archives, extract them, and upload the files to the console. If a package includes plugins or launch.ini support, XeCLI will write those as part of the install.";
	}

	public static async Task<HomebrewInstallResult> InstallAsync(string packageId, string targetRoot, string? explicitCacheDirectory, bool forceDownload, bool autoConfirm, CancellationToken cancellationToken)
	{
		IReadOnlyList<HomebrewPackageDefinition> packages = ResolveSelection(packageId);
		string text = Path.Combine(GetPackageCacheRoot(explicitCacheDirectory), "archives");
		string extractRoot = ResolveStagingRoot(targetRoot, explicitCacheDirectory);
		Directory.CreateDirectory(text);
		Directory.CreateDirectory(extractRoot);
		Directory.CreateDirectory(targetRoot);
		try
		{
			if (!autoConfirm && !Console.IsInputRedirected)
			{
				Table table = CliOutput.CreateTable();
				table.AddColumn(new TableColumn("[white]Field[/]"));
				table.AddColumn(new TableColumn("[white]Value[/]"));
				table.AddRow("[white]Target[/]", "[springgreen3_1]" + Markup.Escape(targetRoot) + "[/]");
				table.AddRow("[white]Packages[/]", "[deepskyblue1]" + Markup.Escape(DescribePackageSelection(packages)) + "[/]");
				table.AddRow("[white]Action[/]", "[grey]" + Markup.Escape(DescribeInstallAction(consoleInstall: false)) + "[/]");
				table.AddRow("[white]Archive Cache[/]", "[cyan]" + Markup.Escape(text) + "[/]");
				table.AddRow("[white]Staging[/]", "[gold1]" + Markup.Escape(extractRoot) + "[/]");
				AnsiConsole.Write(table);
				if (!AnsiConsole.Confirm("Continue with the install?"))
				{
					throw new OperationCanceledException("Install cancelled by user.");
				}
			}
			HomebrewInstallResult homebrewInstallResult = await StagePackagesAsync(packageId, targetRoot, explicitCacheDirectory, forceDownload, cancellationToken);
			bool launchIniWritten = WriteLaunchIni(targetRoot, homebrewInstallResult.Packages);
			return new HomebrewInstallResult(targetRoot, homebrewInstallResult.Packages, launchIniWritten, homebrewInstallResult.PluginsCopied);
		}
		finally
		{
			TryDeleteDirectory(extractRoot);
		}
	}

	internal static async Task<HomebrewInstallResult> StagePackagesAsync(string packageId, string targetRoot, string? explicitCacheDirectory, bool forceDownload, CancellationToken cancellationToken)
	{
		IReadOnlyList<HomebrewPackageDefinition> readOnlyList = ResolveSelection(packageId);
		string packageCacheRoot = GetPackageCacheRoot(explicitCacheDirectory);
		string archiveRoot = Path.Combine(packageCacheRoot, "archives");
		string extractRoot = ResolveStagingRoot(targetRoot, explicitCacheDirectory);
		Directory.CreateDirectory(archiveRoot);
		Directory.CreateDirectory(extractRoot);
		Directory.CreateDirectory(targetRoot);
		try
		{
			List<InstalledHomebrewPackage> installed = new List<InstalledHomebrewPackage>();
			foreach (HomebrewPackageDefinition definition in readOnlyList)
			{
				string archivePath = await EnsureArchiveAsync(definition, archiveRoot, forceDownload, cancellationToken);
				string packageExtractRoot = Path.Combine(extractRoot, definition.Id);
				Status status = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
				Color? foreground = Color.SpringGreen3_1;
				Decoration? decoration = Decoration.Bold;
				status.SpinnerStyle(new Style(foreground, null, decoration)).Start("Extracting " + definition.DisplayName + "...", delegate
				{
					ExtractArchive(archivePath, packageExtractRoot);
				});
				string sourceRoot = CollapseRootDirectory(packageExtractRoot);
				string installRoot = Path.Combine(targetRoot, definition.InstallFolderName);
				var (fileCount, totalBytes) = await CopyDirectoryAsync(sourceRoot, installRoot, definition.DisplayName + " files", cancellationToken);
				installed.Add(new InstalledHomebrewPackage(definition.Id, definition.DisplayName, definition.InstallFolderName, archivePath, installRoot, fileCount, totalBytes));
			}
			return new HomebrewInstallResult(targetRoot, installed, LaunchIniWritten: false, await CopyConsoleDependenciesAsync(targetRoot, cancellationToken));
		}
		finally
		{
			TryDeleteDirectory(extractRoot);
		}
	}

	public static void RenderInstallResult(HomebrewInstallResult result)
	{
		long bytes = result.Packages.Sum((InstalledHomebrewPackage installedHomebrewPackage) => installedHomebrewPackage.TotalBytes);
		OperationFeedback.WriteSuccess("Homebrew install complete", $"[green]{result.Packages.Count}[/] package(s) [silver]{FormatBytes(bytes)}[/] -> [cyan]{Markup.Escape(result.TargetRoot)}[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[green]Package[/]"));
		table.AddColumn(new TableColumn("[grey]Description[/]"));
		table.AddColumn(new TableColumn("[cyan]Installed To[/]"));
		table.AddColumn(new TableColumn("[gold1]Files[/]"));
		table.AddColumn(new TableColumn("[grey]Size[/]"));
		foreach (InstalledHomebrewPackage package in result.Packages)
		{
			HomebrewPackageDefinition homebrewPackageDefinition = Definitions.First((HomebrewPackageDefinition candidate) => string.Equals(candidate.Id, package.Id, StringComparison.OrdinalIgnoreCase));
			table.AddRow("[green]" + Markup.Escape(package.DisplayName) + "[/]", "[grey]" + Markup.Escape(homebrewPackageDefinition.Description) + "[/]", "[cyan]" + Markup.Escape(package.InstallPath) + "[/]", $"[gold1]{package.FileCount}[/]", "[grey]" + FormatBytes(package.TotalBytes) + "[/]");
		}
		AnsiConsole.Write(table);
		if (result.LaunchIniWritten)
		{
			AnsiConsole.MarkupLine("[grey]Generated[/] [springgreen3_1]" + Markup.Escape(Path.Combine(result.TargetRoot, "launch.ini")) + "[/]");
		}
		if (result.PluginsCopied)
		{
			AnsiConsole.MarkupLine("[grey]Copied bundled console plugins into[/] [springgreen3_1]" + Markup.Escape(Path.Combine(result.TargetRoot, "Plugins")) + "[/]");
		}
	}

	internal static string CreateLaunchIniText(string launchRootAlias)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("[Paths]");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
		handler.AppendLiteral("Default = ");
		handler.AppendFormatted(launchRootAlias);
		handler.AppendLiteral(":\\Aurora\\Aurora.xex");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[Plugins]");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(28, 1, stringBuilder2);
		handler.AppendLiteral("plugin1 = ");
		handler.AppendFormatted(launchRootAlias);
		handler.AppendLiteral(":\\Plugins\\xbdm.xex");
		stringBuilder4.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder5 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
		handler.AppendLiteral("plugin2 = ");
		handler.AppendFormatted(launchRootAlias);
		handler.AppendLiteral(":\\Plugins\\JRPC2.xex");
		stringBuilder5.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(29, 1, stringBuilder2);
		handler.AppendLiteral("plugin3 = ");
		handler.AppendFormatted(launchRootAlias);
		handler.AppendLiteral(":\\Plugins\\XDRPC.xex");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("[Settings]");
		stringBuilder.AppendLine("nxemini = false");
		stringBuilder.AppendLine("ftpserv = true");
		stringBuilder.AppendLine("pingpatch = true");
		stringBuilder.AppendLine("contpatch = true");
		stringBuilder.AppendLine("fatalfreeze = false");
		stringBuilder.AppendLine("livestrong = false");
		return stringBuilder.ToString();
	}

	internal static async Task<string> EnsureArchiveAsync(HomebrewPackageDefinition definition, string archiveRoot, bool forceDownload, CancellationToken cancellationToken)
	{
		string archiveExtension = GetArchiveExtension(definition.PrimaryUrl, definition.MirrorUrl);
		string archivePath = Path.Combine(archiveRoot, definition.Id + archiveExtension);
		if (!forceDownload && File.Exists(archivePath) && new FileInfo(archivePath).Length > 0 && IsRecognizedArchive(archivePath))
		{
			return archivePath;
		}
		if (File.Exists(archivePath))
		{
			File.Delete(archivePath);
		}
		Exception ex = null;
		foreach (string item in new string[2] { definition.PrimaryUrl, definition.MirrorUrl }.Where((string url) => !string.IsNullOrWhiteSpace(url)))
		{
			try
			{
				using HttpClient client = CreateHttpClient();
				HttpResponseMessage response = await client.GetAsync(await ResolveDownloadUrlAsync(client, item, cancellationToken), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
					if (!IsRecognizedArchive(archivePath))
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

	internal static void ExtractArchive(string archivePath, string extractRoot)
	{
		if (Directory.Exists(extractRoot))
		{
			Directory.Delete(extractRoot, recursive: true);
		}
		Directory.CreateDirectory(extractRoot);
		if (Path.GetExtension(archivePath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
		{
			using (FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				using IArchive archive = ArchiveFactory.OpenArchive(stream);
				foreach (IArchiveEntry item in archive.Entries.Where((IArchiveEntry entry) => !entry.IsDirectory))
				{
					string text = Path.Combine(extractRoot, (item.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
					string directoryName = Path.GetDirectoryName(text);
					if (!string.IsNullOrWhiteSpace(directoryName))
					{
						Directory.CreateDirectory(directoryName);
					}
					item.WriteToFile(text, new ExtractionOptions
					{
						ExtractFullPath = true,
						Overwrite = true
					});
				}
				return;
			}
		}
		using FileStream stream2 = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using IReader reader = ReaderFactory.OpenReader(stream2);
		while (reader.MoveToNextEntry())
		{
			if (!reader.Entry.IsDirectory)
			{
				string text2 = Path.Combine(extractRoot, (reader.Entry.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar));
				string directoryName2 = Path.GetDirectoryName(text2);
				if (!string.IsNullOrWhiteSpace(directoryName2))
				{
					Directory.CreateDirectory(directoryName2);
				}
				reader.WriteEntryToFile(text2, new ExtractionOptions
				{
					ExtractFullPath = true,
					Overwrite = true
				});
			}
		}
	}

	internal static string CollapseRootDirectory(string extractRoot)
	{
		string[] directories = Directory.GetDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly);
		string[] files = Directory.GetFiles(extractRoot, "*", SearchOption.TopDirectoryOnly);
		if (directories.Length == 1 && files.Length == 0)
		{
			return directories[0];
		}
		return extractRoot;
	}

	internal static async Task<(int FileCount, long TotalBytes)> CopyDirectoryAsync(string sourceRoot, string targetRoot, string title, CancellationToken cancellationToken)
	{
		string[] files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
		IReadOnlyList<CliOutput.TransferBatchItem> items = files.Select((string file) => new CliOutput.TransferBatchItem(file, new FileInfo(file).Length)).ToArray();
		Directory.CreateDirectory(targetRoot);
		await CliOutput.RunBatchProgressAsync(title, items, async delegate(TransferBatchScope batch)
		{
			string[] array = files;
			foreach (string text in array)
			{
				string relativePath = Path.GetRelativePath(sourceRoot, text);
				string path = Path.Combine(targetRoot, relativePath);
				string directoryName = Path.GetDirectoryName(path);
				if (!string.IsNullOrWhiteSpace(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				long length = new FileInfo(text).Length;
				batch.StartFile(relativePath, length);
				await using FileStream sourceStream = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.Read);
				await using FileStream destinationStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
				byte[] buffer = new byte[65536];
				long copied = 0L;
				while (true)
				{
					int read = await sourceStream.ReadAsync(buffer, cancellationToken);
					if (read <= 0)
					{
						break;
					}
					await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
					copied += read;
					batch.ReportFileProgress(copied);
				}
				await destinationStream.FlushAsync(cancellationToken);
				batch.CompleteFile();
			}
		});
		return (FileCount: files.Length, TotalBytes: files.Sum((string file) => new FileInfo(file).Length));
	}

	private static async Task<bool> CopyConsoleDependenciesAsync(string targetRoot, CancellationToken cancellationToken)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "ConsoleDependencies");
		if (!Directory.Exists(text))
		{
			return false;
		}
		string targetRoot2 = Path.Combine(targetRoot, "Plugins");
		await CopyDirectoryAsync(text, targetRoot2, "Bundled console plugins", cancellationToken);
		return true;
	}

	private static bool WriteLaunchIni(string targetRoot, IReadOnlyList<InstalledHomebrewPackage> installedPackages)
	{
		if (!installedPackages.Any((InstalledHomebrewPackage package) => string.Equals(package.Id, "aurora", StringComparison.OrdinalIgnoreCase)) && !File.Exists(Path.Combine(targetRoot, "Aurora", "Aurora.xex")))
		{
			return false;
		}
		File.WriteAllText(Path.Combine(targetRoot, "launch.ini"), CreateLaunchIniText("Usb"), Encoding.ASCII);
		return true;
	}

	internal static string GetArchiveExtension(string primaryUrl, string? mirrorUrl)
	{
		string[] array = new string[2] { primaryUrl, mirrorUrl };
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				string absolutePath = new Uri(text).AbsolutePath;
				if (absolutePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
				{
					return ".7z";
				}
				if (absolutePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
				{
					return ".rar";
				}
				if (absolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				{
					return ".zip";
				}
			}
		}
		return ".pkg";
	}

	internal static string ResolveStagingRoot(string targetRoot, string? explicitCacheDirectory)
	{
		string path = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
		if (!string.IsNullOrWhiteSpace(explicitCacheDirectory))
		{
			return Path.Combine(GetPackageCacheRoot(explicitCacheDirectory), "staging", path);
		}
		return Path.Combine(Path.GetPathRoot(Path.GetFullPath(targetRoot)) ?? targetRoot, "XeCLI-Staging", path);
	}

	internal static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
			string directoryName = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directoryName) && Directory.Exists(directoryName) && !Directory.EnumerateFileSystemEntries(directoryName).Any() && string.Equals(Path.GetFileName(directoryName), "XeCLI-Staging", StringComparison.OrdinalIgnoreCase))
			{
				Directory.Delete(directoryName, recursive: false);
			}
		}
		catch
		{
		}
	}

	internal static HttpClient CreateHttpClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromMinutes(15L);
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XeCLI/1.0.6");
		return httpClient;
	}

	internal static async Task<string> ResolveDownloadUrlAsync(HttpClient client, string url, CancellationToken cancellationToken)
	{
		if (!url.Contains("/wiki/File:", StringComparison.OrdinalIgnoreCase))
		{
			return url;
		}
		Match match = Regex.Match(await client.GetStringAsync(url, cancellationToken), "href=\"(?<path>/wiki/images/[^\"]+\\.(?:7z|zip|rar))\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return url;
		}
		return new Uri(new Uri(url), match.Groups["path"].Value).AbsoluteUri;
	}

	internal static string FormatBytes(long bytes)
	{
		string[] array = new string[5] { "B", "KB", "MB", "GB", "TB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{num:0} {array[num2]}";
		}
		return $"{num:0.##} {array[num2]}";
	}

	internal static bool IsRecognizedArchive(string archivePath)
	{
		string extension = Path.GetExtension(archivePath);
		byte[] array = new byte[8];
		using FileStream fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		int num = fileStream.Read(array, 0, array.Length);
		if (num < 4)
		{
			return false;
		}
		if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
		{
			return array[0] == 80 && array[1] == 75 && (array[2] == 3 || array[2] == 5 || array[2] == 7) && (array[3] == 4 || array[3] == 6 || array[3] == 8);
		}
		if (extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
		{
			return num >= 7 && array[0] == 82 && array[1] == 97 && array[2] == 114 && array[3] == 33 && array[4] == 26 && array[5] == 7 && (array[6] == 0 || array[6] == 1);
		}
		if (extension.Equals(".7z", StringComparison.OrdinalIgnoreCase))
		{
			return num >= 6 && array[0] == 55 && array[1] == 122 && array[2] == 188 && array[3] == 175 && array[4] == 39 && array[5] == 28;
		}
		return true;
	}

	internal static string GetSourceLabel(string url)
	{
		Uri uri = new Uri(url);
		string host = uri.Host;
		if (!(host == "consolemods.org"))
		{
			if (host == "github.com")
			{
				return "GitHub";
			}
			return uri.Host;
		}
		return "ConsoleMods";
	}
}
