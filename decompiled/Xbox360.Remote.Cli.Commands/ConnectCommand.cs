using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ConnectCommand : AsyncCommand<ConnectSettings>
{
	public override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings)
	{
		CliConfig config = CliConfig.Load();
		string target = settings.Target;
		CliConfig cliConfig;
		int? defaultPort;
		if (string.IsNullOrWhiteSpace(target))
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
			config.DefaultIp = text;
			cliConfig = config;
			defaultPort = cliConfig.DefaultPort;
			defaultPort.GetValueOrDefault();
			if (!defaultPort.HasValue)
			{
				int value = 730;
				cliConfig.DefaultPort = value;
			}
			config.Save();
			AnsiConsole.MarkupLine("[green]Default console set to[/] " + text);
			return 0;
		}
		if (int.TryParse(target, out var index))
		{
			IReadOnlyList<DiscoveredConsole> readOnlyList;
			try
			{
				readOnlyList = await DiscoveryHelpers.DiscoverAsync(settings, CancellationToken.None);
			}
			catch
			{
				readOnlyList = Array.Empty<DiscoveredConsole>();
			}
			if (index <= 0 || index > readOnlyList.Count)
			{
				AnsiConsole.MarkupLine("[red]Invalid index.[/]");
				return 1;
			}
			string text2 = (config.DefaultIp = readOnlyList[index - 1].Ip.ToString());
			cliConfig = config;
			defaultPort = cliConfig.DefaultPort;
			defaultPort.GetValueOrDefault();
			if (!defaultPort.HasValue)
			{
				int value = 730;
				cliConfig.DefaultPort = value;
			}
			config.Save();
			AnsiConsole.MarkupLine("[green]Default console set to[/] " + text2);
			return 0;
		}
		config.DefaultIp = target;
		cliConfig = config;
		defaultPort = cliConfig.DefaultPort;
		defaultPort.GetValueOrDefault();
		if (!defaultPort.HasValue)
		{
			int value = 730;
			cliConfig.DefaultPort = value;
		}
		config.Save();
		AnsiConsole.MarkupLine("[green]Default console set to[/] " + target);
		return 0;
	}
}
