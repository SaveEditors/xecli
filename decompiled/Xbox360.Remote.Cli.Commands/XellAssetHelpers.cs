using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Spectre.Console;
using Xbox360.Remote.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class XellAssetHelpers
{
	internal sealed record XellLaunchAssetResolution(string FileName, string Path, string SourceDescription, string? BehaviorSummary, string? Sha256);

	private static readonly SemaphoreSlim Sync = new SemaphoreSlim(1, 1);

	private const string AlexFreeReleaseTag = "v0.993-d4f08b4";

	private static readonly string[] RequiredFiles = new string[2] { "default.xex", "xell.bin" };

	private static readonly Uri[] DownloadUris = new Uri[2]
	{
		new Uri("https://github.com/alex-free/xell-reloaded/releases/download/" + AlexFreeReleaseTag + "/xelllaunch-" + AlexFreeReleaseTag + ".zip"),
		new Uri("https://github.com/alex-free/xell-reloaded/releases/download/" + AlexFreeReleaseTag + "/xell-reloaded-" + AlexFreeReleaseTag + ".zip")
	};

	private static readonly IReadOnlyDictionary<string, (string SourceDescription, string BehaviorSummary)> KnownXellPayloads = new Dictionary<string, (string SourceDescription, string BehaviorSummary)>(StringComparer.OrdinalIgnoreCase)
	{
		{
			"21CDA528AEADE160B35BC89EC41E499E7B912EAD2207EFC2C7779C92328A4844",
			("stock XellLaunch-compatible payload (alex-free/xell-reloaded v0.993-d4f08b4, xell-2f)", "Shows the normal XeLL UI and continues the standard USB/SATA/TFTP search loop after HTTP starts.")
		},
		{
			"2FA7272D9011F8CC3BA847C85E5FA9F0F83205AD4AF26D54A15C944B649A80EC",
			("stock RGH payload (alex-free/xell-reloaded v0.993-d4f08b4, xell-gggggg)", "Shows the normal XeLL UI and continues the standard USB/SATA/TFTP search loop after HTTP starts.")
		},
		{
			"985C34E84D9DF99213DCDFE0C52170575293C9BDD7D588EBB182034D498617EC",
			("XeCLI custom payload build (X360Tools/xell-reloaded e7dd322, xell-2f)", "Intended to show the XeCLI waiting screen and pause the stock media/TFTP loop.")
		},
		{
			"F53C65698C85419CF5709A2A79EC9BAEE26382BB85717CD551C0069E4F3EF47E",
			("XeCLI-XellFetch custom payload build (alex-free/xell-reloaded v0.993-d4f08b4, xell-2f)", "Shows the XeCLI waiting screen, job-aware progress states, and token-gated verified reboot flow used by XeCLI.")
		},
		{
			"FB84B61F0DC1E245D282B92D7300409C1049342386C36F4363D170A23466757E",
			("XeCLI-XellFetch custom payload build (alex-free/xell-reloaded v0.993-d4f08b4, xell-2f)", "Shows the XeCLI waiting screen, job-aware progress states, verified keyvault export support, and token-gated verified reboot flow used by XeCLI.")
		},
		{
			"1D5581C9C4FA01E4604A706ABA494AF72A163B1A9889EB31E5A2BA1536B3EA8C",
			("XeCLI custom payload build (X360Tools/xell-reloaded e7dd322, xell-gggggg)", "Intended to show the XeCLI waiting screen and pause the stock media/TFTP loop.")
		},
		{
			"4E0995FD3C1A6FB0451FB11B449F83CFEEE7E581C9B7F190C83FBB8B9BDA0F20",
			("XeCLI minimal payload build (custom no-fileloop experiment, xell-gggggg)", "Intended to skip the stock search loop entirely and wait for XeCLI over HTTP.")
		}
	};

	public static async Task<XellLaunchAssetResolution> ResolveXellLaunchAssetAsync(string fileName, CancellationToken cancellationToken)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "Assets", "XellLaunch", fileName);
		if (File.Exists(text))
		{
			return DescribeResolvedAsset(fileName, text, "bundled XeCLI XellLaunch asset");
		}
		await Sync.WaitAsync(cancellationToken);
		try
		{
			if (File.Exists(text))
			{
				return DescribeResolvedAsset(fileName, text, "bundled XeCLI XellLaunch asset");
			}
			string text2 = await EnsureCachedXellLaunchAssetsAsync(cancellationToken);
			string text3 = Path.Combine(text2, fileName);
			if (!File.Exists(text3))
			{
				throw new FileNotFoundException("XeCLI XellLaunch asset not found after cache preparation: " + text3, text3);
			}
			return DescribeResolvedAsset(fileName, text3, "downloaded/cached XellLaunch bundle asset");
		}
		finally
		{
			Sync.Release();
		}
	}

	public static async Task<string> ResolveXellLaunchAssetPathAsync(string fileName, CancellationToken cancellationToken)
	{
		return (await ResolveXellLaunchAssetAsync(fileName, cancellationToken)).Path;
	}

	public static XellLaunchAssetResolution DescribeLocalXellBinary(string path)
	{
		string fullPath = Path.GetFullPath(path);
		return DescribeResolvedAsset("xell.bin", fullPath, "local staged xell.bin");
	}

	public static string DescribeManagedHelperProvision()
	{
		string text = "use bundled files when present, otherwise download the alex-free/xell-reloaded " + AlexFreeReleaseTag + " helper bundle locally first.";
		string text2 = Path.Combine(AppContext.BaseDirectory, "Assets", "XellLaunch", "xell.bin");
		if (File.Exists(text2))
		{
			XellLaunchAssetResolution xellLaunchAssetResolution = DescribeResolvedAsset("xell.bin", text2, "bundled XeCLI XellLaunch asset");
			text = xellLaunchAssetResolution.SourceDescription;
		}
		return "[cyan]If the console does not already have a usable XellLaunch helper, XeCLI will install a managed helper and xell.bin automatically[/]\n[grey]Default payload:[/] " + text;
	}

	private static async Task<string> EnsureCachedXellLaunchAssetsAsync(CancellationToken cancellationToken)
	{
		string text = Path.Combine(CliPaths.CachePath, "xelllaunch-assets", "current");
		if (HasRequiredFiles(text))
		{
			return text;
		}
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(CliPaths.CachePath, "xelllaunch-assets", "downloads");
		Directory.CreateDirectory(text2);
		foreach (string item in EnumerateLocalArchiveCandidates())
		{
			if (await TryExtractRequiredFilesAsync(item, text, cancellationToken))
			{
				return text;
			}
		}
		OperationFeedback.WriteWarning("Bundled XellLaunch assets missing", "XeCLI is downloading the alex-free/xell-reloaded " + AlexFreeReleaseTag + " helper assets so it can install the helper automatically.");
		Exception? ex = null;
		int num = 0;
		foreach (Uri downloadUri in DownloadUris)
		{
			num++;
			string text3 = Path.Combine(text2, "xelllaunch-" + num + Path.GetExtension(downloadUri.AbsolutePath));
			try
			{
				await DownloadFileAsync(downloadUri, text3, cancellationToken);
				if (await TryExtractRequiredFilesAsync(text3, text, cancellationToken))
				{
					return text;
				}
				ex = new InvalidOperationException("Downloaded XellLaunch archive did not contain both default.xex and xell.bin.");
			}
			catch (Exception ex2)
			{
				ex = ex2;
			}
		}
		throw new InvalidOperationException("XeCLI could not obtain the XellLaunch helper assets automatically.", ex);
	}

	private static IEnumerable<string> EnumerateLocalArchiveCandidates()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string text = Path.Combine(CliPaths.CachePath, "xelllaunch-assets", "downloads");
		if (Directory.Exists(text))
		{
			foreach (string item in Directory.EnumerateFiles(text))
			{
				if (hashSet.Add(item))
				{
					yield return item;
				}
			}
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 6 && directoryInfo != null; i++)
		{
			string text2 = Path.Combine(directoryInfo.FullName, "captures", "xelllaunch-" + AlexFreeReleaseTag + ".zip");
			if (File.Exists(text2) && hashSet.Add(text2))
			{
				yield return text2;
			}
			string text3 = Path.Combine(directoryInfo.FullName, "captures", "xell-reloaded-" + AlexFreeReleaseTag + ".zip");
			if (File.Exists(text3) && hashSet.Add(text3))
			{
				yield return text3;
			}
			directoryInfo = directoryInfo.Parent;
		}
	}

	private static async Task DownloadFileAsync(Uri uri, string destinationPath, CancellationToken cancellationToken)
	{
		AnsiConsole.MarkupLine("[grey]Downloading XellLaunch assets:[/] [cyan]" + Markup.Escape(uri.ToString()) + "[/]");
		using HttpClient httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(45.0)
		};
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("XeCLI/1.0.6");
		using HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		response.EnsureSuccessStatusCode();
		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using FileStream fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		await stream.CopyToAsync(fileStream, cancellationToken);
		await fileStream.FlushAsync(cancellationToken);
	}

	private static async Task<bool> TryExtractRequiredFilesAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
	{
		if (!File.Exists(archivePath))
		{
			return false;
		}
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			if (Path.GetExtension(archivePath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
			{
				using FileStream stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using IArchive archive = ArchiveFactory.OpenArchive(stream);
				foreach (IArchiveEntry item in archive.Entries.Where((IArchiveEntry entry) => !entry.IsDirectory))
				{
					string fileName = Path.GetFileName((item.Key ?? string.Empty).Replace('/', '\\'));
					string? destinationFileName = ResolveDestinationFileName(fileName, destinationDirectory);
					if (destinationFileName == null)
					{
						continue;
					}
					string destinationPath = Path.Combine(destinationDirectory, destinationFileName);
					item.WriteToFile(destinationPath, new ExtractionOptions
					{
						ExtractFullPath = false,
						Overwrite = true
					});
				}
			}
			else
			{
				using FileStream stream2 = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using IReader reader = ReaderFactory.OpenReader(stream2);
				while (reader.MoveToNextEntry())
				{
					if (reader.Entry.IsDirectory)
					{
						continue;
					}
					string fileName2 = Path.GetFileName((reader.Entry.Key ?? string.Empty).Replace('/', '\\'));
					string? destinationFileName2 = ResolveDestinationFileName(fileName2, destinationDirectory);
					if (destinationFileName2 == null)
					{
						continue;
					}
					string destinationPath2 = Path.Combine(destinationDirectory, destinationFileName2);
					reader.WriteEntryToFile(destinationPath2, new ExtractionOptions
					{
						ExtractFullPath = false,
						Overwrite = true
					});
				}
			}
			return HasRequiredFiles(destinationDirectory);
		}
		catch
		{
			return false;
		}
	}

	private static string? ResolveDestinationFileName(string archiveFileName, string destinationDirectory)
	{
		if (string.Equals(archiveFileName, "default.xex", StringComparison.OrdinalIgnoreCase))
		{
			return "default.xex";
		}
		if (string.Equals(archiveFileName, "xell.bin", StringComparison.OrdinalIgnoreCase))
		{
			return "xell.bin";
		}
		if (string.Equals(archiveFileName, "xell-2f.bin", StringComparison.OrdinalIgnoreCase))
		{
			string text = Path.Combine(destinationDirectory, "xell.bin");
			return File.Exists(text) ? null : "xell.bin";
		}
		return null;
	}

	private static bool HasRequiredFiles(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return false;
		}
		return RequiredFiles.All((string fileName) => File.Exists(Path.Combine(directory, fileName)));
	}

	private static XellLaunchAssetResolution DescribeResolvedAsset(string fileName, string path, string sourceLabel)
	{
		string? text = null;
		string? text2 = null;
		if (string.Equals(fileName, "xell.bin", StringComparison.OrdinalIgnoreCase))
		{
			string text3 = ComputeSha256(path);
			if (KnownXellPayloads.TryGetValue(text3, out var value))
			{
				text = sourceLabel + " -> " + value.SourceDescription;
				text2 = value.BehaviorSummary;
			}
			else
			{
				text = sourceLabel + " -> unrecognized xell.bin";
			}
			return new XellLaunchAssetResolution(fileName, path, text, text2, text3);
		}
		return new XellLaunchAssetResolution(fileName, path, sourceLabel, null, null);
	}

	private static string ComputeSha256(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		byte[] array = sHA.ComputeHash(inputStream);
		return Convert.ToHexString(array);
	}
}
