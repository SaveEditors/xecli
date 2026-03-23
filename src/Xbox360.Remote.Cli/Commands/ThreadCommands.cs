using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadsListCommand : AsyncCommand<XbdmThreadsListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--no-names")]
        [Description("Compatibility flag. Thread list now uses start-address metadata instead of thread names.")]
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
        [Description("Thread ID in hex or decimal.")]
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

public sealed class XbdmThreadSuspendCommand : AsyncCommand<XbdmThreadSuspendCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [Description("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            AnsiConsole.MarkupLine("[red]--id is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.SuspendThreadAsync(threadId, cts.Token);
            AnsiConsole.MarkupLine($"[green]Suspended thread[/] 0x{threadId:X8}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmThreadResumeCommand : AsyncCommand<XbdmThreadResumeCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--id <ID>")]
        [Description("Thread ID in hex or decimal.")]
        public string? ThreadId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseThreadId(settings.ThreadId, out uint threadId)) {
            AnsiConsole.MarkupLine("[red]--id is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            await client.ResumeThreadAsync(threadId, cts.Token);
            AnsiConsole.MarkupLine($"[green]Resumed thread[/] 0x{threadId:X8}");
            return 0;
        }, CancellationToken.None);
    }
}
