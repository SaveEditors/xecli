using System.ComponentModel;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugStopCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.DebugStopAsync(cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new { Operation = "debug stop", State = "stopped", Acknowledged = true, Verified = true });
            }
            else {
                AnsiConsole.MarkupLine("[green]Execution stopped.[/]");
            }
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDebugGoCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.DebugGoAsync(cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new { Operation = "debug go", State = "running", Acknowledged = true, Verified = true });
            }
            else {
                AnsiConsole.MarkupLine("[green]Execution resumed.[/]");
            }
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmDebugWatchCommand : AsyncCommand<XbdmDebugWatchCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--duration <SEC>")]
        [LocalizedDescription("Total watch runtime in seconds (default: 30).")]
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

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write debug events to a JSONL log file.")]
        public string? Output { get; init; }

        [CommandOption("--context")]
        [LocalizedDescription("Capture thread register context for break, databreak, and exception events when thread id is present.")]
        public bool CaptureContext { get; init; }

        [CommandOption("--require-event")]
        [LocalizedDescription("Fail if the watch window ends without receiving at least one notification.")]
        public bool RequireEvent { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.TimeoutMs.HasValue && settings.TimeoutMs.Value <= 0) {
            CliValidationOutput.Write(
                settings,
                "Debug watch validation failed",
                "--timeout must be greater than zero.",
                "DEBUG_WATCH_VALIDATION_FAILED",
                "Pass a positive --timeout value in milliseconds.");
            return 1;
        }

        if (settings.DurationSeconds.HasValue && settings.DurationSeconds.Value <= 0) {
            CliValidationOutput.Write(
                settings,
                "Debug watch validation failed",
                "--duration must be greater than zero.",
                "DEBUG_WATCH_VALIDATION_FAILED",
                "Pass a positive --duration value in seconds.");
            return 1;
        }

        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        int durationSeconds = settings.DurationSeconds ?? 30;
        int maxEvents = Math.Max(1, settings.MaxEvents ?? int.MaxValue);
        string debuggerName = string.IsNullOrWhiteSpace(settings.DebuggerName) ? "XeCLI" : settings.DebuggerName.Trim();

        using CancellationTokenSource durationCts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        CancellationToken durationToken = durationCts.Token;

        using CancellationTokenSource notifyConnectCts = CancellationTokenSource.CreateLinkedTokenSource(durationToken);
        notifyConnectCts.CancelAfter(timeout);
        using TcpClient notifyClient = new TcpClient();
        notifyClient.ReceiveTimeout = timeout;
        notifyClient.SendTimeout = timeout;
        await notifyClient.ConnectAsync(ip, port, notifyConnectCts.Token);
        using NetworkStream notifyStream = notifyClient.GetStream();
        using StreamReader reader = new StreamReader(notifyStream, Encoding.ASCII, false, 4096, true);
        using StreamWriter writer = new StreamWriter(notifyStream, Encoding.ASCII, 4096, true) {
            NewLine = "\r\n",
            AutoFlush = true
        };

        string? welcome = await ReadLineWithTimeoutAsync(reader, timeout, durationToken, "Notification handshake");
        EnsureWatchResponse(welcome, 201, "Notification handshake");

        List<string> initialEventLines = new List<string>();
        await SendWatchSetupCommandAsync(
            writer,
            reader,
            $"debugger connect override name=\"{EscapeQuoted(debuggerName)}\" user=\"{EscapeQuoted(Environment.MachineName)}\"",
            200,
            timeout,
            durationToken,
            initialEventLines);

        XbdmClient? control = null;
        if (settings.CaptureContext || settings.StopOnFce) {
            using CancellationTokenSource controlConnectCts = CancellationTokenSource.CreateLinkedTokenSource(durationToken);
            controlConnectCts.CancelAfter(timeout);
            control = await XbdmClient.ConnectAsync(new XbdmConnectionOptions {
                Host = ip,
                Port = port,
                TimeoutMs = timeout
            }, controlConnectCts.Token);
        }

        int count = 0;
        List<object> jsonEvents = new List<object>();
        bool notificationStreamClosed = false;
        bool notificationStreamFailed = false;
        bool stopOnFceRestored = !settings.StopOnFce;
        try {
            if (settings.StopOnFce) {
                await SendWatchSetupCommandAsync(
                    writer,
                    reader,
                    "stopon fce",
                    200,
                    timeout,
                    durationToken,
                    initialEventLines);
            }

            await SendWatchSetupCommandAsync(
                writer,
                reader,
                "notify reconnectport=1",
                205,
                timeout,
                durationToken,
                initialEventLines);

            if (!settings.Json) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Debug Watch[/]").RuleStyle("grey"));
                AnsiConsole.MarkupLine($"[grey]Target:[/] [cyan]{ip}:{port}[/]  [grey]Session:[/] [green]{Markup.Escape(debuggerName)}[/]  [grey]Duration:[/] [cyan]{durationSeconds}s[/]");
            }

            await using StreamWriter? eventLog = OpenEventLog(settings.Output);
            if (eventLog != null && !settings.Json)
                AnsiConsole.MarkupLine($"[grey]Logging events to[/] [cyan]{Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.Output))}[/]");

            foreach (string initialLine in initialEventLines) {
                if (count >= maxEvents)
                    break;
                await ProcessEventLineAsync(initialLine, control, eventLog);
            }

            while (!durationToken.IsCancellationRequested && count < maxEvents) {
                string? line = await reader.ReadLineAsync(durationToken);
                if (line == null) {
                    notificationStreamClosed = true;
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                await ProcessEventLineAsync(line, control, eventLog);
            }

            async Task ProcessEventLineAsync(string line, XbdmClient? contextClient, StreamWriter? eventLogWriter) {
                XbdmNotifyEvent evt = XbdmNotifyEvent.Parse(line);
                IReadOnlyDictionary<string, string>? contextMap = settings.CaptureContext && contextClient != null
                    ? await TryCaptureThreadContextAsync(contextClient, evt, durationToken)
                    : null;
                if (settings.Json) {
                    jsonEvents.Add(BuildEventJson(evt, contextMap));
                }
                else {
                    RenderEvent(evt, settings.Raw);
                    if (contextMap != null && !settings.Raw)
                        RenderContextSummary(contextMap);
                }
                if (eventLogWriter != null)
                    await WriteEventLogAsync(eventLogWriter, evt, contextMap, durationToken);
                count++;
            }
        }
        catch (OperationCanceledException) when (durationToken.IsCancellationRequested) {
            // duration elapsed
        }
        catch (IOException) {
            notificationStreamFailed = true;
        }
        finally {
            if (settings.StopOnFce && control != null) {
                using CancellationTokenSource cleanupCts = new CancellationTokenSource(Math.Clamp(timeout, 250, 5000));
                try {
                    await control.SendCommandExpectOkAsync("nostopon fce", cleanupCts.Token);
                    stopOnFceRestored = true;
                }
                catch {
                    stopOnFceRestored = false;
                }
            }

            if (control != null)
                await control.DisposeAsync();
        }

        if (!settings.Json)
            AnsiConsole.MarkupLine($"[grey]Watch ended after[/] [cyan]{count}[/] [grey]event(s).[/]");

        if (notificationStreamClosed || notificationStreamFailed) {
            CliValidationOutput.Write(
                settings,
                "Debug watch failed",
                notificationStreamClosed
                    ? "The XBDM notification stream closed before the requested watch duration elapsed."
                    : "The XBDM notification stream failed before the requested watch duration elapsed.",
                notificationStreamClosed ? "DEBUG_WATCH_STREAM_CLOSED" : "DEBUG_WATCH_STREAM_FAILED",
                "Check the console connection and retry the watch.");
            return 1;
        }

        if (!stopOnFceRestored) {
            CliValidationOutput.Write(
                settings,
                "Debug watch cleanup failed",
                "XeCLI could not restore the first-chance exception policy after the watch ended.",
                "DEBUG_WATCH_CLEANUP_FAILED",
                "Reconnect to the target, run `rgh xbdm raw --cmd \"nostopon fce\"`, and verify normal execution before continuing.");
            return 1;
        }

        if (settings.RequireEvent && count == 0) {
            CliValidationOutput.Write(
                settings,
                "Debug watch failed",
                "No debug notifications were observed; --require-event requires at least one event.",
                "DEBUG_WATCH_NO_EVENTS",
                "Trigger a controlled debug event during the watch window, or omit --require-event.");
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(new {
                Operation = "debug watch",
                Session = debuggerName,
                DurationSeconds = durationSeconds,
                EventCount = count,
                RequireEvent = settings.RequireEvent,
                Output = string.IsNullOrWhiteSpace(settings.Output) ? null : Path.GetFullPath(settings.Output),
                Events = jsonEvents
            });
        }

        return 0;
    }

    private static async Task SendWatchSetupCommandAsync(
        StreamWriter writer,
        StreamReader reader,
        string command,
        int expectedStatus,
        int timeoutMs,
        CancellationToken cancellationToken,
        ICollection<string> initialEventLines) {
        await writer.WriteLineAsync(command);
        while (true) {
            string? line = await ReadLineWithTimeoutAsync(reader, timeoutMs, cancellationToken, $"{command} response");
            if (TryGetWatchResponseStatus(line, out int status)) {
                EnsureWatchResponse(line, expectedStatus, command);
                return;
            }

            if (!string.IsNullOrWhiteSpace(line))
                initialEventLines.Add(line);
        }
    }

    private static void EnsureWatchResponse(string? line, int expectedStatus, string operation) {
        if (!TryGetWatchResponseStatus(line, out int status) || status != expectedStatus) {
            throw new IOException(
                $"{operation} failed: expected XBDM status {expectedStatus}, received {line ?? "(no response)"}.");
        }
    }

    private static bool TryGetWatchResponseStatus(string? line, out int status) {
        status = 0;
        if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
            return false;
        if (!char.IsDigit(line[0]) || !char.IsDigit(line[1]) || !char.IsDigit(line[2]))
            return false;
        if (line.Length > 3 && line[3] is not ('-' or ' '))
            return false;

        status = ((line[0] - '0') * 100) + ((line[1] - '0') * 10) + (line[2] - '0');
        return true;
    }

    private static object BuildEventJson(XbdmNotifyEvent evt, IReadOnlyDictionary<string, string>? contextMap) {
        return new {
            evt.Type,
            evt.Raw,
            Fields = evt.Fields,
            Commands = evt.Commands,
            Context = contextMap
        };
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
                AnsiConsole.MarkupLine($"{stamp} [grey]{Markup.Escape(DescribeUnknownEvent(evt))}[/]");
                break;
        }
    }

    private static string DescribeUnknownEvent(XbdmNotifyEvent evt) {
        StringBuilder summary = new StringBuilder(evt.Type);
        if (evt.Fields.Count > 0) {
            foreach ((string key, _) in evt.Fields) {
                summary.Append(' ');
                summary.Append(key);
                summary.Append("=[redacted]");
            }
        }
        else if (evt.Commands.Count > 1) {
            summary.Append(" tokens=");
            summary.Append(evt.Commands.Count - 1);
        }

        return summary.ToString();
    }

    private static string EscapeQuoted(string text) => text.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, int timeoutMs, CancellationToken cancellationToken, string label) {
        using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try {
            return await reader.ReadLineAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            throw new TimeoutException($"{label} did not return a line within {timeoutMs} ms.");
        }
    }

    private static StreamWriter? OpenEventLog(string? outputPath) {
        if (string.IsNullOrWhiteSpace(outputPath))
            return null;

        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        return new StreamWriter(new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8) {
            AutoFlush = true
        };
    }

    private static async Task<IReadOnlyDictionary<string, string>?> TryCaptureThreadContextAsync(
        XbdmClient client,
        XbdmNotifyEvent evt,
        CancellationToken cancellationToken) {
        if (!IsThreadContextEvent(evt))
            return null;

        if (!CliHelpers.TryParseThreadId(evt.GetString("thread"), out uint threadId))
            return null;

        try {
            return await client.GetThreadContextAsync(threadId, cancellationToken);
        }
        catch (Exception ex) {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["thread"] = $"0x{threadId:X8}",
                ["context_error"] = ex.Message
            };
        }
    }

    private static bool IsThreadContextEvent(XbdmNotifyEvent evt) {
        string type = evt.Type.ToLowerInvariant();
        return type is "exception" or "break" or "databreak";
    }

    private static void RenderContextSummary(IReadOnlyDictionary<string, string> contextMap) {
        if (contextMap.TryGetValue("context_error", out string? error)) {
            AnsiConsole.MarkupLine($"[yellow]context unavailable:[/] {Markup.Escape(error)}");
            return;
        }

        string cia = contextMap.TryGetValue("cia", out string? ciaValue) ? ciaValue ?? "n/a" : "n/a";
        string lr = contextMap.TryGetValue("lr", out string? lrValue) ? lrValue ?? "n/a" : "n/a";
        string ctr = contextMap.TryGetValue("ctr", out string? ctrValue) ? ctrValue ?? "n/a" : "n/a";
        AnsiConsole.MarkupLine($"[grey]context[/] cia=[white]{Markup.Escape(cia)}[/] lr=[white]{Markup.Escape(lr)}[/] ctr=[white]{Markup.Escape(ctr)}[/]");
    }

    private static async Task WriteEventLogAsync(
        StreamWriter writer,
        XbdmNotifyEvent evt,
        IReadOnlyDictionary<string, string>? contextMap,
        CancellationToken cancellationToken) {
        var payload = new {
            TimestampUtc = DateTimeOffset.UtcNow,
            evt.Type,
            evt.Raw,
            Fields = evt.Fields,
            Commands = evt.Commands,
            Context = contextMap
        };

        string json = JsonSerializer.Serialize(payload);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}

