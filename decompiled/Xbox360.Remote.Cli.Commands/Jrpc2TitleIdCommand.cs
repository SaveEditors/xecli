using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2TitleIdCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			uint value = await new Jrpc2Client(client).GetTitleIdAsync(CancellationToken.None);
			AnsiConsole.WriteLine($"0x{value:X8}");
			return 0;
		}, CancellationToken.None);
	}
}
