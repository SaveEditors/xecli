using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text;
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
            "uint" => "u32",
            "long" => "s64",
            "ulong" => "u64",
            "float" => "f32",
            "double" => "f64",
            "string" => "ascii",
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
                short val = short.Parse(value, CultureInfo.InvariantCulture);
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
                int val = int.Parse(value, CultureInfo.InvariantCulture);
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
                long val = long.Parse(value, CultureInfo.InvariantCulture);
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

    private static uint ParseUInt32(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ulong ParseUInt64(string value) {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ulong.Parse(value, CultureInfo.InvariantCulture);
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

public sealed class XbdmMemRegionsCommand : AsyncCommand<ConnectionSettings> {
    public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings) {
        return await CliHelpers.WithClientAsync(settings, async client => {
            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            IReadOnlyList<XbdmMemoryRegion> regions = await client.GetMemoryRegionsAsync(cts.Token);
            if (settings.Json) {
                CliOutput.EmitJson(regions);
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Memory Regions[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[cyan]Base[/]"));
            table.AddColumn(new TableColumn("[cyan]Size[/]"));
            table.AddColumn(new TableColumn("[grey]Protect[/]"));
            table.AddColumn(new TableColumn("[grey]Phys[/]"));
            foreach (XbdmMemoryRegion region in regions) {
                table.AddRow(
                    $"[cyan]0x{region.BaseAddress:X8}[/]",
                    $"[cyan]0x{region.Size:X8}[/]",
                    $"[grey]0x{region.Protect:X8}[/]",
                    $"[grey]0x{region.Phys:X8}[/]");
            }
            AnsiConsole.Write(table);
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemPeekCommand : AsyncCommand<XbdmMemPeekCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii plus byte/int/float/string aliases")]
        public string? Type { get; init; }

        [CommandOption("--len <N>")]
        [Description("Length for ascii reads (default 32).")]
        public int? Length { get; init; }

        [CommandOption("--le")]
        [Description("Interpret as little-endian (default is big-endian).")]
        public bool LittleEndian { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) || string.IsNullOrWhiteSpace(settings.Type)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type>");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            string type = MemoryValueCodec.NormalizeType(settings.Type);
            int length = type == "ascii" ? Math.Max(1, settings.Length ?? 32) : MemoryValueCodec.GetSize(type);
            if (length <= 0) {
                AnsiConsole.MarkupLine("[red]Unknown type.[/]");
                return 1;
            }

            using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
            byte[] bytes = await client.ReadMemoryBytesAsync(address, length, cts.Token);
            object value = MemoryValueCodec.ParseValue(type, bytes, settings.LittleEndian);
            AnsiConsole.WriteLine(value.ToString() ?? "unknown");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemPokeCommand : AsyncCommand<XbdmMemPokeCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description("u8|u16|u32|u64|s8|s16|s32|s64|f32|f64|ascii|hex plus byte/int/float/string/bytes aliases")]
        public string? Type { get; init; }

        [CommandOption("--value <VALUE>")]
        public string? Value { get; init; }

        [CommandOption("--le")]
        [Description("Write little-endian (default is big-endian).")]
        public bool LittleEndian { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            string.IsNullOrWhiteSpace(settings.Type) ||
            string.IsNullOrWhiteSpace(settings.Value)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --type <type> --value <value>");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            byte[] bytes = MemoryValueCodec.BuildBytes(settings.Type, settings.Value, settings.LittleEndian);
            await client.WriteMemoryAsync(address, bytes, CancellationToken.None);
            AnsiConsole.MarkupLine("[green]Wrote memory.[/]");
            return 0;
        }, CancellationToken.None);
    }
}

public sealed class XbdmMemWatchCommand : AsyncCommand<XbdmMemWatchCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--interval <MS>")]
        [Description("Poll interval in milliseconds (default 500).")]
        public int? IntervalMs { get; init; }

        [CommandOption("--count <N>")]
        [Description("Number of iterations (default 0 = infinite).")]
        public int? Count { get; init; }

        [CommandOption("--clear")]
        [Description("Clear the screen between updates.")]
        public bool Clear { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            !CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
            return 1;
        }

        int interval = Math.Max(50, settings.IntervalMs ?? 500);
        int count = settings.Count ?? 0;

        int iteration = 0;
        while (count == 0 || iteration < count) {
            iteration++;
            await CliHelpers.WithClientAsync(settings, async client => {
                if (settings.Clear)
                    AnsiConsole.Clear();
                AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] 0x{address:X8} ({size} bytes)");
                byte[] data = await client.ReadMemoryBytesReliableAsync(address, checked((int) size), CancellationToken.None);
                CliOutput.RenderHexDump(address, data);
                return 0;
            }, CancellationToken.None);
            await Task.Delay(interval);
        }

        return 0;
    }
}

