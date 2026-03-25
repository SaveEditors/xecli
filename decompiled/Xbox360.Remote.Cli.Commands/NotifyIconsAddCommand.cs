using System;
using System.Collections.Generic;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NotifyIconsAddCommand : Command<NotifyIconsAddCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--name <NAME>")]
		public string? Name { get; init; }

		[CommandOption("--logo <ID>")]
		public string? Logo { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Logo))
		{
			AnsiConsole.MarkupLine("[red]--name and --logo are required.[/]");
			return 1;
		}
		if (!NotifyHelpers.TryResolveLogo(null, settings.Logo, out int logo, out string error))
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Invalid --logo.") + "[/]");
			return 1;
		}
		CliConfig cliConfig2;
		CliConfig cliConfig = (cliConfig2 = CliConfig.Load());
		if (cliConfig2.NotifyIcons == null)
		{
			Dictionary<string, int> dictionary = (cliConfig2.NotifyIcons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
		}
		cliConfig.NotifyIcons[settings.Name] = logo;
		cliConfig.Save();
		AnsiConsole.MarkupLine($"[green]Added icon preset[/] {Markup.Escape(settings.Name)} [grey]->[/] [aqua]{Markup.Escape(NotifyHelpers.DescribeLogo(logo))}[/]");
		return 0;
	}
}
