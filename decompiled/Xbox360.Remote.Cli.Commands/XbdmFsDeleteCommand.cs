using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmFsDeleteCommand : AsyncCommand<XbdmFsDeleteCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--path <PATH>")]
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
			await client.DeleteAsync(settings.Path, CancellationToken.None);
			AnsiConsole.MarkupLine("[green]Deleted[/] " + settings.Path);
			return 0;
		}, CancellationToken.None);
	}
}
