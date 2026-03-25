using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Spectre.Console;
using Spectre.Console.Cli;
using Xbox360.Remote.Cli.Homebrew;

namespace Xbox360.Remote.Cli.Commands;

public sealed class HomebrewListCommand : Command<HomebrewListCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--json")]
		[Description("Emit machine-readable output.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		IReadOnlyList<string> knownPackageIds = HomebrewPackageService.KnownPackageIds;
		if (settings.Json)
		{
			CliOutput.EmitJson(new
			{
				Packages = knownPackageIds.Select((string id) => new
				{
					Id = id,
					Package = id
				})
			});
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]Package[/]"));
		table.AddColumn(new TableColumn("[bold white]Description[/]"));
		table.AddColumn(new TableColumn("[bold white]Source[/]"));
		foreach (HomebrewPackageDefinition item in HomebrewPackageService.Catalog)
		{
			Table table2 = table;
			string text = "[springgreen3_1]" + Markup.Escape(item.Id) + "[/]";
			string text2 = "[cyan]" + Markup.Escape(item.Description) + "[/]";
			string host = new Uri(item.PrimaryUrl).Host;
			string text3 = ((host == "consolemods.org") ? "ConsoleMods" : ((!(host == "github.com")) ? new Uri(item.PrimaryUrl).Host : "GitHub"));
			table2.AddRow(text, text2, "[grey]" + Markup.Escape(text3) + "[/]");
		}
		table.AddRow("[springgreen3_1]all[/]", "[cyan]Installs every built-in homebrew package.[/]", "[grey]-[/]");
		AnsiConsole.Write(table);
		AnsiConsole.MarkupLine("[grey]Use[/] [springgreen3_1]rgh homebrew install <package> --usb E:[/] [grey]to stage locally, or omit[/] [springgreen3_1]--usb[/] [grey]to install directly to a detected console drive.[/]");
		return 0;
	}
}
