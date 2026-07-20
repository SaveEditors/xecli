using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexInfoCommand : AsyncCommand<XexInfoCommand.Settings> {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    private const string ValidationTitle = "XEX info validation failed";
    private const string FileFailureTitle = "XEX info file failed";
    private const string ParseFailureTitle = "XEX info parse failed";
    private const string ValidationCode = "XEX_INFO_VALIDATION_FAILED";
    private const string FileFailureCode = "XEX_INFO_FILE_FAILED";
    private const string ParseFailureCode = "XEX_INFO_PARSE_FAILED";

    public sealed class Settings : CommandSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Input XEX file to inspect.")]
        public string? File { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Output JSON.")]
        public bool Json { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write JSON output to a file.")]
        public string? Out { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.File)) {
            if (WriteJsonError(settings.Json, ValidationTitle, "Provide --file <path>.", ValidationCode))
                return 1;

            AnsiConsole.MarkupLine("[red]Provide --file <path>.[/]");
            return 1;
        }

        if (!System.IO.File.Exists(settings.File)) {
            if (WriteJsonError(settings.Json, FileFailureTitle, "Input file not found.", FileFailureCode))
                return 1;

            AnsiConsole.MarkupLine("[red]Input file not found.[/]");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(settings.Out) && !settings.Json) {
            if (WriteJsonError(settings.Json, ValidationTitle, "Use --json with --out.", ValidationCode))
                return 1;

            AnsiConsole.MarkupLine("[red]Use --json with --out.[/]");
            return 1;
        }

        XexMetadata metadata;
        try {
            byte[] data = await System.IO.File.ReadAllBytesAsync(settings.File);
            metadata = XexMetadataParser.Parse(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string message = BuildReadFailureMessage(settings.File);
            if (WriteJsonError(settings.Json, FileFailureTitle, message, FileFailureCode))
                return 1;

            AnsiConsole.MarkupLine($"[red]Failed to read XEX:[/] {Markup.Escape(message)}");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException) {
            string message = $"Failed to parse XEX: {SanitizePathLikeMessage(ex.Message, settings.File)}";
            if (WriteJsonError(settings.Json, ParseFailureTitle, message, ParseFailureCode))
                return 1;

            AnsiConsole.MarkupLine($"[red]Failed to parse XEX:[/] {Markup.Escape(SanitizePathLikeMessage(ex.Message, settings.File))}");
            return 1;
        }

        if (settings.Json) {
            object payload = BuildJsonPayload(settings.File, metadata);
            string json = JsonSerializer.Serialize(payload, JsonOptions);

            if (!string.IsNullOrWhiteSpace(settings.Out)) {
                if (!await TryWriteJsonOutputAsync(settings.Out, json))
                    return 1;
            }

            AnsiConsole.Console.Profile.Out.Writer.WriteLine(json);
            return 0;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]XEX Info[/]").RuleStyle("grey"));
        Table summary = CliOutput.CreateTable();
        summary.AddColumn(new TableColumn("[grey]Field[/]"));
        summary.AddColumn(new TableColumn("[white]Value[/]"));
        summary.AddRow("[grey]File[/]", $"[green]{Markup.Escape(GetDisplayFileName(settings.File))}[/]");
        summary.AddRow("[grey]Magic[/]", $"[cyan]{metadata.Magic}[/]");
        summary.AddRow("[grey]Module flags[/]", $"[white]0x{metadata.ModuleFlags.Raw:X8}[/] [grey]{Markup.Escape(FormatFlagList(metadata.ModuleFlags.Names))}[/]");
        summary.AddRow("[grey]PE data offset[/]", $"[cyan]0x{metadata.PeDataOffset:X8}[/]");
        summary.AddRow("[grey]Security info offset[/]", $"[cyan]0x{metadata.SecurityInfoOffset:X8}[/]");
        summary.AddRow("[grey]Optional header count[/]", $"[white]{metadata.OptionalHeaderCount}[/]");
        summary.AddRow("[grey]Header size[/]", $"[white]0x{metadata.HeaderSize:X8}[/]");
        summary.AddRow("[grey]Original base[/]", FormatValue(metadata.OriginalBaseAddress));
        summary.AddRow("[grey]Entry point[/]", FormatValue(metadata.EntryPoint));
        summary.AddRow("[grey]Image base[/]", FormatValue(metadata.ImageBaseAddress));
        summary.AddRow("[grey]System flags[/]", FormatValue(metadata.SystemFlags));
        if (metadata.OriginalPeName != null)
            summary.AddRow("[grey]Original PE name[/]", FormatHeaderText(metadata.OriginalPeName.Text));
        if (metadata.BoundingPath != null)
            summary.AddRow("[grey]Bounding path[/]", FormatHeaderText(metadata.BoundingPath.Text));
        if (metadata.ExecutionInfo != null)
            summary.AddRow("[grey]Title ID[/]", $"[gold1]0x{metadata.ExecutionInfo.TitleId:X8}[/]");
        AnsiConsole.Write(summary);

        if (metadata.ExecutionInfo != null) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Execution[/]").RuleStyle("grey"));
            Table execution = CliOutput.CreateTable();
            execution.AddColumn(new TableColumn("[grey]Field[/]"));
            execution.AddColumn(new TableColumn("[white]Value[/]"));
            execution.AddRow("[grey]Media ID[/]", FormatHex(metadata.ExecutionInfo.MediaId));
            execution.AddRow("[grey]Version[/]", FormatHex(metadata.ExecutionInfo.Version));
            execution.AddRow("[grey]Base version[/]", FormatHex(metadata.ExecutionInfo.BaseVersion));
            execution.AddRow("[grey]Platform[/]", FormatByte(metadata.ExecutionInfo.Platform));
            execution.AddRow("[grey]Executable type[/]", FormatByte(metadata.ExecutionInfo.ExecutableType));
            execution.AddRow("[grey]Disc number[/]", $"[white]{metadata.ExecutionInfo.DiscNumber}[/]");
            execution.AddRow("[grey]Disc count[/]", $"[white]{metadata.ExecutionInfo.DiscCount}[/]");
            execution.AddRow("[grey]Save game ID[/]", FormatHex(metadata.ExecutionInfo.SaveGameId));
            AnsiConsole.Write(execution);
        }

        if (metadata.SecurityInfo != null) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Security[/]").RuleStyle("grey"));
            Table security = CliOutput.CreateTable();
            security.AddColumn(new TableColumn("[grey]Field[/]"));
            security.AddColumn(new TableColumn("[white]Value[/]"));
            security.AddRow("[grey]Image size[/]", $"[cyan]0x{metadata.SecurityInfo.ImageSize:X8}[/]");
            security.AddRow("[grey]Load address[/]", $"[cyan]0x{metadata.SecurityInfo.LoadAddress:X8}[/]");
            security.AddRow("[grey]Image flags[/]", $"[white]0x{metadata.SecurityInfo.ImageFlags:X8}[/]");
            security.AddRow("[grey]Signature[/]", metadata.SecurityInfo.SignaturePresent ? "[green]present[/]" : "[grey]absent[/]");
            security.AddRow("[grey]Import table count[/]", $"[white]{metadata.SecurityInfo.ImportTableCount}[/]");
            AnsiConsole.Write(security);
        }

        if (metadata.BaseFileFormat != null) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Basefile[/]").RuleStyle("grey"));
            Table basefile = CliOutput.CreateTable();
            basefile.AddColumn(new TableColumn("[grey]Field[/]"));
            basefile.AddColumn(new TableColumn("[white]Value[/]"));
            basefile.AddRow("[grey]Encryption[/]", metadata.BaseFileFormat.IsEncrypted ? $"[red]encrypted (0x{metadata.BaseFileFormat.Encryption:X8})[/]" : "[green]unencrypted[/]");
            basefile.AddRow("[grey]Compression[/]", $"[white]{metadata.BaseFileFormat.CompressionName}[/] [grey](0x{metadata.BaseFileFormat.Compression:X8})[/]");
            basefile.AddRow("[grey]Size[/]", $"[white]0x{metadata.BaseFileFormat.Size:X8}[/]");
            AnsiConsole.Write(basefile);
        }

        if (metadata.ImportLibraries != null) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Code Tables[/]").RuleStyle("grey"));
            Table codeTables = CliOutput.CreateTable();
            codeTables.AddColumn(new TableColumn("[grey]Field[/]"));
            codeTables.AddColumn(new TableColumn("[white]Value[/]"));
            if (metadata.ImportLibraries != null) {
                codeTables.AddRow("[grey]Import libraries[/]", $"[white]{metadata.ImportLibraries.LibraryCount}[/]");
                codeTables.AddRow("[grey]Import table bytes[/]", $"[white]0x{metadata.ImportLibraries.LibraryEntriesSize:X8}[/]");
            }
            AnsiConsole.Write(codeTables);
        }

        if (metadata.ExportTable != null) {
            AnsiConsole.Write(new Rule("[bold deepskyblue1]Export Table[/]").RuleStyle("grey"));
            Table exportTable = CliOutput.CreateTable();
            exportTable.AddColumn(new TableColumn("[grey]Field[/]"));
            exportTable.AddColumn(new TableColumn("[white]Value[/]"));
            exportTable.AddRow("[grey]Address[/]", FormatHex(metadata.ExportTable.Address));
            exportTable.AddRow("[grey]Count[/]", $"[white]{metadata.ExportTable.Count}[/]");
            exportTable.AddRow("[grey]Base ordinal[/]", $"[white]{metadata.ExportTable.BaseOrdinal}[/]");
            exportTable.AddRow("[grey]Image base address[/]", FormatHex(metadata.ExportTable.ImageBaseAddress));
            exportTable.AddRow("[grey]Magic[/]", $"[white]0x{metadata.ExportTable.Magic0:X8} 0x{metadata.ExportTable.Magic1:X8} 0x{metadata.ExportTable.Magic2:X8}[/]");
            exportTable.AddRow("[grey]Module numbers[/]", $"[white]0x{metadata.ExportTable.ModuleNumber0:X8} 0x{metadata.ExportTable.ModuleNumber1:X8}[/]");
            exportTable.AddRow("[grey]Version[/]", $"[white]0x{metadata.ExportTable.Version0:X8} 0x{metadata.ExportTable.Version1:X8} 0x{metadata.ExportTable.Version2:X8}[/]");
            AnsiConsole.Write(exportTable);
        }

        return 0;
    }

    private static object BuildJsonPayload(string? file, XexMetadata metadata) {
        return new {
            File = GetDisplayFileName(file),
            metadata.Magic,
            ModuleFlags = new {
                Raw = $"0x{metadata.ModuleFlags.Raw:X8}",
                metadata.ModuleFlags.TitleModule,
                metadata.ModuleFlags.ExportsToTitle,
                metadata.ModuleFlags.SystemDebugger,
                metadata.ModuleFlags.DllModule,
                metadata.ModuleFlags.ModulePatch,
                metadata.ModuleFlags.FullPatch,
                metadata.ModuleFlags.DeltaPatch,
                metadata.ModuleFlags.UserMode,
                Names = metadata.ModuleFlags.Names
            },
            PeDataOffset = $"0x{metadata.PeDataOffset:X8}",
            SecurityInfoOffset = $"0x{metadata.SecurityInfoOffset:X8}",
            OptionalHeaderCount = metadata.OptionalHeaderCount,
            HeaderSize = $"0x{metadata.HeaderSize:X8}",
            OriginalBaseAddress = FormatNullableHex(metadata.OriginalBaseAddress),
            EntryPoint = FormatNullableHex(metadata.EntryPoint),
            ImageBaseAddress = FormatNullableHex(metadata.ImageBaseAddress),
            SystemFlags = FormatNullableHex(metadata.SystemFlags),
            ExecutionInfo = metadata.ExecutionInfo == null ? null : new {
                MediaId = $"0x{metadata.ExecutionInfo.MediaId:X8}",
                Version = $"0x{metadata.ExecutionInfo.Version:X8}",
                BaseVersion = $"0x{metadata.ExecutionInfo.BaseVersion:X8}",
                TitleId = $"0x{metadata.ExecutionInfo.TitleId:X8}",
                metadata.ExecutionInfo.Platform,
                metadata.ExecutionInfo.ExecutableType,
                metadata.ExecutionInfo.DiscNumber,
                metadata.ExecutionInfo.DiscCount,
                SaveGameId = $"0x{metadata.ExecutionInfo.SaveGameId:X8}"
            },
            BaseFileFormat = metadata.BaseFileFormat == null ? null : new {
                Size = $"0x{metadata.BaseFileFormat.Size:X8}",
                Encryption = new {
                    Raw = $"0x{metadata.BaseFileFormat.Encryption:X8}",
                    State = metadata.BaseFileFormat.IsEncrypted ? "encrypted" : "unencrypted"
                },
                Compression = new {
                    Raw = $"0x{metadata.BaseFileFormat.Compression:X8}",
                    State = metadata.BaseFileFormat.CompressionName
                }
            },
            ImportLibraries = metadata.ImportLibraries == null ? null : new {
                Size = $"0x{metadata.ImportLibraries.Size:X8}",
                LibraryEntriesSize = $"0x{metadata.ImportLibraries.LibraryEntriesSize:X8}",
                LibraryCount = metadata.ImportLibraries.LibraryCount
            },
            OriginalPeName = FormatPrivateHeaderString(metadata.OriginalPeName?.Text),
            BoundingPath = FormatPrivateHeaderString(metadata.BoundingPath?.Text),
            SecurityInfo = metadata.SecurityInfo == null ? null : new {
                Size = $"0x{metadata.SecurityInfo.Size:X8}",
                ImageSize = $"0x{metadata.SecurityInfo.ImageSize:X8}",
                SignaturePresent = metadata.SecurityInfo.SignaturePresent,
                InfoSize = $"0x{metadata.SecurityInfo.InfoSize:X8}",
                ImageFlags = $"0x{metadata.SecurityInfo.ImageFlags:X8}",
                LoadAddress = $"0x{metadata.SecurityInfo.LoadAddress:X8}",
                ImportTableCount = metadata.SecurityInfo.ImportTableCount,
                ExportTableAddress = $"0x{metadata.SecurityInfo.ExportTableAddress:X8}",
                GameRegion = $"0x{metadata.SecurityInfo.GameRegion:X8}",
                AllowedMediaTypes = $"0x{metadata.SecurityInfo.AllowedMediaTypes:X8}",
                PageDescriptorCount = metadata.SecurityInfo.PageDescriptorCount
            },
            ExportTable = metadata.ExportTable == null ? null : new {
                Address = $"0x{metadata.ExportTable.Address:X8}",
                Count = metadata.ExportTable.Count,
                BaseOrdinal = metadata.ExportTable.BaseOrdinal,
                ImageBaseAddress = $"0x{metadata.ExportTable.ImageBaseAddress:X8}"
            }
        };
    }

    private static string FormatByte(byte value) {
        return $"[white]0x{value:X2}[/]";
    }

    private static string FormatHex(uint value) {
        return $"[cyan]0x{value:X8}[/]";
    }

    private static string FormatFlagList(IReadOnlyList<string> names) {
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string FormatValue(uint? value) {
        return value.HasValue ? $"[cyan]0x{value.Value:X8}[/]" : "[grey]unknown[/]";
    }

    private static string? FormatNullableHex(uint? value) {
        return value.HasValue ? $"0x{value.Value:X8}" : null;
    }

    private static async Task<bool> TryWriteJsonOutputAsync(string outputPath, string json) {
        try {
            string fullPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException) {
            string leafName = GetDisplayFileName(outputPath);
            string message = string.IsNullOrWhiteSpace(leafName)
                ? "Failed to write JSON output file."
                : $"Failed to write JSON output file '{leafName}'.";

            CliOutput.EmitJsonError(new CliErrorEnvelope(
                FileFailureTitle,
                message,
                FileFailureCode,
                new[] { "Check the output path, parent directory, and permissions, then run xex info again." }));
            return false;
        }
    }

    private static bool WriteJsonError(bool json, string title, string message, string code) {
        if (!json)
            return false;

        CliOutput.EmitJsonError(new CliErrorEnvelope(
            title,
            message,
            code,
            new[] { "Check the XEX file path and contents, then run xex info again." }));
        return true;
    }

    internal static string BuildReadFailureMessage(string? inputPath) {
        string leafName = GetDisplayFileName(inputPath);
        return string.IsNullOrWhiteSpace(leafName)
            ? "Failed to read XEX input file."
            : $"Failed to read XEX input file '{leafName}'.";
    }

    internal static string SanitizePathLikeMessage(string? message, string? inputPath) {
        if (string.IsNullOrWhiteSpace(message))
            return "Invalid XEX data.";

        if (string.IsNullOrWhiteSpace(inputPath))
            return message;

        string sanitized = message;
        string leafName = GetDisplayFileName(inputPath);
        if (!string.IsNullOrWhiteSpace(leafName) && !string.Equals(leafName, inputPath, StringComparison.OrdinalIgnoreCase)) {
            sanitized = sanitized.Replace(inputPath, leafName, StringComparison.OrdinalIgnoreCase);

            try {
                string fullPath = Path.GetFullPath(inputPath);
                sanitized = sanitized.Replace(fullPath, leafName, StringComparison.OrdinalIgnoreCase);
            }
            catch {
                // Ignore invalid path shapes while still returning a safe message.
            }
        }

        return sanitized;
    }

    internal static string GetDisplayFileName(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string trimmed = path.Trim();
        string fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        if (Path.IsPathRooted(trimmed) && string.Equals(fileName, trimmed, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return fileName;
    }

    internal static string? FormatPrivateHeaderString(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Length > 64)
            return null;

        for (int i = 0; i < value.Length; i++) {
            char ch = value[i];
            if (ch < 0x20 || ch > 0x7E)
                return null;

            if (ch is '\\' or '/' or ':')
                return null;
        }

        return value;
    }

    private static string FormatHeaderText(string? value) {
        string? safe = FormatPrivateHeaderString(value);
        return safe != null ? $"[green]{Markup.Escape(safe)}[/]" : "[grey]redacted[/]";
    }
}
