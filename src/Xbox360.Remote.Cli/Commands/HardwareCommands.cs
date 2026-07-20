using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
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

internal enum XamShortcutOrdinal {
    OpenTray = 0x60,
    CloseTray = 0x62,
    TurnOffConsole = 0x295
}

internal enum PopupStylePreset : uint {
    None = 0x00,
    Error = 0x01,
    Warning = 0x02,
    Question = 0x03
}

internal sealed record SmcTemperatureSnapshot(
    ushort CpuRaw,
    ushort GpuRaw,
    ushort EdramRaw,
    ushort MotherboardRaw) {
    public double CpuCelsius => CpuRaw / 256.0;
    public double GpuCelsius => GpuRaw / 256.0;
    public double EdramCelsius => EdramRaw / 256.0;
    public double MotherboardCelsius => MotherboardRaw / 256.0;
}

internal sealed class SmcScratchRestorationException : IOException {
    public SmcScratchRestorationException(Exception innerException)
        : base("SMC scratch restoration could not be verified. Stop live work until console memory integrity is checked.", innerException) {
    }
}

internal static class PopupTextPolicy {
    public const int MaxPopupTextLength = 29;

    public static bool TryPrepareMessageBox(
        string? title,
        string? body,
        IReadOnlyList<string>? buttons,
        out string normalizedTitle,
        out string normalizedBody,
        out string[] normalizedButtons,
        out string error) {
        normalizedTitle = string.Empty;
        normalizedBody = string.Empty;
        normalizedButtons = Array.Empty<string>();
        error = string.Empty;

        normalizedTitle = string.IsNullOrWhiteSpace(title) ? "XeCLI" : title.Trim();
        if (!TryValidatePopupText(normalizedTitle, "Popup title", out error))
            return false;

        if (string.IsNullOrWhiteSpace(body)) {
            error = "Popup body is required.";
            return false;
        }

        normalizedBody = body;
        if (!TryValidatePopupText(normalizedBody, "Popup body", out error))
            return false;

        List<string> normalizedButtonList = new List<string>();
        if (buttons != null) {
            foreach (string? button in buttons) {
                if (string.IsNullOrWhiteSpace(button))
                    continue;

                string trimmed = button.Trim();
                if (!TryValidatePopupText(trimmed, "Popup button", out error))
                    return false;

                normalizedButtonList.Add(trimmed);
            }
        }

        if (normalizedButtonList.Count == 0) {
            normalizedButtonList.Add("Continue");
        }

        if (normalizedButtonList.Count > 4) {
            error = "Use between 1 and 4 --button values.";
            return false;
        }

        normalizedButtons = normalizedButtonList.ToArray();
        return true;
    }

    private static bool TryValidatePopupText(string value, string fieldName, out string error) {
        error = string.Empty;

        if (value.Length > MaxPopupTextLength) {
            error = $"{fieldName} must be at most {MaxPopupTextLength} characters.";
            return false;
        }

        foreach (char ch in value) {
            if (char.IsControl(ch)) {
                error = $"{fieldName} cannot contain control characters or newlines.";
                return false;
            }
        }

        return true;
    }
}

