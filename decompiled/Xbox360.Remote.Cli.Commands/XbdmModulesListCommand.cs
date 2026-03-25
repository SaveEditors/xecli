using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesListCommand : AsyncCommand<XbdmModulesListCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--sections")]
		[LocalizedDescription("Include section details.")]
		public bool Sections { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			IReadOnlyList<XbdmModuleInfo> readOnlyList = await client.GetModulesAsync(settings.Sections, CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(readOnlyList);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Modules[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(AlignableExtensions.Centered(new TableColumn("[grey]#[/]")));
			table.AddColumn(new TableColumn("[green]Name[/]"));
			table.AddColumn(new TableColumn("[cyan]Base[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[gold1]Entry[/]"));
			table.AddColumn(new TableColumn("[grey]Timestamp (Local)[/]"));
			int num = 1;
			foreach (XbdmModuleInfo item in readOnlyList)
			{
				table.AddRow($"[grey]{num}[/]", "[green]" + Markup.Escape(item.Name) + "[/]", $"[cyan]0x{item.BaseAddress:X8}[/]", $"[cyan]0x{item.ModuleSize:X8}[/]", item.EntryPoint.HasValue ? $"[gold1]0x{item.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]", CliOutput.FormatTimestamp(item.Timestamp));
				num++;
			}
			AnsiConsole.Write(table);
			if (settings.Sections)
			{
				foreach (XbdmModuleInfo item2 in readOnlyList)
				{
					if (item2.Sections.Count != 0)
					{
						AnsiConsole.Write(new Rule("[bold deepskyblue1]Sections for " + Markup.Escape(item2.Name) + "[/]").RuleStyle("grey"));
						Table table2 = CliOutput.CreateTable();
						table2.AddColumn(new TableColumn("[green]Name[/]"));
						table2.AddColumn(new TableColumn("[cyan]Base[/]"));
						table2.AddColumn(new TableColumn("[cyan]Size[/]"));
						table2.AddColumn(new TableColumn("[grey]Index[/]"));
						table2.AddColumn(new TableColumn("[grey]Flags[/]"));
						foreach (XbdmSectionInfo section in item2.Sections)
						{
							table2.AddRow((section.Name != null) ? ("[green]" + Markup.Escape(section.Name) + "[/]") : "[grey](unnamed)[/]", $"[cyan]0x{section.BaseAddress:X8}[/]", $"[cyan]0x{section.Size:X8}[/]", $"[grey]{section.Index}[/]", $"[grey]0x{section.Flags:X8}[/]");
						}
						AnsiConsole.Write(table2);
					}
				}
			}
			return 0;
		}, CancellationToken.None);
	}
}
