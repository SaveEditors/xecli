using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexMapCommand : AsyncCommand<XexMapCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Input XEX file to map against.")]
        public string? File { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Base address used when importing in Ghidra.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Address copied from Ghidra.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (!CliHelpers.TryParseUInt32(settings.GhidraBase?.Trim(), out uint ghidraBase)) {
            if (WriteJsonError(settings.Json, "XEX map validation failed", "Provide valid --ghidra-base <addr>.", "XEX_MAP_VALIDATION_FAILED"))
                return 1;

            AnsiConsole.MarkupLine("[red]Provide valid --ghidra-base <addr>.[/]");
            return 1;
        }

        if (!CliHelpers.TryParseUInt32(settings.GhidraAddress?.Trim(), out uint ghidraAddress)) {
            if (WriteJsonError(settings.Json, "XEX map validation failed", "Provide valid --ghidra <addr>.", "XEX_MAP_VALIDATION_FAILED"))
                return 1;

            AnsiConsole.MarkupLine("[red]Provide valid --ghidra <addr>.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(settings.File)) {
            if (WriteJsonError(settings.Json, "XEX map validation failed", "Provide --file <path>.", "XEX_MAP_VALIDATION_FAILED"))
                return 1;

            AnsiConsole.MarkupLine("[red]Provide --file <path>.[/]");
            return 1;
        }

        if (!System.IO.File.Exists(settings.File)) {
            if (WriteJsonError(settings.Json, "XEX map file failed", "Input file not found.", "XEX_MAP_FILE_FAILED"))
                return 1;

            AnsiConsole.MarkupLine("[red]Input file not found.[/]");
            return 1;
        }

        XexMetadata metadata;
        try {
            byte[] data = await System.IO.File.ReadAllBytesAsync(settings.File);
            metadata = XexMetadataParser.Parse(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string message = BuildReadFailureMessage(settings.File);
            if (WriteJsonError(settings.Json, "XEX map parse failed", message, "XEX_MAP_PARSE_FAILED"))
                return 1;

            AnsiConsole.MarkupLine($"[red]Failed to read XEX:[/] {Markup.Escape(message)}");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) {
            string message = $"Failed to parse XEX: {SanitizePathLikeMessage(ex.Message, settings.File)}";
            if (WriteJsonError(settings.Json, "XEX map parse failed", message, "XEX_MAP_PARSE_FAILED"))
                return 1;

            AnsiConsole.MarkupLine($"[red]Failed to parse XEX:[/] {Markup.Escape(SanitizePathLikeMessage(ex.Message, settings.File))}");
            return 1;
        }

        if (!XexAddressMap.TryMap(metadata, ghidraBase, ghidraAddress, out XexAddressMapResult map, out string? errorMessage)) {
            if (WriteJsonError(settings.Json, "XEX map failed", errorMessage ?? "Failed to map address.", "XEX_MAP_FAILED"))
                return 1;

            AnsiConsole.MarkupLine($"[red]{Markup.Escape(errorMessage ?? "Failed to map address.")}[/]");
            return 1;
        }

        if (settings.Json) {
            CliOutput.EmitJson(BuildJsonPayload(settings.File, ghidraBase, ghidraAddress, map));
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]XEX Address Map[/]").RuleStyle("grey"));
        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[grey]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[grey]File[/]", $"[green]{Markup.Escape(XexInfoCommand.GetDisplayFileName(settings.File))}[/]");
        table.AddRow("[grey]Ghidra base[/]", $"[cyan]{XexAddressMap.Hex(ghidraBase)}[/]");
        table.AddRow("[grey]Ghidra address[/]", $"[cyan]{XexAddressMap.Hex(ghidraAddress)}[/]");
        table.AddRow("[grey]RVA[/]", $"[cyan]{XexAddressMap.Hex(map.Rva)}[/]");
        table.AddRow("[grey]XEX load base[/]", FormatNullableValue(map.XexLoadBase));
        table.AddRow("[grey]XEX image base[/]", FormatNullableValue(map.XexImageBase));
        table.AddRow("[grey]Ghidra delta[/]", FormatNullableDelta(map.GhidraDelta));
        table.AddRow("[grey]Mapped XEX address[/]", FormatNullableValue(map.MappedXexAddress));
        table.AddRow("[grey]In image[/]", FormatInImage(map.InImage));
        if (!string.IsNullOrWhiteSpace(map.Warning))
            table.AddRow("[grey]Warning[/]", $"[yellow]{Markup.Escape(map.Warning)}[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    internal static object BuildJsonPayload(string inputPath, uint ghidraBase, uint ghidraAddress, XexAddressMapResult map) {
        return new {
            File = XexInfoCommand.GetDisplayFileName(inputPath),
            GhidraBase = XexAddressMap.Hex(ghidraBase),
            GhidraAddress = XexAddressMap.Hex(ghidraAddress),
            Rva = XexAddressMap.Hex(map.Rva),
            XexLoadBase = XexAddressMap.Hex(map.XexLoadBase),
            XexImageBase = XexAddressMap.Hex(map.XexImageBase),
            GhidraDelta = map.GhidraDelta.HasValue ? XexAddressMap.FormatSignedDelta(map.GhidraDelta.Value) : null,
            MappedXexAddress = XexAddressMap.Hex(map.MappedXexAddress),
            InImage = map.InImage,
            Warning = map.Warning
        };
    }

    private static bool WriteJsonError(bool json, string title, string message, string code) {
        if (!json)
            return false;

        CliOutput.EmitJsonError(new CliErrorEnvelope(
            title,
            message,
            code,
            new[] { "Check the input file and address arguments, then run xex map again." }));
        return true;
    }

    internal static string BuildReadFailureMessage(string? inputPath) {
        string leafName = XexInfoCommand.GetDisplayFileName(inputPath ?? string.Empty);
        return string.IsNullOrWhiteSpace(leafName)
            ? "Failed to read XEX input file."
            : $"Failed to read XEX input file '{leafName}'.";
    }

    internal static string SanitizePathLikeMessage(string? message, string? inputPath) {
        if (string.IsNullOrWhiteSpace(message))
            return "Invalid XEX data.";

        string sanitized = message;
        foreach (string sensitiveValue in GetSensitivePathValues(inputPath)) {
            string replacement = Path.GetFileName(sensitiveValue);
            sanitized = sanitized.Replace(
                sensitiveValue,
                string.IsNullOrWhiteSpace(replacement) ? "[path]" : replacement,
                StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }

    private static IEnumerable<string> GetSensitivePathValues(string? inputPath) {
        if (string.IsNullOrWhiteSpace(inputPath))
            yield break;

        yield return inputPath;

        string fullPath;
        try {
            fullPath = Path.GetFullPath(inputPath);
        }
        catch {
            yield break;
        }

        yield return fullPath;

        string? directory = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(directory)) {
            yield return directory;
            string? parent = Path.GetDirectoryName(directory);
            if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                yield break;
            directory = parent;
        }
    }

    private static string FormatNullableValue(uint? value) =>
        value.HasValue ? $"[cyan]{XexAddressMap.Hex(value.Value)}[/]" : "[grey]unknown[/]";

    private static string FormatNullableDelta(long? value) =>
        value.HasValue ? $"[yellow]{XexAddressMap.FormatSignedDelta(value.Value)}[/]" : "[grey]unknown[/]";

    private static string FormatInImage(bool? value) =>
        value.HasValue
            ? value.Value ? "[green]yes[/]" : "[yellow]no[/]"
            : "[grey]unknown[/]";
}
