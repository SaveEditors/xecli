using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Fatx;
using Xbox360.Remote.Cli.Fatman;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FatmanMetadataPatchPlanCommand : AsyncCommand<FatmanMetadataPatchPlanCommand.Settings>
{
    private const string FailureTitle = "Fatman metadata patch plan failed";
    private const string FailureCode = "FATX_METADATA_PATCH_PLAN_FAILED";

    public sealed class Settings : FatmanBaseSettings
    {
        [CommandOption("--from <DIR>")]
        [LocalizedDescription("Source metadata backup directory to use as the baseline.")]
        public string? FromDirectory { get; init; }

        [CommandOption("--to <DIR>")]
        [LocalizedDescription("Target metadata backup directory to use as the patch source.")]
        public string? ToDirectory { get; init; }

        [CommandOption("--out <FILE>")]
        [LocalizedDescription("Write the patch manifest to this file and store payload files beside it.")]
        public string? OutputPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            FatmanMetadataBackupDirectory baseline = await FatmanMetadataPatchHelpers.LoadBackupDirectoryAsync(settings.FromDirectory, "--from");
            FatmanMetadataBackupDirectory target = await FatmanMetadataPatchHelpers.LoadBackupDirectoryAsync(settings.ToDirectory, "--to");
            FatmanMetadataPatchPlan plan = await FatmanMetadataPatchHelpers.BuildPlanAsync(baseline, target, CancellationToken.None);

            string? outputPath = FatmanMetadataPatchHelpers.NormalizeOptionalOutputPath(settings.OutputPath);
            if (!string.IsNullOrWhiteSpace(outputPath))
                await FatmanMetadataPatchHelpers.WritePatchManifestAsync(outputPath, plan, CancellationToken.None);

            if (settings.Json)
            {
                CliOutput.EmitJson(new
                {
                    From = baseline.DisplayName,
                    To = target.DisplayName,
                    Output = outputPath,
                    Manifest = outputPath,
                    RegionCount = plan.Regions.Count,
                    Regions = plan.Regions
                });
                return 0;
            }

            if (plan.Regions.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]The backups match. No patch regions were generated.[/]");
                if (!string.IsNullOrWhiteSpace(outputPath))
                    AnsiConsole.MarkupLine($"[grey]Patch manifest written:[/] [white]{Markup.Escape(outputPath)}[/]");
                return 0;
            }

            AnsiConsole.Write(new Rule("[bold deepskyblue1]Fatman Metadata Patch Plan[/]").RuleStyle("grey"));
            AnsiConsole.MarkupLine($"[grey]From:[/] [white]{Markup.Escape(baseline.DisplayName)}[/]");
            AnsiConsole.MarkupLine($"[grey]To:[/] [white]{Markup.Escape(target.DisplayName)}[/]");

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold springgreen3_1]Region[/]"));
            table.AddColumn(new TableColumn("[bold deepskyblue1]Offset[/]"));
            table.AddColumn(new TableColumn("[bold cyan1]Length[/]"));
            table.AddColumn(new TableColumn("[bold gold1]File[/]"));
            table.AddColumn(new TableColumn("[bold white]Baseline SHA-256[/]"));
            table.AddColumn(new TableColumn("[bold white]Patched SHA-256[/]"));
            foreach (FatmanMetadataPatchRegion region in plan.Regions)
            {
                table.AddRow(
                    $"[springgreen3_1]{Markup.Escape(region.Name)}[/]",
                    $"[deepskyblue1]0x{region.Offset:X}[/]",
                    FatmanMetadataPatchHelpers.FormatBytes(region.Length),
                    $"[white]{Markup.Escape(region.FileName)}[/]",
                    $"[grey]{Markup.Escape(region.BaselineSha256)}[/]",
                    $"[grey]{Markup.Escape(region.PatchedSha256)}[/]");
            }

            AnsiConsole.Write(table);
            if (!string.IsNullOrWhiteSpace(outputPath))
                AnsiConsole.MarkupLine($"[green]Patch manifest written:[/] [white]{Markup.Escape(outputPath)}[/]");
            else
                AnsiConsole.MarkupLine($"[green]{plan.Regions.Count}[/] region(s) changed.");

            return 0;
        }
        catch (Exception ex) when (FatmanMetadataPatchHelpers.TryWriteFailure(settings.Json, FailureTitle, FailureCode, ex))
        {
            return 1;
        }
    }
}

