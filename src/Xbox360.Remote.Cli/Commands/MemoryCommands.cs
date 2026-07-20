using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

internal static class MemoryValueCodec {
    public static string NormalizeType(string? type) {
        return type?.Trim().ToLowerInvariant() switch {
            "byte" => "u8",
            "short" => "s16",
            "ushort" => "u16",
            "int" => "s32",
            "int32" => "s32",
            "int64" => "s64",
            "uint" => "u32",
            "uint32" => "u32",
            "uint64" => "u64",
            "long" => "s64",
            "ulong" => "u64",
            "float" => "f32",
            "float32" => "f32",
            "double" => "f64",
            "string" => "ascii",
            "bytes" => "hex",
            not null => type.Trim().ToLowerInvariant(),
            _ => string.Empty
        };
    }

    public static byte[] BuildSearchBytes(string type, string value, bool littleEndian) {
        return NormalizeSearchType(type) switch {
            "string" => Encoding.UTF8.GetBytes(value),
            "utf16" => Encoding.Unicode.GetBytes(value),
            string normalized => BuildBytes(normalized, value, littleEndian)
        };
    }

    public static string NormalizeSearchType(string? type) {
        return type?.Trim().ToLowerInvariant() switch {
            "byte" => "u8",
            "short" => "s16",
            "ushort" => "u16",
            "int" => "s32",
            "int32" => "s32",
            "int64" => "s64",
            "uint" => "u32",
            "uint32" => "u32",
            "uint64" => "u64",
            "long" => "s64",
            "ulong" => "u64",
            "float" => "f32",
            "float32" => "f32",
            "double" => "f64",
            "string" => "string",
            "utf16" or "utf16le" or "utf-16" or "utf-16le" => "utf16",
            "bytes" => "hex",
            not null => type.Trim().ToLowerInvariant(),
            _ => string.Empty
        };
    }

    public static int GetSize(string type) {
        return NormalizeType(type) switch {
            "u8" or "s8" => 1,
            "u16" or "s16" => 2,
            "u32" or "s32" or "f32" => 4,
            "u64" or "s64" or "f64" => 8,
            "ascii" or "hex" => 0,
            _ => -1
        };
    }

