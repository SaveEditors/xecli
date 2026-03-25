using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class StartCommand : AsyncCommand<DiscoverySettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, DiscoverySettings settings)
	{
		IReadOnlyList<DiscoveredConsole> consoles;
		try
		{
			consoles = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
		}
		catch
		{
			consoles = Array.Empty<DiscoveredConsole>();
		}
		string text = CliOutput.PromptForConsole(consoles);
		if (string.IsNullOrWhiteSpace(text))
		{
			AnsiConsole.MarkupLine("[red]No console selected.[/]");
			return 1;
		}
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.DefaultIp = text;
		CliConfig cliConfig2 = cliConfig;
		int? defaultPort = cliConfig2.DefaultPort;
		defaultPort.GetValueOrDefault();
		if (!defaultPort.HasValue)
		{
			int value = 730;
			cliConfig2.DefaultPort = value;
		}
		cliConfig.Save();
		AnsiConsole.MarkupLine("[green]Default console set to[/] " + text);
		return 0;
	}
}
