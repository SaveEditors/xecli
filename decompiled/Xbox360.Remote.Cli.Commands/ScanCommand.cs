using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ScanCommand : AsyncCommand<DiscoverySettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings)
	{
		IReadOnlyList<DiscoveredConsole> consoles;
		try
		{
			consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine("[red]Discovery failed:[/] " + Markup.Escape(ex.Message));
			consoles = Array.Empty<DiscoveredConsole>();
		}
		CliOutput.RenderDiscovery(consoles, settings.Json);
		return 0;
	}
}
