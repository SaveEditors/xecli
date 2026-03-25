using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class SaveListCommand : AsyncCommand<SaveListCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--titleid <TITLEID>")]
		[Description("Title ID in hex (for example 4D530805).")]
		public string? TitleId { get; init; }

		[CommandOption("--profile <PROFILE>")]
		[Description("Restrict to one 16-character profile ID.")]
		public string? ProfileId { get; init; }

		[CommandOption("--device <ROOTS>")]
		[Description("Optional comma-separated storage roots, for example Hdd1 or Hdd1,Usb0.")]
		public string? Devices { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!SaveHelpers.TryParseTitleId(settings.TitleId, out var titleId))
		{
			AnsiConsole.MarkupLine("[red]--titleid is required.[/]");
			return 1;
		}
		(string, int, string, string, int) obj = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string item = obj.Item1;
		int item2 = obj.Item2;
		string item3 = obj.Item3;
		string item4 = obj.Item4;
		int item5 = obj.Item5;
		List<SaveHelpers.SaveFileRecord> list = await SaveHelpers.EnumerateSaveFilesAsync(item, item2, item3, item4, item5, titleId, settings.ProfileId, settings.Devices);
		if (settings.Json)
		{
			CliOutput.EmitJson(list);
			return 0;
		}
		string text = SaveHelpers.FormatTitleLabel(titleId);
		AnsiConsole.Write(new Rule("[bold deepskyblue1]Save Files[/] [grey]" + Markup.Escape(text) + "[/]").RuleStyle("grey"));
		if (list.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No save files found for this title.[/]");
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[green]Path[/]"));
		table.AddColumn(new TableColumn("[grey]Profile[/]"));
		table.AddColumn(new TableColumn("[cyan]Size[/]"));
		table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
		foreach (SaveHelpers.SaveFileRecord item6 in list.OrderBy<SaveHelpers.SaveFileRecord, string>((SaveHelpers.SaveFileRecord f) => f.Device, StringComparer.OrdinalIgnoreCase).ThenBy<SaveHelpers.SaveFileRecord, string>((SaveHelpers.SaveFileRecord f) => f.ProfileId, StringComparer.OrdinalIgnoreCase).ThenBy<SaveHelpers.SaveFileRecord, string>((SaveHelpers.SaveFileRecord f) => f.RelativePath, StringComparer.OrdinalIgnoreCase))
		{
			table.AddRow("[green]" + Markup.Escape(item6.RemotePath) + "[/]", "[grey]" + Markup.Escape(item6.ProfileId) + "[/]", "[cyan]" + FtpHelpers.FormatBytes(item6.Size) + "[/]", (item6.Modified != DateTime.MinValue) ? CliOutput.FormatTimestamp(item6.Modified) : "[grey]unknown[/]");
		}
		AnsiConsole.Write(table);
		return 0;
	}
}