public sealed class XbdmBreakpointAddCommand : AsyncCommand<XbdmBreakpointAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live runtime address. Use this when you already have the XBDM address.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address inside --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base shown by Ghidra or the XEX loader.")]
        public string? GhidraBase { get; init; }

    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        if (!string.IsNullOrWhiteSpace(settings.Address)) {
            WriteUnsafeBreakpointFailure(
                settings,
                "Direct code breakpoints are not available in the stable command surface. Resolve the target through a live module and executable section instead.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved;
            try {
                resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(
                    client,
                    settings,
                    cts.Token,
                    printResolution: !settings.Json,
                    includeSections: true);
            }
            catch (Exception ex) {
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug break",
                    CliErrorReporter.BuildError("debug break", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }
            if (resolved == null)
                return 1;

            if (!XbdmAddressResolutionHelpers.IsExecutableSection(resolved.Section)) {
                string location = resolved.Section == null
                    ? "The resolved address is outside every known module section."
                    : $"The resolved section '{resolved.Section.Name ?? "unnamed"}' is not executable.";
                WriteUnsafeBreakpointFailure(settings, location);
                return 1;
            }

            if (IsProtectedDebuggerModule(resolved.Module?.Name)) {
                WriteUnsafeBreakpointFailure(
                    settings,
                    $"Breakpoints inside protected debugger module '{resolved.Module?.Name}' are blocked because they can disable XBDM recovery.");
                return 1;
            }

            bool addAcknowledged = false;
            try {
                uint existingType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (existingType != 0) {
                    WriteExistingBreakpointFailure(settings, resolved.Address, existingType);
                    return 1;
                }

                await client.SendCommandExpectOkAsync($"break addr=0x{resolved.Address:X8}", cts.Token);
                addAcknowledged = true;
                uint verifiedType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (verifiedType != 1) {
                    bool cleanupVerified = await TryClearBreakpointAsync(client, resolved.Address, settings.TimeoutMs);
                    WriteBreakpointVerificationFailure(settings, resolved.Address, verifiedType, cleanupVerified);
                    return 1;
                }
            }
            catch (XbdmProtocolViolationException ex) when (!addAcknowledged && ex.StatusCode == 407) {
                WriteBreakpointUnsupportedFailure(settings);
                return 1;
            }
            catch (Exception ex) {
                if (addAcknowledged)
                    await TryClearBreakpointAsync(client, resolved.Address, settings.TimeoutMs);
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug break",
                    CliErrorReporter.BuildError("debug break", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "debug break add",
                    Address = $"0x{resolved.Address:X8}",
                    Acknowledged = true,
                    RequestAcknowledged = true,
                    Verified = true,
                    BreakpointType = "execution",
                    Safety = new {
                        SectionKnown = resolved.Section != null,
                        Executable = XbdmAddressResolutionHelpers.IsExecutableSection(resolved.Section),
                        SectionFlags = resolved.Section == null
                            ? null
                            : XbdmAddressResolutionHelpers.Hex(resolved.Section.Flags),
                        AddressVerifiedForCodeBreakpoint = XbdmAddressResolutionHelpers.IsExecutableSection(resolved.Section)
                    },
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
            }
            else {
                AnsiConsole.MarkupLine("[green]Breakpoint set and verified.[/]");
            }
            return 0;
        }, CancellationToken.None);
    }

    private static bool IsProtectedDebuggerModule(string? moduleName) {
        if (string.IsNullOrWhiteSpace(moduleName))
            return true;

        string leaf = Path.GetFileName(moduleName.Replace('/', '\\'));
        return leaf.Equals("xbdm.xex", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("xboxkrnl.exe", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("xboxkrnl", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteBreakpointUnsupportedFailure(Settings settings) {
        CliValidationOutput.Write(
            settings,
            "Code breakpoints are unavailable",
            "This XBDM build does not support the isbreak readback required to verify breakpoint state. XeCLI did not set a breakpoint.",
            "DEBUG_BREAKPOINT_UNSUPPORTED",
            "Use debug watch and read-only thread or module inspection on this target.",
            "Install an XBDM build with isbreak support before using verified code breakpoints.");
    }

    private static async Task<bool> TryClearBreakpointAsync(XbdmClient client, uint address, int? timeoutMs) {
        using CancellationTokenSource cleanupCts = new CancellationTokenSource(Math.Clamp(timeoutMs ?? 5000, 250, 5000));
        try {
            await client.SendCommandExpectOkAsync($"break addr=0x{address:X8} clear", cleanupCts.Token);
            return await client.GetBreakpointTypeAsync(address, cleanupCts.Token) == 0;
        }
        catch {
            return false;
        }
    }

    private static void WriteExistingBreakpointFailure(Settings settings, uint address, uint breakpointType) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint safety check failed",
            $"Address 0x{address:X8} already has breakpoint type 0x{breakpointType:X}; XeCLI will not replace it implicitly.",
            "DEBUG_BREAKPOINT_ALREADY_PRESENT",
            "Remove the existing breakpoint explicitly, then retry.");
    }

    private static void WriteBreakpointVerificationFailure(Settings settings, uint address, uint breakpointType, bool cleanupVerified) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint verification failed",
            $"XBDM acknowledged the breakpoint at 0x{address:X8}, but isbreak returned type 0x{breakpointType:X}. Cleanup verified: {cleanupVerified}.",
            "DEBUG_BREAKPOINT_VERIFY_FAILED",
            cleanupVerified
                ? "The unverified breakpoint was cleared. Recheck the target and retry with a known executable module address."
                : "Treat the target as potentially unsafe. Clear all breakpoints and verify execution state before continuing.");
    }

    private static void WriteUnsafeBreakpointFailure(Settings settings, string message) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint safety check failed",
            message,
            "DEBUG_BREAKPOINT_UNSAFE_ADDRESS",
            "Use --module with --rva or --ghidra to choose an address inside a known executable title section.");
    }
}