    public static object ParseValue(string type, ReadOnlySpan<byte> bytes, bool littleEndian) {
        return NormalizeType(type) switch {
            "u8" => bytes[0],
            "s8" => (sbyte) bytes[0],
            "u16" => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            "s16" => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes),
            "u32" => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            "s32" => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
            "u64" => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            "s64" => littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes),
            "f32" => BitConverter.Int32BitsToSingle(littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes)),
            "f64" => BitConverter.Int64BitsToDouble(littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes)),
            "ascii" => Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
            "hex" => ConvertToHex(bytes),
            _ => "unknown"
        };
    }

    public static byte[] BuildBytes(string type, string value, bool littleEndian) {
        switch (NormalizeType(type)) {
            case "u8":
                return new[] { byte.Parse(value, CultureInfo.InvariantCulture) };
            case "s8":
                return new[] { (byte) sbyte.Parse(value, CultureInfo.InvariantCulture) };
            case "u16": {
                Span<byte> buffer = stackalloc byte[2];
                ushort val = ParseUInt16(value);
                if (littleEndian)
                    BinaryPrimitives.WriteUInt16LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteUInt16BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "s16": {
                Span<byte> buffer = stackalloc byte[2];
                short val = ParseInt16(value);
                if (littleEndian)
                    BinaryPrimitives.WriteInt16LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteInt16BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "u32": {
                Span<byte> buffer = stackalloc byte[4];
                uint val = ParseUInt32(value);
                if (littleEndian)
                    BinaryPrimitives.WriteUInt32LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "s32": {
                Span<byte> buffer = stackalloc byte[4];
                int val = ParseInt32(value);
                if (littleEndian)
                    BinaryPrimitives.WriteInt32LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteInt32BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "u64": {
                Span<byte> buffer = stackalloc byte[8];
                ulong val = ParseUInt64(value);
                if (littleEndian)
                    BinaryPrimitives.WriteUInt64LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteUInt64BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "s64": {
                Span<byte> buffer = stackalloc byte[8];
                long val = ParseInt64(value);
                if (littleEndian)
                    BinaryPrimitives.WriteInt64LittleEndian(buffer, val);
                else
                    BinaryPrimitives.WriteInt64BigEndian(buffer, val);
                return buffer.ToArray();
            }
            case "f32": {
                Span<byte> buffer = stackalloc byte[4];
                float val = float.Parse(value, CultureInfo.InvariantCulture);
                int bits = BitConverter.SingleToInt32Bits(val);
                if (littleEndian)
                    BinaryPrimitives.WriteInt32LittleEndian(buffer, bits);
                else
                    BinaryPrimitives.WriteInt32BigEndian(buffer, bits);
                return buffer.ToArray();
            }
            case "f64": {
                Span<byte> buffer = stackalloc byte[8];
                double val = double.Parse(value, CultureInfo.InvariantCulture);
                long bits = BitConverter.DoubleToInt64Bits(val);
                if (littleEndian)
                    BinaryPrimitives.WriteInt64LittleEndian(buffer, bits);
                else
                    BinaryPrimitives.WriteInt64BigEndian(buffer, bits);
                return buffer.ToArray();
            }
            case "ascii":
                return Encoding.ASCII.GetBytes(value);
            case "hex":
                return ParseHex(value);
            default:
                throw new InvalidOperationException("Unknown type.");
        }
    }

    public static byte[] ParseHexPattern(string text) {
        string cleaned = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");
        if (cleaned.Length % 2 != 0)
            throw new InvalidOperationException("Hex pattern must have even length.");
        byte[] bytes = new byte[cleaned.Length / 2];
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static ushort ParseUInt16(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ushort.Parse(value, CultureInfo.InvariantCulture);
    }

    private static short ParseInt16(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((short) ushort.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return short.Parse(value, CultureInfo.InvariantCulture);
    }

    private static uint ParseUInt32(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseInt32(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((int) uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ulong ParseUInt64(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ulong.Parse(value, CultureInfo.InvariantCulture);
    }

    private static long ParseInt64(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return unchecked((long) ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return long.Parse(value, CultureInfo.InvariantCulture);
    }

    private static byte[] ParseHex(string hex) {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);
        hex = hex.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (hex.Length % 2 != 0)
            throw new InvalidOperationException("Hex string must have even length.");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++) {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static string ConvertToHex(ReadOnlySpan<byte> data) {
        char[] chars = new char[data.Length * 2];
        for (int i = 0; i < data.Length; i++) {
            byte b = data[i];
            chars[i * 2] = GetHex((byte) (b >> 4));
            chars[i * 2 + 1] = GetHex((byte) (b & 0x0F));
        }

        return new string(chars);
    }

    private static char GetHex(byte value) {
        return (char) (value < 10 ? '0' + value : 'A' + (value - 10));
    }
}

internal static class MemorySearchHelpers {
    public static List<uint> FindMatches(uint baseAddress, ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> pattern, int maxCount) {
        if (pattern.Length == 0)
            throw new ArgumentException("Pattern cannot be empty.", nameof(pattern));
        if (maxCount <= 0)
            return new List<uint>();

        List<uint> matches = new List<uint>();
        int searchIndex = 0;
        while (searchIndex < buffer.Length) {
            int idx = buffer.Slice(searchIndex).IndexOf(pattern);
            if (idx < 0)
                break;

            matches.Add(baseAddress + (uint) (searchIndex + idx));
            if (matches.Count >= maxCount)
                break;

            searchIndex += idx + 1;
        }

        return matches;
    }
}

internal static class MemoryRangeValidationHelpers {
    private const ulong AddressSpaceSize = (ulong) uint.MaxValue + 1UL;

    public static bool ValidateRequestedSize(uint size, ConnectionSettings? settings = null) {
        if (size == 0) {
            WriteFailure(settings, "--size must be greater than zero.");
            return false;
        }

        return true;
    }

    public static bool TryValidateDirectSpan(string? address, uint size, ConnectionSettings? settings = null) {
        if (!CliHelpers.TryParseUInt32(address?.Trim(), out uint directAddress))
            return true;

        return ValidateResolvedSpan(directAddress, size, settings);
    }

    public static bool ValidateResolvedSpan(uint address, uint size, ConnectionSettings? settings = null) {
        if (size == 0) {
            WriteFailure(settings, "--size must be greater than zero.");
            return false;
        }

        if ((ulong) address + size > AddressSpaceSize) {
            WriteFailure(settings, "Requested memory range exceeds the 32-bit address space.");
            return false;
        }

        return true;
    }

    private static void WriteFailure(ConnectionSettings? settings, string message) {
        if (settings != null) {
            CliValidationOutput.Write(
                settings,
                "Memory range validation failed",
                message,
                "XBDM_MEMORY_RANGE_VALIDATION_FAILED",
                "Use a positive size that stays inside the 32-bit console address space.");
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }
}

public abstract class XbdmMemMapCommandBase : AsyncCommand<ConnectionSettings> {
    protected abstract bool EmitMapJson { get; }

    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientOnceAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            IReadOnlyList<XbdmMemoryRegion> regions = await client.GetMemoryRegionsAsync(cts.Token);
            IReadOnlyList<XbdmModuleInfo> modules = Array.Empty<XbdmModuleInfo>();
            try {
                modules = await client.GetModulesAsync(false, cts.Token);
            }
            catch {
                // Best-effort mapping only. The region walk remains the source of truth.
            }

            if (settings.Json) {
                if (EmitMapJson)
                    CliOutput.EmitJson(MemoryMapHelpers.BuildMapJsonPayload(regions, modules));
                else
                    CliOutput.EmitJson(MemoryMapHelpers.BuildRawRegionsJsonPayload(regions));
                return 0;
            }

            MemoryMapHelpers.MemoryMapSnapshot snapshot = MemoryMapHelpers.BuildSnapshot(regions, modules);
            RenderMemoryMap(snapshot);
            return 0;
        }, CancellationToken.None);
    }

    private static void RenderMemoryMap(MemoryMapHelpers.MemoryMapSnapshot snapshot) {
        AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Map[/]").RuleStyle("grey"));

        Table legend = CliOutput.CreateTable();
        legend.AddColumn(new TableColumn("[grey]Start[/]"));
        legend.AddColumn(new TableColumn("[grey]End[/]"));
        legend.AddColumn(new TableColumn("[green]Band[/]"));
        legend.AddColumn(new TableColumn("[yellow]Label[/]"));
        legend.AddColumn(new TableColumn("[grey]Safety[/]"));
        foreach (MemoryMapHelpers.MemoryBandSnapshot band in snapshot.Bands) {
            legend.AddRow(
                $"[grey]{band.Start}[/]",
                $"[grey]{band.End}[/]",
                $"[green]{Markup.Escape(band.Name)}[/]",
                $"[yellow]{Markup.Escape(band.Label)}[/]",
                $"[grey]{Markup.Escape(band.SafetyNote)}[/]");
        }
        AnsiConsole.Write(legend);

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[cyan]Base[/]"));
        table.AddColumn(new TableColumn("[cyan]End[/]"));
        table.AddColumn(new TableColumn("[cyan]Size[/]"));
        table.AddColumn(new TableColumn("[green]Band[/]"));
        table.AddColumn(new TableColumn("[yellow]Label[/]"));
        table.AddColumn(new TableColumn("[grey]Modules[/]"));
        table.AddColumn(new TableColumn("[grey]Protect[/]"));
        table.AddColumn(new TableColumn("[grey]Phys[/]"));
        foreach (MemoryMapHelpers.MemoryRegionSnapshot region in snapshot.Regions) {
            string modulesText = region.Modules.Count == 0
                ? "[grey]-[/]"
                : $"[green]{Markup.Escape(string.Join(", ", region.Modules))}[/]";
            table.AddRow(
                $"[cyan]{region.Base}[/]",
                $"[cyan]{region.End}[/]",
                $"[cyan]{region.Size}[/]",
                $"[green]{Markup.Escape(region.Band)}[/]",
                $"[yellow]{Markup.Escape(region.Label)}[/]",
                modulesText,
                $"[grey]{region.Protect}[/]",
                $"[grey]{region.Phys}[/]");
        }
        AnsiConsole.Write(table);
    }
}

public sealed class XbdmMemRegionsCommand : XbdmMemMapCommandBase {
    protected override bool EmitMapJson => false;
}

public sealed class XbdmMemMapCommand : XbdmMemMapCommandBase {
    protected override bool EmitMapJson => true;
}

public sealed class XbdmMemPeekCommand : AsyncCommand<XbdmMemPeekCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("RVA relative to --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address to translate through --module and --ghidra-base.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base used by Ghidra when passing --ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii plus byte/int/float/string aliases")]
        public string? Type { get; init; }

        [CommandOption("--len <N>")]
        [LocalizedDescription("Length for ascii reads (default 32).")]
        public int? Length { get; init; }

        [CommandOption("--le")]
        [LocalizedDescription("Interpret as little-endian (default is big-endian).")]
        public bool LittleEndian { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings)) {
            if (!settings.Json)
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> or --module <name> --rva <addr> --type <type>");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Type)) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory peek validation failed",
                    "--type is required.",
                    "XBDM_MEMORY_PEEK_VALIDATION_FAILED",
                    "Pass --type with a supported scalar or ASCII type.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> or --module <name> --rva <addr> --type <type>");
            }
            return 1;
        }

        string type = MemoryValueCodec.NormalizeType(settings.Type);
        int length = type == "ascii" ? Math.Max(1, settings.Length ?? 32) : MemoryValueCodec.GetSize(type);
        if (length <= 0) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory peek validation failed",
                    "Unknown --type value.",
                    "XBDM_MEMORY_PEEK_VALIDATION_FAILED",
                    "Use u8, u16, u32, u64, s8, s16, s32, s64, f32, f64, or ascii.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Unknown type.[/]");
            }
            return 1;
        }

        if (!MemoryRangeValidationHelpers.TryValidateDirectSpan(settings.Address, (uint) length, settings))
            return 1;

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint address = resolved.Address;
            if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, (uint) length, settings))
                return 1;

            byte[] bytes = await client.ReadMemoryBytesAsync(address, length, cts.Token);
            object value = MemoryValueCodec.ParseValue(type, bytes, settings.LittleEndian);
            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Address = $"0x{address:X8}",
                    Type = type,
                    Length = bytes.Length,
                    LittleEndian = settings.LittleEndian,
                    BytesHex = Convert.ToHexString(bytes),
                    Value = value,
                    Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                });
                return 0;
            }

            AnsiConsole.WriteLine(value.ToString() ?? "unknown");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemPokeCommand : AsyncCommand<XbdmMemPokeCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("RVA relative to --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address to translate through --module and --ghidra-base.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base used by Ghidra when passing --ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii|hex plus byte/int/float/string/bytes aliases")]
        public string? Type { get; init; }

        [CommandOption("--value <VALUE>")]
        public string? Value { get; init; }

        [CommandOption("--le")]
        [LocalizedDescription("Write little-endian (default is big-endian).")]
        public bool LittleEndian { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings)) {
            if (!settings.Json)
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> --value <value> or --module <name> --rva <addr> --type <type> --value <value>");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Type) || string.IsNullOrWhiteSpace(settings.Value)) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory poke validation failed",
                    "--type and --value are required.",
                    "XBDM_MEMORY_POKE_VALIDATION_FAILED",
                    "Pass a supported --type and the --value to write.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> --value <value> or --module <name> --rva <addr> --type <type> --value <value>");
            }
            return 1;
        }

        byte[] bytes;
        try {
            bytes = MemoryValueCodec.BuildBytes(settings.Type, settings.Value, settings.LittleEndian);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException) {
            CliValidationOutput.Write(
                settings,
                "Memory poke validation failed",
                $"Invalid typed value for --type {settings.Type}: {settings.Value}.",
                "XBDM_MEMORY_POKE_VALIDATION_FAILED",
                "Check the value range and syntax for the selected type.");
            return 1;
        }

        if (!MemoryRangeValidationHelpers.TryValidateDirectSpan(settings.Address, (uint) bytes.Length, settings))
            return 1;

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint address = resolved.Address;
            if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, (uint) bytes.Length, settings))
                return 1;

            try {
                await client.WriteMemoryVerifiedAsync(address, bytes, cts.Token);
                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "mem poke",
                        Address = $"0x{address:X8}",
                        Type = MemoryValueCodec.NormalizeType(settings.Type),
                        Length = bytes.Length,
                        BytesHex = Convert.ToHexString(bytes),
                        Verified = true,
                        Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                    });
                }
                else {
                    AnsiConsole.MarkupLine($"[green]Wrote and verified[/] 0x{address:X8} ({bytes.Length} byte(s))");
                }
                return 0;
            }
            catch (Exception ex) {
                return ReportCommandFailure(settings, "mem poke", CliErrorReporter.BuildError(
                    "mem poke",
                    ex,
                    GetTargetDisplay(settings)));
            }
        }, CancellationToken.None);
    }

    private static int ReportCommandFailure(Settings settings, string operation, CliErrorEnvelope error) {
        if (settings.Json) {
            CliOutput.EmitJsonError(error);
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
        return 1;
    }

    private static string? GetTargetDisplay(Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Ip) || !settings.Port.HasValue)
            return null;
        return $"{settings.Ip}:{settings.Port.Value}";
    }

    private static string? GetTargetDisplay(string? ip, int port) {
        if (string.IsNullOrWhiteSpace(ip))
            return null;
        return $"{ip}:{port}";
    }
}

