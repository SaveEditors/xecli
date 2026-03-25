using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Fatx;
using Xbox360.Fatx.Repair;
using Xbox360.Remote.Cli.Fatman;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FatmanCheckCommand : AsyncCommand<FatmanCheckCommand.Settings>
{
    public sealed class Settings : FatmanPartitionSettings { }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
        => FatmanRepairCommand.RunAsync(settings, applyRepairs: false);
}

public sealed class FatmanRepairCommand : AsyncCommand<FatmanRepairCommand.Settings>
{
    public sealed class Settings : FatmanPartitionSettings
    {
        [CommandOption("--auto-confirm")]
        [LocalizedDescription("Skip the destructive-action confirmation prompt when applying repairs.")]
        public bool AutoConfirm { get; init; }
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
        => RunAsync(settings, applyRepairs: true);

    internal static async Task<int> RunAsync(FatmanPartitionSettings settings, bool applyRepairs)
    {
        FatmanSourceInfo source = FatmanCommandHelpers.ResolveSource(settings, readOnly: !applyRepairs);
        FatxOpenOptions openOptions = FatmanCommandHelpers.CreateOpenOptions(source, settings, readOnly: !applyRepairs);
        await using FatxDisk disk = applyRepairs
            ? await FatxDisk.OpenAsync(source.SourcePath, openOptions)
            : await FatxDisk.OpenReadAsync(source.SourcePath, openOptions);
        FatxPartition partition = await FatmanCommandHelpers.ResolvePartitionAsync(disk, settings.Partition);
        await using FatxVolume volume = await disk.OpenVolumeAsync(partition);

        bool autoConfirm = settings is Settings repairSettings && repairSettings.AutoConfirm;
        if (applyRepairs && !autoConfirm)
        {
            if (!AnsiConsole.Confirm($"This will modify FATX chain metadata in {partition.Name}. Continue?", false))
            {
                AnsiConsole.MarkupLine("[yellow]Fatman repair cancelled.[/]");
                return 1;
            }
        }

        FatxRepairService repair = new(volume);
        FatxRepairReport report = await repair.AnalyzeAsync(applyRepairs);

        if (settings.Json)
        {
            CliOutput.EmitJson(new
            {
                Source = FatmanCommandHelpers.DescribeSource(source),
                Open = FatmanCommandHelpers.DescribeOpenOptions(openOptions),
                Report = report
            });
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[bold white]Field[/]"));
        table.AddColumn(new TableColumn("[bold white]Value[/]"));
        table.AddRow("[white]Partition[/]", $"[springgreen3_1]{Markup.Escape(report.Partition)}[/]");
        table.AddRow("[white]Allocated Clusters[/]", $"[deepskyblue1]{report.AllocatedClusters}[/]");
        table.AddRow("[white]Referenced Clusters[/]", $"[deepskyblue1]{report.ReferencedClusters}[/]");
        table.AddRow("[white]Orphaned Clusters[/]", $"[gold1]{report.OrphanedClusters}[/]");
        table.AddRow("[white]Invalid Unreferenced[/]", $"[gold1]{report.InvalidUnreferencedClusters}[/]");
        table.AddRow("[white]Repairs Applied[/]", report.RepairsApplied ? "[springgreen3_1]Yes[/]" : "[grey70]No[/]");
        if (report.RepairsApplied)
        {
            table.AddRow("[white]Reclaimed Chains[/]", $"[springgreen3_1]{report.ReclaimedChains}[/]");
            table.AddRow("[white]Reclaimed Clusters[/]", $"[springgreen3_1]{report.ReclaimedClusters}[/]");
        }

        AnsiConsole.Write(table);
        if (report.Issues.Count > 0)
        {
            AnsiConsole.Write(new Rule("[bold gold1]Repair Notes[/]").RuleStyle("silver"));
            foreach (string issue in report.Issues.Take(20))
            {
                AnsiConsole.MarkupLine($"[yellow]-[/] {Markup.Escape(issue)}");
            }
            if (report.Issues.Count > 20)
                AnsiConsole.MarkupLine($"[grey70]... {report.Issues.Count - 20} more issue(s) omitted[/]");
        }

        return 0;
    }
}

