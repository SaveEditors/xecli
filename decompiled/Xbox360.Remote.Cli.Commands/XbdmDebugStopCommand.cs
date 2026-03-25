using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDebugStopCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			await client.DebugStopAsync(CancellationToken.None);
			AnsiConsole.MarkupLine("[green]Execution stopped.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