public sealed class XbdmMemWatchCommand : AsyncCommand<XbdmMemWatchCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("RVA relative to --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address to translate through --module and --ghidra-base.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base used by Ghidra when passing --ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--interval <MS>")]
        [LocalizedDescription("Poll interval in milliseconds (default 500).")]
        public int? IntervalMs { get; init; }

        [CommandOption("--count <N>")]
        [LocalizedDescription("Number of iterations (default 0 = infinite).")]
        public int? Count { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear the screen between updates.")]
        public bool Clear { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        if (!CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory watch validation failed",
                    "--size is required and must be a hexadecimal or decimal integer.",
                    "XBDM_MEMORY_WATCH_VALIDATION_FAILED",
                    "Pass a positive --size value.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> or --module <name> (--rva <addr> | --ghidra <addr> --ghidra-base <addr>) --size <hex|dec>");
            }
            return 1;
        }

        if (!MemoryRangeValidationHelpers.ValidateRequestedSize(size, settings) ||
            !MemoryRangeValidationHelpers.TryValidateDirectSpan(settings.Address, size, settings)) {
            return 1;
        }

        int interval = Math.Max(50, settings.IntervalMs ?? 500);
        int count = settings.Count ?? 0;
        if (count < 0 || (settings.Json && count == 0)) {
            CliValidationOutput.Write(
                settings,
                "Memory watch validation failed",
                settings.Json ? "--json requires a positive --count." : "--count cannot be negative.",
                "XBDM_MEMORY_WATCH_VALIDATION_FAILED",
                "Pass a positive --count for bounded machine-readable output.");
            return 1;
        }

        try {
            return await CliHelpers.WithClientAsync(settings, async client => {
                using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);

                XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
                if (resolved == null)
                    return 1;

                uint address = resolved.Address;
                if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, size, settings))
                    return 1;
                int iteration = 0;
                List<object> samples = new List<object>();
                while (count == 0 || iteration < count) {
                    iteration++;
                    if (settings.Clear && !settings.Json) {
                        try {
                            AnsiConsole.Clear();
                        }
                        catch (IOException) {
                            // Screen clearing is best-effort when no interactive console is attached.
                        }
                        catch (InvalidOperationException) {
                            // Screen clearing is best-effort when no interactive console is attached.
                        }
                    }
                    byte[] data = await client.ReadMemoryBytesReliableAsync(address, checked((int) size), cts.Token);
                    if (settings.Json) {
                        samples.Add(new {
                            Iteration = iteration,
                            TimestampUtc = DateTimeOffset.UtcNow,
                            Address = $"0x{address:X8}",
                            Size = data.Length,
                            DataHex = Convert.ToHexString(data)
                        });
                    }
                    else {
                        AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] 0x{address:X8} ({size} bytes)");
                        CliOutput.RenderHexDump(address, data);
                    }
                    if (count > 0 && iteration >= count)
                        break;

                    await Task.Delay(interval, cts.Token);
                }

                if (settings.Json) {
                    CliOutput.EmitJson(new {
                        Operation = "mem watch",
                        Address = $"0x{address:X8}",
                        Size = size,
                        IntervalMs = interval,
                        Count = iteration,
                        Samples = samples,
                        Resolution = XbdmMemoryJsonOutput.BuildAddressResolutionJson(resolved)
                    });
                }

                return 0;
            }, CancellationToken.None);
        }
        catch (OperationCanceledException) {
            CliValidationOutput.Write(
                settings,
                "Memory watch failed",
                "Memory watch timed out.",
                "XBDM_MEMORY_WATCH_TIMEOUT",
                "Increase --timeout, reduce --count, or reduce the watched size.");
            return 1;
        }
    }
}

