using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Buffers.Binary;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Fatx;
using Xbox360.Fatx.Entries;
using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.Formatting;
using Xbox360.Fatx.Reading;
using Xbox360.Fatx.Writing;
using Xbox360.Remote.Cli.Fatman;

namespace Xbox360.Remote.Cli.Commands;

public class FatmanBaseSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit machine-readable JSON output.")]
    public bool Json { get; init; }
}

public class FatmanImageSettings : FatmanBaseSettings
{
    [CommandOption("--image <FILE>")]
    [Description("Path to a raw Xbox 360 HDD image (.img / .bin).")]
    public string? ImagePath { get; init; }

    [CommandOption("--disk <NUMBER|PATH>")]
    [Description("Windows physical disk number or device path (for example 2 or \\\\.\\PhysicalDrive2).")]
    public string? Disk { get; init; }

    [CommandOption("--offset <VALUE>")]
    [Description("Open a partition manually at a byte offset (decimal or 0x hex).")]
    public string? Offset { get; init; }

    [CommandOption("--length <VALUE>")]
    [Description("Limit the manually opened partition to a byte length (decimal or 0x hex).")]
    public string? Length { get; init; }
}

public class FatmanPartitionSettings : FatmanImageSettings
{
    [CommandOption("--partition <NAME|INDEX>")]
    [Description("Partition name or zero-based partition index.")]
    public string? Partition { get; init; }
}

public sealed class FatmanDevicesCommand : Command<FatmanDevicesCommand.Settings>
{
    public sealed class Settings : FatmanBaseSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        IReadOnlyList<FatmanDriveInfo> drives = DriveInfo.GetDrives()
            .Select(drive => {
                bool ready = false;
                long? totalSize = null;
                long? freeSpace = null;
                string? volumeLabel = null;
                try
                {
                    ready = drive.IsReady;
                    if (ready)
                    {
                        totalSize = drive.TotalSize;
                        freeSpace = drive.AvailableFreeSpace;
                        volumeLabel = drive.VolumeLabel;
                    }
                }
                catch
                {
                    // ignored
                }

                return new FatmanDriveInfo(
                    drive.Name,
                    drive.DriveType.ToString(),
                    ready ? drive.DriveFormat : null,
                    volumeLabel,
                    ready,
                    totalSize,
                    freeSpace);
            })
            .ToArray();

