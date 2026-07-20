using System.ComponentModel;
using System.Globalization;
using System.IO;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2CpuKeyCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                string key = await jrpc.GetCpuKeyAsync(CancellationToken.None);
                if (settings.Json) {
                    CliOutput.EmitJson(new { CpuKey = key });
                }
                else {
                    AnsiConsole.WriteLine(key);
                }

                return 0;
            }
            catch (XbdmProtocolViolationException ex) {
                return Jrpc2ReadoutHelpers.WriteProtocolFailure(settings.Json, "JRPC2 CPU key", ex);
            }
            catch (IOException) {
                return Jrpc2ReadoutHelpers.WriteReadoutFailure(settings.Json, "JRPC2 CPU key failed", "Failed to read the CPU key from JRPC2.", "JRPC2_CPU_KEY_FAILED");
            }
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2TempsCommand : AsyncCommand<Jrpc2TempsCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--sensor <SENSOR>")]
        [LocalizedDescription("cpu|gpu|edram|motherboard")]
        public string? Sensor { get; init; }
    }

    internal static bool TryParseSensorType(string? value, out SensorType sensor, out string? error) {
        sensor = SensorType.CPU;
        error = null;

        if (value == null)
            return true;

        switch (value.Trim().ToLowerInvariant()) {
            case "cpu":
                sensor = SensorType.CPU;
                return true;
            case "gpu":
                sensor = SensorType.GPU;
                return true;
            case "edram":
                sensor = SensorType.EDRAM;
                return true;
            case "motherboard":
                sensor = SensorType.MotherBoard;
                return true;
            default:
                error = $"Unsupported --sensor value '{value.Trim()}'. Use cpu|gpu|edram|motherboard.";
                return false;
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!TryParseSensorType(settings.Sensor, out SensorType sensor, out string? sensorError)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(sensorError ?? "Invalid --sensor value.")}[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                uint temp = await jrpc.GetTemperatureAsync(sensor, CancellationToken.None);
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Sensor = FormatSensorType(sensor),
                        Temperature = temp,
                        Unit = "Celsius",
                        Source = "JRPC2"
                    });
                }
                else {
                    AnsiConsole.WriteLine(temp.ToString(CultureInfo.InvariantCulture));
                }

                return 0;
            }
            catch (XbdmProtocolViolationException ex) {
                return Jrpc2ReadoutHelpers.WriteProtocolFailure(settings.Json, "JRPC2 temperature", ex);
            }
        }, CancellationToken.None);
    }

    internal static string FormatSensorType(SensorType sensor) {
        return sensor switch {
            SensorType.CPU => "cpu",
            SensorType.GPU => "gpu",
            SensorType.EDRAM => "edram",
            SensorType.MotherBoard => "motherboard",
            _ => sensor.ToString()
        };
    }
}

public sealed class Jrpc2TitleIdCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                uint titleId = await jrpc.GetTitleIdAsync(CancellationToken.None);
                if (settings.Json) {
                    CliOutput.EmitJson(new { TitleId = $"0x{titleId:X8}" });
                }
                else {
                    AnsiConsole.WriteLine($"0x{titleId:X8}");
                }

                return 0;
            }
            catch (XbdmProtocolViolationException ex) {
                return Jrpc2ReadoutHelpers.WriteProtocolFailure(settings.Json, "JRPC2 title ID", ex);
            }
            catch (IOException) {
                return Jrpc2ReadoutHelpers.WriteReadoutFailure(settings.Json, "JRPC2 title ID failed", "Failed to read the title ID from JRPC2.", "JRPC2_TITLE_ID_FAILED");
            }
        }, CancellationToken.None);
    }
}

internal static class Jrpc2ReadoutHelpers {
    public static int WriteProtocolFailure(bool json, string operation, XbdmProtocolViolationException ex) {
        CliErrorEnvelope error = CliErrorReporter.BuildError(operation, ex);
        if (json) {
            CliOutput.EmitJsonError(error);
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
        }

        return 1;
    }

    public static int WriteReadoutFailure(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                new[] {
                    "Verify JRPC2 is enabled on the console.",
                    "Try the readout again once the console is ready."
                }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }

        return 1;
    }

    public static int WriteValidationFailure(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                message,
                code,
                new[] { "Correct the command arguments and retry." }));
        }
        else {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        }
        return 1;
    }
}

public sealed class Jrpc2CallCommand : AsyncCommand<Jrpc2CallCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
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

    internal static bool TryParseReturnType(string? value, out RpcDataType returnType, out string? error) {
        returnType = RpcDataType.Int;
        error = null;

        if (string.IsNullOrWhiteSpace(value)) {
            error = "--ret is required (int|uint|float|string|byte|u64|void).";
            return false;
        }

        switch (value.Trim().ToLowerInvariant()) {
            case "int":
            case "i32":
            case "uint":
            case "u32":
                returnType = RpcDataType.Int;
                return true;
            case "float":
            case "double":
                returnType = RpcDataType.Float;
                return true;
            case "string":
                returnType = RpcDataType.String;
                return true;
            case "byte":
                returnType = RpcDataType.Byte;
                return true;
            case "u64":
            case "uint64":
            case "ulong":
            case "i64":
            case "int64":
            case "long":
                returnType = RpcDataType.Uint64;
                return true;
            case "void":
                returnType = RpcDataType.Void;
                return true;
            default:
                error = $"Unsupported --ret value '{value.Trim()}'. Use int|uint|float|string|byte|u64|void.";
                return false;
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.ReturnType)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", "--ret is required (int|uint|float|string|byte|u64|void).", "JRPC2_CALL_RETURN_TYPE_REQUIRED");
        }

        if (!TryParseReturnType(settings.ReturnType, out RpcDataType ret, out string? retError)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", retError ?? "Invalid --ret value.", "JRPC2_CALL_RETURN_TYPE_INVALID");
        }

        uint address = 0;
        if (settings.Address != null && !CliHelpers.TryParseUInt32(settings.Address, out address)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", "Invalid --addr.", "JRPC2_CALL_ADDRESS_INVALID");
        }

        List<RpcArgument> args = new List<RpcArgument>();
        foreach (string arg in settings.Args) {
            if (!CliHelpers.TryParseRpcArgument(arg, out RpcArgument parsed)) {
                return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", $"Invalid arg: {arg}", "JRPC2_CALL_ARGUMENT_INVALID");
            }
            args.Add(parsed);
        }

        if (!string.IsNullOrWhiteSpace(settings.Module) && !settings.Ordinal.HasValue) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", "--ordinal is required when using --module.", "JRPC2_CALL_ORDINAL_REQUIRED");
        }

        if (settings.Address == null && string.IsNullOrWhiteSpace(settings.Module)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 call failed", "--addr or --module is required.", "JRPC2_CALL_TARGET_REQUIRED");
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint? addrValue = settings.Address != null ? address : null;
            string result = await jrpc.CallAsync(ret, addrValue, settings.Module, settings.Ordinal, settings.SystemThread, settings.Vm, args, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "jrpc2-call",
                    ReturnType = settings.ReturnType.Trim().ToLowerInvariant(),
                    Address = addrValue.HasValue ? $"0x{addrValue.Value:X8}" : null,
                    settings.Module,
                    settings.Ordinal,
                    settings.SystemThread,
                    settings.Vm,
                    ArgumentCount = args.Count,
                    Result = result
                });
                return 0;
            }
            AnsiConsole.WriteLine(result);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2NotifyCommand : AsyncCommand<Jrpc2NotifyCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandArgument(0, "[message]")]
        [LocalizedDescription("Notification text.")]
        public string? MessageArgument { get; init; }

        [CommandArgument(1, "[logo]")]
        [LocalizedDescription("Optional icon id or built-in icon name.")]
        public string? LogoArgument { get; init; }

        [CommandOption("--logo <ID>")]
        [LocalizedDescription("Notification logo id or built-in icon name.")]
        public string? Logo { get; init; }

        [CommandOption("--icon <NAME>")]
        [LocalizedDescription("Notification icon preset name from config.")]
        public string? Icon { get; init; }

        [CommandOption("--message <TEXT>")]
        [LocalizedDescription("Notification text.")]
        public string? Message { get; init; }

        [CommandOption("--position <POS>")]
        [LocalizedDescription("Notification position (top|bottom|center|left|right|top-left|top-right|bottom-left|bottom-right).")]
        public string? Position { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? message = !string.IsNullOrWhiteSpace(settings.Message) ? settings.Message : settings.MessageArgument;
        if (string.IsNullOrWhiteSpace(message)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 notification failed", "Notification text is required. Use `rgh jrpc2 notify \"text\" 14` or `--message`.", "JRPC2_NOTIFY_MESSAGE_REQUIRED");
        }

        if (!NotificationTextPolicy.TryPrepareNotificationMessage(message, out string normalizedMessage, out string? messageError)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 notification failed", messageError ?? "Invalid notification text.", "JRPC2_NOTIFY_MESSAGE_INVALID");
        }

        string? logoValue = !string.IsNullOrWhiteSpace(settings.Logo) ? settings.Logo : settings.LogoArgument;
        if (!NotifyHelpers.TryResolveLogo(settings.Icon, logoValue, out int logo, out string? error)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 notification failed", error ?? "Invalid notify options.", "JRPC2_NOTIFY_LOGO_INVALID");
        }

        if (!NotifyHelpers.TryResolvePosition(settings.Position, out int position, out error)) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 notification failed", error ?? "Invalid notify options.", "JRPC2_NOTIFY_POSITION_INVALID");
        }

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            if (!string.IsNullOrWhiteSpace(settings.Position))
                await jrpc.SetNotificationPositionAsync(position, CancellationToken.None);
            await jrpc.ShowNotificationAsync(logo, normalizedMessage, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "jrpc2-notify",
                    Sent = true,
                    Logo = logo,
                    LogoName = NotifyHelpers.DescribeLogo(logo),
                    Position = string.IsNullOrWhiteSpace(settings.Position) ? "default" : settings.Position.Trim(),
                    MessageLength = normalizedMessage.Length
                });
                return 0;
            }
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
        [LocalizedDescription("Module name. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--ordinal <ORD>")]
        public uint? Ordinal { get; init; }
    }

    internal static string FormatResolvedAddress(uint address) {
        return address == 0 ? "0x00000000" : $"0x{address:X8}";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Module) || !settings.Ordinal.HasValue) {
            return Jrpc2ReadoutHelpers.WriteValidationFailure(settings.Json, "JRPC2 resolve failed", "--module and --ordinal are required.", "JRPC2_RESOLVE_ARGUMENTS_REQUIRED");
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            Jrpc2Client jrpc = new Jrpc2Client(client);
            uint address = await jrpc.ResolveFunctionAsync(settings.Module, settings.Ordinal.Value, CancellationToken.None);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Operation = "jrpc2-resolve",
                    settings.Module,
                    Ordinal = settings.Ordinal.Value,
                    Address = FormatResolvedAddress(address),
                    Resolved = address != 0
                });
                return 0;
            }
            AnsiConsole.WriteLine(FormatResolvedAddress(address));
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2DashboardCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                uint version = await jrpc.GetDashboardVersionAsync(CancellationToken.None);
                if (settings.Json) {
                    CliOutput.EmitJson(new { DashboardVersion = version });
                }
                else {
                    AnsiConsole.WriteLine(version.ToString(CultureInfo.InvariantCulture));
                }

                return 0;
            }
            catch (XbdmProtocolViolationException ex) {
                return Jrpc2ReadoutHelpers.WriteProtocolFailure(settings.Json, "JRPC2 dashboard", ex);
            }
        }, CancellationToken.None);
    }
}

public sealed class Jrpc2MotherboardCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            try {
                Jrpc2Client jrpc = new Jrpc2Client(client);
                string type = await jrpc.GetMotherboardTypeAsync(CancellationToken.None);
                if (settings.Json) {
                    CliOutput.EmitJson(new { Motherboard = type });
                }
                else {
                    AnsiConsole.WriteLine(type);
                }

                return 0;
            }
            catch (XbdmProtocolViolationException ex) {
                return Jrpc2ReadoutHelpers.WriteProtocolFailure(settings.Json, "JRPC2 motherboard", ex);
            }
        }, CancellationToken.None);
    }
}