internal static class HardwareHelpers {
    private const int HalSendSmcMessageOrdinal = 0x29;
    private const int XamAllocOrdinal = 0x1EA;
    private const int XamShowMessageBoxUiOrdinal = 714;

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
        if ((reply[2] == 0 && reply[3] == 0) || (reply[2] == byte.MaxValue && reply[3] == byte.MaxValue))
            throw new IOException("SMC returned an invalid version sentinel.");
        return $"{reply[2]}.{reply[3]}";
    }

    public static async Task<SmcTemperatureSnapshot> GetSmcTemperaturesAsync(XbdmClient client, CancellationToken cancellationToken) {
        byte[] reply = await SendSmcMessageAsync(client, new byte[] { 0x07 }, cancellationToken);
        return ParseSmcTemperatures(reply);
    }

    internal static SmcTemperatureSnapshot ParseSmcTemperatures(byte[] reply) {
        if (reply.Length < 9)
            throw new IOException("SMC did not return a complete temperature payload.");

        SmcTemperatureSnapshot snapshot = new(
            ReadUInt16LittleEndian(reply, 1),
            ReadUInt16LittleEndian(reply, 3),
            ReadUInt16LittleEndian(reply, 5),
            ReadUInt16LittleEndian(reply, 7));

        ValidateSmcTemperature("CPU", snapshot.CpuCelsius);
        ValidateSmcTemperature("GPU", snapshot.GpuCelsius);
        ValidateSmcTemperature("EDRAM", snapshot.EdramCelsius);
        ValidateSmcTemperature("motherboard", snapshot.MotherboardCelsius);
        return snapshot;
    }

    private static ushort ReadUInt16LittleEndian(byte[] bytes, int offset) {
        return (ushort) (bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static void ValidateSmcTemperature(string sensor, double temperature) {
        if (temperature < Jrpc2Client.MinimumTemperatureCelsius ||
            temperature > Jrpc2Client.MaximumTemperatureCelsius) {
            throw new IOException($"SMC returned an invalid {sensor} temperature: {temperature.ToString("0.00", CultureInfo.InvariantCulture)} Celsius.");
        }
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

    public static async Task ExecuteXamShortcutAsync(XbdmClient client, XamShortcutOrdinal ordinal, CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        await jrpc.DispatchAsync(
            RpcDataType.Void,
            null,
            "xam.xex",
            (int) ordinal,
            false,
            false,
            new[] {
                new RpcArgument(RpcArgType.Int, 0),
                new RpcArgument(RpcArgType.Int, 0),
                new RpcArgument(RpcArgType.Int, 0),
                new RpcArgument(RpcArgType.Int, 0)
            },
            cancellationToken);
    }

    public static string FormatLedSummary(CliConfig.LedStateInfo state) {
        return $"TL={state.TopLeft}, TR={state.TopRight}, BL={state.BottomLeft}, BR={state.BottomRight}";
    }

    public static bool TryParsePopupStylePreset(string? value, out PopupStylePreset preset) {
        preset = PopupStylePreset.None;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant()) {
            case "none":
            case "plain":
            case "clean":
            case "noicon":
            case "no-icon":
                preset = PopupStylePreset.None;
                return true;
            case "error":
            case "alert":
            case "x":
                preset = PopupStylePreset.Error;
                return true;
            case "question":
            case "ask":
                preset = PopupStylePreset.Question;
                return true;
            case "warning":
            case "warn":
            case "caution":
                preset = PopupStylePreset.Warning;
                return true;
            default:
                return false;
        }
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

    public static async Task ShowMessageBoxAsync(
        XbdmClient client,
        string title,
        string body,
        IReadOnlyList<string> buttons,
        uint messageBoxType,
        uint focusedButtonIndex,
        CancellationToken cancellationToken) {
        if (!PopupTextPolicy.TryPrepareMessageBox(title, body, buttons, out string normalizedTitle, out string normalizedBody, out string[] normalizedButtons, out string error))
            throw new ArgumentException(error);

        uint? baseAddress = await TryAllocateXamMemoryAsync(client, EstimateMessageBoxBufferSize(normalizedTitle, normalizedBody, normalizedButtons), cancellationToken);
        if (!baseAddress.HasValue) {
            uint? scratchBase = await FindScratchBaseAsync(client, cancellationToken);
            if (!scratchBase.HasValue)
                throw new IOException("Could not locate writable memory for popup UI.");
            baseAddress = scratchBase.Value + 0x2000;
        }

        RemoteUiBuffer buffer = new RemoteUiBuffer(baseAddress.Value);
        uint titleAddress = buffer.WriteUtf16String(normalizedTitle);
        uint bodyAddress = buffer.WriteUtf16String(normalizedBody);

        List<uint> buttonAddresses = new List<uint>(normalizedButtons.Length);
        foreach (string button in normalizedButtons) {
            buttonAddresses.Add(buffer.WriteUtf16String(button));
        }

        uint buttonArrayAddress = buffer.WritePointerArray(buttonAddresses);
        uint resultAddress = buffer.WriteZeroBlock(0x20, 0x10);
        uint overlappedAddress = buffer.WriteZeroBlock(0x20, 0x10);

        foreach ((uint address, byte[] data) in buffer.GetSegments()) {
            await client.WriteMemoryAsync(address, data, cancellationToken);
        }

        Jrpc2Client jrpc = new Jrpc2Client(client);
        await jrpc.DispatchAsync(
            RpcDataType.Int,
            null,
            "xam.xex",
            XamShowMessageBoxUiOrdinal,
            false,
            false,
            new[] {
                new RpcArgument(RpcArgType.UInt, 0u),
                new RpcArgument(RpcArgType.UInt, titleAddress),
                new RpcArgument(RpcArgType.UInt, bodyAddress),
                new RpcArgument(RpcArgType.UInt, (uint) normalizedButtons.Length),
                new RpcArgument(RpcArgType.UInt, buttonArrayAddress),
                new RpcArgument(RpcArgType.UInt, focusedButtonIndex),
                new RpcArgument(RpcArgType.UInt, messageBoxType),
                new RpcArgument(RpcArgType.UInt, resultAddress),
                new RpcArgument(RpcArgType.UInt, overlappedAddress)
            },
            cancellationToken);
    }

    public static bool TryConfirmHardwareAction(string title, string prompt, bool yes) {
        if (yes)
            return true;

        return ConfirmationHelpers.TryConfirm(
            title,
            prompt,
            autoConfirm: false,
            nonInteractiveDetail: "Interactive confirmation is required. Re-run with --yes in a terminal.");
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
        byte[] originalInput = await client.ReadMemoryBytesAsync(inputAddress, input.Length, cancellationToken);
        byte[] originalOutput = await client.ReadMemoryBytesAsync(outputAddress, output.Length, cancellationToken);
        if (originalInput.Length != input.Length || originalOutput.Length != output.Length)
            throw new IOException("Could not snapshot the complete SMC scratch buffers.");

        byte[]? reply = null;
        Exception? operationException = null;
        try {
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

            reply = await client.ReadMemoryBytesAsync(outputAddress, output.Length, cancellationToken);
        }
        catch (Exception ex) {
            operationException = ex;
        }

        Exception? restorationException = null;
        try {
            using CancellationTokenSource restoreCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.WriteMemoryVerifiedAsync(outputAddress, originalOutput, restoreCts.Token);
            await client.WriteMemoryVerifiedAsync(inputAddress, originalInput, restoreCts.Token);
        }
        catch (Exception ex) {
            restorationException = ex;
        }

        if (restorationException != null) {
            throw new SmcScratchRestorationException(restorationException);
        }

        if (operationException != null)
            ExceptionDispatchInfo.Capture(operationException).Throw();

        if (reply == null || reply.Length != output.Length)
            throw new IOException("SMC did not return a complete reply buffer.");

        return reply;
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

    private static int EstimateMessageBoxBufferSize(string title, string body, IReadOnlyList<string> buttons) {
        int size = 0x80;
        size += AlignInt((title.Length + 1) * 2, 4);
        size += AlignInt((body.Length + 1) * 2, 4);
        size += AlignInt(buttons.Count * sizeof(uint), 4);
        foreach (string button in buttons) {
            size += AlignInt((button.Length + 1) * 2, 4);
        }

        return size;
    }

    private static async Task<uint?> TryAllocateXamMemoryAsync(XbdmClient client, int size, CancellationToken cancellationToken) {
        Jrpc2Client jrpc = new Jrpc2Client(client);
        try {
            string response = await jrpc.CallAsync(
                RpcDataType.Int,
                null,
                "xam.xex",
                XamAllocOrdinal,
                true,
                false,
                new[] { new RpcArgument(RpcArgType.Int, size) },
                cancellationToken);
            return ParseRpcUInt32(response);
        }
        catch {
            return null;
        }
    }

    private static uint ParseRpcUInt32(string value) {
        string text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);
        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex)
            ? hex
            : uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint dec)
                ? dec
                : 0;
    }

    private static int AlignInt(int value, int alignment) {
        int remainder = value % alignment;
        return remainder == 0 ? value : value + (alignment - remainder);
    }

    private sealed class RemoteUiBuffer {
        private readonly List<(uint Address, byte[] Data)> segments = new();
        private uint cursor;

        public RemoteUiBuffer(uint baseAddress) {
            cursor = baseAddress;
        }

        public uint WriteUtf16String(string value) {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value + "\0");
            return WriteBlock(bytes, 2);
        }

        public uint WritePointerArray(IReadOnlyList<uint> pointers) {
            byte[] bytes = new byte[pointers.Count * sizeof(uint)];
            for (int i = 0; i < pointers.Count; i++) {
                WriteUInt32BigEndian(bytes, i * sizeof(uint), pointers[i]);
            }

            return WriteBlock(bytes, 4);
        }

        public uint WriteZeroBlock(int size, int alignment) {
            return WriteBlock(new byte[size], alignment);
        }

        public IReadOnlyList<(uint Address, byte[] Data)> GetSegments() {
            return segments;
        }

        private uint WriteBlock(byte[] data, int alignment) {
            cursor = Align(cursor, (uint) alignment);
            uint address = cursor;
            segments.Add((address, data));
            cursor += (uint) data.Length;
            return address;
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value) {
            buffer[offset] = (byte) (value >> 24);
            buffer[offset + 1] = (byte) (value >> 16);
            buffer[offset + 2] = (byte) (value >> 8);
            buffer[offset + 3] = (byte) value;
        }

        private static uint Align(uint value, uint alignment) {
            uint remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }
    }
}

