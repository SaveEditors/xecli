using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugStopCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.DebugStopAsync(CancellationToken.None);
            AnsiConsole.MarkupLine("[green]Execution stopped.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDebugGoCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            await client.DebugGoAsync(CancellationToken.None);
            AnsiConsole.MarkupLine("[green]Execution resumed.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDebugWatchCommand : AsyncCommand<XbdmDebugWatchCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--duration <SEC>")]
        [LocalizedDescription("How long to wait for notifications before exiting (default: 30).")]
        public int? DurationSeconds { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum notifications to print before exiting.")]
        public int? MaxEvents { get; init; }

        [CommandOption("--raw")]
        [LocalizedDescription("Print raw notify lines instead of parsed summaries.")]
        public bool Raw { get; init; }

        [CommandOption("--stopon-fce")]
        [LocalizedDescription("Ask XBDM to stop on first-chance exceptions.")]
        public bool StopOnFce { get; init; }

        [CommandOption("--name <NAME>")]
        [LocalizedDescription("Debugger session name (default: XeCLI).")]
        public string? DebuggerName { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        int durationSeconds = Math.Max(1, settings.DurationSeconds ?? 30);
        int maxEvents = Math.Max(1, settings.MaxEvents ?? int.MaxValue);
        string debuggerName = string.IsNullOrWhiteSpace(settings.DebuggerName) ? "XeCLI" : settings.DebuggerName.Trim();

        using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        CancellationToken token = timeoutCts.Token;

        await using XbdmClient control = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
            Host = ip,
            Port = port,
            TimeoutMs = timeout
        }, token);

        await control.SendCommandAsync($"debugger connect override name=\"{EscapeQuoted(debuggerName)}\" user=\"{EscapeQuoted(Environment.MachineName)}\"", token);
        if (settings.StopOnFce)
            await control.SendCommandAsync("stopon fce", token);

        using TcpClient notifyClient = new TcpClient();
        notifyClient.ReceiveTimeout = timeout;
        notifyClient.SendTimeout = timeout;
        await notifyClient.ConnectAsync(ip, port, token);
        using NetworkStream notifyStream = notifyClient.GetStream();
        using StreamReader reader = new StreamReader(notifyStream, Encoding.ASCII, false, 4096, true);
        using StreamWriter writer = new StreamWriter(notifyStream, Encoding.ASCII, 4096, true) {
            NewLine = "\r\n",
            AutoFlush = true
        };

        string? welcome = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(welcome))
            throw new IOException("Notification connection did not return a handshake.");

        await writer.WriteLineAsync("notify reconnectport=1");
        string? response = await reader.ReadLineAsync(token);
        if (string.IsNullOrWhiteSpace(response) || !response.StartsWith("205", StringComparison.Ordinal))
            throw new IOException($"Notification setup failed: {response ?? "(no response)"}");

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Debug Watch[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]Target:[/] [cyan]{ip}:{port}[/]  [grey]Session:[/] [green]{Markup.Escape(debuggerName)}[/]  [grey]Timeout:[/] [cyan]{durationSeconds}s[/]");

        int count = 0;
        try {
            while (!token.IsCancellationRequested && count < maxEvents) {
                string? line = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                XbdmNotifyEvent evt = XbdmNotifyEvent.Parse(line);
                RenderEvent(evt, settings.Raw);
                count++;
            }
        }
        catch (OperationCanceledException) {
            // duration elapsed
        }

        AnsiConsole.MarkupLine($"[grey]Watch ended after[/] [cyan]{count}[/] [grey]event(s).[/]");
        return 0;
    }

    private static void RenderEvent(XbdmNotifyEvent evt, bool raw) {
        string stamp = $"[green]{DateTime.Now:HH:mm:ss}[/]";
        if (raw) {
            AnsiConsole.MarkupLine($"{stamp} [grey]{Markup.Escape(evt.Raw)}[/]");
            return;
        }

        switch (evt.Type.ToLowerInvariant()) {
            case "debugstr":
                AnsiConsole.MarkupLine($"{stamp} [deepskyblue1]debugstr[/] {Markup.Escape(evt.GetString("string") ?? evt.Raw)}");
                break;
            case "execution":
                string state = evt.Commands.Count > 1 ? evt.Commands[1] : evt.Raw;
                AnsiConsole.MarkupLine($"{stamp} [yellow]execution[/] [white]{Markup.Escape(state)}[/]");
                break;
            case "exception":
                AnsiConsole.MarkupLine(
                    $"{stamp} [red]exception[/] code=[white]{Markup.Escape(evt.GetHexOrDefault("code"))}[/] " +
                    $"thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/] " +
                    $"address=[white]{Markup.Escape(evt.GetHexOrDefault("address"))}[/] " +
                    $"{Markup.Escape(evt.GetFaultOperationSummary())}");
                break;
            case "break":
                AnsiConsole.MarkupLine(
                    $"{stamp} [gold1]break[/] addr=[white]{Markup.Escape(evt.GetHexOrDefault("addr"))}[/] " +
                    $"thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/]");
                break;
            case "databreak":
                AnsiConsole.MarkupLine(
                    $"{stamp} [gold1]databreak[/] {Markup.Escape(evt.GetFaultOperationSummary())} " +
                    $"pc=[white]{Markup.Escape(evt.GetHexOrDefault("addr"))}[/] " +
                    $"thread=[white]{Markup.Escape(evt.GetHexOrDefault("thread"))}[/]");
                break;
            default:
                AnsiConsole.MarkupLine($"{stamp} [grey]{Markup.Escape(evt.Raw)}[/]");
                break;
        }
    }

    private static string EscapeQuoted(string text) => text.Replace("\"", "\\\"", StringComparison.Ordinal);
}

