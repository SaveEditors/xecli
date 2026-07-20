using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class GodInfoCommand : Command<GodInfoCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<ISO>")]
        [LocalizedDescription("Path to the ISO image.")]
        public string IsoPath { get; init; } = string.Empty;

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.IsoPath)) {
            AnsiConsole.MarkupLine("[red]ISO file not found.[/]");
            return 1;
        }

        string isoPath = Path.GetFullPath(settings.IsoPath);
        if (!File.Exists(isoPath)) {
            AnsiConsole.MarkupLine("[red]ISO file not found.[/]");
            return 1;
        }

        using FileStream isoFile = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        IsoReader iso = IsoReader.Read(isoFile);
        TitleInfo title = TitleInfo.FromImage(iso);

        TitleIdDatabase.Instance.TryResolve(title.ExecutionInfo.TitleId, null, out TitleIdEntry? entry);
        string titleName = entry?.Name ?? TitleIdDatabase.FormatTitleId(title.ExecutionInfo.TitleId);

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Iso = Path.GetFileName(isoPath),
                TitleId = $"0x{title.ExecutionInfo.TitleId:X8}",
                MediaId = $"0x{title.ExecutionInfo.MediaId:X8}",
                ContentType = title.ContentType.ToString(),
                Name = titleName,
                Region = entry?.Region,
                Type = entry?.Type
            });
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]ISO Details[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[grey]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[grey]ISO[/]", $"[cyan]{Markup.Escape(isoPath)}[/]");
        table.AddRow("[grey]Title ID[/]", $"[cyan]0x{title.ExecutionInfo.TitleId:X8}[/]");
        table.AddRow("[grey]Media ID[/]", $"[cyan]0x{title.ExecutionInfo.MediaId:X8}[/]");
        table.AddRow("[grey]Content Type[/]", $"[green]{title.ContentType}[/]");
        table.AddRow("[grey]Title Name[/]", $"[green]{Markup.Escape(titleName)}[/]");
        table.AddRow("[grey]Region[/]", entry?.Region != null ? $"[grey]{Markup.Escape(entry.Region)}[/]" : "[grey]unknown[/]");
        table.AddRow("[grey]Type[/]", entry?.Type != null ? $"[grey]{Markup.Escape(entry.Type)}[/]" : "[grey]unknown[/]");
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class GodBuildCommand : Command<GodBuildCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<ISO>")]
        [LocalizedDescription("Path to the ISO image.")]
        public string IsoPath { get; init; } = string.Empty;

        [CommandArgument(1, "<DEST>")]
        [LocalizedDescription("Output folder for the GOD package.")]
        public string DestDir { get; init; } = string.Empty;

        [CommandOption("--trim <MODE>")]
        [LocalizedDescription("Trim unused space: end or none (default: end).")]
        public string? Trim { get; init; }

        [CommandOption("-j|--threads <N>")]
        [LocalizedDescription("Parallel workers for part files (default: 1).")]
        public int Threads { get; init; } = 1;

        [CommandOption("--title <NAME>")]
        [LocalizedDescription("Override the package display title.")]
        public string? Title { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        string isoPath = Path.GetFullPath(settings.IsoPath);
        if (!File.Exists(isoPath)) {
            AnsiConsole.MarkupLine("[red]ISO file not found.[/]");
            return 1;
        }

        string destDir = Path.GetFullPath(settings.DestDir);
        TrimMode trimMode = ParseTrimMode(settings.Trim);
        if (trimMode == TrimMode.None && string.Equals(settings.Trim, "none", StringComparison.OrdinalIgnoreCase) == false && settings.Trim != null) {
            AnsiConsole.MarkupLine("[red]Invalid trim mode. Use end or none.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[cyan]Reading ISO metadata...[/]");

        object gate = new object();
        void ReportProgress(int done, int total) {
            lock (gate) {
                AnsiConsole.MarkupLine($"[grey]Parts[/] {done}/{total}");
            }
        }

        GodConversionResult result = GodConverter.Convert(
            isoPath,
            destDir,
            new GodConvertOptions(trimMode, Math.Max(1, settings.Threads), settings.Title),
            ReportProgress);

        AnsiConsole.MarkupLine("[green]Package header written.[/]");

        AnsiConsole.Write(new Rule("[bold deepskyblue1]GOD Output[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[grey]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[grey]Title ID[/]", $"[cyan]0x{result.ExecutionInfo.TitleId:X8}[/]");
        table.AddRow("[grey]Media ID[/]", $"[cyan]0x{result.ExecutionInfo.MediaId:X8}[/]");
        table.AddRow("[grey]Content Type[/]", $"[green]{result.ContentType}[/]");
        table.AddRow("[grey]Parts[/]", result.PartCount.ToString());
        table.AddRow("[grey]Data Dir[/]", $"[green]{Markup.Escape(result.OutputDir)}[/]");
        table.AddRow("[grey]Package[/]", $"[green]{Markup.Escape(result.ConHeaderPath)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    private static TrimMode ParseTrimMode(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return TrimMode.FromEnd;
        if (value.Equals("end", StringComparison.OrdinalIgnoreCase))
            return TrimMode.FromEnd;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TrimMode.None;
        return TrimMode.None;
    }
}

public sealed class GodWatchCommand : AsyncCommand<GodWatchCommand.Settings> {
    public sealed class Settings : CommandSettings {
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

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string watchDir = Path.GetFullPath(settings.WatchDir);
        if (!Directory.Exists(watchDir)) {
            AnsiConsole.MarkupLine("[red]Watch directory not found.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.DestDir)) {
            AnsiConsole.MarkupLine("[red]Destination directory is required.[/]");
            return 1;
        }

        string destDir = Path.GetFullPath(settings.DestDir);
        Directory.CreateDirectory(destDir);

        if (!string.IsNullOrWhiteSpace(settings.MoveDoneDir))
            Directory.CreateDirectory(Path.GetFullPath(settings.MoveDoneDir));
        if (!string.IsNullOrWhiteSpace(settings.MoveFailedDir))
            Directory.CreateDirectory(Path.GetFullPath(settings.MoveFailedDir));

        TrimMode trimMode = ParseTrimMode(settings.Trim);
        if (trimMode == TrimMode.None && string.Equals(settings.Trim, "none", StringComparison.OrdinalIgnoreCase) == false && settings.Trim != null) {
            AnsiConsole.MarkupLine("[red]Invalid trim mode. Use end or none.[/]");
            return 1;
        }

        HashSet<string> extensions = ParseExtensions(settings.Extensions);
        int attempts = Math.Max(0, settings.Retries) + 1;
        int settleSeconds = Math.Max(1, settings.SettleSeconds);
        int pollMs = Math.Max(200, settings.PollMs);

        CancellationTokenSource cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => {
            args.Cancel = true;
            cts.Cancel();
        };

        Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        ConcurrentDictionary<string, byte> pending = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        void Enqueue(string path) {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
                return;
            string ext = Path.GetExtension(full);
            if (!extensions.Contains(ext))
                return;
            if (!pending.TryAdd(full, 0))
                return;
            queue.Writer.TryWrite(full);
        }

        foreach (string file in EnumerateIsoFiles(watchDir, settings.Recursive, extensions)) {
            Enqueue(file);
        }

        FileSystemWatcher? watcher = null;
        if (settings.Once) {
            queue.Writer.Complete();
        }
        else {
            watcher = new FileSystemWatcher(watchDir) {
                IncludeSubdirectories = settings.Recursive,
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Created += (_, e) => Enqueue(e.FullPath);
            watcher.Changed += (_, e) => Enqueue(e.FullPath);
            watcher.Renamed += (_, e) => Enqueue(e.FullPath);
            watcher.EnableRaisingEvents = true;

            AnsiConsole.MarkupLine($"[green]Watching[/] {Markup.Escape(watchDir)} for {string.Join(", ", extensions)} files.");
        }

        try {
            while (!cts.IsCancellationRequested) {
                if (!await queue.Reader.WaitToReadAsync(cts.Token))
                    break;
                while (queue.Reader.TryRead(out string? isoPath)) {
                    await ProcessIsoAsync(
                        isoPath,
                        destDir,
                        trimMode,
                        settings.Threads,
                        settings.Title,
                        settleSeconds,
                        pollMs,
                        settings.TimeoutSeconds,
                        attempts,
                        settings.DeleteSource,
                        settings.MoveDoneDir,
                        settings.MoveFailedDir,
                        cts.Token);

                    pending.TryRemove(isoPath, out _);
                }

            }
        }
        finally {
            watcher?.Dispose();
        }

        AnsiConsole.MarkupLine("[grey]Watch stopped.[/]");
        return 0;
    }

    private static async Task ProcessIsoAsync(
        string isoPath,
        string destDir,
        TrimMode trimMode,
        int threads,
        string? titleOverride,
        int settleSeconds,
        int pollMs,
        int timeoutSeconds,
        int attempts,
        bool deleteSource,
        string? moveDoneDir,
        string? moveFailedDir,
        CancellationToken cancellationToken) {
        string fullPath = Path.GetFullPath(isoPath);
        for (int attempt = 1; attempt <= attempts; attempt++) {
            try {
                await WaitForStableFileAsync(fullPath, settleSeconds, pollMs, timeoutSeconds, cancellationToken);

                if (!File.Exists(fullPath)) {
                    AnsiConsole.MarkupLine($"[yellow]Skipped missing file:[/] {Markup.Escape(fullPath)}");
                    return;
                }

                AnsiConsole.MarkupLine($"[cyan]Converting[/] {Markup.Escape(fullPath)}");

                object gate = new object();
                void ReportProgress(int done, int total) {
                    lock (gate) {
                        AnsiConsole.MarkupLine($"[grey]Parts[/] {done}/{total}");
                    }
                }

                GodConversionResult result = await Task.Run(() => GodConverter.Convert(
                    fullPath,
                    destDir,
                    new GodConvertOptions(trimMode, Math.Max(1, threads), titleOverride),
                    ReportProgress,
                    cancellationToken), cancellationToken);

                AnsiConsole.MarkupLine($"[green]Done[/] {Markup.Escape(fullPath)}");
                AnsiConsole.MarkupLine($"[grey]Output[/] {Markup.Escape(result.OutputDir)}");

                if (deleteSource) {
                    File.Delete(fullPath);
                }
                else if (!string.IsNullOrWhiteSpace(moveDoneDir)) {
                    MoveWithUniqueName(fullPath, Path.GetFullPath(moveDoneDir));
                }

                return;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]Failed[/] {Markup.Escape(fullPath)} ({attempt}/{attempts})");
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.Message)}[/]");
                if (attempt == attempts) {
                    if (!string.IsNullOrWhiteSpace(moveFailedDir) && File.Exists(fullPath)) {
                        MoveWithUniqueName(fullPath, Path.GetFullPath(moveFailedDir));
                    }
                }
                else {
                    await Task.Delay(2000, cancellationToken);
                }
            }
        }
    }

    private static async Task WaitForStableFileAsync(
        string path,
        int settleSeconds,
        int pollMs,
        int timeoutSeconds,
        CancellationToken cancellationToken) {
        DateTime start = DateTime.UtcNow;
        DateTime stableSince = DateTime.UtcNow;
        long? lastSize = null;
        DateTime? lastWrite = null;

        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            if (timeoutSeconds > 0 && (DateTime.UtcNow - start).TotalSeconds > timeoutSeconds) {
                throw new TimeoutException("Timed out waiting for the file to stabilize.");
            }

            if (!File.Exists(path)) {
                await Task.Delay(pollMs, cancellationToken);
                continue;
            }

            FileInfo info = new FileInfo(path);
            info.Refresh();
            long size = info.Length;
            DateTime write = info.LastWriteTimeUtc;
            bool available = IsReadyForRead(path);

            if (available && size > 0 && lastSize.HasValue && lastSize == size && lastWrite == write) {
                if ((DateTime.UtcNow - stableSince).TotalSeconds >= settleSeconds)
                    return;
            }
            else {
                lastSize = size;
                lastWrite = write;
                stableSince = DateTime.UtcNow;
            }

            await Task.Delay(pollMs, cancellationToken);
        }
    }

    private static bool IsReadyForRead(string path) {
        try {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (IOException) {
            return false;
        }
        catch (UnauthorizedAccessException) {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateIsoFiles(string path, bool recursive, HashSet<string> extensions) {
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (string file in Directory.EnumerateFiles(path, "*.*", option)) {
            string ext = Path.GetExtension(file);
            if (extensions.Contains(ext))
                yield return file;
        }
    }

    private static HashSet<string> ParseExtensions(string? value) {
        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value)) {
            result.Add(".iso");
            return result;
        }

        foreach (string raw in value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string ext = raw.StartsWith('.') ? raw : $".{raw}";
            result.Add(ext);
        }

        if (result.Count == 0)
            result.Add(".iso");

        return result;
    }

    private static void MoveWithUniqueName(string sourcePath, string destDir) {
        Directory.CreateDirectory(destDir);
        string fileName = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(destDir, fileName);
        if (!File.Exists(destPath)) {
            File.Move(sourcePath, destPath);
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        string stamped = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
        File.Move(sourcePath, Path.Combine(destDir, stamped));
    }

    private static TrimMode ParseTrimMode(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return TrimMode.FromEnd;
        if (value.Equals("end", StringComparison.OrdinalIgnoreCase))
            return TrimMode.FromEnd;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TrimMode.None;
        return TrimMode.None;
    }
}

