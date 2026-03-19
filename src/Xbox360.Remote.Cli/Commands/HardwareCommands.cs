using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal enum RingLedColor {
    Off = 0x00,
    Red = 0x08,
    Green = 0x80,
    Orange = 0x88
}

internal static class HardwareHelpers {
    private const int HalSendSmcMessageOrdinal = 0x29;

    public static string DescribeSignInState(uint? state) {
        return state switch {
            null => "unknown",
            0 => "Not signed in",
            1 => "Signed in locally",
            2 => "Signed in to Xbox Live",
            _ => $"Unknown (0x{state.Value:X})"
        };
    }

    public static async Task<string> GetSmcVersionAsync(XbdmClient client, CancellationToken cancellationToken) {
        byte[] reply = await SendSmcMessageAsync(client, new byte[] { 0x12 }, cancellationToken);
        if (reply.Length < 4)
            throw new IOException("SMC did not return a version payload.");
        return $"{reply[2]}.{reply[3]}";
    }

    public static async Task SetFanSpeedAsync(XbdmClient client, string channel, int speedPercent, CancellationToken cancellationToken) {
        int speed = Math.Clamp(speedPercent, 10, 100);
        byte encoded = speed < 45 ? (byte) 0x7F : (byte) (speed | 0x80);

        if (channel.Equals("both", StringComparison.OrdinalIgnoreCase) ||
            channel.Equals("primary", StringComparison.OrdinalIgnoreCase)) {
            await DispatchSmcMessageAsync(client, new byte[] { 0x89, encoded }, cancellationToken);
        }

        if (channel.Equals("both", StringComparison.OrdinalIgnoreCase) ||
            channel.Equals("secondary", StringComparison.OrdinalIgnoreCase)) {
            await DispatchSmcMessageAsync(client, new byte[] { 0x94, encoded }, cancellationToken);
        }
    }

    public static async Task SetLedsAsync(
        XbdmClient client,
        RingLedColor topLeft,
        RingLedColor topRight,
        RingLedColor bottomLeft,
        RingLedColor bottomRight,
        CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        await jrpc.SetLedsAsync((int) topLeft, (int) topRight, (int) bottomLeft, (int) bottomRight, cancellationToken);
    }

    public static string FormatLedSummary(CliConfig.LedStateInfo state) {
        return $"TL={state.TopLeft}, TR={state.TopRight}, BL={state.BottomLeft}, BR={state.BottomRight}";
    }

