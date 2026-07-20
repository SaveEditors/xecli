using System.ComponentModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Logging;
using Xbox360.Remote.Cli.God;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XexAnalysisBundleCommand : AsyncCommand<XexAnalysisBundleCommand.Settings> {
    private static readonly JsonSerializerOptions PrettyJsonOptions = new() {
        WriteIndented = true
    };

    public sealed class Settings : CommandSettings {
        [CommandOption("--file <FILE>")]
        [LocalizedDescription("Input XEX file to analyze.")]
        public string? File { get; init; }

        [CommandOption("--in <FILE>")]
        [LocalizedDescription("Legacy alias for --file.")]
        public string? LegacyFile { get; init; }

        [CommandOption("--out <PATH>")]
        [LocalizedDescription("Output bundle path. Defaults to xex-bundle-<timestamp>.zip in the current directory.")]
        public string? Output { get; init; }

        [CommandOption("--folder")]
        [LocalizedDescription("Write an unpacked bundle folder instead of a zip archive.")]
        public bool Folder { get; init; }

        [CommandOption("--force")]
        [LocalizedDescription("Overwrite an existing zip file or replace an existing bundle folder.")]
        public bool Force { get; init; }

        [CommandOption("--ghidra-base <ADDR>")]
        [LocalizedDescription("Ghidra base address used for the address map artifact.")]
        public string? GhidraBase { get; init; }

        [CommandOption("--ghidra <ADDR>")]
        [LocalizedDescription("Ghidra address used for the address map artifact.")]
        public string? GhidraAddress { get; init; }

        [CommandOption("--symbols <FILE>")]
        [LocalizedDescription("Optional symbol sidecar JSON file to include in the bundle.")]
        public string? Symbols { get; init; }

        [CommandOption("--json")]
        [LocalizedDescription("Emit JSON output.")]
        public bool Json { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        string? inputPath = settings.File ?? settings.LegacyFile;
        if (string.IsNullOrWhiteSpace(inputPath)) {
            WriteFailure(settings.Json, "XEX bundle validation failed", "Provide --file <path>.", "XEX_BUNDLE_VALIDATION_FAILED");
            return 1;
        }

        if (!File.Exists(inputPath)) {
            WriteFailure(settings.Json, "XEX bundle file failed", "Input file not found.", "XEX_BUNDLE_FILE_FAILED");
            return 1;
        }

        uint? ghidraBase = null;
        uint? ghidraAddress = null;
        if (!string.IsNullOrWhiteSpace(settings.GhidraBase) || !string.IsNullOrWhiteSpace(settings.GhidraAddress)) {
            if (string.IsNullOrWhiteSpace(settings.GhidraBase) || string.IsNullOrWhiteSpace(settings.GhidraAddress)) {
                WriteFailure(settings.Json, "XEX bundle validation failed", "Provide both --ghidra-base and --ghidra when exporting an address map.", "XEX_BUNDLE_VALIDATION_FAILED");
                return 1;
            }

            if (!CliHelpers.TryParseUInt32(settings.GhidraBase.Trim(), out uint parsedGhidraBase)) {
                WriteFailure(settings.Json, "XEX bundle validation failed", "Provide valid --ghidra-base <addr>.", "XEX_BUNDLE_VALIDATION_FAILED");
                return 1;
            }

            if (!CliHelpers.TryParseUInt32(settings.GhidraAddress.Trim(), out uint parsedGhidraAddress)) {
                WriteFailure(settings.Json, "XEX bundle validation failed", "Provide valid --ghidra <addr>.", "XEX_BUNDLE_VALIDATION_FAILED");
                return 1;
            }

            ghidraBase = parsedGhidraBase;
            ghidraAddress = parsedGhidraAddress;
        }

        string? symbolsPath = null;
        if (!string.IsNullOrWhiteSpace(settings.Symbols)) {
            symbolsPath = Path.GetFullPath(settings.Symbols);
            if (!File.Exists(symbolsPath)) {
                WriteFailure(settings.Json, "XEX bundle file failed", "Symbol sidecar not found.", "XEX_BUNDLE_FILE_FAILED");
                return 1;
            }
        }

        try {
            XexAnalysisBundleResult result = await XexAnalysisBundleStore.CreateAsync(new XexAnalysisBundleRequest(
                inputPath,
                settings.Output,
                settings.Folder,
                settings.Force,
                ghidraBase,
                ghidraAddress,
                symbolsPath));

            if (settings.Json) {
                CliOutput.EmitJson(new {
                    Output = XexInfoCommand.GetDisplayFileName(result.OutputPath),
                    result.Kind,
                    result.FileCount,
                    result.HasAddressMap,
                    result.HasSymbols,
                    Source = XexInfoCommand.GetDisplayFileName(inputPath)
                });
            }
            else {
                OperationFeedback.WriteSuccess(
                    "XEX analysis bundle created",
                    $"[grey]Output:[/] [cyan]{Markup.Escape(XexInfoCommand.GetDisplayFileName(result.OutputPath))}[/]\n" +
                    $"[grey]Format:[/] [cyan]{Markup.Escape(result.Kind)}[/]\n" +
                    $"[grey]Files:[/] [cyan]{result.FileCount}[/]\n" +
                    $"[grey]Address map:[/] [cyan]{(result.HasAddressMap ? "included" : "not included")}[/]\n" +
                    $"[grey]Symbols:[/] [cyan]{(result.HasSymbols ? "included" : "not included")}[/]");
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException) {
            WriteFailure(settings.Json, "XEX bundle failed", CommandLogRedactor.RedactFreeText(ex.Message), "XEX_BUNDLE_FAILED");
            return 1;
        }
    }

    private static void WriteFailure(bool json, string title, string message, string code) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, new[] {
                "Check the XEX file, optional sidecar inputs, and output path, then run xex bundle again."
            }));
            return;
        }

        OperationFeedback.WriteFailure(title, message);
    }
}

