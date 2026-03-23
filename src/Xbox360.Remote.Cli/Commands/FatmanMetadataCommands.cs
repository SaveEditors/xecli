using System.ComponentModel;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Fatx;
using Xbox360.Fatx.Layout;
using Xbox360.Remote.Cli.Fatman;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FatmanMetadataBackupCommand : AsyncCommand<FatmanMetadataBackupCommand.Settings>
{
    public sealed class Settings : FatmanImageSettings
    {
        [CommandOption("--out <DIR>")]
        [Description("Directory to receive metadata backup files and the manifest.")]
        public string? OutputDirectory { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        string outputDirectory = FatmanCommandHelpers.RequireOutputPath(settings.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        List<FatmanMetadataRegion> regions = new();
        long prefixLength = Math.Min(source.Length, 0x200000);
        regions.Add(new FatmanMetadataRegion("disk-prefix", 0, prefixLength, "disk-prefix.bin"));

        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using (FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions))
        {
            foreach (FatxPartition partition in disk.Partitions)
            {
                long length = Math.Min(partition.Length, 0x10000);
                if (length <= 0)
                    continue;

                regions.Add(new FatmanMetadataRegion(
                    $"{partition.Index:D2}-{FatmanCommandHelpers.SanitizeName(partition.Name)}-header",
                    partition.Offset,
                    length,
                    $"{partition.Index:D2}-{FatmanCommandHelpers.SanitizeName(partition.Name)}-header.bin"));
            }
        }

        IReadOnlyList<CliOutput.TransferBatchItem> items = regions
            .Select(region => new CliOutput.TransferBatchItem(region.Name, region.Length))
            .ToArray();

        await CliOutput.RunBatchProgressAsync("Backing up FATX metadata", items, async scope =>
        {
            await using FileStream sourceStream = new(source.SourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0x10000, useAsync: true);
            foreach (FatmanMetadataRegion region in regions)
            {
                string filePath = Path.Combine(outputDirectory, region.FileName);
                scope.StartFile(region.FileName, region.Length);
                await CopyRegionAsync(sourceStream, region.Offset, region.Length, filePath, scope, CancellationToken.None);
                scope.CompleteFile();
            }
        });

        FatmanMetadataManifest manifest = new()
        {
            Source = FatmanCommandHelpers.DescribeSource(source),
            CreatedUtc = DateTimeOffset.UtcNow,
            Regions = regions
        };

        string manifestPath = Path.Combine(outputDirectory, "fatman-metadata.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken.None);

        if (settings.Json)
        {
            CliOutput.EmitJson(new
            {
                Source = FatmanCommandHelpers.DescribeSource(source),
                OutputDirectory = outputDirectory,
                Manifest = manifestPath,
                Regions = regions
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Metadata backup complete.[/] [grey]{Markup.Escape(outputDirectory)}[/]");
        }

        return 0;
    }

    private static async Task CopyRegionAsync(FileStream sourceStream, long offset, long length, string filePath, TransferBatchScope scope, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await using FileStream destination = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 0x10000, useAsync: true);
        byte[] buffer = new byte[0x10000];
        long written = 0;
        while (written < length)
        {
            int toRead = (int)Math.Min(buffer.Length, length - written);
            sourceStream.Position = offset + written;
            int read = await sourceStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            scope.ReportFileProgress(written, $"backing up {Path.GetFileName(filePath)}");
        }
    }
}

public sealed class FatmanMetadataRestoreCommand : AsyncCommand<FatmanMetadataRestoreCommand.Settings>
{
    public sealed class Settings : FatmanImageSettings
    {
        [CommandOption("--manifest <FILE>")]
        [Description("Manifest produced by fatman metadata backup.")]
        public string? ManifestPath { get; init; }

        [CommandOption("--auto-confirm")]
        [Description("Skip the destructive-action confirmation prompt.")]
        public bool AutoConfirm { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ManifestPath))
            throw new InvalidOperationException("Provide a manifest with --manifest.");
        string manifestPath = Path.GetFullPath(settings.ManifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Metadata manifest not found.", manifestPath);

        FatmanMetadataManifest manifest = JsonSerializer.Deserialize<FatmanMetadataManifest>(
            await File.ReadAllTextAsync(manifestPath, CancellationToken.None))
            ?? throw new InvalidOperationException("The metadata manifest could not be parsed.");

        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        if (!settings.AutoConfirm)
        {
            if (!AnsiConsole.Confirm($"This will overwrite low-level metadata on {source.DisplayName}. Continue?", false))
            {
                AnsiConsole.MarkupLine("[yellow]Fatman metadata restore cancelled.[/]");
                return 1;
            }
        }

        IReadOnlyList<CliOutput.TransferBatchItem> items = manifest.Regions
            .Select(region => new CliOutput.TransferBatchItem(region.Name, region.Length))
            .ToArray();

        await CliOutput.RunBatchProgressAsync("Restoring FATX metadata", items, async scope =>
        {
            await using FileStream destination = new(source.SourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 0x10000, useAsync: true);
            foreach (FatmanMetadataRegion region in manifest.Regions)
            {
                string regionPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, region.FileName);
                if (!File.Exists(regionPath))
                    throw new FileNotFoundException("Metadata region file not found.", regionPath);

                scope.StartFile(region.FileName, region.Length);
                await CopyRegionBackAsync(destination, region.Offset, region.Length, regionPath, scope, CancellationToken.None);
                scope.CompleteFile();
            }

            await destination.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new
            {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Manifest = manifestPath,
                Restored = manifest.Regions
            });
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Metadata restore complete.[/]");
        }

        return 0;
    }

    private static async Task CopyRegionBackAsync(FileStream destination, long offset, long length, string filePath, TransferBatchScope scope, CancellationToken cancellationToken)
    {
        await using FileStream source = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 0x10000, useAsync: true);
        byte[] buffer = new byte[0x10000];
        long written = 0;
        while (written < length)
        {
            int toRead = (int)Math.Min(buffer.Length, length - written);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            destination.Position = offset + written;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            scope.ReportFileProgress(written, $"restoring {Path.GetFileName(filePath)}");
        }
    }
}

internal sealed class FatmanMetadataManifest
{
    public object? Source { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public required IReadOnlyList<FatmanMetadataRegion> Regions { get; init; }
}

internal sealed record FatmanMetadataRegion(
    string Name,
    long Offset,
    long Length,
    string FileName);