public sealed class XbdmBreakpointRemoveCommand : AsyncCommand<XbdmBreakpointRemoveCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live runtime address. Use this when you already have the XBDM address.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address inside --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base shown by Ghidra or the XEX loader.")]
        public string? GhidraBase { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            bool clearAcknowledged = false;
            bool alreadyClear = false;
            try {
                uint existingType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (existingType == 0) {
                    alreadyClear = true;
                }
                else {
                    if (existingType != 1) {
                        WriteBreakpointTypeMismatchFailure(settings, resolved.Address, existingType);
                        return 1;
                    }

                    await client.SendCommandExpectOkAsync($"break addr=0x{resolved.Address:X8} clear", cts.Token);
                    clearAcknowledged = true;
                    uint verifiedType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                    if (verifiedType != 0) {
                        WriteBreakpointRemoveVerificationFailure(settings, resolved.Address, verifiedType);
                        return 1;
                    }
                }

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "debug break remove",
                        Address = $"0x{resolved.Address:X8}",
                        Acknowledged = clearAcknowledged,
                        Verified = true,
                        AlreadyClear = alreadyClear,
                        Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                    });
                }
                else {
                    AnsiConsole.MarkupLine(alreadyClear
                        ? "[green]Breakpoint state verified clear.[/]"
                        : "[green]Breakpoint cleared and verified.[/]");
                }
                return 0;
            }
            catch (XbdmProtocolViolationException ex) when (!clearAcknowledged && ex.StatusCode == 407) {
                WriteBreakpointUnsupportedFailure(settings);
                return 1;
            }
            catch (Exception ex) {
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug break",
                    CliErrorReporter.BuildError("debug break", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }
        }, CancellationToken.None);
    }

    private static void WriteBreakpointUnsupportedFailure(Settings settings) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint removal is unavailable",
            "This XBDM build does not support the isbreak readback required to verify breakpoint removal. XeCLI did not change breakpoint state.",
            "DEBUG_BREAKPOINT_UNSUPPORTED",
            "Use a compatible debugger or install an XBDM build with isbreak support.");
    }

    private static void WriteBreakpointTypeMismatchFailure(Settings settings, uint address, uint observedType) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint removal safety check failed",
            $"Address 0x{address:X8} has breakpoint type 0x{observedType:X}, not an execution breakpoint. XeCLI did not clear it.",
            "DEBUG_BREAKPOINT_TYPE_MISMATCH",
            "Inspect the existing breakpoint type and use the matching removal command.");
    }

    private static void WriteBreakpointRemoveVerificationFailure(Settings settings, uint address, uint observedType) {
        CliValidationOutput.Write(
            settings,
            "Breakpoint removal verification failed",
            $"XBDM acknowledged removal at 0x{address:X8}, but isbreak still returned type 0x{observedType:X}.",
            "DEBUG_BREAKPOINT_REMOVE_VERIFY_FAILED",
            "Treat the breakpoint as active and verify target state with a compatible debugger before continuing.");
    }
}

public sealed class XbdmBreakpointClearAllCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            try {
                await client.SendCommandExpectOkAsync("break clearall", cts.Token);
                CliValidationOutput.Write(
                    settings,
                    "Breakpoint clear-all is unverified",
                    "XBDM acknowledged `break clearall`, but the protocol cannot prove that every code and data breakpoint was removed.",
                    "DEBUG_BREAKPOINT_CLEARALL_UNVERIFIED",
                    "Verify known breakpoint addresses individually with an isbreak-capable debugger.",
                    "Reboot the target before release testing if complete breakpoint reset must be guaranteed.");
                return 1;
            }
            catch (Exception ex) {
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug break",
                    CliErrorReporter.BuildError("debug break", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }
        }, CancellationToken.None);
    }
}

public sealed class XbdmDataBreakpointAddCommand : AsyncCommand<XbdmDataBreakpointAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live runtime address. Use this when you already have the XBDM address.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address inside --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base shown by Ghidra or the XEX loader.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        [LocalizedDescription("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("write|read|exec|rw (default write).")]
        public string? Type { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Allow a direct address or a range outside a known compatible module section.")]
        public bool Force { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        uint size = 4;
        if (!string.IsNullOrWhiteSpace(settings.Size)) {
            if (!CliHelpers.TryParseUInt32(settings.Size, out size)) {
                CliValidationOutput.Write(
                    settings,
                    "Data breakpoint validation failed",
                    "Invalid --size.",
                    "DEBUG_DATABREAK_VALIDATION_FAILED",
                    "Pass a positive data-breakpoint size in bytes.");
                return 1;
            }
        }

        if (size == 0) {
            CliValidationOutput.Write(
                settings,
                "Data breakpoint validation failed",
                "--size must be greater than zero.",
                "DEBUG_DATABREAK_VALIDATION_FAILED",
                "Pass a positive data-breakpoint size in bytes.");
            return 1;
        }

        if (!XbdmDebugCommandHelpers.TryNormalizeDataBreakType(settings.Type, out string type, out string? typeError)) {
            CliValidationOutput.Write(
                settings,
                "Data breakpoint validation failed",
                typeError ?? "Invalid --type.",
                "DEBUG_DATABREAK_VALIDATION_FAILED",
                "Use write, read, exec, or rw.");
            return 1;
        }

        if (!settings.Force && !string.IsNullOrWhiteSpace(settings.Address)) {
            WriteUnsafeDataBreakpointFailure(
                settings,
                "Direct data-breakpoint addresses require --force because their mapped section cannot be verified.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(
                client,
                settings,
                cts.Token,
                printResolution: !settings.Json,
                includeSections: !settings.Force);
            if (resolved == null)
                return 1;

            if (!settings.Force && !XbdmAddressResolutionHelpers.IsRangeInsideSection(resolved.Section, resolved.Address, size)) {
                WriteUnsafeDataBreakpointFailure(
                    settings,
                    "The requested data-breakpoint range is outside a single known module section.");
                return 1;
            }

            if (!settings.Force && type == "execute" && !XbdmAddressResolutionHelpers.IsExecutableSection(resolved.Section)) {
                WriteUnsafeDataBreakpointFailure(
                    settings,
                    $"The resolved section '{resolved.Section?.Name ?? "unnamed"}' is not executable.");
                return 1;
            }

            uint expectedBreakpointType = XbdmDebugCommandHelpers.GetDataBreakpointType(type);
            bool addAcknowledged = false;
            try {
                uint existingType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (existingType != 0) {
                    WriteExistingDataBreakpointFailure(settings, resolved.Address, existingType);
                    return 1;
                }

                await client.SendCommandExpectOkAsync($"debugger connect override name=\"rgh\" user=\"{Environment.MachineName}\"", cts.Token);
                await client.SendCommandExpectOkAsync($"break {type}=0x{resolved.Address:X8} size=0x{size:X8}", cts.Token);
                addAcknowledged = true;
                uint verifiedType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (verifiedType != expectedBreakpointType) {
                    bool cleanupVerified = await TryClearDataBreakpointAsync(client, resolved.Address, size, type, settings.TimeoutMs);
                    WriteDataBreakpointVerificationFailure(settings, resolved.Address, expectedBreakpointType, verifiedType, cleanupVerified);
                    return 1;
                }
            }
            catch (XbdmProtocolViolationException ex) when (!addAcknowledged && ex.StatusCode == 407) {
                WriteDataBreakpointUnsupportedFailure(settings);
                return 1;
            }
            catch (Exception ex) {
                if (addAcknowledged)
                    await TryClearDataBreakpointAsync(client, resolved.Address, size, type, settings.TimeoutMs);
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug databreak",
                    CliErrorReporter.BuildError("debug databreak", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "debug databreak add",
                    Address = $"0x{resolved.Address:X8}",
                    Type = type,
                    Size = size,
                    Acknowledged = true,
                    RequestAcknowledged = true,
                    Verified = true,
                    BreakpointType = expectedBreakpointType,
                    Safety = new {
                        ForceUsed = settings.Force,
                        SectionKnown = resolved.Section != null,
                        RangeVerified = !settings.Force && XbdmAddressResolutionHelpers.IsRangeInsideSection(resolved.Section, resolved.Address, size),
                        Executable = XbdmAddressResolutionHelpers.IsExecutableSection(resolved.Section),
                        SectionFlags = resolved.Section == null
                            ? null
                            : XbdmAddressResolutionHelpers.Hex(resolved.Section.Flags)
                    },
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
            }
            else {
                AnsiConsole.MarkupLine("[green]Data breakpoint set and verified.[/]");
            }
            return 0;
        }, CancellationToken.None);
    }

    private static void WriteUnsafeDataBreakpointFailure(Settings settings, string message) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint safety check failed",
            message,
            "DEBUG_DATABREAK_UNSAFE_ADDRESS",
            "Choose a range inside one known module section.",
            "For execute breakpoints, choose an executable section.",
            "Use --force only when the live range and memory protection are independently verified.");
    }

    private static async Task<bool> TryClearDataBreakpointAsync(
        XbdmClient client,
        uint address,
        uint size,
        string type,
        int? timeoutMs) {
        using CancellationTokenSource cleanupCts = new CancellationTokenSource(Math.Clamp(timeoutMs ?? 5000, 250, 5000));
        try {
            await client.SendCommandExpectOkAsync(
                $"break {type}=0x{address:X8} size=0x{size:X8} clear",
                cleanupCts.Token);
            return await client.GetBreakpointTypeAsync(address, cleanupCts.Token) == 0;
        }
        catch {
            return false;
        }
    }

    private static void WriteExistingDataBreakpointFailure(Settings settings, uint address, uint breakpointType) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint safety check failed",
            $"Address 0x{address:X8} already has breakpoint type 0x{breakpointType:X}; XeCLI will not replace it implicitly.",
            "DEBUG_DATABREAK_ALREADY_PRESENT",
            "Remove the existing breakpoint explicitly, then retry.");
    }

    private static void WriteDataBreakpointVerificationFailure(
        Settings settings,
        uint address,
        uint expectedType,
        uint observedType,
        bool cleanupVerified) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint verification failed",
            $"XBDM acknowledged the data breakpoint at 0x{address:X8}, but isbreak returned type 0x{observedType:X} instead of 0x{expectedType:X}. Cleanup verified: {cleanupVerified}.",
            "DEBUG_DATABREAK_VERIFY_FAILED",
            cleanupVerified
                ? "The unverified data breakpoint was cleared. Recheck the address and retry."
                : "Treat the target as potentially unsafe. Clear all breakpoints and verify execution state before continuing.");
    }

    private static void WriteDataBreakpointUnsupportedFailure(Settings settings) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoints are unavailable",
            "This XBDM build does not support the isbreak readback required to verify data-breakpoint state. XeCLI did not set a data breakpoint.",
            "DEBUG_DATABREAK_UNSUPPORTED",
            "Use debug watch and read-only memory inspection on this target.",
            "Install an XBDM build with isbreak support before using verified data breakpoints.");
    }
}

