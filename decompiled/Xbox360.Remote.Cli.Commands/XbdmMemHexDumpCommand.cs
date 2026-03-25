using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemHexDumpCommand : AsyncCommand<XbdmMemHexDumpCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		public string? Size { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || !CliHelpers.TryParseUInt32(settings.Size, out var size))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
			return 1;
		}
		if (size > 1048576)
		{
			AnsiConsole.MarkupLine("[yellow]Large hexdumps are truncated to 1MB. Use mem-dump for larger ranges.[/]");
			size = 1048576u;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			byte[] data = await client.ReadMemoryBytesReliableAsync(address, checked((int)size), CancellationToken.None);
			CliOutput.RenderHexDump(address, data);
			return 0;
		}, CancellationToken.None);
	}
}
