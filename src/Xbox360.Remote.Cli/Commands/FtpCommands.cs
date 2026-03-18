using System.ComponentModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using FluentFTP;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

internal static class FtpHelpers {
    private const int ReconnectAttempts = 3;
    private const int ReconnectDelaySeconds = 10;
    private static readonly HashSet<string> RootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "Hdd1",
        "HddX",
        "Usb0",
        "Usb1",
        "Usb2",
        "System",
        "SysExt",
        "Game",
        "Cache",
        "Flash",
        "D"
    };

    public static async Task<(string Ip, int Port, string User, string Pass, int TimeoutMs)> ResolveAsync(FtpConnectionSettings settings, CancellationToken cancellationToken) {
        CliConfig config = CliConfig.Load();

        string? ip = settings.Ip ?? config.DefaultIp;
        if (string.IsNullOrWhiteSpace(ip)) {
            (string resolvedIp, int _, int __) = await CliHelpers.ResolveTargetAsync(new ConnectionSettings(), cancellationToken);
            ip = resolvedIp;
        }

        int port = settings.Port ?? config.DefaultFtpPort ?? 21;
        string user = settings.User ?? config.DefaultFtpUser ?? "xboxftp";
        string pass = settings.Pass ?? config.DefaultFtpPassword ?? "xboxftp";
        int timeout = settings.TimeoutMs ?? 5000;

        config.DefaultIp = ip;
        config.DefaultFtpPort = port;
        config.DefaultFtpUser = user;
        config.DefaultFtpPassword = pass;
        config.Save();

        return (ip, port, user, pass, timeout);
    }

    public static async Task<int> WithClientAsync(FtpConnectionSettings settings, Func<AsyncFtpClient, Task<int>> action, CancellationToken cancellationToken) {
        (string ip, int port, string user, string pass, int timeout) = await ResolveAsync(settings, cancellationToken);
        Exception? lastError = null;

        for (int attempt = 1; attempt <= ReconnectAttempts; attempt++) {
            try {
                await using AsyncFtpClient client = new AsyncFtpClient(ip, user, pass, port);
                client.Config.ConnectTimeout = timeout;
                client.Config.ReadTimeout = timeout;
                client.Config.DataConnectionConnectTimeout = timeout;
                client.Config.DataConnectionReadTimeout = timeout;
                await client.Connect(cancellationToken);
                return await action(client);
            }
            catch (Exception ex) when (IsTransient(ex)) {
                lastError = ex;
                if (attempt >= ReconnectAttempts)
                    break;
                if (await ShowReconnectCountdownAsync(attempt, ReconnectAttempts, ReconnectDelaySeconds, cancellationToken))
                    throw new OperationCanceledException("Reconnection cancelled by user.");
            }
        }

        throw lastError ?? new IOException("Unable to connect to FTP server.");
    }

    private static bool IsTransient(Exception ex) {
        return ex is IOException ||
               ex is SocketException ||
               ex is TimeoutException;
    }

    private static async Task<bool> ShowReconnectCountdownAsync(int attempt, int total, int seconds, CancellationToken cancellationToken) {
        Task<string?> stopTask = EnsureStopTask();
        for (int remaining = seconds; remaining > 0; remaining--) {
            AnsiConsole.MarkupLine($"[yellow]Attempting FTP reconnection {attempt}/{total} ({remaining}s). Enter \"stop\" to cancel.[/]");
            Task delay = Task.Delay(1000, cancellationToken);
            Task completed = await Task.WhenAny(delay, stopTask);
            if (completed == stopTask) {
                string? input = await stopTask;
                stopTask = EnsureStopTask();
                if (input != null && input.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static Task<string?> EnsureStopTask() {
        if (Console.IsInputRedirected)
            return Task.FromResult<string?>(null);
        return Task.Run(() => Console.ReadLine());
    }

    public static string NormalizePath(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        string cleaned = path.Trim().Replace('\\', '/');
        int colon = cleaned.IndexOf(':');
        if (colon > 0) {
            string drive = cleaned.Substring(0, colon);
            string rest = cleaned.Substring(colon + 1).TrimStart('/');
            return rest.Length == 0 ? $"/{drive}" : $"/{drive}/{rest}";
        }

        if (!cleaned.StartsWith("/", StringComparison.Ordinal))
            cleaned = "/" + cleaned.TrimStart('/');
        return cleaned;
    }

    public static bool IsLikelyRootListing(string requestedPath, IReadOnlyList<FtpListItem> items) {
        if (string.Equals(requestedPath, "/", StringComparison.Ordinal))
            return false;
        if (items.Count == 0)
            return false;
        int rootMatches = items.Count(i => RootNames.Contains(i.Name));
        int threshold = Math.Min(3, items.Count);
        return rootMatches >= threshold;
    }

    public static async Task<(FtpListItem[] Items, bool RootListing)> GetListingWithFallbackAsync(AsyncFtpClient client, string path) {
        FtpListItem[] items = await client.GetListing(path, FtpListOption.AllFiles);
        bool rootListing = IsLikelyRootListing(path, items);
        if (rootListing) {
            FluentFTP.FtpReply cwd = await client.Execute($"CWD {path}");
            if (cwd.Success) {
                FtpListItem[] cwdItems = await client.GetListing(".", FtpListOption.AllFiles);
                if (!IsLikelyRootListing(path, cwdItems)) {
                    items = cwdItems;
                    rootListing = false;
                }
            }
        }

        return (items, rootListing);
    }

    public static string FormatBytes(long bytes) {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}

public sealed class FtpListCommand : AsyncCommand<FtpListCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [Description("Remote directory path (default: /).")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await FtpHelpers.WithClientAsync(settings, async client => {
            string path = FtpHelpers.NormalizePath(settings.Path ?? "/");
            (FtpListItem[] items, bool rootListing) = await FtpHelpers.GetListingWithFallbackAsync(client, path);
            if (settings.Json) {
                CliOutput.EmitJson(items.Select(i => new {
                    i.FullName,
                    i.Name,
                    Type = i.Type.ToString(),
                    i.Size,
                    i.Modified
                }));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]FTP List[/] [grey]{Markup.Escape(path)}[/]").RuleStyle("grey"));
            if (rootListing) {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] Server returned a root listing for this path. This FTP server may not support listing subfolders.");
            }
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[green]Name[/]"));
            table.AddColumn(new TableColumn("[grey]Type[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
            foreach (FtpListItem item in items) {
                table.AddRow(
                    $"[green]{Markup.Escape(item.Name)}[/]",
                    $"[grey]{item.Type}[/]",
                    item.Type == FtpObjectType.File ? $"[cyan]{FtpHelpers.FormatBytes(item.Size)}[/]" : "[grey]-[/]",
                    item.Modified != DateTime.MinValue
                        ? CliOutput.FormatTimestamp(item.Modified)
                        : "[grey]unknown[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FtpFindCommand : AsyncCommand<FtpFindCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        [Description("Root path to search (default: /).")]
        public string? Path { get; init; }

        [CommandOption("--name <PATTERN>")]
        [Description("File or folder name pattern (supports * and ? wildcards).")]
        public string? Name { get; init; }

        [CommandOption("--regex")]
        [Description("Interpret --name as a regular expression.")]
        public bool Regex { get; init; }

        [CommandOption("--depth <N>")]
        [Description("Maximum recursion depth (default: 6).")]
        public int? Depth { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum results to return (default: 200).")]
        public int? Max { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            AnsiConsole.MarkupLine("[red]--name is required.[/]");
            return 1;
        }

        int maxDepth = settings.Depth.GetValueOrDefault(6);
        if (maxDepth < 0) maxDepth = 0;
        int maxResults = settings.Max.GetValueOrDefault(200);
        if (maxResults <= 0) maxResults = 200;

        Regex matcher = settings.Regex
            ? new Regex(settings.Name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : BuildWildcardRegex(settings.Name);

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string startPath = FtpHelpers.NormalizePath(settings.Path ?? "/");
            Queue<(string Path, int Depth)> pending = new Queue<(string, int)>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FtpFindResult> matches = new List<FtpFindResult>();

            pending.Enqueue((startPath, 0));
            while (pending.Count > 0 && matches.Count < maxResults) {
                (string current, int depth) = pending.Dequeue();
                if (!visited.Add(current))
                    continue;

                FtpListItem[] items;
                bool rootListing;
                try {
                    (items, rootListing) = await FtpHelpers.GetListingWithFallbackAsync(client, current);
                }
                catch {
                    continue;
                }

                if (rootListing && !string.Equals(current, "/", StringComparison.Ordinal)) {
                    continue;
                }

                foreach (FtpListItem item in items) {
                    if (string.IsNullOrWhiteSpace(item.Name) || item.Name == "." || item.Name == "..")
                        continue;
                    string itemPath = string.IsNullOrWhiteSpace(item.FullName)
                        ? CombinePath(current, item.Name)
                        : item.FullName;

                    if (matcher.IsMatch(item.Name)) {
                        matches.Add(new FtpFindResult {
                            Path = itemPath,
                            Name = item.Name,
                            Type = item.Type,
                            Size = item.Size,
                            Modified = item.Modified
                        });
                        if (matches.Count >= maxResults)
                            break;
                    }

                    if (item.Type == FtpObjectType.Directory && depth < maxDepth) {
                        pending.Enqueue((itemPath, depth + 1));
                    }
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(matches.Select(i => new {
                    Path = i.Path,
                    i.Name,
                    Type = i.Type.ToString(),
                    i.Size,
                    i.Modified
                }));
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]FTP Find[/] [grey]{Markup.Escape(startPath)}[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[green]Path[/]"));
            table.AddColumn(new TableColumn("[grey]Type[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
            foreach (FtpFindResult item in matches) {
                table.AddRow(
                    $"[green]{Markup.Escape(item.Path)}[/]",
                    $"[grey]{item.Type}[/]",
                    item.Type == FtpObjectType.File ? $"[cyan]{FtpHelpers.FormatBytes(item.Size)}[/]" : "[grey]-[/]",
                    item.Modified != DateTime.MinValue
                        ? CliOutput.FormatTimestamp(item.Modified)
                        : "[grey]unknown[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }

    private static Regex BuildWildcardRegex(string pattern) {
        string escaped = Regex.Escape(pattern);
        escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string CombinePath(string basePath, string name) {
        if (string.Equals(basePath, "/", StringComparison.Ordinal))
            return "/" + name;
        return basePath.TrimEnd('/') + "/" + name;
    }

    private sealed class FtpFindResult {
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public FtpObjectType Type { get; init; }
        public long Size { get; init; }
        public DateTime Modified { get; init; }
    }
}

public sealed class FtpTargetCommand : Command<FtpTargetCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--set <IP>")]
        [Description("Set the default FTP IP.")]
        public string? Ip { get; init; }

        [CommandOption("--port <PORT>")]
        [Description("Set the default FTP port (default: 21).")]
        public int? Port { get; init; }

        [CommandOption("--user <USER>")]
        [Description("Set the default FTP username.")]
        public string? User { get; init; }

        [CommandOption("--pass <PASS>")]
        [Description("Set the default FTP password.")]
        public string? Pass { get; init; }

        [CommandOption("--clear")]
        [Description("Clear saved FTP target settings.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        if (settings.Clear) {
            config.DefaultFtpPort = null;
            config.DefaultFtpUser = null;
            config.DefaultFtpPassword = null;
            config.Save();
            AnsiConsole.MarkupLine("[green]FTP target cleared.[/]");
            return 0;
        }

        bool updated = false;
        if (!string.IsNullOrWhiteSpace(settings.Ip)) {
            config.DefaultIp = settings.Ip;
            updated = true;
        }
        if (settings.Port.HasValue) {
            config.DefaultFtpPort = settings.Port;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.User)) {
            config.DefaultFtpUser = settings.User;
            updated = true;
        }
        if (!string.IsNullOrWhiteSpace(settings.Pass)) {
            config.DefaultFtpPassword = settings.Pass;
            updated = true;
        }
        if (updated) {
            config.Save();
            AnsiConsole.MarkupLine("[green]FTP target updated.[/]");
            return 0;
        }

        string ip = config.DefaultIp ?? "unknown";
        string port = (config.DefaultFtpPort ?? 21).ToString(CultureInfo.InvariantCulture);
        string user = config.DefaultFtpUser ?? "xboxftp";
        string pass = string.IsNullOrWhiteSpace(config.DefaultFtpPassword) ? "unknown" : "********";
        AnsiConsole.MarkupLine($"[green]FTP target:[/] {ip}:{port} user={user} pass={pass}");
        return 0;
    }
}

public sealed class FtpGetCommand : AsyncCommand<FtpGetCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }

        [CommandOption("--out <FILE>")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Output)) {
            AnsiConsole.MarkupLine("[red]--path and --out are required.[/]");
            return 1;
        }

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string remote = FtpHelpers.NormalizePath(settings.Path);
            string local = settings.Output!;
            long size = 0;
            if (await client.FileExists(remote)) {
                size = await client.GetFileSize(remote);
            }

            await CliOutput.RunWithProgressAsync($"FTP download {Markup.Escape(remote)}", size > 0 ? (uint) size : null, async progress => {
                Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                    if (p.TransferredBytes > 0)
                        progress.Report(p.TransferredBytes);
                });
                await client.DownloadFile(local, remote, FtpLocalExists.Overwrite, FtpVerify.None, ftpProgress);
            });

            AnsiConsole.MarkupLine($"[green]Downloaded[/] {Markup.Escape(remote)} -> {Markup.Escape(local)}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FtpPutCommand : AsyncCommand<FtpPutCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }

        [CommandOption("--in <FILE>")]
        public string? Input { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path) || string.IsNullOrWhiteSpace(settings.Input)) {
            AnsiConsole.MarkupLine("[red]--path and --in are required.[/]");
            return 1;
        }

        if (!File.Exists(settings.Input)) {
            AnsiConsole.MarkupLine("[red]Input file not found.[/]");
            return 1;
        }

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string remote = FtpHelpers.NormalizePath(settings.Path);
            string local = settings.Input!;
            long size = new FileInfo(local).Length;
            await CliOutput.RunWithProgressAsync($"FTP upload {Markup.Escape(remote)}", size > 0 ? (uint) size : null, async progress => {
                Progress<FtpProgress> ftpProgress = new Progress<FtpProgress>(p => {
                    if (p.TransferredBytes > 0)
                        progress.Report(p.TransferredBytes);
                });
                await client.UploadFile(local, remote, FtpRemoteExists.Overwrite, true, FtpVerify.None, ftpProgress);
            });

            AnsiConsole.MarkupLine($"[green]Uploaded[/] {Markup.Escape(local)} -> {Markup.Escape(remote)}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FtpDeleteCommand : AsyncCommand<FtpDeleteCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string remote = FtpHelpers.NormalizePath(settings.Path);
            try {
                if (await client.DirectoryExists(remote)) {
                    await client.DeleteDirectory(remote);
                    AnsiConsole.MarkupLine($"[green]Deleted directory[/] {Markup.Escape(remote)}");
                    return 0;
                }
            }
            catch {
                // fall through to raw RMD attempt
            }

            try {
                if (await client.FileExists(remote)) {
                    await client.DeleteFile(remote);
                    AnsiConsole.MarkupLine($"[green]Deleted file[/] {Markup.Escape(remote)}");
                    return 0;
                }
            }
            catch {
                // fall through to raw delete attempts
            }

            FluentFTP.FtpReply reply = await client.Execute($"RMD {remote}");
            if (reply.Success) {
                AnsiConsole.MarkupLine($"[green]Deleted directory[/] {Markup.Escape(remote)}");
                return 0;
            }

            reply = await client.Execute($"DELE {remote}");
            if (reply.Success) {
                AnsiConsole.MarkupLine($"[green]Deleted file[/] {Markup.Escape(remote)}");
                return 0;
            }

            throw new IOException($"FTP delete failed: {reply.Code} {reply.Message}");
        }, CancellationToken.None);
    }
}

public sealed class FtpMkdirCommand : AsyncCommand<FtpMkdirCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string remote = FtpHelpers.NormalizePath(settings.Path);
            await client.CreateDirectory(remote);
            AnsiConsole.MarkupLine($"[green]Created directory[/] {Markup.Escape(remote)}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FtpMoveCommand : AsyncCommand<FtpMoveCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--from <PATH>")]
        public string? From { get; init; }

        [CommandOption("--to <PATH>")]
        public string? To { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.From) || string.IsNullOrWhiteSpace(settings.To)) {
            AnsiConsole.MarkupLine("[red]--from and --to are required.[/]");
            return 1;
        }

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string from = FtpHelpers.NormalizePath(settings.From);
            string to = FtpHelpers.NormalizePath(settings.To);
            if (await client.DirectoryExists(from)) {
                await client.MoveDirectory(from, to);
                AnsiConsole.MarkupLine($"[green]Moved directory[/] {Markup.Escape(from)} -> {Markup.Escape(to)}");
                return 0;
            }

            await client.MoveFile(from, to);
            AnsiConsole.MarkupLine($"[green]Moved file[/] {Markup.Escape(from)} -> {Markup.Escape(to)}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FtpCatCommand : AsyncCommand<FtpCatCommand.Settings> {
    public sealed class Settings : FtpConnectionSettings {
        [CommandOption("--path <PATH>")]
        public string? Path { get; init; }

        [CommandOption("--max <BYTES>")]
        [Description("Maximum bytes to read (default: 65536).")]
        public string? MaxBytes { get; init; }

        [CommandOption("--encoding <ENC>")]
        [Description("ascii|utf8 (default: utf8).")]
        public string? EncodingName { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Path)) {
            AnsiConsole.MarkupLine("[red]--path is required.[/]");
            return 1;
        }

        int maxBytes = 65536;
        if (!string.IsNullOrWhiteSpace(settings.MaxBytes) &&
            !int.TryParse(settings.MaxBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxBytes)) {
            AnsiConsole.MarkupLine("[red]Invalid --max value.[/]");
            return 1;
        }

        Encoding encoding = settings.EncodingName?.Equals("ascii", StringComparison.OrdinalIgnoreCase) == true
            ? Encoding.ASCII
            : Encoding.UTF8;

        return await FtpHelpers.WithClientAsync(settings, async client => {
            string remote = FtpHelpers.NormalizePath(settings.Path);
            using Stream stream = await client.OpenRead(remote);
            byte[] buffer = new byte[Math.Min(maxBytes, 1024 * 1024)];
            int totalRead = 0;
            using MemoryStream ms = new MemoryStream();
            while (totalRead < maxBytes) {
                int toRead = Math.Min(buffer.Length, maxBytes - totalRead);
                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead));
                if (read <= 0)
                    break;
                ms.Write(buffer, 0, read);
                totalRead += read;
            }

            string text = encoding.GetString(ms.ToArray());
            AnsiConsole.WriteLine(text);
            return 0;
        }, CancellationToken.None);
    }
}