public sealed class SmcVersionCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
            string version = await HardwareHelpers.GetSmcVersionAsync(client, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    SmcVersion = version,
                    ScratchRestored = true,
                    Operation = "ReadOnlyProbe"
                });
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
        [LocalizedDescription("Manual fan speed percentage (10-100).")]
        public int? SpeedPercent { get; init; }

        [CommandOption("--channel <CHANNEL>")]
        [LocalizedDescription("primary|secondary|both (default: both).")]
        public string? Channel { get; init; }

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
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
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!CliConfig.TryLoad(out CliConfig config)) {
            HardwareCommandFailureHelpers.WriteConfigFailure(settings.Json, "Fan state unavailable", "Config file is present but could not be parsed. Fix or remove config.json, then run rgh fan show again.");
            return 1;
        }

        if (config.LastFanState == null) {
            if (settings.Json) {
                CliOutput.EmitJsonError(new CliErrorEnvelope(
                    "Fan state unavailable",
                    "No cached fan state yet.",
                    "FAN_STATE_UNAVAILABLE",
                    new[] {
                        "Run `rgh fan set --speed <PERCENT>` to save one.",
                        "Then rerun `rgh fan show`."
                    }));
            }
            else {
                AnsiConsole.MarkupLine("[yellow]No cached fan state yet. Run `rgh fan set --speed <PERCENT>` to save one, then rerun `rgh fan show`.[/]");
            }
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
        [LocalizedDescription("all-green|all-red|all-orange|all-off|quadrant1|quadrant2|quadrant3|quadrant4")]
        public string? Preset { get; init; }

        [CommandOption("--tl <COLOR>")]
        [LocalizedDescription("Top-left color: off|green|red|orange")]
        public string? TopLeft { get; init; }

        [CommandOption("--tr <COLOR>")]
        [LocalizedDescription("Top-right color: off|green|red|orange")]
        public string? TopRight { get; init; }

        [CommandOption("--bl <COLOR>")]
        [LocalizedDescription("Bottom-left color: off|green|red|orange")]
        public string? BottomLeft { get; init; }

        [CommandOption("--br <COLOR>")]
        [LocalizedDescription("Bottom-right color: off|green|red|orange")]
        public string? BottomRight { get; init; }

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
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
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!CliConfig.TryLoad(out CliConfig config)) {
            HardwareCommandFailureHelpers.WriteConfigFailure(settings.Json, "LED state unavailable", "Config file is present but could not be parsed. Fix or remove config.json, then run rgh led state again.");
            return 1;
        }

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
        (string ip, int port, int timeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
        return await CliHelpers.WithClientAsync((ip, port, timeout), settings, async client => {
            ProfileHelpers.ResolvedIdentityInfo identity = await ProfileHelpers.ResolveSignedInIdentityAsync(
                client,
                ip,
                port,
                timeout,
                CliConfig.Load(),
                allowF3: true,
                allowProfilePackage: true,
                CancellationToken.None);
            bool signedIn = identity.IsSignedIn;
            string signInState = identity.SignInStateText;

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    SignedIn = signedIn,
                    SignInState = signInState,
                    Slot = identity.Slot,
                    Gamertag = identity.Gamertag,
                    Xuid = identity.Xuid,
                    Source = identity.Source
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
            table.AddRow("[white]Signed In[/]", signedIn ? "[springgreen3_1]Yes[/]" : "[red1]No[/]");
            table.AddRow("[white]State[/]", $"[gold1]{Markup.Escape(signInState)}[/]");
            table.AddRow("[white]Gamertag[/]", signedIn ? $"[springgreen3_1]{Markup.Escape(identity.Gamertag ?? "unknown")}[/]" : "[grey70]none[/]");
            table.AddRow("[white]XUID[/]", signedIn ? $"[gold1]{Markup.Escape(identity.Xuid ?? "unknown")}[/]" : "[grey70]none[/]");
            table.AddRow("[white]Slot[/]", identity.Slot.HasValue ? $"[deepskyblue1]{identity.Slot.Value.ToString(CultureInfo.InvariantCulture)}[/]" : "[grey70]-[/]");
            if (!string.IsNullOrWhiteSpace(identity.Source))
                table.AddRow("[white]Source[/]", $"[mediumpurple3]{Markup.Escape(identity.Source)}[/]");
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class SmcTemperaturesCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 30000));
            SmcTemperatureSnapshot temperatures = await HardwareHelpers.GetSmcTemperaturesAsync(client, cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Source = "SMC",
                    CpuCelsius = temperatures.CpuCelsius,
                    GpuCelsius = temperatures.GpuCelsius,
                    EdramCelsius = temperatures.EdramCelsius,
                    MotherboardCelsius = temperatures.MotherboardCelsius,
                    Raw = new {
                        Cpu = temperatures.CpuRaw,
                        Gpu = temperatures.GpuRaw,
                        Edram = temperatures.EdramRaw,
                        Motherboard = temperatures.MotherboardRaw
                    },
                    ScratchRestored = true,
                    Operation = "ReadOnlyProbe"
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn("Sensor");
            table.AddColumn("Temperature");
            table.AddRow("CPU", $"[cyan]{temperatures.CpuCelsius.ToString("0.00", CultureInfo.InvariantCulture)} C[/]");
            table.AddRow("GPU", $"[cyan]{temperatures.GpuCelsius.ToString("0.00", CultureInfo.InvariantCulture)} C[/]");
            table.AddRow("EDRAM", $"[cyan]{temperatures.EdramCelsius.ToString("0.00", CultureInfo.InvariantCulture)} C[/]");
            table.AddRow("Motherboard", $"[cyan]{temperatures.MotherboardCelsius.ToString("0.00", CultureInfo.InvariantCulture)} C[/]");
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

internal static class HardwareCommandFailureHelpers {
    public static void WriteConfigFailure(bool json, string title, string detail) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                detail,
                "HARDWARE_CONFIG_INVALID",
                new[] {
                    "Fix or remove config.json, then run the command again.",
                    "Run rgh health to inspect local configuration issues."
                }));
            return;
        }

        OperationFeedback.WriteFailure(title, detail);
    }
}

