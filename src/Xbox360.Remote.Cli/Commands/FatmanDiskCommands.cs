using System.ComponentModel;
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
        [Description("Volume label to apply when formatting a single selected partition.")]
        public string? Label { get; init; }

        [CommandOption("--full-zero")]
        [Description("Zero the full target partition instead of performing a quick FATX initialization.")]
        public bool FullZero { get; init; }

        [CommandOption("--sectors-per-cluster <COUNT>")]
        [Description("Override the FATX sectors-per-cluster value (must be a power of two).")]
        public uint? SectorsPerCluster { get; init; }

        [CommandOption("--auto-confirm")]
        [Description("Skip the destructive-action confirmation prompt.")]
        public bool AutoConfirm { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: false);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: false);
        await using FatxDisk disk = await FatxDisk.OpenAsync(source.SourcePath, openOptions);

        IReadOnlyList<FatxPartition> selected;
        if (!string.IsNullOrWhiteSpace(settings.Partition))
        {
            selected = await FatmanCommandHelpers.ResolvePartitionsAsync(disk, settings.Partition);
        }
        else if (!openOptions.PartitionOffset.HasValue)
        {
            IReadOnlyList<Xbox360PartitionDefinition> retailPlan = Xbox360RetailLayoutPlanner.Create(source.Length);
            selected = retailPlan.Count == 0
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
        else
        {
            selected = disk.Partitions;
        }
        if (selected.Count == 0)
            throw new InvalidOperationException("No FATX partitions are available to format.");

        if (!settings.AutoConfirm)
        {
            string partitionList = string.Join(", ", selected.Select(partition => partition.Name));
            string warning = source.IsPhysicalDisk && source.Disk is not null
                ? $"This will erase FATX data on {source.Disk.FriendlyName} ({source.Disk.DevicePath}) for: {partitionList}. Continue?"
                : $"This will erase FATX data inside {source.DisplayName} for: {partitionList}. Continue?";
            if (!AnsiConsole.Confirm(warning, false))
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

    private static string ResolveLabel(Settings settings, FatxPartition partition, int partitionCount)
    {
        if (partitionCount == 1 && !string.IsNullOrWhiteSpace(settings.Label))
            return settings.Label.Trim();
        return partition.Name;
    }
}