public sealed class FatmanMetadataPatchApplyCommand : AsyncCommand<FatmanMetadataPatchApplyCommand.Settings>
{
    private const string SuccessTitle = "Fatman metadata patch applied";
    private const string FailureTitle = "Fatman metadata patch apply failed";
    private const string FailureCode = "FATX_METADATA_PATCH_APPLY_FAILED";

    public sealed class Settings : FatmanBaseSettings
    {
        [CommandOption("--patch <FILE>")]
        [LocalizedDescription("Patch manifest produced by fatman patch plan.")]
        public string? PatchPath { get; init; }

        [CommandOption("--image <FILE>")]
        [LocalizedDescription("Path to a raw Xbox 360 HDD image (.img / .bin).")]
        public string? ImagePath { get; init; }

        [CommandOption("--disk <NUMBER|PATH>")]
        [LocalizedDescription("Windows physical disk number or device path (for example 2 or \\\\.\\PhysicalDrive2).")]
        public string? Disk { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the patch plan without writing to the destination.")]
        public bool DryRun { get; init; }

        [CommandOption("--auto-confirm")]
        [LocalizedDescription("Skip the destructive-action confirmation prompt.")]
        public bool AutoConfirm { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            FatmanMetadataPatchDocument patchDocument = await FatmanMetadataPatchHelpers.LoadPatchManifestAsync(settings.PatchPath, CancellationToken.None);
            bool dryRun = settings.DryRun;
            FatmanSourceInfo source = FatmanWindowsStorage.ResolveSource(settings.ImagePath, settings.Disk, requireWritable: !dryRun);

            IReadOnlyList<FatmanMetadataPatchApplyRegion> validation = await FatmanMetadataPatchHelpers.ValidatePatchAsync(patchDocument, source, CancellationToken.None);

            if (dryRun)
            {
                if (settings.Json)
                {
                    CliOutput.EmitJson(new
                    {
                        Source = FatmanCommandHelpers.DescribeSource(source),
                        Patch = patchDocument.Path,
                        DryRun = true,
                        RegionCount = validation.Count,
                        Regions = validation.Select(region => new
                        {
                            region.Region.Name,
                            region.Region.Offset,
                            region.Region.Length,
                            region.Region.FileName,
                            region.Region.BaselineSha256,
                            region.Region.PatchedSha256
                        })
                    });
                }
                else
                {
                    AnsiConsole.Write(new Rule("[bold deepskyblue1]Fatman Metadata Patch Preview[/]").RuleStyle("grey"));
                    AnsiConsole.MarkupLine($"[grey]Source:[/] [white]{Markup.Escape(source.DisplayName)}[/]");
                    AnsiConsole.MarkupLine($"[grey]Patch:[/] [white]{Markup.Escape(patchDocument.Path)}[/]");

                    Table table = CliOutput.CreateTable();
                    table.AddColumn(new TableColumn("[bold springgreen3_1]Region[/]"));
                    table.AddColumn(new TableColumn("[bold deepskyblue1]Offset[/]"));
                    table.AddColumn(new TableColumn("[bold cyan1]Length[/]"));
                    table.AddColumn(new TableColumn("[bold gold1]File[/]"));
                    table.AddColumn(new TableColumn("[bold white]Baseline SHA-256[/]"));
                    table.AddColumn(new TableColumn("[bold white]Patched SHA-256[/]"));
                    foreach (FatmanMetadataPatchApplyRegion region in validation)
                    {
                        table.AddRow(
                            $"[springgreen3_1]{Markup.Escape(region.Region.Name)}[/]",
                            $"[deepskyblue1]0x{region.Region.Offset:X}[/]",
                            FatmanMetadataPatchHelpers.FormatBytes(region.Region.Length),
                            $"[white]{Markup.Escape(region.Region.FileName)}[/]",
                            $"[grey]{Markup.Escape(region.Region.BaselineSha256)}[/]",
                            $"[grey]{Markup.Escape(region.Region.PatchedSha256)}[/]");
                    }

                    AnsiConsole.Write(table);
                }

                return 0;
            }

            if (validation.Count == 0)
            {
                if (settings.Json)
                {
                    CliOutput.EmitJson(new
                    {
                        Source = FatmanCommandHelpers.DescribeSource(source),
                        Patch = patchDocument.Path,
                        DryRun = false,
                        RegionCount = 0,
                        Regions = validation.Select(region => new
                        {
                            region.Region.Name,
                            region.Region.Offset,
                            region.Region.Length,
                            region.Region.FileName,
                            region.Region.BaselineSha256,
                            region.Region.PatchedSha256
                        })
                    });
                }
                else
                {
                    AnsiConsole.MarkupLine("[green]The patch contains no regions. Nothing to apply.[/]");
                }

                return 0;
            }

            if (!settings.AutoConfirm)
            {
                if (!ConfirmationHelpers.TryConfirm(
                        FailureTitle,
                        $"This will overwrite FATX metadata on {source.DisplayName} using {Path.GetFileName(patchDocument.Path)}. Continue?",
                        settings.AutoConfirm,
                        emitWarning: !settings.Json))
                {
                    if (settings.Json)
                    {
                        CliOutput.EmitJsonError(new CliErrorEnvelope(
                            FailureTitle,
                            "The patch operation was cancelled before any changes were made.",
                            "FATX_METADATA_PATCH_APPLY_CANCELLED",
                            new[]
                            {
                                "Re-run the command with --dry-run to review the patch, then add --auto-confirm to apply it."
                            }));
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]Fatman metadata patch cancelled.[/]");
                    }

                    return 1;
                }
            }

            if (settings.Json)
            {
                await using FileStream destination = new(source.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 0x10000, useAsync: true);
                foreach (FatmanMetadataPatchApplyRegion region in validation)
                {
                    destination.Position = region.Region.Offset;
                    await destination.WriteAsync(region.PatchedBytes, CancellationToken.None).ConfigureAwait(false);
                }

                await destination.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                IReadOnlyList<CliOutput.TransferBatchItem> items = validation
                    .Select(region => new CliOutput.TransferBatchItem(region.Region.Name, region.Region.Length))
                    .ToArray();

                await CliOutput.RunBatchProgressAsync("Applying FATX metadata patch", items, async scope =>
                {
                    await using FileStream destination = new(source.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 0x10000, useAsync: true);
                    foreach (FatmanMetadataPatchApplyRegion region in validation)
                    {
                        scope.StartFile(region.Region.FileName, region.Region.Length);
                        destination.Position = region.Region.Offset;
                        await destination.WriteAsync(region.PatchedBytes, CancellationToken.None).ConfigureAwait(false);
                        scope.ReportFileProgress(region.Region.Length, $"writing {Path.GetFileName(region.Region.FileName)}");
                        scope.CompleteFile();
                    }

                    await destination.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                });
            }

            if (settings.Json)
            {
                CliOutput.EmitJson(new
                {
                    Source = FatmanCommandHelpers.DescribeSource(source),
                    Patch = patchDocument.Path,
                    DryRun = false,
                    RegionCount = validation.Count,
                    Regions = validation.Select(region => new
                    {
                        region.Region.Name,
                        region.Region.Offset,
                        region.Region.Length,
                        region.Region.FileName,
                        region.Region.BaselineSha256,
                        region.Region.PatchedSha256
                    })
                });
            }
            else
            {
                OperationFeedback.WriteSuccess(SuccessTitle, $"[green]{validation.Count}[/] region(s) written to [white]{Markup.Escape(source.DisplayName)}[/].");
            }

            return 0;
        }
        catch (Exception ex) when (FatmanMetadataPatchHelpers.TryWriteFailure(settings.Json, FailureTitle, FailureCode, ex))
        {
            return 1;
        }
    }
}