public sealed class XbdmMemStringsCommand : AsyncCommand<XbdmMemStringsCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("RVA relative to --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address to translate through --module and --ghidra-base.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base used by Ghidra when passing --ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--min <N>")]
        [LocalizedDescription("Minimum string length (default 4).")]
        public int? MinLength { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum strings to return (default 200).")]
        public int? MaxCount { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write output to a file (.json for JSON, otherwise text).")]
        public string? Output { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        if (!CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            if (settings.Json) {
                CliValidationOutput.Write(
                    settings,
                    "Memory strings validation failed",
                    "--size is required and must be a hexadecimal or decimal integer.",
                    "XBDM_MEMORY_STRINGS_VALIDATION_FAILED",
                    "Pass a positive --size value.");
            }
            else {
                AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> or --module <name> (--rva <addr> | --ghidra <addr> --ghidra-base <addr>) --size <hex|dec>");
            }
            return 1;
        }

        if (!MemoryRangeValidationHelpers.ValidateRequestedSize(size, settings) ||
            !MemoryRangeValidationHelpers.TryValidateDirectSpan(settings.Address, size, settings)) {
            return 1;
        }

        int minLength = Math.Max(2, settings.MinLength ?? 4);
        int maxCount = Math.Max(1, settings.MaxCount ?? 200);

        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
            if (resolved == null)
                return 1;

            uint address = resolved.Address;
            if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, size, settings))
                return 1;
            List<(uint Address, string Text)> results = new List<(uint, string)>();
            const int chunkSize = 0x8000;
            byte[]? carry = null;
            uint carryStart = 0;

            ulong remaining = size;
            uint current = address;
            while (remaining > 0 && results.Count < maxCount) {
                int read = (int) Math.Min((ulong) chunkSize, remaining);
                byte[] bytes = await client.ReadMemoryBytesAsync(current, read, cts.Token);
                int idx = 0;

                if (carry != null && carry.Length > 0) {
                    while (idx < bytes.Length && IsPrintable(bytes[idx])) {
                        Array.Resize(ref carry, carry.Length + 1);
                        carry[^1] = bytes[idx];
                        idx++;
                    }

                    if (carry.Length >= minLength) {
                        results.Add((carryStart, Encoding.ASCII.GetString(carry)));
                    }

                    carry = null;
                }

                int start = -1;
                for (; idx < bytes.Length; idx++) {
                    if (IsPrintable(bytes[idx])) {
                        if (start == -1)
                            start = idx;
                        continue;
                    }

                    if (start != -1) {
                        int length = idx - start;
                        if (length >= minLength) {
                            uint foundAddr = current + (uint) start;
                            results.Add((foundAddr, Encoding.ASCII.GetString(bytes, start, length)));
                            if (results.Count >= maxCount)
                                break;
                        }
                        start = -1;
                    }
                }

                if (results.Count >= maxCount)
                    break;

                if (start != -1 && start < bytes.Length) {
                    int length = bytes.Length - start;
                    carry = new byte[length];
                    Buffer.BlockCopy(bytes, start, carry, 0, length);
                    carryStart = current + (uint) start;
                }

                remaining -= (ulong) read;
                current += (uint) read;
            }

            var jsonResults = results.Select(r => new { Address = $"0x{r.Address:X8}", Text = r.Text }).ToList();

            if (!string.IsNullOrWhiteSpace(settings.Output)) {
                string outputPath = Path.GetFullPath(settings.Output);
                string? directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory)) {
                    Directory.CreateDirectory(directory);
                }

                if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    string json = JsonSerializer.Serialize(jsonResults, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(outputPath, json, cts.Token);
                }
                else {
                    await File.WriteAllLinesAsync(outputPath, results.Select(r => $"0x{r.Address:X8}\t{r.Text}"), cts.Token);
                }

                string displayName = XexInfoCommand.GetDisplayFileName(settings.Output) ?? string.Empty;
                if (!settings.Json) {
                    AnsiConsole.MarkupLine($"[green]Wrote[/] {Markup.Escape(displayName)}");
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(jsonResults);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Strings[/]").RuleStyle("grey"));
            foreach ((uint addrFound, string text) in results) {
                AnsiConsole.MarkupLine($"[grey]0x{addrFound:X8}[/] [green]{Markup.Escape(text)}[/]");
            }

            if (results.Count == 0) {
                AnsiConsole.MarkupLine("[yellow]No strings found.[/]");
            }

            return 0;
        }, CancellationToken.None);
    }

    private static bool IsPrintable(byte value) {
        return value >= 32 && value <= 126;
    }
}

