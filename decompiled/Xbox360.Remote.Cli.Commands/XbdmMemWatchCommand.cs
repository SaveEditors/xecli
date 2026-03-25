using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmMemWatchCommand : AsyncCommand<XbdmMemWatchCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		public string? Size { get; init; }

		[CommandOption("--interval <MS>")]
		[Description("Poll interval in milliseconds (default 500).")]
		public int? IntervalMs { get; init; }

		[CommandOption("--count <N>")]
		[Description("Number of iterations (default 0 = infinite).")]
		public int? Count { get; init; }

		[CommandOption("--clear")]
		[Description("Clear the screen between updates.")]
		public bool Clear { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address) || !CliHelpers.TryParseUInt32(settings.Size, out var size))
		{
			AnsiConsole.MarkupLine("[red]Usage:[/] --addr <hex|dec> --size <hex|dec>");
			return 1;
		}
		int interval = Math.Max(50, settings.IntervalMs ?? 500);
		int count = settings.Count.GetValueOrDefault();
		int iteration = 0;
		while (count == 0 || iteration < count)
		{
			iteration++;
			await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
			{
				if (settings.Clear)
				{
					AnsiConsole.Clear();
				}
				AnsiConsole.MarkupLine($"[grey]{DateTime.Now:HH:mm:ss}[/] 0x{address:X8} ({size} bytes)");
				byte[] data = await client.ReadMemoryBytesReliableAsync(address, checked((int)size), CancellationToken.None);
				CliOutput.RenderHexDump(address, data);
				return 0;
			}, CancellationToken.None);
			await Task.Delay(interval);
		}
		return 0;
	}
}