        if (settings.Json)
        {
            CliOutput.EmitJson(drives);
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Drive[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Type[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Label[/]"));
        table.AddColumn(new TableColumn("[bold cyan1]Format[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Total[/]"));
        table.AddColumn(new TableColumn("[bold white]Free[/]"));
        foreach (FatmanDriveInfo drive in drives)
        {
            table.AddRow(
                $"[white]{Markup.Escape(drive.Name)}[/]",
                $"[deepskyblue1]{Markup.Escape(drive.Type)}[/]",
                string.IsNullOrWhiteSpace(drive.Label) ? "[grey70]-[/]" : $"[springgreen3_1]{Markup.Escape(drive.Label)}[/]",
                string.IsNullOrWhiteSpace(drive.Format) ? "[grey70]-[/]" : $"[cyan1]{Markup.Escape(drive.Format)}[/]",
                FatmanCommandHelpers.FormatBytes(drive.TotalBytes),
                FatmanCommandHelpers.FormatBytes(drive.FreeBytes));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanScanCommand : AsyncCommand<FatmanScanCommand.Settings>
{
    public sealed class Settings : FatmanImageSettings
    {
        [CommandOption("--limit <COUNT>")]
        [Description("Maximum number of plausible FATX/XTAF header candidates to return.")]
        [DefaultValue(32)]
        public int Limit { get; init; } = 32;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        IReadOnlyList<FatmanVolumeCandidateInfo> candidates = await FatmanCommandHelpers.ScanVolumeCandidatesAsync(source.SourcePath, settings.Limit);
        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Candidates = candidates });
            return 0;
        }

        if (candidates.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No plausible FATX/XTAF headers were found.[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Offset[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Magic[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Sectors/Cluster[/]"));
        table.AddColumn(new TableColumn("[bold cyan1]Cluster Size[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Root Cluster[/]"));
        table.AddColumn(new TableColumn("[bold white]Notes[/]"));
        foreach (FatmanVolumeCandidateInfo candidate in candidates)
        {
            table.AddRow(
                $"[cyan1]0x{candidate.Offset:X}[/]",
                $"[springgreen3_1]{Markup.Escape(candidate.Magic)}[/]",
                $"[deepskyblue1]{candidate.SectorsPerCluster}[/]",
                FatmanCommandHelpers.FormatBytes(candidate.ClusterSize),
                $"[gold1]{candidate.RootDirectoryCluster}[/]",
                string.IsNullOrWhiteSpace(candidate.Notes) ? "[grey70]-[/]" : $"[white]{Markup.Escape(candidate.Notes)}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanPartitionsCommand : AsyncCommand<FatmanPartitionsCommand.Settings>
{
    public sealed class Settings : FatmanImageSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        IReadOnlyList<FatmanPartitionInfo> rows = await FatmanCommandHelpers.DescribePartitionsAsync(disk);
        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partitions = rows });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Index[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Kind[/]"));
        table.AddColumn(new TableColumn("[bold cyan1]Offset[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Length[/]"));
        table.AddColumn(new TableColumn("[bold white]FATX[/]"));
        foreach (FatmanPartitionInfo row in rows)
        {
            table.AddRow(
                $"[white]{row.Index}[/]",
                $"[springgreen3_1]{Markup.Escape(row.Name)}[/]",
                $"[deepskyblue1]{Markup.Escape(row.Kind)}[/]",
                $"[cyan1]0x{row.Offset:X}[/]",
                FatmanCommandHelpers.FormatBytes(row.Length),
                row.IsFatx == true ? "[springgreen3_1]Yes[/]" : "[grey70]No[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanInfoCommand : AsyncCommand<FatmanInfoCommand.Settings>
{
    public sealed class Settings : FatmanPartitionSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        try
        {
            await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
            if (settings.Json)
            {
                CliOutput.EmitJson(new {
                    Source = FatmanCommandHelpers.DescribeSource(source),
                    Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                    Partition = new {
                        partition.Index,
                        partition.Name,
                        Kind = partition.Kind.ToString(),
                        partition.Offset,
                        partition.Length
                    },
                    Volume = volume.Info
                });
                return 0;
            }

            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[bold white]Field[/]"));
            table.AddColumn(new TableColumn("[bold white]Value[/]"));
            table.AddRow("[white]Source[/]", $"[deepskyblue1]{Markup.Escape(source.DisplayName)}[/]");
            if (openOptions.PartitionOffset.HasValue)
            {
                table.AddRow("[white]Manual Offset[/]", $"[cyan1]0x{openOptions.PartitionOffset.Value:X}[/]");
                if (openOptions.PartitionLength.HasValue)
                    table.AddRow("[white]Manual Length[/]", FatmanCommandHelpers.FormatBytes(openOptions.PartitionLength.Value));
            }
            table.AddRow("[white]Partition[/]", $"[springgreen3_1]{Markup.Escape(partition.Name)}[/]");
            table.AddRow("[white]Kind[/]", $"[deepskyblue1]{Markup.Escape(partition.Kind.ToString())}[/]");
            table.AddRow("[white]Offset[/]", $"[cyan1]0x{partition.Offset:X}[/]");
            table.AddRow("[white]Length[/]", FatmanCommandHelpers.FormatBytes(partition.Length));
            table.AddRow("[white]Magic[/]", $"[gold1]{Markup.Escape(volume.Info.Magic)}[/]");
            table.AddRow("[white]Label[/]", $"[springgreen3_1]{Markup.Escape(volume.Info.Label)}[/]");
            table.AddRow("[white]Root Cluster[/]", $"[deepskyblue1]{volume.Info.RootDirectoryCluster}[/]");
            table.AddRow("[white]Cluster Size[/]", FatmanCommandHelpers.FormatBytes(volume.Info.ClusterSize));
            table.AddRow("[white]Total Clusters[/]", $"[cyan1]{volume.Info.TotalClusters}[/]");
            AnsiConsole.Write(table);
            return 0;
        }
        catch (FatxException ex)
        {
            throw new InvalidOperationException($"Partition '{partition.Name}' is not a readable FATX volume: {ex.Message}");
        }
    }
}

public sealed class FatmanDumpCommand : AsyncCommand<FatmanDumpCommand.Settings>
{
    public sealed class Settings : FatmanPartitionDumpSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        string outputDirectory = FatmanCommandHelpers.RequireOutputPath(settings.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);

        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        IReadOnlyList<FatxPartition> selected = await FatmanCommandHelpers.ResolvePartitionsAsync(disk, settings.Partition);
        IReadOnlyList<CliOutput.TransferBatchItem> items = selected
            .Select(partition => new CliOutput.TransferBatchItem(partition.Name, partition.Length))
            .ToArray();

        await CliOutput.RunBatchProgressAsync("Dumping FATX partitions", items, async scope => {
            foreach (FatxPartition partition in selected)
            {
                string fileName = $"{partition.Index:D2}-{FatmanCommandHelpers.SanitizeName(partition.Name)}.bin";
                string outputPath = Path.Combine(outputDirectory, fileName);
                scope.StartFile(fileName, partition.Length);

                await using Stream source = disk.OpenPartitionStream(partition);
                await using FileStream destination = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 0x10000, useAsync: true);
                byte[] buffer = new byte[0x10000];
                long written = 0;
                while (true)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), CancellationToken.None);
                    if (read == 0)
                        break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                    written += read;
                    scope.ReportFileProgress(written, $"dumping {partition.Name}");
                }

                scope.CompleteFile();
            }
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                OutputDirectory = outputDirectory,
                Dumped = selected.Select(partition => new {
                    partition.Index,
                    partition.Name,
                    partition.Kind,
                    partition.Offset,
                    partition.Length,
                    File = Path.Combine(outputDirectory, $"{partition.Index:D2}-{FatmanCommandHelpers.SanitizeName(partition.Name)}.bin")
                })
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Dump complete.[/] [grey]{Markup.Escape(outputDirectory)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanListCommand : AsyncCommand<FatmanListCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        string fatxPath = FatmanCommandHelpers.NormalizeFatxPath(settings.Path);
        IReadOnlyList<FatxEntry> entries = await volume.ListDirectoryAsync(fatxPath);
        IReadOnlyList<FatmanEntryInfo> rows = entries.Select(FatmanCommandHelpers.ToEntryInfo).ToArray();

        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partition = partition.Name, Path = fatxPath, Entries = rows });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Type[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Path[/]"));
        table.AddColumn(new TableColumn("[bold cyan1]Cluster[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Size[/]"));
        foreach (FatmanEntryInfo row in rows.OrderByDescending(r => r.IsDirectory).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                row.IsDirectory ? "[springgreen3_1]DIR[/]" : "[white]FILE[/]",
                $"[springgreen3_1]{Markup.Escape(row.Name)}[/]",
                $"[deepskyblue1]{Markup.Escape(row.FullPath)}[/]",
                row.FirstCluster.HasValue ? $"[cyan1]{row.FirstCluster.Value}[/]" : "[grey70]-[/]",
                FatmanCommandHelpers.FormatBytes(row.Size));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanFindCommand : AsyncCommand<FatmanFindCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings
    {
        [CommandOption("--query <TEXT>")]
        [Description("Case-insensitive text to match in entry names or full FATX paths.")]
        public string? Query { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Query))
            throw new InvalidOperationException("Provide a search string with --query.");

        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        string rootPath = FatmanCommandHelpers.NormalizeFatxPath(settings.Path);
        List<FatmanEntryInfo> matches = new();
        await FatmanCommandHelpers.CollectMatchingEntriesAsync(volume, rootPath, settings.Query, matches);

        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partition = partition.Name, Root = rootPath, Query = settings.Query, Matches = matches });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Type[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Path[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Size[/]"));
        foreach (FatmanEntryInfo match in matches.OrderBy(m => m.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            table.AddRow(
                match.IsDirectory ? "[springgreen3_1]DIR[/]" : "[white]FILE[/]",
                $"[springgreen3_1]{Markup.Escape(match.Name)}[/]",
                $"[deepskyblue1]{Markup.Escape(match.FullPath)}[/]",
                FatmanCommandHelpers.FormatBytes(match.Size));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanGetCommand : AsyncCommand<FatmanGetCommand.Settings>
{
    public sealed class Settings : FatmanPathExportSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxEntry entry = await volume.ResolvePathAsync(fatxPath);
        if (entry is not FatxFileEntry)
            throw new InvalidOperationException($"Path '{fatxPath}' does not reference a FATX file.");

        string outputPath = FatmanCommandHelpers.ResolveExportPath(settings.OutputPath, entry.Name);
        await CliOutput.RunWithProgressAsync($"Extracting {entry.Name}", entry.Size, async progress => {
            byte[] payload = await volume.ReadFileAsync(fatxPath);
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(outputPath, payload);
            progress.Report(new CliOutput.TransferProgressUpdate(payload.LongLength, "written"));
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partition = partition.Name, Path = fatxPath, Output = outputPath, Size = entry.Size });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Extracted file.[/] [grey]{Markup.Escape(outputPath)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanCatCommand : AsyncCommand<FatmanCatCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        byte[] payload = await volume.ReadFileAsync(fatxPath);
        string text = FatmanCommandHelpers.DecodeText(payload);

        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partition = partition.Name, Path = fatxPath, Text = text });
            return 0;
        }

        AnsiConsole.Write(new Text(text));
        if (!text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            AnsiConsole.WriteLine();
        return 0;
    }
}

public sealed class FatmanExtractCommand : AsyncCommand<FatmanExtractCommand.Settings>
{
    public sealed class Settings : FatmanPathExportSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        string outputPath = FatmanCommandHelpers.RequireOutputPath(settings.OutputPath);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings);
        await using FatxDisk disk = await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxEntry entry = await volume.ResolvePathAsync(fatxPath);
        FatxExtractionService extractor = new(volume);

        await CliOutput.RunWithProgressAsync($"Extracting {entry.Name}", null, async progress => {
            if (entry is FatxDirectoryEntry)
            {
                await extractor.ExtractDirectoryAsync(fatxPath, outputPath);
                progress.Report(new CliOutput.TransferProgressUpdate(1, "directory extracted"));
            }
            else
            {
                await extractor.ExtractFileAsync(fatxPath, outputPath);
                progress.Report(new CliOutput.TransferProgressUpdate(entry.Size, "file extracted"));
            }
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new { Source = FatmanCommandHelpers.DescribeSource(source), Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions), Partition = partition.Name, Path = fatxPath, Output = outputPath, Entry = FatmanCommandHelpers.ToEntryInfo(entry) });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Extraction complete.[/] [grey]{Markup.Escape(outputPath)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanPutCommand : AsyncCommand<FatmanPutCommand.Settings>
{
    public sealed class Settings : FatmanPathExportSettings
    {
        [CommandOption("--in <FILE>")]
        [Description("Host file to write into the FATX image.")]
        public string? InputPath { get; init; }

        [CommandOption("--overwrite")]
        [Description("Replace an existing FATX file at the target path.")]
        public bool Overwrite { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        if (string.IsNullOrWhiteSpace(settings.InputPath))
            throw new InvalidOperationException("Provide a host file with --in.");

        string inputPath = Path.GetFullPath(settings.InputPath);
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("Host file not found.", inputPath);

        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: false);
        await using FatxDisk disk = await FatxDisk.OpenAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxMutationService mutation = new(volume);
        long length = new FileInfo(inputPath).Length;

        await CliOutput.RunWithProgressAsync($"Writing {Path.GetFileName(inputPath)}", length, async progress => {
            await mutation.PutFileAsync(inputPath, fatxPath, settings.Overwrite);
            progress.Report(new CliOutput.TransferProgressUpdate(length, "written"));
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                Partition = partition.Name,
                Input = inputPath,
                Path = fatxPath,
                Overwrite = settings.Overwrite
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]File written.[/] [grey]{Markup.Escape(fatxPath)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanMkdirCommand : AsyncCommand<FatmanMkdirCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings { }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: false);
        await using FatxDisk disk = await FatxDisk.OpenAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxMutationService mutation = new(volume);
        await mutation.CreateDirectoryAsync(fatxPath);

        if (settings.Json)
        {
            CliOutput.EmitJson(new {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                Partition = partition.Name,
                Path = fatxPath
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Directory created.[/] [grey]{Markup.Escape(fatxPath)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanMoveCommand : AsyncCommand<FatmanMoveCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings
    {
        [CommandOption("--to <FATXPATH>")]
        [Description("Destination FATX path.")]
        public string? DestinationPath { get; init; }

        [CommandOption("--overwrite")]
        [Description("Replace an existing destination file if it already exists.")]
        public bool Overwrite { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        string sourcePath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        if (string.IsNullOrWhiteSpace(settings.DestinationPath))
            throw new InvalidOperationException("Provide a destination FATX path with --to.");

        string destinationPath = FatmanCommandHelpers.NormalizeFatxPath(settings.DestinationPath);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: false);
        await using FatxDisk disk = await FatxDisk.OpenAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxMutationService mutation = new(volume);
        await mutation.MoveAsync(sourcePath, destinationPath, settings.Overwrite);

        if (settings.Json)
        {
            CliOutput.EmitJson(new {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                Partition = partition.Name,
                Path = sourcePath,
                Destination = destinationPath,
                Overwrite = settings.Overwrite
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Move complete.[/] [grey]{Markup.Escape(sourcePath)}[/] [white]->[/] [grey]{Markup.Escape(destinationPath)}[/]");
        }

        return 0;
    }
}

public sealed class FatmanDeleteCommand : AsyncCommand<FatmanDeleteCommand.Settings>
{
    public sealed class Settings : FatmanPathSettings
    {
        [CommandOption("--recursive")]
        [Description("Delete directory contents recursively.")]
        public bool Recursive { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        string fatxPath = FatmanCommandHelpers.RequireFatxPath(settings.Path);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: false);
        await using FatxDisk disk = await FatxDisk.OpenAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
        FatxMutationService mutation = new(volume);
        await mutation.DeleteAsync(fatxPath, settings.Recursive);

        if (settings.Json)
        {
            CliOutput.EmitJson(new {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                Partition = partition.Name,
                Path = fatxPath,
                Recursive = settings.Recursive
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]Entry removed.[/] [grey]{Markup.Escape(fatxPath)}[/]");
        }

        return 0;
    }
}

public class FatmanPartitionDumpSettings : FatmanPartitionSettings
{
    [CommandOption("--out <PATH>")]
    [Description("Directory to receive dumped partition images.")]
    public string? OutputDirectory { get; init; }
}

public class FatmanPathSettings : FatmanPartitionSettings
{
    [CommandOption("--path <FATXPATH>")]
    [Description("Path inside the selected FATX partition.")]
    public string? Path { get; init; }
}

public class FatmanPathExportSettings : FatmanPathSettings
{
    [CommandOption("--out <PATH>")]
    [Description("Host output path.")]
    public string? OutputPath { get; init; }
}

internal static class FatmanCommandHelpers
{
    private static readonly byte[] FatxMagic = Encoding.ASCII.GetBytes("FATX");
    private static readonly byte[] XtafMagic = Encoding.ASCII.GetBytes("XTAF");

    public static FatmanSourceInfo ResolveSource(FatmanImageSettings settings, bool readOnly = true)
        => FatmanWindowsStorage.ResolveSource(settings.ImagePath, settings.Disk, requireWritable: !readOnly);

    public static FatxOpenOptions CreateOpenOptions(FatmanSourceInfo source, FatmanImageSettings settings, bool readOnly = true)
    {
        long? offset = ParseOptionalLong(settings.Offset, "--offset");
        long? length = ParseOptionalLong(settings.Length, "--length");
        if (!offset.HasValue && length.HasValue)
            throw new InvalidOperationException("--length requires --offset.");

        long fileLength = source.Length;
        if (offset.HasValue)
        {
            if (offset.Value < 0)
                throw new InvalidOperationException("--offset must be zero or greater.");
            if (offset.Value >= fileLength)
                throw new InvalidOperationException($"--offset points past the end of the image (size 0x{fileLength:X}).");
        }

        if (length.HasValue)
        {
            if (length.Value <= 0)
                throw new InvalidOperationException("--length must be greater than zero.");
            if (offset!.Value + length.Value > fileLength)
                throw new InvalidOperationException($"The requested byte range exceeds the image size (size 0x{fileLength:X}).");
        }

        return new FatxOpenOptions
        {
            ReadOnly = readOnly,
            PartitionOffset = offset,
            PartitionLength = length
        };
    }

    public static object DescribeOpenOptions(FatxOpenOptions options)
    {
        return new
        {
            ManualOffset = options.PartitionOffset,
            ManualLength = options.PartitionLength
        };
    }

    public static object DescribeSource(FatmanSourceInfo source)
    {
        return new
        {
            source.DisplayName,
            source.SourcePath,
            source.Length,
            source.IsPhysicalDisk,
            Disk = source.Disk is null ? null : new
            {
                source.Disk.Number,
                source.Disk.DevicePath,
                source.Disk.FriendlyName,
                source.Disk.SizeBytes,
                source.Disk.InterfaceType,
                source.Disk.MediaType,
                source.Disk.MountedVolumes
            }
        };
    }

    public static async Task<IReadOnlyList<FatmanVolumeCandidateInfo>> ScanVolumeCandidatesAsync(string imagePath, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new InvalidOperationException("--limit must be greater than zero.");

        List<FatmanVolumeCandidateInfo> results = new();
        const int sectorSize = 0x200;
        const int bufferSize = 16 * 1024 * 1024;
        byte[] buffer = new byte[bufferSize];
        long baseOffset = 0;

        await using FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize, useAsync: true);
        while (results.Count < limit)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
                break;

            for (int index = 0; index <= read - 4 && results.Count < limit; index += sectorSize)
            {
                long offset = baseOffset + index;
                FatmanVolumeCandidateInfo? candidate = await ProbeVolumeCandidateAsync(stream, offset, cancellationToken);
                if (candidate is null)
                    continue;

                if (results.Any(existing => existing.Offset == candidate.Offset))
                    continue;

                results.Add(candidate);
            }

            baseOffset += read;
        }

        return results;
    }

    public static string RequireOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("Provide an output directory with --out.");
        return Path.GetFullPath(outputPath);
    }

    public static string RequireFatxPath(string? fatxPath)
    {
        if (string.IsNullOrWhiteSpace(fatxPath))
            throw new InvalidOperationException("Provide a FATX path with --path.");
        return NormalizeFatxPath(fatxPath);
    }

    public static async Task<IReadOnlyList<FatmanPartitionInfo>> DescribePartitionsAsync(FatxDisk disk)
    {
        List<FatmanPartitionInfo> rows = new List<FatmanPartitionInfo>();
        foreach (FatxPartition partition in disk.Partitions)
        {
            bool isFatx = false;
            string? label = null;
            string? magic = null;
            try
            {
                await using FatxVolume volume = await disk.OpenVolumeAsync(partition);
                isFatx = true;
                label = volume.Info.Label;
                magic = volume.Info.Magic;
            }
            catch (FatxException)
            {
                // ignored
            }

            rows.Add(new FatmanPartitionInfo(
                partition.Index,
                partition.Name,
                partition.Kind.ToString(),
                partition.Offset,
                partition.Length,
                isFatx,
                label,
                magic));
        }

        return rows;
    }

    public static async Task<FatxPartition> ResolvePartitionAsync(FatxDisk disk, string? selector)
    {
        IReadOnlyList<FatxPartition> matches = await ResolvePartitionsAsync(disk, selector);
        return matches[0];
    }

    public static Task<IReadOnlyList<FatxPartition>> ResolvePartitionsAsync(FatxDisk disk, string? selector)
    {
        if (disk.Partitions.Count == 0)
            throw new InvalidOperationException("No partitions were detected in this image.");

        if (string.IsNullOrWhiteSpace(selector))
        {
            FatxPartition? preferred = disk.Partitions.FirstOrDefault(p => p.Kind == FatxPartitionKind.Content)
                ?? disk.Partitions.FirstOrDefault(p => p.Kind == FatxPartitionKind.Compatibility)
                ?? disk.Partitions.First();
            return Task.FromResult<IReadOnlyList<FatxPartition>>(new[] { preferred });
        }

        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            FatxPartition match = disk.Partitions.FirstOrDefault(p => p.Index == index)
                ?? throw new InvalidOperationException($"Partition index {index} was not found.");
            return Task.FromResult<IReadOnlyList<FatxPartition>>(new[] { match });
        }

        List<FatxPartition> named = disk.Partitions
            .Where(p =>
                p.Name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                p.Kind.ToString().Equals(selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (named.Count == 0)
            throw new InvalidOperationException($"Partition '{selector}' was not found.");
        return Task.FromResult<IReadOnlyList<FatxPartition>>(named);
    }

    public static long? ParseOptionalLong(string? rawValue, string optionName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        string trimmed = rawValue.Trim();
        NumberStyles styles = NumberStyles.Integer;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
            styles = NumberStyles.AllowHexSpecifier;
        }

        if (!long.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out long value))
            throw new InvalidOperationException($"Could not parse {optionName} value '{rawValue}'. Use decimal bytes or 0x-prefixed hex.");

        return value;
    }

    private static bool MatchesMagic(byte[] buffer, int index, byte[] magic)
    {
        return buffer[index] == magic[0] &&
               buffer[index + 1] == magic[1] &&
               buffer[index + 2] == magic[2] &&
               buffer[index + 3] == magic[3];
    }

    private static async Task<FatmanVolumeCandidateInfo?> ProbeVolumeCandidateAsync(FileStream stream, long offset, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset > stream.Length - 0x10)
            return null;

        byte[] header = new byte[0x40];
        stream.Position = offset;
        int read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (read < 0x10)
            return null;

        if (!MatchesMagic(header, 0, FatxMagic) && !MatchesMagic(header, 0, XtafMagic))
        {
            return null;
        }
        string magic = Encoding.ASCII.GetString(header, 0, 4);

        uint sectorsPerCluster = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        uint rootDirectoryCluster = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x0C, 4));
        if (!IsPlausibleSectorsPerCluster(sectorsPerCluster))
            return null;

        long clusterSize = sectorsPerCluster * 0x200L;
        if (clusterSize <= 0 || clusterSize > 0x20000)
            return null;

        string notes = offset % 0x200 == 0
            ? "sector-aligned candidate"
            : "non-sector-aligned candidate";

        return new FatmanVolumeCandidateInfo(
            offset,
            magic.ToUpperInvariant(),
            sectorsPerCluster,
            clusterSize,
            rootDirectoryCluster == 0 ? 1u : rootDirectoryCluster,
            notes);
    }

    private static bool IsPlausibleSectorsPerCluster(uint value)
    {
        if (value == 0 || value > 128)
            return false;

        return (value & (value - 1)) == 0;
    }

    public static string SanitizeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    public static string NormalizeFatxPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        string normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    public static string ResolveExportPath(string? outputPath, string defaultName)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return Path.GetFullPath(defaultName);

        string full = Path.GetFullPath(outputPath);
        if (Directory.Exists(full))
            return Path.Combine(full, defaultName);

        if (full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar))
            return Path.Combine(full, defaultName);

        return full;
    }

    public static FatmanEntryInfo ToEntryInfo(FatxEntry entry)
    {
        uint? firstCluster = entry switch
        {
            FatxDirectoryEntry directory => directory.FirstCluster,
            FatxFileEntry file => file.FirstCluster,
            _ => null
        };

        return new FatmanEntryInfo(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.Size,
            firstCluster,
            entry.Attributes.ToString(),
            entry.CreatedAtUtc,
            entry.ModifiedAtUtc);
    }

    public static async Task CollectMatchingEntriesAsync(FatxVolume volume, string rootPath, string query, List<FatmanEntryInfo> matches)
    {
        IReadOnlyList<FatxEntry> entries = await volume.ListDirectoryAsync(rootPath);
        foreach (FatxEntry entry in entries)
        {
            if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(ToEntryInfo(entry));
            }

            if (entry is FatxDirectoryEntry)
                await CollectMatchingEntriesAsync(volume, entry.FullPath, query, matches);
        }
    }

    public static string DecodeText(byte[] payload)
    {
        if (payload.Length >= 2)
        {
            if (payload[0] == 0xFF && payload[1] == 0xFE)
                return Encoding.Unicode.GetString(payload, 2, payload.Length - 2);
            if (payload[0] == 0xFE && payload[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(payload, 2, payload.Length - 2);
        }

        if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
            return Encoding.UTF8.GetString(payload, 3, payload.Length - 3);

        try
        {
            return new UTF8Encoding(false, true).GetString(payload);
        }
        catch
        {
            return Encoding.Latin1.GetString(payload);
        }
    }

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
}

internal sealed record FatmanDriveInfo(
    string Name,
    string Type,
    string? Format,
    string? Label,
    bool Ready,
    long? TotalBytes,
    long? FreeBytes);

internal sealed record FatmanPartitionInfo(
    int Index,
    string Name,
    string Kind,
    long Offset,
    long Length,
    bool IsFatx,
    string? Label,
    string? Magic);

internal sealed record FatmanEntryInfo(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    uint? FirstCluster,
    string Attributes,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

internal sealed record FatmanVolumeCandidateInfo(
    long Offset,
    string Magic,
    uint SectorsPerCluster,
    long ClusterSize,
    uint RootDirectoryCluster,
    string Notes);
