using System.ComponentModel;
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

public sealed class XbdmDataBreakpointAddCommand : AsyncCommand<XbdmDataBreakpointAddCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        [Description("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("write|read|exec|rw (default write).")]
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
        [Description("Size in bytes (default 4).")]
        public string? Size { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("write|read|exec|rw (default write).")]
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
