using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Fatx;
using Xbox360.Fatx.Formatting;
using Xbox360.Fatx.Layout;
using Xbox360.Remote.Cli.Fatman;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FatmanDisksCommand : Command<FatmanDisksCommand.Settings>
{
    public sealed class Settings : FatmanBaseSettings { }

    public override int Execute(CommandContext context, Settings settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("fatman disks is currently supported on Windows only.");

        IReadOnlyList<FatmanPhysicalDiskInfo> disks = FatmanWindowsStorage.EnumeratePhysicalDisks();
        if (settings.Json)
        {
            CliOutput.EmitJson(disks);
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Disk[/]"));
        table.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
        table.AddColumn(new TableColumn("[bold deepskyblue1]Interface[/]"));
        table.AddColumn(new TableColumn("[bold cyan1]Size[/]"));
        table.AddColumn(new TableColumn("[bold gold1]Mounted Volumes[/]"));
        foreach (FatmanPhysicalDiskInfo disk in disks)
        {
            table.AddRow(
                $"[white]{disk.Number}[/]",
                $"[springgreen3_1]{Markup.Escape(disk.FriendlyName)}[/]",
                string.IsNullOrWhiteSpace(disk.InterfaceType) ? "[grey70]-[/]" : $"[deepskyblue1]{Markup.Escape(disk.InterfaceType)}[/]",
                FatmanCommandHelpers.FormatBytes(disk.SizeBytes is null ? null : (long?)disk.SizeBytes.Value),
                disk.MountedVolumes.Count == 0
                    ? "[grey70]-[/]"
                    : $"[gold1]{Markup.Escape(string.Join(", ", disk.MountedVolumes))}[/]");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class FatmanFormatCommand : AsyncCommand<FatmanFormatCommand.Settings>
{
    public sealed class Settings : FatmanPartitionSettings
    {
        [CommandOption("--label <TEXT>")]
        [LocalizedDescription("Volume label to apply when formatting a single selected partition.")]
        public string? Label { get; init; }

        [CommandOption("--full-zero")]
        [LocalizedDescription("Zero the full target partition instead of performing a quick FATX initialization.")]
        public bool FullZero { get; init; }

        [CommandOption("--sectors-per-cluster <COUNT>")]
        [LocalizedDescription("Override the FATX sectors-per-cluster value (must be a power of two).")]
        public uint? SectorsPerCluster { get; init; }

        [CommandOption("--auto-confirm")]
        [LocalizedDescription("Skip the destructive-action confirmation prompt.")]
        public bool AutoConfirm { get; init; }

        [CommandOption("--dry-run")]
        [LocalizedDescription("Show the partitions and format options that would be used without writing anything.")]
        public bool DryRun { get; init; }
    }

    private sealed record FormatPreviewItem(
        int Index,
        string Name,
        string Kind,
        long Offset,
        long Length,
        string Label);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        bool dryRun = settings.DryRun;
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: dryRun);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: dryRun);
        await using FatxDisk disk = dryRun
            ? await FatxDisk.OpenReadAsync(source.SourcePath, openOptions)
            : await FatxDisk.OpenAsync(source.SourcePath, openOptions);

        IReadOnlyList<FatxPartition> selected = await ResolveSelectedPartitionsAsync(disk, source, openOptions, settings);
        if (selected.Count == 0)
            throw new InvalidOperationException("No FATX partitions are available to format.");

        if (dryRun)
        {
            IReadOnlyList<FormatPreviewItem> preview = BuildPreview(selected, settings);
            if (settings.Json)
            {
                CliOutput.EmitJson(new
                {
                    Source = FatmanCommandHelpers.DescribeSource(source),
                    Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                    DryRun = true,
                    FullZero = settings.FullZero,
                    settings.SectorsPerCluster,
                    Partitions = preview
                });
            }
            else
            {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Fatman Format Preview[/]").RuleStyle("grey"));
                AnsiConsole.MarkupLine($"[grey]Source:[/] [white]{Markup.Escape(source.DisplayName)}[/]");
                AnsiConsole.MarkupLine($"[grey]Open:[/] {FormatOpenOptionsText(openOptions)}");
                AnsiConsole.MarkupLine($"[grey]Mode:[/] {(settings.FullZero ? "full-zero" : "quick initialization")}, [grey]sectors-per-cluster:[/] {(settings.SectorsPerCluster.HasValue ? settings.SectorsPerCluster.Value.ToString(CultureInfo.InvariantCulture) : "default")}");

                Table previewTable = CliOutput.CreateTable();
                previewTable.AddColumn(new TableColumn("[bold white]Index[/]"));
                previewTable.AddColumn(new TableColumn("[bold springgreen3_1]Name[/]"));
                previewTable.AddColumn(new TableColumn("[bold deepskyblue1]Kind[/]"));
                previewTable.AddColumn(new TableColumn("[bold cyan1]Offset[/]"));
                previewTable.AddColumn(new TableColumn("[bold gold1]Length[/]"));
                previewTable.AddColumn(new TableColumn("[bold white]Label[/]"));
                foreach (FormatPreviewItem row in preview)
                {
                    previewTable.AddRow(
                        $"[white]{row.Index}[/]",
                        $"[springgreen3_1]{Markup.Escape(row.Name)}[/]",
                        $"[deepskyblue1]{Markup.Escape(row.Kind)}[/]",
                        $"[cyan1]0x{row.Offset:X}[/]",
                        FatmanCommandHelpers.FormatBytes(row.Length),
                        $"[white]{Markup.Escape(row.Label)}[/]");
                }

                AnsiConsole.Write(previewTable);
            }

            return 0;
        }

        if (!settings.AutoConfirm)
        {
            string partitionList = string.Join(", ", selected.Select(partition => partition.Name));
            string warning = source.IsPhysicalDisk && source.Disk is not null
                ? $"This will erase FATX data on {source.Disk.FriendlyName} ({source.Disk.DevicePath}) for: {partitionList}. Continue?"
                : $"This will erase FATX data inside {source.DisplayName} for: {partitionList}. Continue?";
            if (!ConfirmationHelpers.TryConfirm(
                    "Fatman format",
                    warning,
                    settings.AutoConfirm,
                    emitWarning: false))
            {
                AnsiConsole.MarkupLine("[yellow]Fatman format cancelled.[/]");
                return 1;
            }
        }

        IReadOnlyList<CliOutput.TransferBatchItem> items = selected
            .Select(partition => new CliOutput.TransferBatchItem(partition.Name, partition.Length))
            .ToArray();

        List<object> results = new();
        await CliOutput.RunBatchProgressAsync("Formatting FATX partitions", items, async scope => {
            foreach (FatxPartition partition in selected)
            {
                scope.StartFile(partition.Name, partition.Length);
                await using Stream partitionStream = disk.OpenPartitionStream(partition);
                string label = ResolveLabel(settings, partition, selected.Count);
                FatxFormatOptions options = new()
                {
                    Label = label,
                    FullZero = settings.FullZero,
                    SectorsPerCluster = settings.SectorsPerCluster
                };

                FatxVolumeHeader header = await FatxFormatter.FormatAsync(
                    partitionStream,
                    options,
                    new Progress<long>(written => scope.ReportFileProgress(written, $"formatting {partition.Name}")),
                    CancellationToken.None);

                results.Add(new
                {
                    partition.Index,
                    partition.Name,
                    partition.Kind,
                    partition.Offset,
                    partition.Length,
                    header.Magic,
                    header.Label,
                    header.ClusterSize,
                    header.RootDirectoryCluster,
                    header.TotalClusters
                });
                scope.CompleteFile();
            }
        });

        if (settings.Json)
        {
            CliOutput.EmitJson(new
            {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                FullZero = settings.FullZero,
                settings.SectorsPerCluster,
                Partitions = results
            });
        }
        else
        {
            AnsiConsole.MarkupLine("[green]Format complete.[/]");
        }

        return 0;
    }

    private static async Task<IReadOnlyList<FatxPartition>> ResolveSelectedPartitionsAsync(FatxDisk disk, FatmanSourceInfo source, FatxOpenOptions openOptions, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Partition))
            return await FatmanCommandHelpers.ResolvePartitionsAsync(disk, settings.Partition);

        if (!openOptions.PartitionOffset.HasValue)
        {
            IReadOnlyList<Xbox360PartitionDefinition> retailPlan = Xbox360RetailLayoutPlanner.Create(source.Length);
            return retailPlan.Count == 0
                ? disk.Partitions
                : retailPlan.Select(partition => new FatxPartition
                {
                    Index = partition.Index,
                    Name = partition.Name,
                    Kind = partition.Kind,
                    Offset = partition.Offset,
                    Length = partition.Length
                }).ToArray();
        }

        return disk.Partitions;
    }

    private static IReadOnlyList<FormatPreviewItem> BuildPreview(IReadOnlyList<FatxPartition> selected, Settings settings)
        => selected.Select(partition => new FormatPreviewItem(
            partition.Index,
            partition.Name,
            partition.Kind.ToString(),
            partition.Offset,
            partition.Length,
            ResolveLabel(settings, partition, selected.Count))).ToArray();

    private static string FormatOpenOptionsText(FatxOpenOptions options)
    {
        string offset = options.PartitionOffset.HasValue
            ? $"0x{options.PartitionOffset.Value:X}"
            : "default";
        string length = options.PartitionLength.HasValue
            ? $"0x{options.PartitionLength.Value:X}"
            : "default";
        return $"offset=[cyan]{offset}[/], length=[cyan]{length}[/]";
    }

    private static string ResolveLabel(Settings settings, FatxPartition partition, int partitionCount)
    {
        if (partitionCount == 1 && !string.IsNullOrWhiteSpace(settings.Label))
            return settings.Label.Trim();
        return partition.Name;
    }
}