public sealed class XbdmDataBreakpointRemoveCommand : AsyncCommand<XbdmDataBreakpointRemoveCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        [LocalizedDescription("Live runtime address. Use this when you already have the XBDM address.")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Live module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("Relative virtual address inside --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base shown by Ghidra or the XEX loader.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        [LocalizedDescription("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("write|read|exec|rw (default write).")]
        public string? Type { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        uint size = 4;
        if (!string.IsNullOrWhiteSpace(settings.Size)) {
            if (!CliHelpers.TryParseUInt32(settings.Size, out size)) {
                CliValidationOutput.Write(
                    settings,
                    "Data breakpoint validation failed",
                    "Invalid --size.",
                    "DEBUG_DATABREAK_VALIDATION_FAILED",
                    "Pass a positive data-breakpoint size in bytes.");
                return 1;
            }
        }

        if (size == 0) {
            CliValidationOutput.Write(
                settings,
                "Data breakpoint validation failed",
                "--size must be greater than zero.",
                "DEBUG_DATABREAK_VALIDATION_FAILED",
                "Pass a positive data-breakpoint size in bytes.");
            return 1;
        }

        if (!XbdmDebugCommandHelpers.TryNormalizeDataBreakType(settings.Type, out string type, out string? typeError)) {
            CliValidationOutput.Write(
                settings,
                "Data breakpoint validation failed",
                typeError ?? "Invalid --type.",
                "DEBUG_DATABREAK_VALIDATION_FAILED",
                "Use write, read, exec, or rw.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint expectedBreakpointType = XbdmDebugCommandHelpers.GetDataBreakpointType(type);
            bool clearAcknowledged = false;
            bool alreadyClear = false;
            try {
                uint existingType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                if (existingType == 0) {
                    alreadyClear = true;
                }
                else {
                    if (existingType != expectedBreakpointType) {
                        WriteDataBreakpointTypeMismatchFailure(settings, resolved.Address, expectedBreakpointType, existingType);
                        return 1;
                    }

                    await client.SendCommandExpectOkAsync($"debugger connect override name=\"rgh\" user=\"{Environment.MachineName}\"", cts.Token);
                    await client.SendCommandExpectOkAsync($"break {type}=0x{resolved.Address:X8} size=0x{size:X8} clear", cts.Token);
                    clearAcknowledged = true;
                    uint verifiedType = await client.GetBreakpointTypeAsync(resolved.Address, cts.Token);
                    if (verifiedType != 0) {
                        WriteDataBreakpointRemoveVerificationFailure(settings, resolved.Address, verifiedType);
                        return 1;
                    }
                }
            }
            catch (XbdmProtocolViolationException ex) when (!clearAcknowledged && ex.StatusCode == 407) {
                WriteDataBreakpointUnsupportedFailure(settings);
                return 1;
            }
            catch (Exception ex) {
                return XbdmCommandFailureReporter.ReportCommandFailure(
                    settings,
                    "debug databreak",
                    CliErrorReporter.BuildError("debug databreak", ex, XbdmCommandFailureReporter.GetTargetDisplay(settings)));
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "debug databreak remove",
                    Address = $"0x{resolved.Address:X8}",
                    Type = type,
                    Size = size,
                    Acknowledged = clearAcknowledged,
                    Verified = true,
                    AlreadyClear = alreadyClear,
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
            }
            else {
                AnsiConsole.MarkupLine(alreadyClear
                    ? "[green]Data breakpoint state verified clear.[/]"
                    : "[green]Data breakpoint cleared and verified.[/]");
            }
            return 0;
        }, CancellationToken.None);
    }

    private static void WriteDataBreakpointUnsupportedFailure(Settings settings) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint removal is unavailable",
            "This XBDM build does not support the isbreak readback required to verify data-breakpoint removal. XeCLI did not change breakpoint state.",
            "DEBUG_DATABREAK_UNSUPPORTED",
            "Use a compatible debugger or install an XBDM build with isbreak support.");
    }

    private static void WriteDataBreakpointTypeMismatchFailure(
        Settings settings,
        uint address,
        uint expectedType,
        uint observedType) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint removal safety check failed",
            $"Address 0x{address:X8} has breakpoint type 0x{observedType:X}, not the requested type 0x{expectedType:X}. XeCLI did not clear it.",
            "DEBUG_DATABREAK_TYPE_MISMATCH",
            "Inspect the existing breakpoint type and retry with the matching --type.");
    }

    private static void WriteDataBreakpointRemoveVerificationFailure(Settings settings, uint address, uint observedType) {
        CliValidationOutput.Write(
            settings,
            "Data breakpoint removal verification failed",
            $"XBDM acknowledged removal at 0x{address:X8}, but isbreak still returned type 0x{observedType:X}.",
            "DEBUG_DATABREAK_REMOVE_VERIFY_FAILED",
            "Treat the breakpoint as active and verify target state with a compatible debugger before continuing.");
    }
}