internal sealed record XexAnalysisBundleRequest(
    string InputPath,
    string? OutputPath,
    bool Folder,
    bool Force,
    uint? GhidraBase,
    uint? GhidraAddress,
    string? SymbolSidecarPath,
    DateTimeOffset? GeneratedUtc = null);

internal sealed record XexAnalysisBundleResult(
    string OutputPath,
    string Kind,
    int FileCount,
    bool HasAddressMap,
    bool HasSymbols);

internal sealed class XexAnalysisBundleManifest {
    public int Schema { get; set; } = XexAnalysisBundleStore.CurrentSchemaVersion;

    public string Application { get; set; } = "XeCLI";

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; }

    public string Kind { get; set; } = string.Empty;

    public XexAnalysisBundleSourceManifest Source { get; set; } = new();

    public XexAnalysisBundleAddressMapManifest? AddressMap { get; set; }

    public XexAnalysisBundleSymbolManifest? SymbolSidecar { get; set; }

    public List<string> Files { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraData { get; set; }
}

internal sealed class XexAnalysisBundleSourceManifest {
    public string File { get; set; } = string.Empty;

    public long Length { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string Magic { get; set; } = string.Empty;

    public string? OriginalPeName { get; set; }

    public string? BoundingPath { get; set; }

    public string? TitleId { get; set; }
}

internal sealed class XexAnalysisBundleAddressMapManifest {
    public string File { get; set; } = string.Empty;

    public string GhidraBase { get; set; } = string.Empty;

    public string GhidraAddress { get; set; } = string.Empty;

    public string Rva { get; set; } = string.Empty;

    public string? XexLoadBase { get; set; }

    public string? XexImageBase { get; set; }

    public string? GhidraDelta { get; set; }

    public string? MappedXexAddress { get; set; }

    public bool? InImage { get; set; }

