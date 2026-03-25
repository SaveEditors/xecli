using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GodWatchCommand : AsyncCommand<GodWatchCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<WATCH>")]
		[LocalizedDescription("Directory to watch for new ISO files.")]
		public string WatchDir { get; init; } = string.Empty;

		[CommandOption("--dest <DIR>")]
		[LocalizedDescription("Output root directory for GOD packages.")]
		public string DestDir { get; init; } = string.Empty;

		[CommandOption("--trim <MODE>")]
		[LocalizedDescription("Trim unused space: end or none (default: end).")]
		public string? Trim { get; init; }

		[CommandOption("-j|--threads <N>")]
		[LocalizedDescription("Parallel workers for part files (default: 1).")]
		public int Threads { get; init; } = 1;

		[CommandOption("--title <NAME>")]
		[LocalizedDescription("Override the package display title for all conversions.")]
		public string? Title { get; init; }

		[CommandOption("--ext <LIST>")]
		[LocalizedDescription("Comma-separated extensions to include (default: iso).")]
		public string? Extensions { get; init; }

		[CommandOption("--recursive")]
		[LocalizedDescription("Watch subdirectories recursively.")]
		public bool Recursive { get; init; }

		[CommandOption("--settle <SECONDS>")]
		[LocalizedDescription("Seconds a file must remain unchanged before converting (default: 8).")]
		public int SettleSeconds { get; init; } = 8;

		[CommandOption("--poll <MS>")]
		[LocalizedDescription("Poll interval in milliseconds for stability checks (default: 500).")]
		public int PollMs { get; init; } = 500;

		[CommandOption("--timeout <SECONDS>")]
		[LocalizedDescription("Max seconds to wait for a file to become stable (0 = no timeout).")]
		public int TimeoutSeconds { get; init; }

		[CommandOption("--retries <N>")]
		[LocalizedDescription("Retry failed conversions (default: 2).")]
		public int Retries { get; init; } = 2;

		[CommandOption("--delete-source")]
		[LocalizedDescription("Delete ISO files after a successful conversion.")]
		public bool DeleteSource { get; init; }

		[CommandOption("--move-done <DIR>")]
		[LocalizedDescription("Move ISO files to this folder after successful conversion.")]
		public string? MoveDoneDir { get; init; }

		[CommandOption("--move-failed <DIR>")]
		[LocalizedDescription("Move ISO files to this folder after failed conversion.")]
		public string? MoveFailedDir { get; init; }

		[CommandOption("--once")]
		[LocalizedDescription("Process existing ISOs once and exit.")]
		public bool Once { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		string fullPath = Path.GetFullPath(settings.WatchDir);
		if (!Directory.Exists(fullPath))
		{
			AnsiConsole.MarkupLine("[red]Watch directory not found.[/]");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(settings.DestDir))
		{
			AnsiConsole.MarkupLine("[red]Destination directory is required.[/]");
			return 1;
		}
		string destDir = Path.GetFullPath(settings.DestDir);
		Directory.CreateDirectory(destDir);
		if (!string.IsNullOrWhiteSpace(settings.MoveDoneDir))
		{
			Directory.CreateDirectory(Path.GetFullPath(settings.MoveDoneDir));
		}
		if (!string.IsNullOrWhiteSpace(settings.MoveFailedDir))
		{
			Directory.CreateDirectory(Path.GetFullPath(settings.MoveFailedDir));
		}
		TrimMode trimMode = ParseTrimMode(settings.Trim);
		if (trimMode == TrimMode.None && !string.Equals(settings.Trim, "none", StringComparison.OrdinalIgnoreCase) && settings.Trim != null)
		{
			AnsiConsole.MarkupLine("[red]Invalid trim mode. Use end or none.[/]");
			return 1;
		}
		HashSet<string> extensions = ParseExtensions(settings.Extensions);
		int attempts = Math.Max(0, settings.Retries) + 1;
		int settleSeconds = Math.Max(1, settings.SettleSeconds);
		int pollMs = Math.Max(200, settings.PollMs);
		CancellationTokenSource cts = new CancellationTokenSource();
		Console.CancelKeyPress += delegate(object? _, ConsoleCancelEventArgs args)
		{
			args.Cancel = true;
			cts.Cancel();
		};
		Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});
		ConcurrentDictionary<string, byte> pending = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
		foreach (string item in EnumerateIsoFiles(fullPath, settings.Recursive, extensions))
		{
			Enqueue(item);
		}
		FileSystemWatcher watcher = null;
		if (settings.Once)
		{
			queue.Writer.Complete();
		}
		else
		{
			watcher = new FileSystemWatcher(fullPath)
			{
				IncludeSubdirectories = settings.Recursive,
				Filter = "*.*",
				NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite)
			};
			watcher.Created += delegate(object _, FileSystemEventArgs e)
			{
				Enqueue(e.FullPath);
			};
			watcher.Changed += delegate(object _, FileSystemEventArgs e)
			{
				Enqueue(e.FullPath);
			};
			watcher.Renamed += delegate(object _, RenamedEventArgs e)
			{
				Enqueue(e.FullPath);
			};
			watcher.EnableRaisingEvents = true;
			AnsiConsole.MarkupLine($"[green]Watching[/] {Markup.Escape(fullPath)} for {string.Join(", ", extensions)} files.");
		}
		try
		{
			while (!cts.IsCancellationRequested && await queue.Reader.WaitToReadAsync(cts.Token))
			{
				string isoPath;
				while (queue.Reader.TryRead(out isoPath))
				{
					await ProcessIsoAsync(isoPath, destDir, trimMode, settings.Threads, settings.Title, settleSeconds, pollMs, settings.TimeoutSeconds, attempts, settings.DeleteSource, settings.MoveDoneDir, settings.MoveFailedDir, cts.Token);
					pending.TryRemove(isoPath, out var _);
				}
			}
		}
		finally
		{
			watcher?.Dispose();
		}
		AnsiConsole.MarkupLine("[grey]Watch stopped.[/]");
		return 0;
		void Enqueue(string path)
		{
			string fullPath2 = Path.GetFullPath(path);
			if (File.Exists(fullPath2))
			{
				string extension = Path.GetExtension(fullPath2);
				if (extensions.Contains(extension) && pending.TryAdd(fullPath2, 0))
				{
					queue.Writer.TryWrite(fullPath2);
				}
			}
		}
	}

	private static async Task ProcessIsoAsync(string isoPath, string destDir, TrimMode trimMode, int threads, string? titleOverride, int settleSeconds, int pollMs, int timeoutSeconds, int attempts, bool deleteSource, string? moveDoneDir, string? moveFailedDir, CancellationToken cancellationToken)
	{
		string fullPath = Path.GetFullPath(isoPath);
		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			try
			{
				await WaitForStableFileAsync(fullPath, settleSeconds, pollMs, timeoutSeconds, cancellationToken);
				if (!File.Exists(fullPath))
				{
					AnsiConsole.MarkupLine("[yellow]Skipped missing file:[/] " + Markup.Escape(fullPath));
					break;
				}
				AnsiConsole.MarkupLine("[cyan]Converting[/] " + Markup.Escape(fullPath));
				object gate = new object();
				GodConversionResult godConversionResult = await Task.Run(() => GodConverter.Convert(fullPath, destDir, new GodConvertOptions(trimMode, Math.Max(1, threads), titleOverride), ReportProgress, cancellationToken), cancellationToken);
				AnsiConsole.MarkupLine("[green]Done[/] " + Markup.Escape(fullPath));
				AnsiConsole.MarkupLine("[grey]Output[/] " + Markup.Escape(godConversionResult.OutputDir));
				if (deleteSource)
				{
					File.Delete(fullPath);
				}
				else if (!string.IsNullOrWhiteSpace(moveDoneDir))
				{
					MoveWithUniqueName(fullPath, Path.GetFullPath(moveDoneDir));
				}
				break;
				void ReportProgress(int done, int total)
				{
					lock (gate)
					{
						AnsiConsole.MarkupLine($"[grey]Parts[/] {done}/{total}");
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex2)
			{
				AnsiConsole.MarkupLine($"[red]Failed[/] {Markup.Escape(fullPath)} ({attempt}/{attempts})");
				AnsiConsole.MarkupLine("[grey]" + Markup.Escape(ex2.Message) + "[/]");
				if (attempt != attempts)
				{
					await Task.Delay(2000, cancellationToken);
				}
				else if (!string.IsNullOrWhiteSpace(moveFailedDir) && File.Exists(fullPath))
				{
					MoveWithUniqueName(fullPath, Path.GetFullPath(moveFailedDir));
				}
			}
		}
	}

	private static async Task WaitForStableFileAsync(string path, int settleSeconds, int pollMs, int timeoutSeconds, CancellationToken cancellationToken)
	{
		DateTime start = DateTime.UtcNow;
		DateTime stableSince = DateTime.UtcNow;
		long? lastSize = null;
		DateTime? lastWrite = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (timeoutSeconds > 0 && (DateTime.UtcNow - start).TotalSeconds > (double)timeoutSeconds)
			{
				throw new TimeoutException("Timed out waiting for the file to stabilize.");
			}
			if (!File.Exists(path))
			{
				await Task.Delay(pollMs, cancellationToken);
				continue;
			}
			FileInfo fileInfo = new FileInfo(path);
			fileInfo.Refresh();
			long length = fileInfo.Length;
			DateTime lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
			if (IsReadyForRead(path) && length > 0 && lastSize.HasValue && lastSize == length && lastWrite == lastWriteTimeUtc)
			{
				if ((DateTime.UtcNow - stableSince).TotalSeconds >= (double)settleSeconds)
				{
					break;
				}
			}
			else
			{
				lastSize = length;
				lastWrite = lastWriteTimeUtc;
				stableSince = DateTime.UtcNow;
			}
			await Task.Delay(pollMs, cancellationToken);
		}
	}

	private static bool IsReadyForRead(string path)
	{
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
			return fileStream.Length > 0;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static IEnumerable<string> EnumerateIsoFiles(string path, bool recursive, HashSet<string> extensions)
	{
		SearchOption searchOption = (recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
		foreach (string item in Directory.EnumerateFiles(path, "*.*", searchOption))
		{
			string extension = Path.GetExtension(item);
			if (extensions.Contains(extension))
			{
				yield return item;
			}
		}
	}

	private static HashSet<string> ParseExtensions(string? value)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(value))
		{
			hashSet.Add(".iso");
			return hashSet;
		}
		string[] array = value.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			string item = (text.StartsWith('.') ? text : ("." + text));
			hashSet.Add(item);
		}
		if (hashSet.Count == 0)
		{
			hashSet.Add(".iso");
		}
		return hashSet;
	}

	private static void MoveWithUniqueName(string sourcePath, string destDir)
	{
		Directory.CreateDirectory(destDir);
		string fileName = Path.GetFileName(sourcePath);
		string text = Path.Combine(destDir, fileName);
		if (!File.Exists(text))
		{
			File.Move(sourcePath, text);
			return;
		}
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
		string extension = Path.GetExtension(fileName);
		string path = $"{fileNameWithoutExtension}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
		File.Move(sourcePath, Path.Combine(destDir, path));
	}

	private static TrimMode ParseTrimMode(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return TrimMode.FromEnd;
		}
		if (value.Equals("end", StringComparison.OrdinalIgnoreCase))
		{
			return TrimMode.FromEnd;
		}
		value.Equals("none", StringComparison.OrdinalIgnoreCase);
		return TrimMode.None;
	}
}