    public static bool TryParseLedColor(string? value, out RingLedColor color) {
        color = RingLedColor.Off;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant()) {
            case "off":
            case "black":
                color = RingLedColor.Off;
                return true;
            case "red":
                color = RingLedColor.Red;
                return true;
            case "green":
                color = RingLedColor.Green;
                return true;
            case "orange":
            case "amber":
            case "yellow":
                color = RingLedColor.Orange;
                return true;
            default:
                return false;
        }
    }

    public static bool TryResolveLedState(
        CliConfig.LedStateInfo? cached,
        string? preset,
        string? topLeft,
        string? topRight,
        string? bottomLeft,
        string? bottomRight,
        out CliConfig.LedStateInfo state,
        out string? error) {
        error = null;
        state = new CliConfig.LedStateInfo();

        if (!string.IsNullOrWhiteSpace(preset)) {
            if (!TryGetPreset(preset, out state)) {
                error = "Unknown preset. Use all-green, all-red, all-orange, all-off, quadrant1, quadrant2, quadrant3, or quadrant4.";
                return false;
            }
        }
        else {
            state = new CliConfig.LedStateInfo {
                TopLeft = cached?.TopLeft ?? "off",
                TopRight = cached?.TopRight ?? "off",
                BottomLeft = cached?.BottomLeft ?? "off",
                BottomRight = cached?.BottomRight ?? "off"
            };
        }

        if (!string.IsNullOrWhiteSpace(topLeft))
            state.TopLeft = topLeft;
        if (!string.IsNullOrWhiteSpace(topRight))
            state.TopRight = topRight;
        if (!string.IsNullOrWhiteSpace(bottomLeft))
            state.BottomLeft = bottomLeft;
        if (!string.IsNullOrWhiteSpace(bottomRight))
            state.BottomRight = bottomRight;

        if (!TryParseLedColor(state.TopLeft, out _) ||
            !TryParseLedColor(state.TopRight, out _) ||
            !TryParseLedColor(state.BottomLeft, out _) ||
            !TryParseLedColor(state.BottomRight, out _)) {
            error = "Invalid LED color. Use off, green, red, or orange.";
            return false;
        }

        state.UpdatedUtc = DateTimeOffset.UtcNow;
        return true;
    }

    public static void SaveLedState(CliConfig.LedStateInfo state) {
        CliConfig config = CliConfig.Load();
        config.LastLedState = state;
        config.Save();
    }

    public static void SaveFanState(int speedPercent, string channel) {
        CliConfig config = CliConfig.Load();
        config.LastFanState = new CliConfig.FanStateInfo {
            SpeedPercent = speedPercent,
            Channel = channel,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        config.Save();
    }

    public static async Task<ProfileHelpers.XamUserInfo?> TryGetSignedInUserAsync(XbdmClient client, CancellationToken cancellationToken) {
        return await ProfileHelpers.TryGetSignedInXamUserAsync(client, cancellationToken);
    }

    private static bool TryGetPreset(string preset, out CliConfig.LedStateInfo state) {
        string key = preset.Trim().ToLowerInvariant();
        state = key switch {
            "all-green" => NewLedState("all-green", "green", "green", "green", "green"),
            "all-red" => NewLedState("all-red", "red", "red", "red", "red"),
            "all-orange" => NewLedState("all-orange", "orange", "orange", "orange", "orange"),
            "all-off" => NewLedState("all-off", "off", "off", "off", "off"),
            "quadrant1" or "player1" => NewLedState("quadrant1", "green", "off", "off", "off"),
            "quadrant2" or "player2" => NewLedState("quadrant2", "off", "green", "off", "off"),
            "quadrant3" or "player3" => NewLedState("quadrant3", "off", "off", "green", "off"),
            "quadrant4" or "player4" => NewLedState("quadrant4", "off", "off", "off", "green"),
            _ => new CliConfig.LedStateInfo()
        };

        return !string.IsNullOrWhiteSpace(state.TopLeft);
    }

    private static CliConfig.LedStateInfo NewLedState(string preset, string tl, string tr, string bl, string br) {
        return new CliConfig.LedStateInfo {
            Preset = preset,
            TopLeft = tl,
            TopRight = tr,
            BottomLeft = bl,
            BottomRight = br,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static async Task<byte[]> SendSmcMessageAsync(XbdmClient client, byte[] message, CancellationToken cancellationToken) {
        uint? scratchBase = await FindScratchBaseAsync(client, cancellationToken);
        if (!scratchBase.HasValue)
            throw new IOException("Could not locate a writable scratch region for SMC RPC.");

        uint inputAddress = scratchBase.Value;
        uint outputAddress = scratchBase.Value + 0x20;
        byte[] input = new byte[16];
        byte[] output = new byte[16];
        Buffer.BlockCopy(message, 0, input, 0, Math.Min(16, message.Length));

        await client.WriteMemoryAsync(inputAddress, input, cancellationToken);
        await client.WriteMemoryAsync(outputAddress, output, cancellationToken);

        Jrpc2Client jrpc = new Jrpc2Client(client);
        await jrpc.CallAsync(
            RpcDataType.Void,
            null,
            "xboxkrnl.exe",
            HalSendSmcMessageOrdinal,
            true,
            false,
            new[] {
                new RpcArgument(RpcArgType.UInt, inputAddress),
                new RpcArgument(RpcArgType.UInt, outputAddress)
            },
            maxLoopCount: 120,
            loopDelayMs: 250,
            cancellationToken);

        return await client.ReadMemoryBytesAsync(outputAddress, 16, cancellationToken);
    }

    private static async Task DispatchSmcMessageAsync(XbdmClient client, byte[] message, CancellationToken cancellationToken) {
        byte[] payload = new byte[16];
        Buffer.BlockCopy(message, 0, payload, 0, Math.Min(16, message.Length));

        Jrpc2Client jrpc = new Jrpc2Client(client);
        await jrpc.DispatchAsync(
            RpcDataType.Void,
            null,
            "xboxkrnl.exe",
            HalSendSmcMessageOrdinal,
            true,
            false,
            new[] { new RpcArgument(RpcArgType.Bytes, payload) },
            cancellationToken);
    }

    private static async Task<uint?> FindScratchBaseAsync(XbdmClient client, CancellationToken cancellationToken) {
        uint[] preferred = {
            0x30000000,
            0x30000100,
            0x30001000
        };

        foreach (uint candidate in preferred) {
            try {
                byte[] probe = await client.ReadMemoryBytesAsync(candidate, 0x20, cancellationToken);
                if (probe.Length == 0x20)
                    return candidate;
            }
            catch {
                // ignored
            }
        }

        IReadOnlyList<XbdmMemoryRegion> regions = await client.GetMemoryRegionsAsync(cancellationToken);
        IEnumerable<XbdmMemoryRegion> candidates = regions
            .Where(r => r.Protect == 4 && r.Size >= 0x40)
            .OrderBy(r => Math.Abs((long) r.BaseAddress - 0x30000000L));

        foreach (XbdmMemoryRegion region in candidates) {
            try {
                byte[] probe = await client.ReadMemoryBytesAsync(region.BaseAddress, 0x20, cancellationToken);
                if (probe.Length == 0x20)
                    return region.BaseAddress;
            }
            catch {
                // ignored
            }
        }

        return null;
    }
}

public sealed class SmcVersionCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
            string version = await HardwareHelpers.GetSmcVersionAsync(client, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new { SmcVersion = version });
                return 0;
            }

            OperationFeedback.WriteSuccess("SMC version", $"[deepskyblue1]{Markup.Escape(version)}[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FanSetCommand : AsyncCommand<FanSetCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--speed <PERCENT>")]
        [Description("Manual fan speed percentage (10-100).")]
        public int? SpeedPercent { get; init; }

        [CommandOption("--channel <CHANNEL>")]
        [Description("primary|secondary|both (default: both).")]
        public string? Channel { get; init; }

        [CommandOption("--notify")]
        [Description("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [Description("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [Description("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!settings.SpeedPercent.HasValue) {
            AnsiConsole.MarkupLine("[red]--speed is required.[/]");
            return 1;
        }

        string channel = string.IsNullOrWhiteSpace(settings.Channel) ? "both" : settings.Channel.Trim().ToLowerInvariant();
        if (channel is not ("primary" or "secondary" or "both")) {
            AnsiConsole.MarkupLine("[red]--channel must be primary, secondary, or both.[/]");
            return 1;
        }

        int speed = Math.Clamp(settings.SpeedPercent.Value, 10, 100);
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
            await HardwareHelpers.SetFanSpeedAsync(client, channel, speed, cts.Token);
            HardwareHelpers.SaveFanState(speed, channel);

            if (settings.Json) {
                CliOutput.EmitJson(new { SpeedPercent = speed, Channel = channel, Source = "manual" });
                return 0;
            }

            OperationFeedback.WriteSuccess(
                "Fan command sent",
                $"[green]{speed}%[/] [grey]requested for[/] [deepskyblue1]{Markup.Escape(channel)}[/]");
            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class FanShowCommand : Command<FanShowCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [Description("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        if (config.LastFanState == null) {
            AnsiConsole.MarkupLine("[yellow]No cached fan state yet. Use `rgh fan set` first.[/]");
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(config.LastFanState);
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
        table.AddRow("[white]Source[/]", "[gold1]Last XeCLI-applied manual setting[/]");
        table.AddRow("[white]Speed[/]", $"[springgreen3_1]{config.LastFanState.SpeedPercent}%[/]");
        table.AddRow("[white]Channel[/]", $"[deepskyblue1]{Markup.Escape(config.LastFanState.Channel ?? "both")}[/]");
        table.AddRow("[white]Updated[/]", CliOutput.FormatTimestamp(config.LastFanState.UpdatedUtc.UtcDateTime));
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class LedSetCommand : AsyncCommand<LedSetCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--preset <NAME>")]
        [Description("all-green|all-red|all-orange|all-off|quadrant1|quadrant2|quadrant3|quadrant4")]
        public string? Preset { get; init; }

        [CommandOption("--tl <COLOR>")]
        [Description("Top-left color: off|green|red|orange")]
        public string? TopLeft { get; init; }

        [CommandOption("--tr <COLOR>")]
        [Description("Top-right color: off|green|red|orange")]
        public string? TopRight { get; init; }

        [CommandOption("--bl <COLOR>")]
        [Description("Bottom-left color: off|green|red|orange")]
        public string? BottomLeft { get; init; }

        [CommandOption("--br <COLOR>")]
        [Description("Bottom-right color: off|green|red|orange")]
        public string? BottomRight { get; init; }

        [CommandOption("--notify")]
        [Description("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [Description("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [Description("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        if (!HardwareHelpers.TryResolveLedState(
                config.LastLedState,
                settings.Preset,
                settings.TopLeft,
                settings.TopRight,
                settings.BottomLeft,
                settings.BottomRight,
                out CliConfig.LedStateInfo state,
                out string? error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid LED settings.")}[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            HardwareHelpers.TryParseLedColor(state.TopLeft, out RingLedColor tl);
            HardwareHelpers.TryParseLedColor(state.TopRight, out RingLedColor tr);
            HardwareHelpers.TryParseLedColor(state.BottomLeft, out RingLedColor bl);
            HardwareHelpers.TryParseLedColor(state.BottomRight, out RingLedColor br);
            await HardwareHelpers.SetLedsAsync(client, tl, tr, bl, br, CancellationToken.None);
            HardwareHelpers.SaveLedState(state);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    state.Preset,
                    state.TopLeft,
                    state.TopRight,
                    state.BottomLeft,
                    state.BottomRight
                });
                return 0;
            }

            OperationFeedback.WriteSuccess(
                "Ring light updated",
                $"[green]{Markup.Escape(HardwareHelpers.FormatLedSummary(state))}[/]");
            await NotifyHelpers.TrySendOperationNotificationAsync(
                client,
                settings.Notify,
                settings.NotifyIcon,
                settings.NotifyLogo,
                "Success :)",
                CancellationToken.None);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class LedStateCommand : Command<LedStateCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--json")]
        [Description("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        CliConfig config = CliConfig.Load();
        if (config.LastLedState == null) {
            AnsiConsole.MarkupLine("[yellow]No cached LED state yet. Use `rgh led set` first.[/]");
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(config.LastLedState);
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
        table.AddRow("[white]Source[/]", "[gold1]Last XeCLI-applied ring-light state[/]");
        table.AddRow("[white]Preset[/]", $"[deepskyblue1]{Markup.Escape(config.LastLedState.Preset ?? "custom")}[/]");
        table.AddRow("[white]Top Left[/]", $"[springgreen3_1]{Markup.Escape(config.LastLedState.TopLeft ?? "unknown")}[/]");
        table.AddRow("[white]Top Right[/]", $"[springgreen3_1]{Markup.Escape(config.LastLedState.TopRight ?? "unknown")}[/]");
        table.AddRow("[white]Bottom Left[/]", $"[springgreen3_1]{Markup.Escape(config.LastLedState.BottomLeft ?? "unknown")}[/]");
        table.AddRow("[white]Bottom Right[/]", $"[springgreen3_1]{Markup.Escape(config.LastLedState.BottomRight ?? "unknown")}[/]");
        table.AddRow("[white]Updated[/]", CliOutput.FormatTimestamp(config.LastLedState.UpdatedUtc.UtcDateTime));
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class SignInStateCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            ProfileHelpers.XamUserInfo? user = await HardwareHelpers.TryGetSignedInUserAsync(client, CancellationToken.None);
            bool signedIn = user != null && user.SignInState > 0;
            string signInState = HardwareHelpers.DescribeSignInState(user?.SignInState);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    SignedIn = signedIn,
                    SignInState = signInState,
                    Slot = user?.Slot,
                    Gamertag = user?.Gamertag,
                    Xuid = user?.Xuid
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
            table.AddRow("[white]Signed In[/]", signedIn ? "[springgreen3_1]Yes[/]" : "[red1]No[/]");
            table.AddRow("[white]State[/]", $"[gold1]{Markup.Escape(signInState)}[/]");
            table.AddRow("[white]Gamertag[/]", signedIn ? $"[springgreen3_1]{Markup.Escape(user?.Gamertag ?? "unknown")}[/]" : "[grey70]none[/]");
            table.AddRow("[white]XUID[/]", signedIn ? $"[gold1]{Markup.Escape(user?.Xuid ?? "unknown")}[/]" : "[grey70]none[/]");
            table.AddRow("[white]Slot[/]", signedIn ? $"[deepskyblue1]{user!.Slot.ToString(CultureInfo.InvariantCulture)}[/]" : "[grey70]-[/]");
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}