public sealed class XbdmMemStringsCommand : AsyncCommand<XbdmMemStringsCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--min <N>")]
        [Description("Minimum string length (default 4).")]
        public int? MinLength { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum strings to return (default 200).")]
        public int? MaxCount { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            !CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
            return 1;
        }

        int minLength = Math.Max(2, settings.MinLength ?? 4);
        int maxCount = Math.Max(1, settings.MaxCount ?? 200);

        return await CliHelpers.WithClientAsync(settings, async client => {
            List<(uint Address, string Text)> results = new List<(uint, string)>();
            const int chunkSize = 0x8000;
            byte[]? carry = null;
            uint carryStart = 0;

            ulong remaining = size;
            uint current = address;
            while (remaining > 0 && results.Count < maxCount) {
                int read = (int) Math.Min((ulong) chunkSize, remaining);
                byte[] bytes = await client.ReadMemoryBytesAsync(current, read, CancellationToken.None);
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

            if (settings.Json) {
                CliOutput.EmitJson(results.Select(r => new { Address = $"0x{r.Address:X8}", Text = r.Text }).ToList());
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
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--addr <ADDR>")]
        public string? Address { get; init; }

        [CommandOption("--size <SIZE>")]
        public string? Size { get; init; }

        [CommandOption("--chunk <SIZE>")]
        [Description("Read chunk size (hex or dec, default: 0x4000).")]
        public string? ChunkSize { get; init; }

        [CommandOption("--pattern <HEX>")]
        [Description("Hex pattern, e.g. DEADBEEF or 0xDE AD BE EF.")]
        public string? Pattern { get; init; }

        [CommandOption("--ascii <TEXT>")]
        [Description("ASCII text pattern.")]
        public string? Ascii { get; init; }

        [CommandOption("--max <N>")]
        [Description("Maximum matches to return (default 20).")]
        public int? MaxCount { get; init; }

        [CommandOption("--out <FILE>")]
        [Description("Write the hit list to a file (.json for JSON, otherwise text).")]
        public string? Output { get; init; }

        [CommandOption("--freeze")]
        [Description("Continuously rewrite a value to the selected hit(s). Requires --freeze-type and --freeze-value.")]
        public bool Freeze { get; init; }

        [CommandOption("--freeze-type <TYPE>")]
        [Description("Value type for freeze writes: int|uint|float|string|bytes|u32|f32|ascii|hex, etc.")]
        public string? FreezeType { get; init; }

        [CommandOption("--freeze-value <VALUE>")]
        [Description("Value to freeze to the selected hit(s).")]
        public string? FreezeValue { get; init; }

        [CommandOption("--freeze-interval <MS>")]
        [Description("Freeze write interval in milliseconds (default 250).")]
        public int? FreezeIntervalMs { get; init; }

        [CommandOption("--freeze-count <N>")]
        [Description("Number of freeze write passes (default 0 = until Ctrl+C).")]
        public int? FreezeCount { get; init; }

        [CommandOption("--freeze-all")]
        [Description("Freeze all hits instead of just one selected hit.")]
        public bool FreezeAll { get; init; }

        [CommandOption("--hit <N>")]
        [Description("1-based hit index to freeze when --freeze-all is not used (default 1).")]
        public int? HitIndex { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.Address, out uint address) ||
            !CliHelpers.TryParseUInt32(settings.Size, out uint size)) {
            AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.Pattern) == string.IsNullOrWhiteSpace(settings.Ascii)) {
            AnsiConsole.MarkupLine("[red]Provide either --pattern or --ascii.[/]");
            return 1;
        }

        byte[] pattern = settings.Pattern != null ? MemoryValueCodec.ParseHexPattern(settings.Pattern) : Encoding.ASCII.GetBytes(settings.Ascii!);
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

        return await CliHelpers.WithClientAsync(settings, async client => {
            List<uint> matches = new List<uint>();
            int overlap = pattern.Length - 1;
            byte[] tail = Array.Empty<byte>();

            const uint SingleReadMax = 0x400000;
            if (size <= SingleReadMax) {
                byte[] buffer = await client.ReadMemoryBytesAsync(address, (int) size, CancellationToken.None);
                int searchIndex = 0;
                ReadOnlySpan<byte> span = buffer;
                while (searchIndex < span.Length) {
                    int idx = span.Slice(searchIndex).IndexOf(pattern);
                    if (idx < 0)
                        break;
                    matches.Add(address + (uint) (searchIndex + idx));
                    if (matches.Count >= maxCount)
                        break;
                    searchIndex += idx + 1;
                }
            }
            else {
                ulong remaining = size;
                uint current = address;
                while (remaining > 0 && matches.Count < maxCount) {
                    int read = (int) Math.Min((ulong) chunkSize, remaining);
                    byte[] bytes = await client.ReadMemoryBytesAsync(current, read, CancellationToken.None);
                    byte[] buffer = new byte[tail.Length + bytes.Length];
                    if (tail.Length > 0)
                        Buffer.BlockCopy(tail, 0, buffer, 0, tail.Length);
                    Buffer.BlockCopy(bytes, 0, buffer, tail.Length, bytes.Length);

                    int searchIndex = 0;
                    ReadOnlySpan<byte> span = buffer;
                    while (searchIndex < span.Length) {
                        int idx = span.Slice(searchIndex).IndexOf(pattern);
                        if (idx < 0)
                            break;
                        uint foundAddr = current - (uint) tail.Length + (uint) (searchIndex + idx);
                        matches.Add(foundAddr);
                        if (matches.Count >= maxCount)
                            break;
                        searchIndex += idx + 1;
                    }

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
            if (!string.IsNullOrWhiteSpace(settings.Output)) {
                string outputPath = settings.Output;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
                if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
                    string json = System.Text.Json.JsonSerializer.Serialize(formattedMatches, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(outputPath, json, CancellationToken.None);
                }
                else {
                    await File.WriteAllLinesAsync(outputPath, formattedMatches, CancellationToken.None);
                }
            }

            if (settings.Json) {
                CliOutput.EmitJson(formattedMatches);
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
                    AnsiConsole.MarkupLine($"[green]Saved hit list[/] {Markup.Escape(settings.Output)}");
                }
            }

            if (!settings.Freeze || matches.Count == 0)
                return 0;

            byte[] freezeBytes = MemoryValueCodec.BuildBytes(settings.FreezeType!, settings.FreezeValue!, littleEndian: false);
            List<uint> targets = settings.FreezeAll
                ? matches
                : SelectFreezeTarget(matches, settings.HitIndex ?? 1);

            (string freezeIp, int freezePort, int freezeTimeout) = await CliHelpers.ResolveTargetAsync(settings, CancellationToken.None);
            return await CliHelpers.WithClientAsync((freezeIp, freezePort, freezeTimeout), settings, async freezeClient => {
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
                    AnsiConsole.MarkupLine($"[yellow]Freezing[/] {targets.Count} hit(s) every {interval} ms. Press Ctrl+C to stop.");
                    int pass = 0;
                    while (count == 0 || pass < count) {
                        freezeCts.Token.ThrowIfCancellationRequested();
                        foreach (uint target in targets) {
                            await freezeClient.WriteMemoryAsync(target, freezeBytes, freezeCts.Token);
                        }
                        pass++;
                        if (count > 0 && pass >= count)
                            break;
                        await Task.Delay(interval, freezeCts.Token);
                    }

                    AnsiConsole.MarkupLine("[green]Freeze loop completed.[/]");
                    return 0;
                }
                catch (OperationCanceledException) {
                    AnsiConsole.MarkupLine("[yellow]Freeze loop stopped.[/]");
                    return 0;
                }
                finally {
                    if (handler != null)
                        Console.CancelKeyPress -= handler;
                }
            }, CancellationToken.None);
        }, CancellationToken.None);
    }

    private static List<uint> SelectFreezeTarget(List<uint> matches, int hitIndex) {
        if (hitIndex <= 0)
            throw new InvalidOperationException("--hit must be 1 or greater.");
        if (hitIndex > matches.Count)
            throw new InvalidOperationException($"Requested hit #{hitIndex}, but only {matches.Count} match(es) were found.");
        return new List<uint> { matches[hitIndex - 1] };
    }
}
