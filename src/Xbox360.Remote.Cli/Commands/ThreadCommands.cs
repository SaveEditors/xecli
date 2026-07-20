using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadsListCommand : AsyncCommand<XbdmThreadsListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--no-names")]
        [LocalizedDescription("Compatibility flag. Ignored by the current thread list output; thread list now uses start-address metadata instead of thread names.")]
        public bool NoNames { get; init; }
    }

    private readonly record struct ThreadListRow(
        uint Id,
        uint SuspendCount,
        uint Priority,
        uint CurrentProcessor,
        string? ImageName,
        uint? StartAddress,
        uint? EndAddress,
        uint StackBase,
        uint StackLimit);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            IReadOnlyList<XbdmThreadInfo> threads = await client.GetThreadsAsync(includeNames: false, cts.Token);
            IReadOnlyList<ThreadListRow> rows = await BuildRowsAsync(client, threads, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(rows.Select(row => new {
                    row.Id,
                    row.SuspendCount,
                    row.Priority,
                    row.CurrentProcessor,
                    row.ImageName,
                    row.StartAddress,
                    row.EndAddress,
                    row.StackBase,
                    row.StackLimit
                }));
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Threads[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]#[/]").Centered());
            table.AddColumn(new TableColumn("[cyan]ID[/]"));
            table.AddColumn(new TableColumn("[grey]Suspend[/]"));
            table.AddColumn(new TableColumn("[grey]Priority[/]"));
            table.AddColumn(new TableColumn("[grey]CPU[/]"));
            table.AddColumn(new TableColumn("[green]Image[/]"));
            table.AddColumn(new TableColumn("[cyan]Start Addr.[/]"));
            table.AddColumn(new TableColumn("[cyan]End Addr.[/]"));
            table.AddColumn(new TableColumn("[cyan]Stack Base[/]"));
            table.AddColumn(new TableColumn("[cyan]Stack Limit[/]"));
            int index = 1;
            foreach (ThreadListRow row in rows) {
                table.AddRow(
                    $"[grey]{index}[/]",
                    $"[cyan]0x{row.Id:X8}[/]",
                    $"[grey]{row.SuspendCount}[/]",
                    $"[grey]{row.Priority}[/]",
                    $"[grey]{row.CurrentProcessor}[/]",
                    string.IsNullOrWhiteSpace(row.ImageName) ? "[grey]unknown[/]" : $"[green]{Markup.Escape(row.ImageName)}[/]",
                    row.StartAddress.HasValue ? $"[cyan]0x{row.StartAddress.Value:X8}[/]" : "[grey]unknown[/]",
                    row.EndAddress.HasValue ? $"[cyan]0x{row.EndAddress.Value:X8}[/]" : "[grey]unknown[/]",
                    $"[cyan]0x{row.StackBase:X8}[/]",
                    $"[cyan]0x{row.StackLimit:X8}[/]");
                index++;
            }

            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ThreadListRow>> BuildRowsAsync(
        XbdmClient client,
        IReadOnlyList<XbdmThreadInfo> threads,
        CancellationToken cancellationToken) {
        if (threads.Count == 0)
            return Array.Empty<ThreadListRow>();

        IReadOnlyList<XbdmModuleInfo> modules;
        try {
            modules = await client.GetModulesAsync(includeSections: false, cancellationToken);
        }
        catch {
            modules = Array.Empty<XbdmModuleInfo>();
        }

        List<ThreadListRow> rows = new List<ThreadListRow>(threads.Count);
        foreach (XbdmThreadInfo thread in threads) {
            XbdmModuleInfo? module = FindContainingModule(modules, thread.StartAddress);
            uint? startAddress = thread.StartAddress == 0 ? null : thread.StartAddress;
            uint? endAddress = null;
            string? imageName = null;

            if (module != null) {
                imageName = module.Name;
                ulong moduleEnd = (ulong) module.BaseAddress + module.ModuleSize;
                if (moduleEnd <= uint.MaxValue) {
                    endAddress = (uint) moduleEnd;
                }
            }

            rows.Add(new ThreadListRow(
                thread.Id,
                thread.SuspendCount,
                thread.Priority,
                thread.CurrentProcessor,
                imageName,
                startAddress,
                endAddress,
                thread.BaseAddress,
                thread.StackLimit));
        }

        return rows;
    }

    private static XbdmModuleInfo? FindContainingModule(IReadOnlyList<XbdmModuleInfo> modules, uint address) {
        if (address == 0)
            return null;

        ulong target = address;
        foreach (XbdmModuleInfo module in modules) {
            ulong moduleStart = module.BaseAddress;
            ulong moduleEnd = moduleStart + module.ModuleSize;
            if (target >= moduleStart && target < moduleEnd) {
                return module;
            }
        }

        return null;
    }
}