public sealed class XbdmBreakpointAddCommand : AsyncCommand<XbdmBreakpointAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address)) {
            AnsiConsole.MarkupLine("[red]--addr is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SendCommandAsync($"break addr=0x{address:X8}", cts.Token);
            AnsiConsole.MarkupLine("[green]Breakpoint set.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmBreakpointRemoveCommand : AsyncCommand<XbdmBreakpointRemoveCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address)) {
            AnsiConsole.MarkupLine("[red]--addr is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SendCommandAsync($"break addr=0x{address:X8} clear", cts.Token);
            AnsiConsole.MarkupLine("[green]Breakpoint cleared.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmBreakpointClearAllCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SendCommandAsync("break clearall", cts.Token);
            AnsiConsole.MarkupLine("[green]All breakpoints cleared.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDataBreakpointAddCommand : AsyncCommand<XbdmDataBreakpointAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        [LocalizedDescription("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("write|read|exec|rw (default write).")]
        public string? Type { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address)) {
            AnsiConsole.MarkupLine("[red]--addr is required.[/]");
            return 1;
        }

        uint size = 4;
        if (!string.IsNullOrWhiteSpace(settings.Size)) {
            if (!CliHelpers.TryParseUInt32(settings.Size, out size)) {
                AnsiConsole.MarkupLine("[red]Invalid --size.[/]");
                return 1;
            }
        }

        string type = XbdmDebugCommandHelpers.NormalizeDataBreakType(settings.Type);

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SendCommandAsync($"debugger connect override name=\"rgh\" user=\"{Environment.MachineName}\"", cts.Token);
            await client.SendCommandAsync($"break {type}=0x{address:X8} size=0x{size:X8}", cts.Token);
            AnsiConsole.MarkupLine("[green]Data breakpoint set.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDataBreakpointRemoveCommand : AsyncCommand<XbdmDataBreakpointRemoveCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        [LocalizedDescription("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("write|read|exec|rw (default write).")]
        public string? Type { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address)) {
            AnsiConsole.MarkupLine("[red]--addr is required.[/]");
            return 1;
        }

        uint size = 4;
        if (!string.IsNullOrWhiteSpace(settings.Size)) {
            if (!CliHelpers.TryParseUInt32(settings.Size, out size)) {
                AnsiConsole.MarkupLine("[red]Invalid --size.[/]");
                return 1;
            }
        }

        string type = XbdmDebugCommandHelpers.NormalizeDataBreakType(settings.Type);

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SendCommandAsync($"debugger connect override name=\"rgh\" user=\"{Environment.MachineName}\"", cts.Token);
            await client.SendCommandAsync($"break {type}=0x{address:X8} size=0x{size:X8} clear", cts.Token);
            AnsiConsole.MarkupLine("[green]Data breakpoint cleared.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

internal static class XbdmDebugCommandHelpers {
    internal static string NormalizeDataBreakType(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return "write";
        return value.Trim().ToLowerInvariant() switch {
            "read" => "read",
            "rw" => "read",
            "execute" => "execute",
            "exec" => "execute",
            "write" => "write",
            _ => "write"
        };
    }
}

internal sealed class XbdmNotifyEvent {
    public required string Raw { get; init; }
    public required string Type { get; init; }
    public required List<string> Commands { get; init; }
    public required Dictionary<string, string> Fields { get; init; }

    public string? GetString(string key) => Fields.TryGetValue(key, out string? value) ? value : null;

    public string GetHexOrDefault(string key) => GetString(key) ?? "n/a";

    public string GetFaultOperationSummary() {
        foreach (string key in new[] { "write", "read", "readwrite", "execute" }) {
            if (Fields.TryGetValue(key, out string? value))
                return $"{key}=[white]{Markup.Escape(value)}[/]";
        }
        return string.Empty;
    }

    public static XbdmNotifyEvent Parse(string raw) {
        List<string> tokens = Tokenize(raw);
        List<string> commands = new List<string>();
        Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in tokens) {
            int split = token.IndexOf('=');
            if (split > 0) {
                string key = token.Substring(0, split).Trim();
                string value = token.Substring(split + 1).Trim().Trim('"');
                fields[key] = value;
            }
            else {
                commands.Add(token);
            }
        }

        return new XbdmNotifyEvent {
            Raw = raw,
            Type = commands.Count > 0 ? commands[0] : "notify",
            Commands = commands,
            Fields = fields
        };
    }

    private static List<string> Tokenize(string raw) {
        List<string> tokens = new List<string>();
        StringBuilder current = new StringBuilder(raw.Length);
        bool inQuotes = false;
        for (int i = 0; i < raw.Length; i++) {
            char ch = raw[i];
            if (ch == '"') {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch)) {
                if (current.Length > 0) {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }
}