internal static class FatmanMetadataPatchHelpers
{
    private const string BackupManifestName = "fatman-metadata.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<FatmanMetadataBackupDirectory> LoadBackupDirectoryAsync(string? directoryPath, string optionName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new InvalidOperationException($"Provide a backup directory with {optionName}.");

        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
            throw new DirectoryNotFoundException($"Backup directory not found: {fullDirectory}");

        string manifestPath = Path.Combine(fullDirectory, BackupManifestName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Metadata manifest not found.", manifestPath);

        string json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        FatmanMetadataManifest manifest = JsonSerializer.Deserialize<FatmanMetadataManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("The metadata manifest could not be parsed.");

        IReadOnlyList<FatmanMetadataRegion> regions = manifest.Regions
            ?? throw new InvalidOperationException("The metadata manifest could not be parsed.");
        ValidateRegionDefinitions(regions, manifestPath);
        return new FatmanMetadataBackupDirectory(fullDirectory, manifestPath, manifest);
    }

    public static async Task<FatmanMetadataPatchDocument> LoadPatchManifestAsync(string? patchPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(patchPath))
            throw new InvalidOperationException("Provide a patch manifest with --patch.");

        string fullPath = Path.GetFullPath(patchPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Patch manifest not found.", fullPath);

        string json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        FatmanMetadataPatchManifest manifest = JsonSerializer.Deserialize<FatmanMetadataPatchManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("The patch manifest could not be parsed.");

        IReadOnlyList<FatmanMetadataPatchRegion> regions = manifest.Regions
            ?? throw new InvalidOperationException("The patch manifest could not be parsed.");
        ValidatePatchManifest(regions, fullPath);
        return new FatmanMetadataPatchDocument(fullPath, Path.GetDirectoryName(fullPath) ?? string.Empty, manifest);
    }