public sealed class XbdmThreadContextCommand : AsyncCommand<XbdmThreadContextCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [LocalizedDescription("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            AnsiConsole.MarkupLine("[red]--id is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            IReadOnlyDictionary<string, string> contextMap = await client.GetThreadContextAsync(threadId, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(contextMap);
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]Thread 0x{threadId:X8}[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Register[/]"));
            table.AddColumn(new TableColumn("[cyan]Value[/]"));
            foreach ((string key, string value) in contextMap.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
                table.AddRow($"[grey]{Markup.Escape(key)}[/]", $"[cyan]{Markup.Escape(value)}[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmThreadRegisterSetCommand : AsyncCommand<XbdmThreadRegisterSetCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [LocalizedDescription("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }

        [CommandOption("--name <REG>")]
        [LocalizedDescription("Register name to write, for example r3, lr, ctr, cia, or cr.")]
        public string? RegisterName { get; init; }

        [CommandOption("--value <VALUE>")]
        [LocalizedDescription("64-bit register value in hex or decimal.")]
        public string? Value { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Required to write a register.")]
        public bool Force { get; init; }

        [CommandOption("--verify")]
        [LocalizedDescription("Required. Read the thread context after writing and fail if the register did not update.")]
        public bool Verify { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            CliValidationOutput.Write(
                settings,
                "Thread register set validation failed",
                "--id is required.",
                "XBDM_THREAD_REGISTER_SET_VALIDATION_FAILED",
                "Provide a live thread ID in hexadecimal or decimal form with --id.");
            return 1;
        }

        if (!TryNormalizeRegisterName(settings.RegisterName, out string registerName)) {
            CliValidationOutput.Write(
                settings,
                "Thread register set validation failed",
                "--name is required and can only contain ASCII letters and numbers.",
                "XBDM_THREAD_REGISTER_SET_VALIDATION_FAILED",
                "Provide a valid PowerPC register name such as r3, lr, ctr, cia, or cr.");
            return 1;
        }

        if (!TryParseUInt64(settings.Value, out ulong value)) {
            CliValidationOutput.Write(
                settings,
                "Thread register set validation failed",
                "--value is required and must be a 64-bit hex or decimal value.",
                "XBDM_THREAD_REGISTER_SET_VALIDATION_FAILED",
                "Provide a 64-bit hexadecimal or decimal register value with --value.");
            return 1;
        }

        if (!settings.Force) {
            CliValidationOutput.Write(
                settings,
                "Thread register set validation failed",
                "Refusing to write register without --force.",
                "XBDM_THREAD_REGISTER_SET_VALIDATION_FAILED",
                "Confirm the live register write and rerun with --force.");
            return 1;
        }

        if (!settings.Verify) {
            CliValidationOutput.Write(
                settings,
                "Thread register set validation failed",
                "Refusing to write register without --verify because an XBDM acknowledgement does not prove the register changed.",
                "XBDM_THREAD_REGISTER_SET_VALIDATION_FAILED",
                "Confirm the live register write and rerun with --force --verify.");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            try {
                await client.SetThreadContextAsync(threadId, registerName, value, cts.Token);
            }
            catch (IOException ex) when (IsThreadNotDebugStoppedFailure(ex)) {
                CliValidationOutput.Write(
                    settings,
                    "Thread register write requires a debug-event stop",
                    "XBDM refused the register write because the thread is not stopped at a debugger-owned breakpoint or exception event.",
                    "XBDM_THREAD_REGISTER_NOT_DEBUG_STOPPED",
                    "Stop the thread at a real breakpoint or exception; `debug stop` and `threads suspend` do not satisfy this Natelx XBDM precondition.",
                    "Keep the debugger notification session active while retrying the verified register write.",
                    "Resume execution with `rgh debug go` after the debugging operation is complete.");
                return 1;
            }

            ulong? readBack = null;
            string? readBackText = null;
            if (settings.Verify) {
                IReadOnlyDictionary<string, string> contextMap = await client.GetThreadContextAsync(threadId, cts.Token);
                if (!contextMap.TryGetValue(registerName, out readBackText)) {
                    CliValidationOutput.Write(
                        settings,
                        "Thread register verification failed",
                        $"verification failed: register {registerName} was not present in read-back context.",
                        "XBDM_THREAD_REGISTER_VERIFY_FAILED",
                        "Read the live thread context and confirm that the selected register is available.");
                    return 1;
                }

                if (!TryParseContextValue(readBackText, out ulong parsedReadBack)) {
                    CliValidationOutput.Write(
                        settings,
                        "Thread register verification failed",
                        $"verification failed: could not parse read-back value {readBackText}.",
                        "XBDM_THREAD_REGISTER_VERIFY_FAILED",
                        "Read the live thread context and inspect the register value returned by XBDM.");
                    return 1;
                }

                readBack = parsedReadBack;
                if (parsedReadBack != value) {
                    CliValidationOutput.Write(
                        settings,
                        "Thread register verification failed",
                        $"verification failed: {registerName} expected {FormatUInt64(value)}, read {FormatUInt64(parsedReadBack)}.",
                        "XBDM_THREAD_REGISTER_VERIFY_FAILED",
                        "Stop the title, confirm the thread is stable, and retry the verified register write.");
                    return 1;
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    ThreadId = threadId,
                    ThreadIdHex = $"0x{threadId:X8}",
                    Register = registerName,
                    Value = value,
                    ValueHex = FormatUInt64(value),
                    Verified = settings.Verify,
                    ReadBack = readBack,
                    ReadBackHex = readBack.HasValue ? FormatUInt64(readBack.Value) : null,
                    ReadBackRaw = readBackText
                });
                return 0;
            }

            string verifiedText = settings.Verify ? " [green](verified)[/]" : string.Empty;
            AnsiConsole.MarkupLine($"[green]Set[/] [grey]{Markup.Escape(registerName)}[/]=[cyan]{FormatUInt64(value)}[/] on thread [cyan]0x{threadId:X8}[/]{verifiedText}");
            return 0;
        }, CancellationToken.None);
    }

    private static bool IsThreadNotDebugStoppedFailure(IOException exception) {
        string message = exception.Message;
        return message.Contains("408", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("not stopped", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeRegisterName(string? text, out string registerName) {
        registerName = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string candidate = text.Trim();
        if (candidate.Length > 16)
            return false;

        foreach (char c in candidate) {
            bool isAsciiLetter = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isAsciiNumber = c is >= '0' and <= '9';
            if (!isAsciiLetter && !isAsciiNumber)
                return false;
        }

        registerName = candidate;
        return true;
    }

    private static bool TryParseContextValue(string text, out ulong value) {
        string candidate = text.Trim();
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("0q", StringComparison.OrdinalIgnoreCase)) {
            candidate = candidate.Substring(2);
            return ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return TryParseUInt64(candidate, out value) ||
               ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseUInt64(string? text, out ulong value) {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string candidate = text.Trim();
        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("0q", StringComparison.OrdinalIgnoreCase)) {
            return ulong.TryParse(candidate.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
               ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatUInt64(ulong value) {
        return value <= uint.MaxValue ? $"0x{value:X8}" : $"0x{value:X16}";
    }
}

public sealed class XbdmThreadSuspendCommand : AsyncCommand<XbdmThreadSuspendCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [LocalizedDescription("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            CliValidationOutput.Write(
                settings,
                "Thread suspend validation failed",
                "--id is required.",
                "XBDM_THREAD_CONTROL_VALIDATION_FAILED",
                "Provide a live thread ID in hexadecimal or decimal form with --id.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SuspendThreadAsync(threadId, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "thread suspend",
                    ThreadId = threadId,
                    ThreadIdHex = $"0x{threadId:X8}",
                    Acknowledged = true
                });
            }
            else {
                AnsiConsole.MarkupLine($"[green]Suspended thread[/] 0x{threadId:X8}");
            }
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmThreadResumeCommand : AsyncCommand<XbdmThreadResumeCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [LocalizedDescription("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            CliValidationOutput.Write(
                settings,
                "Thread resume validation failed",
                "--id is required.",
                "XBDM_THREAD_CONTROL_VALIDATION_FAILED",
                "Provide a live thread ID in hexadecimal or decimal form with --id.");
            return 1;
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.ResumeThreadAsync(threadId, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "thread resume",
                    ThreadId = threadId,
                    ThreadIdHex = $"0x{threadId:X8}",
                    Acknowledged = true
                });
            }
            else {
                AnsiConsole.MarkupLine($"[green]Resumed thread[/] 0x{threadId:X8}");
            }
            return 0;
        }, CancellationToken.None);
    }
}

