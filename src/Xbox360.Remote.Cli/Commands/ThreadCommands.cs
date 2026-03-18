using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmThreadsListCommand : AsyncCommand<XbdmThreadsListCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--no-names")]
        [Description("Skip resolving thread names.")]
        public bool NoNames { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            IReadOnlyList<XbdmThreadInfo> threads = await client.GetThreadsAsync(!settings.NoNames, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(threads);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Threads[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]#[/]").Centered());
            table.AddColumn(new TableColumn("[cyan]ID[/]"));
            table.AddColumn(new TableColumn("[grey]Suspend[/]"));
            table.AddColumn(new TableColumn("[grey]Priority[/]"));
            table.AddColumn(new TableColumn("[grey]CPU[/]"));
            table.AddColumn(new TableColumn("[green]Name[/]"));
            table.AddColumn(new TableColumn("[cyan]Stack Base[/]"));
            table.AddColumn(new TableColumn("[cyan]Stack Limit[/]"));
            int index = 1;
            foreach (XbdmThreadInfo thread in threads) {
                table.AddRow(
                    $"[grey]{index}[/]",
                    $"[cyan]0x{thread.Id:X8}[/]",
                    $"[grey]{thread.SuspendCount}[/]",
                    $"[grey]{thread.Priority}[/]",
                    $"[grey]{thread.CurrentProcessor}[/]",
                    thread.Name != null ? $"[green]{Markup.Escape(thread.Name)}[/]" : "[grey]unknown[/]",
                    $"[cyan]0x{thread.BaseAddress:X8}[/]",
                    $"[cyan]0x{thread.StackLimit:X8}[/]");
                index++;
            }

            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
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
