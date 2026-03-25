using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class Jrpc2ResolveCommand : AsyncCommand<Jrpc2ResolveCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--module <MODULE>")]
		public string? Module { get; init; }

		[CommandOption("--ordinal <ORD>")]
		public uint? Ordinal { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Module) || !settings.Ordinal.HasValue)
		{
			AnsiConsole.MarkupLine("[red]--module and --ordinal are required.[/]");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			uint num = await new Jrpc2Client(client).ResolveFunctionAsync(settings.Module, settings.Ordinal.Value, CancellationToken.None);
			AnsiConsole.WriteLine((num == 0) ? "0x00000000" : $"0x{num:X8}");
			return 0;
		}, CancellationToken.None);
	}
}
