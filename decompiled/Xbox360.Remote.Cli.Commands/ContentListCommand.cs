using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ContentListCommand : AsyncCommand<ContentListCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--device <ROOTS>")]
		[LocalizedDescription("Comma-separated content roots, for example Hdd1 or Hdd1,Usb0 (default: Hdd1,Usb0,Usb1,HddX).")]
		public string? Devices { get; init; }

		[CommandOption("--titleid <TITLEID>")]
		[LocalizedDescription("Restrict to one Title ID.")]
		public string? TitleId { get; init; }

		[CommandOption("--show-types")]
		[LocalizedDescription("Include content-type breakdowns per title.")]
		public bool ShowTypes { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		uint? titleId = null;
		if (!string.IsNullOrWhiteSpace(settings.TitleId))
		{
			if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId2))
			{
				AnsiConsole.MarkupLine("[red]Invalid --titleid.[/]");
				return 1;
			}
			titleId = titleId2;
		}
		(string, int, string, string, int) obj = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string item = obj.Item1;
		int item2 = obj.Item2;
		string item3 = obj.Item3;
		string item4 = obj.Item4;
		int item5 = obj.Item5;
		List<ContentHelpers.ContentEntry> list = await ContentHelpers.ListAsync(item, item2, item3, item4, item5, settings.Devices, titleId, settings.ShowTypes);
		if (settings.Json)
		{
			CliOutput.EmitJson(list);
			return 0;
		}
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Installed Content[/]").RuleStyle("grey"));
		if (list.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No content entries found.[/]");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[cyan]Title ID[/]"));
		table.AddColumn(new TableColumn("[green]Name[/]"));
		table.AddColumn(new TableColumn("[grey]Devices[/]"));
		table.AddColumn(new TableColumn("[cyan]Types[/]"));
		foreach (ContentHelpers.ContentEntry item6 in list.OrderBy((ContentHelpers.ContentEntry e) => e.TitleId))
		{
			string text = (settings.ShowTypes ? string.Join(", ", item6.Types.Select((ContentHelpers.ContentTypeSummary t) => $"{t.Kind}:{t.Count}")) : item6.Types.Count.ToString(CultureInfo.InvariantCulture));
			table.AddRow($"[cyan]0x{item6.TitleId:X8}[/]", "[green]" + Markup.Escape(item6.Name) + "[/]", "[grey]" + Markup.Escape(string.Join(", ", item6.Devices.OrderBy<string, string>((string d) => d, StringComparer.OrdinalIgnoreCase))) + "[/]", "[cyan]" + Markup.Escape(text) + "[/]");
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
