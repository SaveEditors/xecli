using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class PingCommand : AsyncCommand<ConnectionSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectionSettings settings)
	{
		return await CliHelpers.WithClientOnceAsync(new ConnectionSettings
		{
			Ip = settings.Ip,
			Port = settings.Port,
			TimeoutMs = (settings.TimeoutMs ?? 2000),
			Json = settings.Json
		}, async delegate(XbdmClient client)
		{
			Stopwatch sw = Stopwatch.StartNew();
			await client.GetConsoleInfoAsync(CancellationToken.None);
			sw.Stop();
			AnsiConsole.MarkupLine($"[green]OK[/] {sw.ElapsedMilliseconds} ms");
			return 0;
		}, CancellationToken.None);
	}
}
