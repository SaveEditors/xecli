using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2CpuKeyCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            string key = await jrpc.GetCpuKeyAsync(CancellationToken.None);
            AnsiConsole.WriteLine(key);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2TempsCommand : AsyncCommand<Jrpc2TempsCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--sensor <SENSOR>")]
        [Description("cpu|gpu|edram|motherboard")]
        public string? Sensor { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);

            SensorType sensor = settings.Sensor?.ToLowerInvariant() switch {
                "cpu" => SensorType.CPU,
                "gpu" => SensorType.GPU,
                "edram" => SensorType.EDRAM,
                "motherboard" => SensorType.MotherBoard,
                _ => SensorType.CPU
            };

            uint temp = await jrpc.GetTemperatureAsync(sensor, CancellationToken.None);
            AnsiConsole.WriteLine(temp.ToString(CultureInfo.InvariantCulture));
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2TitleIdCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
            AnsiConsole.WriteLine($"0x{titleId:X8}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2CallCommand : AsyncCommand<Jrpc2CallCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        public string? Module { get; init; }

        [CommandOption("--ordinal <ORD>")]
        public int? Ordinal { get; init; }

        [CommandOption("--ret <TYPE>")]
        public string? ReturnType { get; init; }

        [CommandOption("--arg <ARG>")]
        public string[] Args { get; init; } = Array.Empty<string>();

        [CommandOption("--vm")]
        public bool Vm { get; init; }

        [CommandOption("--system")]
        public bool SystemThread { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.ReturnType)) {
            AnsiConsole.MarkupLine("[red]--ret is required (int|uint|float|string|byte|u64|void).[/]");
            return 1;
        }

        if (settings.Address == null && string.IsNullOrWhiteSpace(settings.Module)) {
            AnsiConsole.MarkupLine("[red]--addr or --module is required.[/]");
            return 1;
        }

        uint address = 0;
        if (settings.Address != null && !CliHelpers.TryParseUInt32(settings.Address, out address)) {
            AnsiConsole.MarkupLine("[red]Invalid --addr.[/]");
            return 1;
        }

        RpcDataType ret = settings.ReturnType.ToLowerInvariant() switch {
            "int" or "i32" or "uint" or "u32" => RpcDataType.Int,
            "float" or "double" => RpcDataType.Float,
            "string" => RpcDataType.String,
            "byte" => RpcDataType.Byte,
            "u64" or "uint64" or "ulong" or "i64" or "int64" or "long" => RpcDataType.Uint64,
            "void" => RpcDataType.Void,
            _ => RpcDataType.Int
        };

        List<RpcArgument> args = new List<RpcArgument>();
        foreach (string arg in settings.Args) {
            if (!CliHelpers.TryParseRpcArgument(arg, out RpcArgument parsed)) {
                AnsiConsole.MarkupLine($"[red]Invalid arg:[/] {arg}");
                return 1;
            }
            args.Add(parsed);
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint? addrValue = settings.Address != null ? address : null;
            string result = await jrpc.CallAsync(ret, addrValue, settings.Module, settings.Ordinal, settings.SystemThread, settings.Vm, args, CancellationToken.None);
            AnsiConsole.WriteLine(result);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2NotifyCommand : AsyncCommand<Jrpc2NotifyCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[message]")]
        [Description("Notification text.")]
        public string? MessageArgument { get; init; }

        [CommandArgument(1, "[logo]")]
        [Description("Optional icon id or built-in icon name.")]
        public string? LogoArgument { get; init; }

        [CommandOption("--logo <ID>")]
        [Description("Notification logo id or built-in icon name.")]
        public string? Logo { get; init; }

        [CommandOption("--icon <NAME>")]
        [Description("Notification icon preset name from config.")]
        public string? Icon { get; init; }

        [CommandOption("--message <TEXT>")]
        [Description("Notification text.")]
        public string? Message { get; init; }

        [CommandOption("--position <POS>")]
        [Description("Notification position (top|bottom|center|left|right|top-left|top-right|bottom-left|bottom-right).")]
        public string? Position { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? message = !string.IsNullOrWhiteSpace(settings.Message) ? settings.Message : settings.MessageArgument;
        if (string.IsNullOrWhiteSpace(message)) {
            AnsiConsole.MarkupLine("[red]Notification text is required. Use `rgh jrpc2 notify \"text\" 14` or `--message`.[/]");
            return 1;
        }

        string? logoValue = !string.IsNullOrWhiteSpace(settings.Logo) ? settings.Logo : settings.LogoArgument;
        if (!NotifyHelpers.TryResolveLogo(settings.Icon, logoValue, out int logo, out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid notify options.")}[/]");
            return 1;
        }

        if (!NotifyHelpers.TryResolvePosition(settings.Position, out int position, out error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid notify options.")}[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            if (!string.IsNullOrWhiteSpace(settings.Position))
                await jrpc.SetNotificationPositionAsync(position, CancellationToken.None);
            await jrpc.ShowNotificationAsync(logo, message, CancellationToken.None);
            string positionText = string.IsNullOrWhiteSpace(settings.Position)
                ? "[grey]default[/]"
                : $"[aqua]{Markup.Escape(settings.Position.Trim())}[/]";
            AnsiConsole.MarkupLine($"[green]Notification sent.[/] [grey]Icon:[/] [aqua]{Markup.Escape(NotifyHelpers.DescribeLogo(logo))}[/] [grey]Position:[/] {positionText}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2ResolveCommand : AsyncCommand<Jrpc2ResolveCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--module <MODULE>")]
        public string? Module { get; init; }

        [CommandOption("--ordinal <ORD>")]
        public uint? Ordinal { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Module) || !settings.Ordinal.HasValue) {
            AnsiConsole.MarkupLine("[red]--module and --ordinal are required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint address = await jrpc.ResolveFunctionAsync(settings.Module, settings.Ordinal.Value, CancellationToken.None);
            AnsiConsole.WriteLine(address == 0 ? "0x00000000" : $"0x{address:X8}");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2DashboardCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint version = await jrpc.GetDashboardVersionAsync(CancellationToken.None);
            AnsiConsole.WriteLine(version.ToString(CultureInfo.InvariantCulture));
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2MotherboardCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            string type = await jrpc.GetMotherboardTypeAsync(CancellationToken.None);
            AnsiConsole.WriteLine(type);
            return 0;
        }, CancellationToken.None);
    }
}