public sealed class TrayOpenCommand : AsyncCommand<TrayOpenCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the tray action without connecting or writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--yes")]
        [LocalizedDescription("Skip the tray action confirmation prompt.")]
        public bool Yes { get; init; }

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new { Tray = "open", Status = "dry-run" });
                return 0;
            }

            OperationFeedback.WriteWarning("Disc tray open preview", "Would send an open-tray request to the console.");
            return 0;
        }

        if (!HardwareHelpers.TryConfirmHardwareAction("Disc tray open", "Open the disc tray?", settings.Yes))
            return 1;

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 15000));
            await HardwareHelpers.ExecuteXamShortcutAsync(client, XamShortcutOrdinal.OpenTray, cts.Token);

            if (settings.Json) {
                CliOutput.EmitJson(new { Tray = "open", Status = "requested" });
                return 0;
            }

            OperationFeedback.WriteSuccess("Disc tray opened", "[deepskyblue1]Open request sent to the console.[/]");
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

public sealed class TrayCloseCommand : AsyncCommand<TrayCloseCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the tray action without connecting or writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--yes")]
        [LocalizedDescription("Skip the tray action confirmation prompt.")]
        public bool Yes { get; init; }

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new { Tray = "close", Status = "dry-run" });
                return 0;
            }

            OperationFeedback.WriteWarning("Disc tray close preview", "Would send a close-tray request to the console.");
            return 0;
        }

        if (!HardwareHelpers.TryConfirmHardwareAction("Disc tray close", "Close the disc tray?", settings.Yes))
            return 1;

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 15000));
            await HardwareHelpers.ExecuteXamShortcutAsync(client, XamShortcutOrdinal.CloseTray, cts.Token);

            if (settings.Json) {
                CliOutput.EmitJson(new { Tray = "close", Status = "requested" });
                return 0;
            }

            OperationFeedback.WriteSuccess("Disc tray closed", "[deepskyblue1]Close request sent to the console.[/]");
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