public sealed class XbdmMemFindCommand : AsyncCommand<XbdmMemFindCommand.Settings> {
    public sealed class Settings : ConnectionSettings, IXbdmAddressResolutionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--module <MODULE>")]
        [LocalizedDescription("Loaded module name for live address resolution. Exact full module path is safest; unique leaf/suffix names are accepted when unambiguous.")]
        public string? Module { get; init; }

        [CommandOption("--rva <ADDR>")]
        [LocalizedDescription("RVA relative to --module.")]
        public string? Rva { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address to translate through --module and --ghidra-base.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Image base used by Ghidra when passing --ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--chunk <SIZE>")]
        [LocalizedDescription("Read chunk size (hex or dec, default: 0x4000).")]
        public string? ChunkSize { get; init; }

        [CommandOption("--pattern <HEX>")]
        [LocalizedDescription("Hex pattern, e.g. DEADBEEF or 0xDE AD BE EF.")]
        public string? Pattern { get; init; }

        [CommandOption("--ascii <TEXT>")]
        [LocalizedDescription("ASCII text pattern.")]
        public string? Ascii { get; init; }

        [CommandOption("--type <TYPE>")]
        [LocalizedDescription("Typed value search: u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|int64|ascii|string|utf16|hex plus aliases.")]
        public string? Type { get; init; }

        [CommandOption("--value <VALUE>")]
        [LocalizedDescription("Value for typed search. Used with --type.")]
        public string? Value { get; init; }

        [CommandOption("--little-endian")]
        [LocalizedDescription("Build typed search and freeze values as little-endian (default is big-endian).")]
        public bool LittleEndian { get; init; }

        [CommandOption("--max <N>")]
        [LocalizedDescription("Maximum matches to return (default 20).")]
        public int? MaxCount { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write the hit list to a file (.json for JSON, otherwise text).")]
        public string? Output { get; init; }

        [CommandOption("--freeze")]
        [LocalizedDescription("Continuously rewrite a value to the selected hit(s). Requires --freeze-type and --freeze-value.")]
        public bool Freeze { get; init; }

        [CommandOption("--freeze-type <TYPE>")]
        [LocalizedDescription("Value type for freeze writes: int|uint|float|string|bytes|u32|f32|ascii|hex, etc.")]
        public string? FreezeType { get; init; }

        [CommandOption("--freeze-value <VALUE>")]
        [LocalizedDescription("Value to freeze to the selected hit(s).")]
        public string? FreezeValue { get; init; }

        [CommandOption("--freeze-interval <MS>")]
        [LocalizedDescription("Freeze write interval in milliseconds (default 250).")]
        public int? FreezeIntervalMs { get; init; }

        [CommandOption("--freeze-count <N>")]
        [LocalizedDescription("Number of freeze write passes (default 0 = until Ctrl+C).")]
        public int? FreezeCount { get; init; }

        [CommandOption("--freeze-all")]
        [LocalizedDescription("Freeze all hits instead of just one selected hit.")]
        public bool FreezeAll { get; init; }

        [CommandOption("--hit <N>")]
        [LocalizedDescription("1-based hit index to freeze when --freeze-all is not used (default 1).")]
        public int? HitIndex { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!XbdmAddressResolutionHelpers.ValidateAddressOptions(settings))
            return 1;

        if (!CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            CliValidationOutput.Write(
                settings,
                "Memory find validation failed",
                "--size is required and must be a hexadecimal or decimal integer.",
                "XBDM_MEMORY_FIND_VALIDATION_FAILED",
                "Pass a positive --size value.");
            return 1;
        }

        if (!MemoryRangeValidationHelpers.ValidateRequestedSize(size, settings) ||
            !MemoryRangeValidationHelpers.TryValidateDirectSpan(settings.Address, size, settings)) {
            return 1;
        }

        bool hasPattern = !string.IsNullOrWhiteSpace(settings.Pattern);
        bool hasAscii = !string.IsNullOrWhiteSpace(settings.Ascii);
        bool hasTypedSearch = !string.IsNullOrWhiteSpace(settings.Type) || !string.IsNullOrWhiteSpace(settings.Value);
        int searchModeCount = (hasPattern ? 1 : 0) + (hasAscii ? 1 : 0) + (hasTypedSearch ? 1 : 0);
        if (searchModeCount != 1) {
            AnsiConsole.MarkupLine("[red]Provide exactly one search mode: --pattern, --ascii, or --type with --value.[/]");
            return 1;
        }

        if (hasTypedSearch && (string.IsNullOrWhiteSpace(settings.Type) || string.IsNullOrWhiteSpace(settings.Value))) {
            AnsiConsole.MarkupLine("[red]--type and --value must be used together.[/]");
            return 1;
        }

        byte[] pattern;
        try {
            pattern = hasPattern
                ? MemoryValueCodec.ParseHexPattern(settings.Pattern!)
                : hasAscii
                    ? Encoding.ASCII.GetBytes(settings.Ascii!)
                    : MemoryValueCodec.BuildSearchBytes(settings.Type!, settings.Value!, settings.LittleEndian);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException) {
            AnsiConsole.MarkupLine($"[red]Invalid search value:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (pattern.Length == 0) {
            AnsiConsole.MarkupLine("[red]Pattern cannot be empty.[/]");
            return 1;
        }

        if (settings.Freeze && (string.IsNullOrWhiteSpace(settings.FreezeType) || string.IsNullOrWhiteSpace(settings.FreezeValue))) {
            AnsiConsole.MarkupLine("[red]--freeze requires --freeze-type and --freeze-value.[/]");
            return 1;
        }

        int maxCount = Math.Max(1, settings.MaxCount ?? 20);
        int chunkSize = 0x4000;
        if (!string.IsNullOrWhiteSpace(settings.ChunkSize)) {
            if (!CliHelpers.TryParseUInt32(settings.ChunkSize, out uint chunkParsed)) {
                AnsiConsole.MarkupLine("[red]Invalid --chunk size.[/]");
                return 1;
            }
            chunkSize = (int) Math.Clamp(chunkParsed, 0x200, 0x100000);
        }

        try {
            return await CliHelpers.WithClientAsync(settings, async client => {
                using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
                XbdmResolvedAddress? resolved = await XbdmAddressResolutionHelpers.ResolveAddressAsync(client, settings, cts.Token, printResolution: !settings.Json);
                if (resolved == null)
                    return 1;

                uint address = resolved.Address;
                if (!MemoryRangeValidationHelpers.ValidateResolvedSpan(address, size, settings))
                    return 1;

                List<uint> matches = new List<uint>();
                int overlap = pattern.Length - 1;
                byte[] tail = Array.Empty<byte>();

                const uint SingleReadMax = 0x400000;
                if (size <= SingleReadMax) {
                    byte[] buffer = await client.ReadMemoryBytesAsync(address, (int) size, cts.Token);
                    matches.AddRange(MemorySearchHelpers.FindMatches(address, buffer, pattern, maxCount));
                }
                else {
                    ulong remaining = size;
                    uint current = address;
                    while (remaining > 0 && matches.Count < maxCount) {
                        int read = (int) Math.Min((ulong) chunkSize, remaining);
                        byte[] bytes = await client.ReadMemoryBytesAsync(current, read, cts.Token);
                        byte[] buffer = new byte[tail.Length + bytes.Length];
                        if (tail.Length > 0)
                            Buffer.BlockCopy(tail, 0, buffer, 0, tail.Length);
                        Buffer.BlockCopy(bytes, 0, buffer, tail.Length, bytes.Length);

                        matches.AddRange(MemorySearchHelpers.FindMatches(
                            current - (uint) tail.Length,
                            buffer,
                            pattern,
                            maxCount - matches.Count));

                        if (overlap > 0) {
                            int tailLen = Math.Min(overlap, buffer.Length);
                            tail = new byte[tailLen];
                            Buffer.BlockCopy(buffer, buffer.Length - tailLen, tail, 0, tailLen);
                        }
                        else {
                            tail = Array.Empty<byte>();
                        }

                        remaining -= (ulong) read;
                        current += (uint) read;
                    }
                }

                List<string> formattedMatches = matches.Select(addr => $"0x{addr:X8}").ToList();
                bool shouldDeferJsonForFreeze = settings.Json && settings.Freeze && matches.Count > 0;
                if (!string.IsNullOrWhiteSpace(settings.Output)) {
                    string outputPath = Path.GetFullPath(settings.Output);
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                        string json = System.Text.Json.JsonSerializer.Serialize(formattedMatches, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(outputPath, json, cts.Token);
                    }
                    else {
                        await File.WriteAllLinesAsync(outputPath, formattedMatches, cts.Token);
                    }
                }

                if (settings.Json) {
                    if (!shouldDeferJsonForFreeze) {
                        CliOutput.EmitJson(formattedMatches);
                    }
                }
                else {
                    AnsiConsole.Write(new Rule("[bold deepskyblue1]Matches[/]").RuleStyle("grey"));
                    foreach (uint addrFound in matches) {
                        AnsiConsole.MarkupLine($"[green]0x{addrFound:X8}[/]");
                    }

                    if (matches.Count == 0) {
                        AnsiConsole.MarkupLine("[yellow]No matches found.[/]");
                    }
                    else if (!string.IsNullOrWhiteSpace(settings.Output)) {
                        AnsiConsole.MarkupLine($"[green]Saved hit list[/] {Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.Output) ?? string.Empty)}");
                    }
                }

                if (!settings.Freeze || matches.Count == 0)
                    return 0;

                byte[] freezeBytes;
                List<uint> targets;
                try {
                    freezeBytes = MemoryValueCodec.BuildBytes(settings.FreezeType!, settings.FreezeValue!, settings.LittleEndian);
                    targets = settings.FreezeAll
                        ? matches
                        : SelectFreezeTarget(matches, settings.HitIndex ?? 1);
                }
                catch (Exception ex) {
                    return ReportCommandFailure(settings, "mem find freeze", CliErrorReporter.BuildError(
                        "mem find freeze",
                        ex,
                        null));
                }

                int interval = Math.Max(25, settings.FreezeIntervalMs ?? 250);
                int count = Math.Max(0, settings.FreezeCount ?? 0);
                using CancellationTokenSource freezeCts = new CancellationTokenSource();
                ConsoleCancelEventHandler? handler = null;
                if (!Console.IsInputRedirected) {
                    handler = (_, e) => {
                        e.Cancel = true;
                        freezeCts.Cancel();
                    };
                    Console.CancelKeyPress += handler;
                }

                try {
                    int pass = 0;
                    if (!settings.Json) {
                        AnsiConsole.MarkupLine($"[yellow]Freezing[/] {targets.Count} hit(s) every {interval} ms. Press Ctrl+C to stop.");
                    }

                    while (count == 0 || pass < count) {
                        freezeCts.Token.ThrowIfCancellationRequested();
                        foreach (uint target in targets) {
                            await client.WriteMemoryVerifiedAsync(target, freezeBytes, freezeCts.Token);
                        }
                        pass++;
                        if (count > 0 && pass >= count)
                            break;
                        await Task.Delay(interval, freezeCts.Token);
                    }

                    if (!settings.Json) {
                        AnsiConsole.MarkupLine("[green]Freeze loop completed with verified writes.[/]");
                    }
                    else {
                        CliOutput.EmitJson(formattedMatches);
                    }

                    return 0;
                }
                catch (OperationCanceledException) {
                    if (!settings.Json) {
                        AnsiConsole.MarkupLine("[yellow]Freeze loop stopped.[/]");
                    }
                    return 0;
                }
                catch (Exception ex) {
                    return ReportCommandFailure(
                        settings,
                        "mem find freeze",
                        CliErrorReporter.BuildError(
                            "mem find freeze",
                            ex,
                            GetTargetDisplay(settings.Ip, settings.Port ?? 730)));
                }
                finally {
                    if (handler != null)
                        Console.CancelKeyPress -= handler;
                }
            }, CancellationToken.None);
        }
        catch (OperationCanceledException) {
            CliValidationOutput.Write(
                settings,
                "Memory find failed",
                "Memory search timed out.",
                "XBDM_MEMORY_FIND_TIMEOUT",
                "Increase --timeout or reduce the search range.");
            return 1;
        }
    }

    private static int ReportCommandFailure(Settings settings, string operation, CliErrorEnvelope error) {
        if (settings.Json) {
            CliOutput.EmitJsonError(error);
            return 1;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
        return 1;
    }

    private static string? GetTargetDisplay(string? ip, int port) {
        if (string.IsNullOrWhiteSpace(ip))
            return null;
        return $"{ip}:{port}";
    }

    private static List<uint> SelectFreezeTarget(List<uint> matches, int hitIndex) {
        if (hitIndex <= 0)
            throw new InvalidOperationException("--hit must be 1 or greater.");
        if (hitIndex > matches.Count)
            throw new InvalidOperationException($"Requested hit #{hitIndex}, but only {matches.Count} match(es) were found.");
        return new List<uint> { matches[hitIndex - 1] };
    }
}

