using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class PluginListCommand : AsyncCommand<PluginListCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--ini <PATH>")]
		[LocalizedDescription("DashLaunch config path (default: /Hdd1/launch.ini).")]
		public string? IniPath { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		(string, int, string, string, int) obj = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string item = obj.Item1;
		int item2 = obj.Item2;
		string item3 = obj.Item3;
		string item4 = obj.Item4;
		int item5 = obj.Item5;
		PluginHelpers.PluginConfig pluginConfig = await PluginHelpers.LoadAsync(item, item2, item3, item4, item5, settings.IniPath);
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Path = pluginConfig.Path,
				Plugins = from s in pluginConfig.Slots
					orderby s.Key
					select new
					{
						Slot = s.Key,
						Path = s.Value
					}
			});
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Plugins[/] [grey]" + Markup.Escape(pluginConfig.Path) + "[/]").RuleStyle("grey"));
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[cyan]Slot[/]"));
		table.AddColumn(new TableColumn("[green]Path[/]"));
		foreach (var (value, text2) in pluginConfig.Slots.OrderBy<KeyValuePair<int, string>, int>((KeyValuePair<int, string> s) => s.Key))
		{
			table.AddRow($"[cyan]plugin{value}[/]", string.IsNullOrWhiteSpace(text2) ? "[grey]disabled[/]" : ("[green]" + Markup.Escape(text2) + "[/]"));
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