public sealed class ShutdownCommand : AsyncCommand<ShutdownCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the shutdown request without connecting or writing anything.")]
        public bool DryRun { get; init; }

        [CommandOption("--yes")]
        [LocalizedDescription("Skip the shutdown confirmation prompt.")]
        public bool Yes { get; init; }

        [CommandOption("--notify")]
        [LocalizedDescription("Send a default success notification to the console before shutdown.")]
        public bool Notify { get; init; }

        [CommandOption("--notify-icon <NAME>")]
        [LocalizedDescription("Notification icon preset name.")]
        public string? NotifyIcon { get; init; }

        [CommandOption("--notify-logo <ID>")]
        [LocalizedDescription("Notification logo id (decimal or 0x hex).")]
        public string? NotifyLogo { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (settings.DryRun) {
            if (settings.Json) {
                CliOutput.EmitJson(new { Power = "off", Status = "dry-run" });
                return 0;
            }

            OperationFeedback.WriteWarning("Shutdown preview", "Would send a power-off request to the console.");
            return 0;
        }

        if (!HardwareHelpers.TryConfirmHardwareAction("Shutdown", "Power off the console now?", settings.Yes))
            return 1;

        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            if (settings.Notify) {
                await NotifyHelpers.TrySendOperationNotificationAsync(
                    client,
                    true,
                    settings.NotifyIcon,
                    settings.NotifyLogo,
                    "Success :)",
                    CancellationToken.None);
                await Task.Delay(300, CancellationToken.None);
            }

            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 10000));
            Jrpc2Client jrpc = new Jrpc2Client(client);
            await jrpc.ShutdownAsync(cts.Token);

            if (settings.Json) {
                CliOutput.EmitJson(new { Power = "off", Status = "requested" });
                return 0;
            }

            OperationFeedback.WriteSuccess("Shutdown requested", "[deepskyblue1]Power-off request sent to the console.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class PopupMessageBoxCommand : AsyncCommand<PopupMessageBoxCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--title <TEXT>")]
        [LocalizedDescription("Popup title text.")]
        public string? Title { get; init; }

        [CommandOption("--body <TEXT>")]
        [LocalizedDescription("Popup body text.")]
        public string? Body { get; init; }

        [CommandOption("--button <TEXT>")]
        [LocalizedDescription("Button label. Repeat to add multiple buttons.")]
        public string[]? Buttons { get; init; }

        [CommandOption("--focus <INDEX>")]
        [LocalizedDescription("Focused button index (default: 0).")]
        public uint? FocusedButtonIndex { get; init; }

        [CommandOption("--preset <NAME>")]
        [LocalizedDescription("Popup icon preset: none|error|warning|question (default: none).")]
        public string? Preset { get; init; }

        [CommandOption("--style <ID>")]
        [LocalizedDescription("Raw popup style id. Overrides --preset when provided.")]
        public uint? MessageBoxType { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!PopupTextPolicy.TryPrepareMessageBox(settings.Title, settings.Body, settings.Buttons, out string title, out string body, out string[] buttons, out string error)) {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error ?? "Invalid popup text.")}[/]");
            return 1;
        }
        uint focus = settings.FocusedButtonIndex ?? 0;
        if (focus >= buttons.Length) {
            AnsiConsole.MarkupLine("[red]--focus must point to an existing button index.[/]");
            return 1;
        }

        uint messageBoxType;
        if (settings.MessageBoxType.HasValue) {
            messageBoxType = settings.MessageBoxType.Value;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Preset)) {
            if (!HardwareHelpers.TryParsePopupStylePreset(settings.Preset, out PopupStylePreset preset)) {
                AnsiConsole.MarkupLine("[red]Unknown popup preset. Use none, error, warning, or question.[/]");
                return 1;
            }

            messageBoxType = (uint) preset;
        }
        else {
            messageBoxType = (uint) PopupStylePreset.None;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings, Math.Max(settings.TimeoutMs ?? 5000, 15000));
            await HardwareHelpers.ShowMessageBoxAsync(client, title, body, buttons, messageBoxType, focus, cts.Token);

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Title = title,
                    Body = body,
                    Buttons = buttons,
                    FocusedButtonIndex = focus,
                    MessageBoxType = messageBoxType,
                    Status = "requested"
                });
                return 0;
            }

            OperationFeedback.WriteSuccess(
                "Popup requested",
                $"[deepskyblue1]{Markup.Escape(title)}[/] [grey]with[/] [springgreen3_1]{buttons.Length}[/] [grey]button(s)[/]");
            return 0;
        }, CancellationToken.None);
    }
}

