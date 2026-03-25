using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class XbdmDataBreakpointAddCommand : AsyncCommand<XbdmDataBreakpointAddCommand.Settings>
{
	public sealed class Settings : ConnectionSettings
	{
		[CommandOption("--addr <ADDR>")]
		public string? Address { get; init; }

		[CommandOption("--size <SIZE>")]
		[Description("Size in bytes (default 4).")]
		public string? Size { get; init; }

		[CommandOption("--type <TYPE>")]
		[Description("write|read|exec|rw (default write).")]
		public string? Type { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!CliHelpers.TryParseUInt32(settings.Address, out var address))
		{
			AnsiConsole.MarkupLine("[red]--addr is required.[/]");
			return 1;
		}
		uint size = 4u;
		if (!string.IsNullOrWhiteSpace(settings.Size) && !CliHelpers.TryParseUInt32(settings.Size, out size))
		{
			AnsiConsole.MarkupLine("[red]Invalid --size.[/]");
			return 1;
		}
		string type = XbdmDebugCommandHelpers.NormalizeDataBreakType(settings.Type);
		return await CliHelpers.WithClientAsync(settings, async delegate(XbdmClient client)
		{
			using CancellationTokenSource cts = CliHelpers.CreateTimeoutTokenSource(settings);
			await client.SendCommandAsync("debugger connect override name=\"rgh\" user=\"" + Environment.MachineName + "\"", cts.Token);
			await client.SendCommandAsync($"break {type}=0x{address:X8} size=0x{size:X8}", cts.Token);
			AnsiConsole.MarkupLine("[green]Data breakpoint set.[/]");
			return 0;
		}, CancellationToken.None);
	}
}
