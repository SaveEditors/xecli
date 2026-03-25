using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2CpuKeyCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			AnsiConsole.WriteLine(await new Jrpc2Client(client).GetCpuKeyAsync(CancellationToken.None));
			return 0;
		}, CancellationToken.None);
	}
}
