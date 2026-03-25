using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsMoveCommand : AsyncCommand<XbdmFsMoveCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--from <PATH>")]
		public string? Source { get; init; }

		[CommandOption("--to <PATH>")]
		public string? Destination { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Source) || string.IsNullOrWhiteSpace(settings.Destination))
		{
			AnsiConsole.MarkupLine("[red]--from and --to are required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			await client.MoveAsync(settings.Source, settings.Destination, CancellationToken.None);
			AnsiConsole.MarkupLine("[green]Moved[/] " + settings.Source + " -> " + settings.Destination);
			return 0;
		}, CancellationToken.None);
	}
}