    public string? Warning { get; set; }
}

internal sealed class XexAnalysisBundleSymbolManifest {
    public string File { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;

    public DateTimeOffset GeneratedUtc { get; set; }

    public int SymbolCount { get; set; }
}

internal sealed record XexAnalysisBundleLoadResult(XexAnalysisBundleManifest Data, string? Issue);

internal static class XexAnalysisBundleStore {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new() {
        WriteIndented = true
    };

    public const int CurrentSchemaVersion = 1;

    public static async Task<XexAnalysisBundleResult> CreateAsync(XexAnalysisBundleRequest request) {
        string inputPath = Path.GetFullPath(request.InputPath);
        byte[] inputBytes = await File.ReadAllBytesAsync(inputPath);
        XexMetadata metadata = XexMetadataParser.Parse(inputBytes);
        string sourceLeaf = XexInfoCommand.GetDisplayFileName(inputPath);

        string outputPath = ResolveOutputPath(request.OutputPath, request.Folder, request.GeneratedUtc ?? DateTimeOffset.UtcNow);
        string stagingRoot = Path.Combine(CliPaths.CachePath, "xex-bundles", Guid.NewGuid().ToString("N"));
        List<string> files = [];

        try {
            Directory.CreateDirectory(stagingRoot);

            string sourceHash = ComputeSha256(inputBytes);
            XexAnalysisBundleManifest manifest = new() {
                GeneratedUtc = request.GeneratedUtc ?? DateTimeOffset.UtcNow,
                Kind = request.Folder ? "folder" : "zip",
                Source = new XexAnalysisBundleSourceManifest {
                    File = sourceLeaf,
                    Length = inputBytes.LongLength,
                    Sha256 = sourceHash,
                    Magic = metadata.Magic,
                    OriginalPeName = metadata.OriginalPeName?.Text,
                    BoundingPath = metadata.BoundingPath?.Text,
                    TitleId = metadata.ExecutionInfo == null ? null : $"0x{metadata.ExecutionInfo.TitleId:X8}"
                }
            };
            manifest.Version = GetApplicationVersion();

            WriteBundleFile(stagingRoot, "xex-info.json", JsonSerializer.Serialize(BuildXexInfoJsonPayload(inputPath, metadata), PrettyJsonOptions), files);

            if (request.GhidraBase.HasValue && request.GhidraAddress.HasValue) {
                if (!XexAddressMap.TryMap(metadata, request.GhidraBase.Value, request.GhidraAddress.Value, out XexAddressMapResult map, out string? mapError))
                    throw new InvalidOperationException(mapError ?? "Failed to map address.");

                manifest.AddressMap = new XexAnalysisBundleAddressMapManifest {
                    File = sourceLeaf,
                    GhidraBase = XexAddressMap.Hex(request.GhidraBase.Value),
                    GhidraAddress = XexAddressMap.Hex(request.GhidraAddress.Value),
                    Rva = XexAddressMap.Hex(map.Rva),
                    XexLoadBase = XexAddressMap.Hex(map.XexLoadBase),
                    XexImageBase = XexAddressMap.Hex(map.XexImageBase),
                    GhidraDelta = map.GhidraDelta.HasValue ? XexAddressMap.FormatSignedDelta(map.GhidraDelta.Value) : null,
                    MappedXexAddress = XexAddressMap.Hex(map.MappedXexAddress),
                    InImage = map.InImage,
                    Warning = map.Warning
                };
                WriteBundleFile(stagingRoot, "xex-address-map.json", JsonSerializer.Serialize(XexMapCommand.BuildJsonPayload(inputPath, request.GhidraBase.Value, request.GhidraAddress.Value, map), PrettyJsonOptions), files);
            }

            if (!string.IsNullOrWhiteSpace(request.SymbolSidecarPath)) {
                IdaSymbolSidecarLoadResult sidecar = IdaSymbolSidecarStore.LoadRaw(request.SymbolSidecarPath);
                if (!string.IsNullOrWhiteSpace(sidecar.Issue))
                    throw new InvalidOperationException(sidecar.Issue);

                manifest.SymbolSidecar = new XexAnalysisBundleSymbolManifest {
                    File = Path.GetFileName(request.SymbolSidecarPath),
                    Source = sidecar.Data.Source,
                    Module = sidecar.Data.Module,
                    GeneratedUtc = sidecar.Data.GeneratedUtc,
                    SymbolCount = sidecar.Data.Symbols.Count
                };
                WriteBundleFile(stagingRoot, "symbols.json", JsonSerializer.Serialize(sidecar.Data, PrettyJsonOptions), files);
            }

            IReadOnlyList<string> manifestFiles = files.Concat(["manifest.json"]).ToArray();
            manifest.Files = manifestFiles.ToList();
            WriteBundleFile(stagingRoot, "manifest.json", JsonSerializer.Serialize(manifest, PrettyJsonOptions), files);

            if (request.Folder)
                CopyStagedFolder(stagingRoot, outputPath, request.Force);
            else
                WriteZipArchive(stagingRoot, outputPath, request.Force);

            XexAnalysisBundleLoadResult validation = LoadRaw(outputPath);
            if (!string.IsNullOrWhiteSpace(validation.Issue))
                throw new InvalidOperationException(validation.Issue);

            return new XexAnalysisBundleResult(
                outputPath,
                request.Folder ? "folder" : "zip",
                files.Count,
                manifest.AddressMap != null,
                manifest.SymbolSidecar != null);
        }
        finally {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public static XexAnalysisBundleLoadResult LoadRaw(string path) {
        try {
            if (Directory.Exists(path))
                return LoadFromFolder(path);

            if (File.Exists(path))
                return LoadFromZip(path);

            return new XexAnalysisBundleLoadResult(new XexAnalysisBundleManifest(), $"Bundle not found: {path}");
        }
        catch (JsonException ex) {
            return new XexAnalysisBundleLoadResult(new XexAnalysisBundleManifest(), $"Stored XEX analysis bundle is malformed: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException or NotSupportedException or InvalidDataException) {
            return new XexAnalysisBundleLoadResult(new XexAnalysisBundleManifest(), $"Stored XEX analysis bundle could not be read: {ex.Message}");
        }
    }

    public static bool TryValidate(XexAnalysisBundleManifest? data, IReadOnlyCollection<string>? files, out XexAnalysisBundleManifest normalized, out string error) {
        normalized = data ?? new XexAnalysisBundleManifest();
        error = string.Empty;

        if (normalized.Schema != CurrentSchemaVersion) {
            error = $"Unsupported XEX analysis bundle schema version {normalized.Schema}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Application)) {
            error = "XEX analysis bundle application is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Version)) {
            error = "XEX analysis bundle version is missing.";
            return false;
        }

        if (normalized.GeneratedUtc == default) {
            error = "XEX analysis bundle generation timestamp is missing.";
            return false;
        }

        if (normalized.Source == null) {
            error = "XEX analysis bundle source summary is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Source.File)) {
            error = "XEX analysis bundle source file is missing.";
            return false;
        }

        if (normalized.Source.Length < 0) {
            error = "XEX analysis bundle source length is invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Source.Sha256) || normalized.Source.Sha256.Length != 64) {
            error = "XEX analysis bundle source hash is missing or invalid.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized.Source.Magic)) {
            error = "XEX analysis bundle source magic is missing.";
            return false;
        }

        if (normalized.Files == null || normalized.Files.Count == 0) {
            error = "XEX analysis bundle file list is missing.";
            return false;
        }

        HashSet<string> expectedFiles = new(normalized.Files, StringComparer.Ordinal);
        if (expectedFiles.Count != normalized.Files.Count) {
            error = "XEX analysis bundle file list contains duplicates.";
            return false;
        }

        foreach (string file in normalized.Files) {
            if (!IsRelativeBundlePath(file)) {
                error = $"Invalid bundle file path '{file}'.";
                return false;
            }
        }

        if (!expectedFiles.Contains("manifest.json")) {
            error = "XEX analysis bundle manifest is missing from the file list.";
            return false;
        }

        if (!expectedFiles.Contains("xex-info.json")) {
            error = "XEX analysis bundle is missing xex-info.json.";
            return false;
        }

        if (normalized.AddressMap != null && !expectedFiles.Contains("xex-address-map.json")) {
            error = "XEX analysis bundle address map is missing from the file list.";
            return false;
        }

        if (normalized.SymbolSidecar != null && !expectedFiles.Contains("symbols.json")) {
            error = "XEX analysis bundle symbol sidecar is missing from the file list.";
            return false;
        }

        if (files != null) {
            HashSet<string> actualFiles = new(files, StringComparer.Ordinal);
            if (!actualFiles.SetEquals(expectedFiles)) {
                error = "XEX analysis bundle file list does not match the stored files.";
                return false;
            }
        }

        return true;
    }

    private static XexAnalysisBundleLoadResult LoadFromFolder(string root) {
        string manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
            return new XexAnalysisBundleLoadResult(new XexAnalysisBundleManifest(), "XEX analysis bundle manifest not found.");

        string manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
        XexAnalysisBundleManifest? manifest = JsonSerializer.Deserialize<XexAnalysisBundleManifest>(manifestJson, JsonOptions);
        List<string> actualFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToList();
        if (!TryValidate(manifest, actualFiles, out XexAnalysisBundleManifest normalized, out string error))
            return new XexAnalysisBundleLoadResult(normalized, error);

        return ValidateArtifacts(root, normalized, path => ReadFolderText(Path.Combine(root, path)));
    }

    private static XexAnalysisBundleLoadResult LoadFromZip(string zipPath) {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
            return new XexAnalysisBundleLoadResult(new XexAnalysisBundleManifest(), "XEX analysis bundle manifest not found.");

        string manifestJson = ReadZipText(manifestEntry);
        XexAnalysisBundleManifest? manifest = JsonSerializer.Deserialize<XexAnalysisBundleManifest>(manifestJson, JsonOptions);
        List<string> actualFiles = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToList();
        if (!TryValidate(manifest, actualFiles, out XexAnalysisBundleManifest normalized, out string error))
            return new XexAnalysisBundleLoadResult(normalized, error);

        return ValidateArtifacts(zipPath, normalized, path => ReadZipText(archive.GetEntry(path) ?? throw new InvalidDataException($"Missing bundle entry '{path}'.")));
    }

    private static XexAnalysisBundleLoadResult ValidateArtifacts(string location, XexAnalysisBundleManifest manifest, Func<string, string> readText) {
        using (JsonDocument infoDocument = JsonDocument.Parse(readText("xex-info.json"))) {
            string fileName = infoDocument.RootElement.GetProperty("File").GetString() ?? string.Empty;
            if (!string.Equals(fileName, manifest.Source.File, StringComparison.OrdinalIgnoreCase))
                return new XexAnalysisBundleLoadResult(manifest, "Stored XEX analysis bundle xex-info.json does not match the manifest.");

            if (!string.Equals(infoDocument.RootElement.GetProperty("Magic").GetString(), "XEX2", StringComparison.OrdinalIgnoreCase))
                return new XexAnalysisBundleLoadResult(manifest, "Stored XEX analysis bundle xex-info.json does not look like a XEX analysis payload.");
        }

        if (manifest.AddressMap != null) {
            using JsonDocument mapDocument = JsonDocument.Parse(readText("xex-address-map.json"));
            string mapFile = mapDocument.RootElement.GetProperty("File").GetString() ?? string.Empty;
            if (!string.Equals(mapFile, manifest.Source.File, StringComparison.OrdinalIgnoreCase))
                return new XexAnalysisBundleLoadResult(manifest, "Stored XEX analysis bundle address map does not match the manifest.");
        }

        if (manifest.SymbolSidecar != null) {
            IdaSymbolSidecarFile? sidecar = JsonSerializer.Deserialize<IdaSymbolSidecarFile>(readText("symbols.json"), JsonOptions);
            if (!IdaSymbolSidecarStore.TryValidate(sidecar, out _, out string sidecarError))
                return new XexAnalysisBundleLoadResult(manifest, $"Stored XEX analysis bundle symbol sidecar is invalid: {sidecarError}");
        }

        return new XexAnalysisBundleLoadResult(manifest, null);
    }

    private static string ResolveOutputPath(string? outputPath, bool folder, DateTimeOffset generatedUtc) {
        string baseName = $"xex-bundle-{generatedUtc:yyyyMMdd-HHmmss}";
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.Combine(Environment.CurrentDirectory, folder ? baseName : baseName + ".zip");

        string fullPath = Path.GetFullPath(outputPath);
        if (folder)
            return fullPath;

        if (Directory.Exists(fullPath))
            return Path.Combine(fullPath, baseName + ".zip");

        return string.IsNullOrWhiteSpace(Path.GetExtension(fullPath))
            ? fullPath + ".zip"
            : fullPath;
    }

    private static string ComputeSha256(byte[] bytes) {
        using SHA256 sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(bytes));
    }

    private static object BuildXexInfoJsonPayload(string? file, XexMetadata metadata) {
        return new {
            File = XexInfoCommand.GetDisplayFileName(file),
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
            OriginalPeName = XexInfoCommand.FormatPrivateHeaderString(metadata.OriginalPeName?.Text),
            BoundingPath = XexInfoCommand.FormatPrivateHeaderString(metadata.BoundingPath?.Text),
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

    private static string? FormatNullableHex(uint? value) {
        return value.HasValue ? $"0x{value.Value:X8}" : null;
    }

    private static void WriteBundleFile(string root, string relativePath, string content, List<string> files) {
        string fullPath = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, content + Environment.NewLine, new UTF8Encoding(false));
        files.Add(relativePath.Replace('\\', '/'));
    }

    private static void CopyStagedFolder(string stagingRoot, string outputPath, bool force) {
        if (Directory.Exists(outputPath) && !force && Directory.EnumerateFileSystemEntries(outputPath).Any())
            throw new IOException("Output folder already exists. Use --force to replace it.");

        if (Directory.Exists(outputPath) && force)
            Directory.Delete(outputPath, recursive: true);

        Directory.CreateDirectory(outputPath);
        foreach (string sourcePath in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(stagingRoot, sourcePath);
            string destinationPath = Path.Combine(outputPath, relativePath);
            string? directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourcePath, destinationPath, overwrite: force);
        }
    }

    private static void WriteZipArchive(string stagingRoot, string outputPath, bool force) {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(outputPath)) {
            if (!force)
                throw new IOException("Output zip already exists. Use --force to overwrite it.");

            File.Delete(outputPath);
        }

        ZipFile.CreateFromDirectory(stagingRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false, entryNameEncoding: Encoding.UTF8);
    }

    private static string ReadFolderText(string path) => File.ReadAllText(path, Encoding.UTF8);

    private static string ReadZipText(ZipArchiveEntry entry) {
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool IsRelativeBundlePath(string path) {
        return !string.IsNullOrWhiteSpace(path) &&
               !Path.IsPathRooted(path) &&
               !path.Contains('\\', StringComparison.Ordinal) &&
               !path.Contains(':', StringComparison.Ordinal) &&
               !path.Contains("..", StringComparison.Ordinal);
    }

    private static string GetApplicationVersion() {
        return typeof(XexAnalysisBundleCommand).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch {
        }
    }
}