    public static async Task<FatmanMetadataPatchPlan> BuildPlanAsync(FatmanMetadataBackupDirectory baseline, FatmanMetadataBackupDirectory target, CancellationToken cancellationToken)
    {
        Dictionary<FatmanMetadataRegionKey, FatmanMetadataRegion> baselineRegions = BuildRegionMap(baseline.Manifest.Regions, baseline.ManifestPath);
        Dictionary<FatmanMetadataRegionKey, FatmanMetadataRegion> targetRegions = BuildRegionMap(target.Manifest.Regions, target.ManifestPath);
        if (baselineRegions.Count != targetRegions.Count)
            throw new InvalidOperationException("The backup directories do not describe the same FATX metadata regions.");

        List<FatmanMetadataPatchRegion> regions = new();
        Dictionary<FatmanMetadataRegionKey, byte[]> payloads = new();
        foreach (FatmanMetadataRegion targetRegion in target.Manifest.Regions)
        {
            FatmanMetadataRegionKey key = new(targetRegion.Name, targetRegion.Offset, targetRegion.Length);
            if (!baselineRegions.TryGetValue(key, out FatmanMetadataRegion? baselineRegion))
                throw new InvalidOperationException("The backup directories do not describe the same FATX metadata regions.");

            string baselinePath = ResolveRelativePathWithinDirectory(baseline.DirectoryPath, baselineRegion.FileName);
            string targetPath = ResolveRelativePathWithinDirectory(target.DirectoryPath, targetRegion.FileName);

            byte[] baselineBytes = await File.ReadAllBytesAsync(baselinePath, cancellationToken).ConfigureAwait(false);
            byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken).ConfigureAwait(false);
            ValidateRegionPayloadSize(baselineRegion, baselinePath, baselineBytes.Length);
            ValidateRegionPayloadSize(targetRegion, targetPath, targetBytes.Length);

            string baselineSha256 = ComputeSha256Hex(baselineBytes);
            string targetSha256 = ComputeSha256Hex(targetBytes);
            if (!string.Equals(baselineSha256, targetSha256, StringComparison.OrdinalIgnoreCase))
            {
                FatmanMetadataPatchRegion patchRegion = new(
                    targetRegion.Name,
                    targetRegion.Offset,
                    targetRegion.Length,
                    targetRegion.FileName,
                    baselineSha256,
                    targetSha256);

                regions.Add(patchRegion);
                payloads[key] = targetBytes;
            }
        }

