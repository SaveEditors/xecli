using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NotifyIconsListCommand : Command<NotifyIconsListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--builtins-only")]
		[LocalizedDescription("Show only the built-in XNotify icon catalog.")]
		public bool BuiltinsOnly { get; init; }

		[CommandOption("--presets-only")]
		[LocalizedDescription("Show only user-defined preset aliases from config.")]
		public bool PresetsOnly { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (settings.BuiltinsOnly && settings.PresetsOnly)
		{
			AnsiConsole.MarkupLine("[red]Choose either --builtins-only or --presets-only.[/]");
			return 1;
		}
		int value;
		if (!settings.PresetsOnly)
		{
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Built-In XNotify Icons[/]").RuleStyle("silver"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[white]ID[/]"));
			table.AddColumn(new TableColumn("[green]Name[/]"));
			table.AddColumn(new TableColumn("[deepskyblue1]Label[/]"));
			table.AddColumn(new TableColumn("[mediumpurple3]Notes[/]"));
			foreach (NotifyLogoDefinition item in NotifyCatalog.All.OrderBy((NotifyLogoDefinition x) => x.Id))
			{
				string[] array = new string[4];
				value = item.Id;
				array[0] = "[white]" + value.ToString(CultureInfo.InvariantCulture) + "[/]";
				array[1] = "[green]" + Markup.Escape(item.Key) + "[/]";
				array[2] = "[deepskyblue1]" + Markup.Escape(item.Label) + "[/]";
				array[3] = "[mediumpurple3]" + Markup.Escape(item.Notes ?? "-") + "[/]";
				table.AddRow(array);
			}
			AnsiConsole.Write(table);
		}
		if (settings.BuiltinsOnly)
		{
			return 0;
		}
		CliConfig cliConfig = CliConfig.Load();
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Preset Aliases[/]").RuleStyle("silver"));
		if (cliConfig.NotifyIcons == null || cliConfig.NotifyIcons.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No notify icon presets configured.[/]");
			return 0;
		}
		Table table2 = CliOutput.CreateTable();
		table2.AddColumn(new TableColumn("[green]Preset[/]"));
		table2.AddColumn(new TableColumn("[cyan]Logo ID[/]"));
		table2.AddColumn(new TableColumn("[deepskyblue1]Resolved[/]"));
		foreach (KeyValuePair<string, int> item2 in cliConfig.NotifyIcons.OrderBy<KeyValuePair<string, int>, string>((KeyValuePair<string, int> kv) => kv.Key, StringComparer.OrdinalIgnoreCase))
		{
			item2.Deconstruct(out var key, out value);
			string text = key;
			int logo = value;
			table2.AddRow("[green]" + Markup.Escape(text) + "[/]", "[cyan]" + logo.ToString(CultureInfo.InvariantCulture) + "[/]", "[deepskyblue1]" + Markup.Escape(NotifyHelpers.DescribeLogo(logo)) + "[/]");
		}
		AnsiConsole.Write(table2);
		return 0;
	}
}
