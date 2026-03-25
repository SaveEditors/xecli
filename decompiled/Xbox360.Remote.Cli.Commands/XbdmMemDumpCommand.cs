using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemDumpCommand : AsyncCommand<XbdmMemDumpCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		public string? Size { get; init; }

		[CommandOption("--out <FILE>")]
		public string? Output { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || !CliHelpers.TryParseUInt32(settings.Size, out var size) || string.IsNullOrWhiteSpace(settings.Output))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec> --out <file>");
			return 1;
		}
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			FileStream stream = new FileStream(settings.Output, FileMode.Create, FileAccess.Write, FileShare.None);
			try
			{
				await CliOutput.RunWithProgressAsync($"Dumping 0x{address:X8}", size, (IProgress<long> progress) => client.ReadMemoryAsync(address, size, stream, progress, CancellationToken.None));
				AnsiConsole.MarkupLine($"[green]Dumped[/] 0x{address:X8} ({size} bytes) to {settings.Output}");
				return 0;
			}
			finally
			{
				if (stream != null)
				{
					((IDisposable)stream).Dispose();
				}
			}
		}, CancellationToken.None);
	}
}
