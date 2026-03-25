using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NotifyIconsRemoveCommand : Command<NotifyIconsRemoveCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--name <NAME>")]
		public string? Name { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Name))
		{
			AnsiConsole.MarkupLine("[red]--name is required.[/]");
			return 1;
		}
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.NotifyIcons != null && cliConfig.NotifyIcons.Remove(settings.Name))
		{
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]Removed icon preset[/] " + Markup.Escape(settings.Name));
			return 0;
		}
		AnsiConsole.MarkupLine("[yellow]Icon preset not found:[/] " + Markup.Escape(settings.Name));
		return 1;
	}
}
