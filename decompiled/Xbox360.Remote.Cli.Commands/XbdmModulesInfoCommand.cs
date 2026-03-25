using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmModulesInfoCommand : AsyncCommand<XbdmModulesInfoCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--name <MODULE>")]
		[Description("Module name, e.g. default.xex or xam.xex.")]
		public string? Name { get; init; }

		[CommandOption("--sections")]
		[Description("Include section details.")]
		public bool Sections { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Name))
		{
			AnsiConsole.MarkupLine("[red]--name is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientOnceAsync(settings, async delegate(XbdmClient client)
		{
			XbdmModuleInfo xbdmModuleInfo = (await client.GetModulesAsync(settings.Sections, CancellationToken.None)).FirstOrDefault((XbdmModuleInfo m) => string.Equals(m.Name, settings.Name, StringComparison.OrdinalIgnoreCase));
			if (xbdmModuleInfo == null)
			{
				AnsiConsole.MarkupLine("[red]Module not found:[/] " + settings.Name);
				return 1;
			}
			if (settings.Json)
			{
				CliOutput.EmitJson(xbdmModuleInfo);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]" + Markup.Escape(xbdmModuleInfo.Name) + "[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[grey]Field[/]"));
			table.AddColumn(new TableColumn("[white]Value[/]"));
			table.AddRow("[grey]Base[/]", $"[cyan]0x{xbdmModuleInfo.BaseAddress:X8}[/]");
			table.AddRow("[grey]Size[/]", $"[cyan]0x{xbdmModuleInfo.ModuleSize:X8}[/]");
			table.AddRow("[grey]Entry[/]", xbdmModuleInfo.EntryPoint.HasValue ? $"[gold1]0x{xbdmModuleInfo.EntryPoint.Value:X8}[/]" : "[grey]unknown[/]");
			table.AddRow("[grey]Timestamp[/]", CliOutput.FormatTimestamp(xbdmModuleInfo.Timestamp));
			AnsiConsole.Write(table);
			if (settings.Sections && xbdmModuleInfo.Sections.Count > 0)
			{
				AnsiConsole.Write(new Rule("[bold deepskyblue1]Sections[/]").RuleStyle("grey"));
				Table table2 = CliOutput.CreateTable();
				table2.AddColumn(new TableColumn("[green]Name[/]"));
				table2.AddColumn(new TableColumn("[cyan]Base[/]"));
				table2.AddColumn(new TableColumn("[cyan]Size[/]"));
				table2.AddColumn(new TableColumn("[grey]Index[/]"));
				table2.AddColumn(new TableColumn("[grey]Flags[/]"));
				foreach (XbdmSectionInfo section in xbdmModuleInfo.Sections)
				{
					table2.AddRow((section.Name != null) ? ("[green]" + Markup.Escape(section.Name) + "[/]") : "[grey](unnamed)[/]", $"[cyan]0x{section.BaseAddress:X8}[/]", $"[cyan]0x{section.Size:X8}[/]", $"[grey]{section.Index}[/]", $"[grey]0x{section.Flags:X8}[/]");
				}
				AnsiConsole.Write(table2);
			}
			return 0;
		}, CancellationToken.None);
	}
}
