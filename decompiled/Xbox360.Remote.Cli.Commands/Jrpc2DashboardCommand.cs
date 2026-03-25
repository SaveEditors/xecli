using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2DashboardCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			AnsiConsole.WriteLine((await new Jrpc2Client(client).GetDashboardVersionAsync(CancellationToken.None)).ToString(CultureInfo.InvariantCulture));
			return 0;
		}, CancellationToken.None);
	}
}