        return new FatmanMetadataPatchPlan(baseline, target, regions, payloads);
    }

    public static async Task<IReadOnlyList<FatmanMetadataPatchApplyRegion>> ValidatePatchAsync(FatmanMetadataPatchDocument patchDocument, FatmanSourceInfo source, CancellationToken cancellationToken)
    {
        List<FatmanMetadataPatchApplyRegion> regions = new();
        await using FileStream sourceStream = new(source.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x10000, useAsync: true);

        foreach (FatmanMetadataPatchRegion region in patchDocument.Manifest.Regions)
        {
            ValidateRegionBounds(region, source.Length);
            byte[] sourceBytes = await ReadRegionBytesAsync(sourceStream, region.Offset, region.Length, region.Name, cancellationToken).ConfigureAwait(false);
            string sourceSha256 = ComputeSha256Hex(sourceBytes);
            if (!string.Equals(sourceSha256, region.BaselineSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Baseline hash mismatch for region '{region.Name}'.");

            string payloadPath = ResolveRelativePathWithinDirectory(patchDocument.DirectoryPath, region.FileName);
            byte[] payloadBytes = await File.ReadAllBytesAsync(payloadPath, cancellationToken).ConfigureAwait(false);
            ValidateRegionPayloadSize(region, payloadPath, payloadBytes.Length);

            string payloadSha256 = ComputeSha256Hex(payloadBytes);
            if (!string.Equals(payloadSha256, region.PatchedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Patched hash mismatch for region '{region.Name}'.");

            regions.Add(new FatmanMetadataPatchApplyRegion(region, payloadBytes));
        }

        return regions;
    }

    public static async Task WritePatchManifestAsync(string outputPath, FatmanMetadataPatchPlan plan, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string manifestDirectory = directory ?? string.Empty;
        foreach (FatmanMetadataPatchRegion region in plan.Regions)
        {
            string payloadPath = ResolveRelativePathWithinDirectory(manifestDirectory, region.FileName);
            string? payloadDirectory = Path.GetDirectoryName(payloadPath);
            if (!string.IsNullOrWhiteSpace(payloadDirectory))
                Directory.CreateDirectory(payloadDirectory);

            await File.WriteAllBytesAsync(payloadPath, plan.GetPayloadBytes(region), cancellationToken).ConfigureAwait(false);
        }

        FatmanMetadataPatchManifest manifest = new()
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            Regions = plan.Regions
        };

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    }

    public static string NormalizeOptionalOutputPath(string? outputPath)
        => string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetFullPath(outputPath);

    public static string FormatBytes(long? value)
    {
        if (!value.HasValue)
            return "[grey70]-[/]";

        double size = value.Value;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"[gold1]{size:0.##} {units[unit]}[/]";
    }

    public static bool TryWriteFailure(bool json, string title, string code, Exception ex)
    {
        string detail = ex is JsonException
            ? "The manifest could not be parsed."
            : string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

        if (json)
        {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                title,
                detail,
                code,
                new[]
                {
                    "Verify the manifest paths and region files, then run the command again."
                }));
        }
        else
        {
            OperationFeedback.WriteFailure(title, detail);
        }

        return true;
    }

    public static async Task<byte[]> ReadRegionBytesAsync(FileStream stream, long offset, long length, string regionName, CancellationToken cancellationToken)
    {
        ValidateLength(length, regionName);
        if (length > int.MaxValue)
            throw new InvalidOperationException($"Region '{regionName}' is too large to process.");

        byte[] bytes = new byte[(int)length];
        stream.Position = offset;
        int read = 0;
        while (read < bytes.Length)
        {
            int chunk = await stream.ReadAsync(bytes.AsMemory(read, bytes.Length - read), cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
                throw new EndOfStreamException($"The source ended before region '{regionName}' could be read.");
            read += chunk;
        }

        return bytes;
    }

    private static Dictionary<FatmanMetadataRegionKey, FatmanMetadataRegion> BuildRegionMap(IReadOnlyList<FatmanMetadataRegion> regions, string manifestPath)
    {
        Dictionary<FatmanMetadataRegionKey, FatmanMetadataRegion> map = new();
        foreach (FatmanMetadataRegion region in regions)
        {
            ValidateRegionBounds(region, long.MaxValue);
            if (string.IsNullOrWhiteSpace(region.FileName))
                throw new InvalidOperationException($"Metadata region '{region.Name}' is missing a file name.");

            FatmanMetadataRegionKey key = new(region.Name, region.Offset, region.Length);
            if (!map.TryAdd(key, region))
                throw new InvalidOperationException($"Duplicate metadata region '{region.Name}' was found in {Path.GetFileName(manifestPath)}.");
        }

        return map;
    }

    private static void ValidateRegionDefinitions(IReadOnlyList<FatmanMetadataRegion> regions, string manifestPath)
    {
        HashSet<FatmanMetadataRegionKey> keys = new();
        foreach (FatmanMetadataRegion region in regions)
        {
            ValidateRegionBounds(region, long.MaxValue);
            if (string.IsNullOrWhiteSpace(region.FileName))
                throw new InvalidOperationException($"Metadata region '{region.Name}' is missing a file name.");

            FatmanMetadataRegionKey key = new(region.Name, region.Offset, region.Length);
            if (!keys.Add(key))
                throw new InvalidOperationException($"Duplicate metadata region '{region.Name}' was found in {Path.GetFileName(manifestPath)}.");
        }
    }

    private static void ValidatePatchManifest(IReadOnlyList<FatmanMetadataPatchRegion> regions, string patchPath)
    {
        HashSet<FatmanMetadataRegionKey> keys = new();
        foreach (FatmanMetadataPatchRegion region in regions)
        {
            ValidateRegionBounds(region, long.MaxValue);
            if (string.IsNullOrWhiteSpace(region.FileName))
                throw new InvalidOperationException($"Patch region '{region.Name}' is missing a file name.");
            if (string.IsNullOrWhiteSpace(region.BaselineSha256) || string.IsNullOrWhiteSpace(region.PatchedSha256))
                throw new InvalidOperationException($"Patch region '{region.Name}' is missing a SHA-256 hash.");

            FatmanMetadataRegionKey key = new(region.Name, region.Offset, region.Length);
            if (!keys.Add(key))
                throw new InvalidOperationException($"Duplicate patch region '{region.Name}' was found in {Path.GetFileName(patchPath)}.");
        }
    }

    private static void ValidateRegionBounds(FatmanMetadataRegion region, long sourceLength)
    {
        if (region.Offset < 0)
            throw new InvalidOperationException($"Region '{region.Name}' has a negative offset.");
        if (region.Length <= 0)
            throw new InvalidOperationException($"Region '{region.Name}' has an invalid length.");
        if (region.Offset > sourceLength)
            throw new InvalidOperationException($"Region '{region.Name}' points past the end of the source.");
        if (region.Length > sourceLength - region.Offset)
            throw new InvalidOperationException($"Region '{region.Name}' exceeds the source length.");
    }

    private static void ValidateRegionBounds(FatmanMetadataPatchRegion region, long sourceLength)
    {
        if (region.Offset < 0)
            throw new InvalidOperationException($"Region '{region.Name}' has a negative offset.");
        if (region.Length <= 0)
            throw new InvalidOperationException($"Region '{region.Name}' has an invalid length.");
        if (region.Offset > sourceLength)
            throw new InvalidOperationException($"Region '{region.Name}' points past the end of the source.");
        if (region.Length > sourceLength - region.Offset)
            throw new InvalidOperationException($"Region '{region.Name}' exceeds the source length.");
    }

    private static void ValidateLength(long length, string regionName)
    {
        if (length <= 0)
            throw new InvalidOperationException($"Region '{regionName}' has an invalid length.");
    }

    private static void ValidateRegionPayloadSize(FatmanMetadataRegion region, string path, long actualLength)
    {
        if (actualLength != region.Length)
            throw new InvalidOperationException($"Region file '{Path.GetFileName(path)}' does not match the expected length for '{region.Name}'.");
    }

    private static void ValidateRegionPayloadSize(FatmanMetadataPatchRegion region, string path, long actualLength)
    {
        if (actualLength != region.Length)
            throw new InvalidOperationException($"Region file '{Path.GetFileName(path)}' does not match the expected length for '{region.Name}'.");
    }

    private static string ComputeSha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ResolveRelativePathWithinDirectory(string rootDirectory, string relativePath)
    {
        string fullRoot = string.IsNullOrWhiteSpace(rootDirectory) ? Path.GetFullPath(".") : Path.GetFullPath(rootDirectory);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string comparisonRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!candidate.StartsWith(comparisonRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, comparisonRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A region file path escapes the manifest directory.");
        }

        return candidate;
    }
}

internal sealed record FatmanMetadataBackupDirectory(
    string DirectoryPath,
    string ManifestPath,
    FatmanMetadataManifest Manifest)
{
    public string DisplayName
    {
        get
        {
            string trimmed = DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(leaf) ? DirectoryPath : leaf;
        }
    }
}

internal sealed class FatmanMetadataPatchPlan
{
    private readonly Dictionary<FatmanMetadataRegionKey, byte[]> _payloads;

    public FatmanMetadataPatchPlan(
        FatmanMetadataBackupDirectory baseline,
            FatmanMetadataBackupDirectory target,
            IReadOnlyList<FatmanMetadataPatchRegion> regions,
            Dictionary<FatmanMetadataRegionKey, byte[]> payloads)
    {
            Baseline = baseline;
            Target = target;
            Regions = regions;
            _payloads = payloads;
    }

    public FatmanMetadataBackupDirectory Baseline { get; }

    public FatmanMetadataBackupDirectory Target { get; }

    public IReadOnlyList<FatmanMetadataPatchRegion> Regions { get; }

    public byte[] GetPayloadBytes(FatmanMetadataPatchRegion region)
        => _payloads[new FatmanMetadataRegionKey(region.Name, region.Offset, region.Length)];
}

internal sealed record FatmanMetadataPatchApplyRegion(
    FatmanMetadataPatchRegion Region,
    byte[] PatchedBytes);

internal sealed record FatmanMetadataPatchDocument(
    string Path,
    string DirectoryPath,
    FatmanMetadataPatchManifest Manifest);

internal sealed class FatmanMetadataPatchManifest
{
    public DateTimeOffset CreatedUtc { get; init; }

    public required IReadOnlyList<FatmanMetadataPatchRegion> Regions { get; init; }
}

internal sealed record FatmanMetadataPatchRegion(
    string Name,
    long Offset,
    long Length,
    string FileName,
    string BaselineSha256,
    string PatchedSha256);

internal sealed record FatmanMetadataRegionKey(string Name, long Offset, long Length);