internal static class XbdmCommandFailureReporter {
    public static int ReportCommandFailure(ConnectionSettings settings, string operation, CliErrorEnvelope error) {
        if (settings.Json) {
            CliOutput.EmitJsonError(error);
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
        return 1;
    }

    public static string? GetTargetDisplay(ConnectionSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.Ip) || !settings.Port.HasValue)
            return null;
        return $"{settings.Ip}:{settings.Port.Value}";
    }
}

internal static class XbdmDebugCommandHelpers {
    internal static bool TryNormalizeDataBreakType(string? value, out string normalized, out string? errorMessage) {
        normalized = "write";
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(value))
            return true;

        switch (value.Trim().ToLowerInvariant()) {
            case "read":
                normalized = "read";
                return true;
            case "rw":
                normalized = "readwrite";
                return true;
            case "execute":
            case "exec":
                normalized = "execute";
                return true;
            case "write":
                normalized = "write";
                return true;
            default:
                errorMessage = "Invalid --type. Use write, read, exec, or rw.";
                return false;
        }
    }

    internal static uint GetDataBreakpointType(string normalizedType) {
        return normalizedType switch {
            "read" => 2,
            "write" => 3,
            "readwrite" => 4,
            "execute" => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(normalizedType), normalizedType, "Unsupported data-breakpoint type.")
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

