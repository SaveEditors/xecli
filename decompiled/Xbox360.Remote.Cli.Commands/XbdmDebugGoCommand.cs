using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugGoCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			await client.DebugGoAsync(CancellationToken.None);
			AnsiConsole.MarkupLine("[green]Execution resumed.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
