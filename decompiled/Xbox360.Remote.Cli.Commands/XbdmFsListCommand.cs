using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsListCommand : AsyncCommand<XbdmFsListCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <DIR>")]
		public string? Path { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Path))
		{
			AnsiConsole.MarkupLine("[red]--path is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			IReadOnlyList<XbdmFileEntry> readOnlyList = await client.GetDirectoryAsync(settings.Path, CancellationToken.None);
			if (settings.Json)
			{
				CliOutput.EmitJson(readOnlyList);
				return 0;
			}
			AnsiConsole.Write(new Rule("[bold deepskyblue1]Directory[/]").RuleStyle("grey"));
			Table table = CliOutput.CreateTable();
			table.AddColumn(new TableColumn("[green]Name[/]"));
			table.AddColumn(new TableColumn("[grey]Type[/]"));
			table.AddColumn(new TableColumn("[cyan]Size[/]"));
			table.AddColumn(new TableColumn("[grey]Modified (Local)[/]"));
			foreach (XbdmFileEntry item in readOnlyList)
			{
				string text = Markup.Escape(item.Name);
				string text2 = (item.IsDirectory ? "[yellow]Dir[/]" : "[grey]File[/]");
				table.AddRow(item.IsDirectory ? ("[yellow]" + text + "[/]") : ("[green]" + text + "[/]"), text2, $"[cyan]{item.Size}[/]", CliOutput.FormatTimestamp(item.ModifiedUtc));
			}
			AnsiConsole.Write(table);
			return 0;
		}, CancellationToken.None);
	}
}
