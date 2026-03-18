using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesInfoCommand : AsyncCommand<XbdmModulesInfoCommand.Settings> {
    public sealed class Settings : ConnectionSettings {
        [CommandOption("--name <MODULE>")]
        [Description("Module name, e.g. default.xex or xam.xex.")]
        public string? Name { get; init; }

        [CommandOption("--sections")]
        [Description("Include section details.")]
        public bool Sections { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings) {
        if (string.IsNullOrWhiteSpace(settings.Name)) {
            AnsiConsole.MarkupLine("[red]--name is required.[/]");
            return 1;
        }

        return await CliHelpers.WithClientAsync(settings, async client => {
            IReadOnlyList<XbdmModuleInfo> modules = await client.GetModulesAsync(settings.Sections, CancellationToken.None);
            XbdmModuleInfo? module = modules.FirstOrDefault(m => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
            if (module == null) {
                AnsiConsole.MarkupLine($"[red]Module not found:[/] {settings.Name}");
                return 1;
            }

            if (settings.Json) {
                CliOutput.EmitJson(module);
                return 0;
            }

            AnsiConsole.Write(new Rule($"[bold deepskyblue1]{Markup.Escape(module.Name)}[/]").RuleStyle("grey"));
            Table table = CliOutput.CreateTable();
            table.AddColumn(new TableColumn("[grey]Field[/]"));
            table.AddColumn(new TableColumn("[white]Value[/]"));
            table.AddRow("[grey]Base[/]", $"[cyan]0x{module.BaseAddress:X8}[/]");
            table.AddRow("[grey]Size[/]", $"[cyan]0x{module.ModuleSize:X8}[/]");
            table.AddRow("[grey]Entry[/]", module.EntryPoint.HasValue ? $"[gold1]0x{module.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]");
            table.AddRow("[grey]Timestamp[/]", CliOutput.FormatTimestamp(module.Timestamp));
            AnsiConsole.Write(table);

            if (settings.Sections && module.Sections.Count > 0) {
                AnsiConsole.Write(new Rule("[bold deepskyblue1]Sections[/]").RuleStyle("grey"));
                Table secTable = CliOutput.CreateTable();
                secTable.AddColumn(new TableColumn("[green]Name[/]"));
                secTable.AddColumn(new TableColumn("[cyan]Base[/]"));
                secTable.AddColumn(new TableColumn("[cyan]Size[/]"));
                secTable.AddColumn(new TableColumn("[grey]Index[/]"));
                secTable.AddColumn(new TableColumn("[grey]Flags[/]"));
                foreach (XbdmSectionInfo sec in module.Sections) {
                    secTable.AddRow(
                        sec.Name != null ? $"[green]{Markup.Escape(sec.Name)}[/]" : "[grey](unnamed)[/]",
                        $"[cyan]0x{sec.BaseAddress:X8}[/]",
                        $"[cyan]0x{sec.Size:X8}[/]",
                        $"[grey]{sec.Index}[/]",
                        $"[grey]0x{sec.Flags:X8}[/]");
                }
                AnsiConsole.Write(secTable);
            }

            return 0;
        }, CancellationToken.None);
    }
}
