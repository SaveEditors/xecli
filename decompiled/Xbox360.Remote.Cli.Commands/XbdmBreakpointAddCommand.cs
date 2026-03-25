using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmBreakpointAddCommand : AsyncCommand<XbdmBreakpointAddCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address))
		{
			AnsiConsole.MarkupLine("[red]--addr is required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			await client.SendCommandAsync($"break addr=0x{address:X8}", cts.Token);
			AnsiConsole.MarkupLine("[green]Breakpoint set.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
