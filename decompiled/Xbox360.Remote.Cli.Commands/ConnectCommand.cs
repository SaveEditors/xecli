using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class ConnectCommand : AsyncCommand<ConnectSettings>
{
	private const int DiscoveryCeilingSeconds = 12;

	public override async Task<int> ExecuteAsync(CommandContext context, ConnectSettings settings)
	{
		CliConfig config = CliConfig.Load();
		string target = settings.Target;
		CliConfig cliConfig;
		int? defaultPort;
		if (string.IsNullOrWhiteSpace(target))
		{
			IReadOnlyList<DiscoveredConsole> consoles = await DiscoverWithFeedbackAsync(settings);
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
			IReadOnlyList<DiscoveredConsole> readOnlyList = await DiscoverWithFeedbackAsync(settings);
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

	private static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverWithFeedbackAsync(DiscoverySettings settings)
	{
		string ports = string.IsNullOrWhiteSpace(settings.Ports) ? "730,731" : settings.Ports;
		int timeoutMs = settings.TimeoutMs ?? 400;
		AnsiConsole.MarkupLine($"[grey]Discovery started:[/] ports [deepskyblue1]{Markup.Escape(ports)}[/], per-host timeout [deepskyblue1]{timeoutMs} ms[/].");
		AnsiConsole.MarkupLine("[grey]Scanning local network...[/]");

		IReadOnlyList<DiscoveredConsole> consoles = Array.Empty<DiscoveredConsole>();
		try
		{
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(DiscoveryCeilingSeconds));
			await AnsiConsole.Status()
				.Spinner(Spinner.Known.Dots)
				.SpinnerStyle(new Style(Spectre.Console.Color.DeepSkyBlue1, decoration: Decoration.Bold))
				.StartAsync("Discovering Xbox 360 consoles...", async _ =>
				{
					consoles = await DiscoveryHelpers.DiscoverAsync(settings, cts.Token);
				});
		}
		catch (OperationCanceledException)
		{
			AnsiConsole.MarkupLine($"[yellow]Discovery timed out after {DiscoveryCeilingSeconds.ToString(CultureInfo.InvariantCulture)}s.[/] You can enter the IP manually.");
			return Array.Empty<DiscoveredConsole>();
		}
		catch (Exception ex)
		{
			AnsiConsole.MarkupLine("[yellow]Discovery failed:[/] " + Markup.Escape(ex.Message));
			AnsiConsole.MarkupLine("[grey]You can still enter the IP manually.[/]");
			return Array.Empty<DiscoveredConsole>();
		}

		if (consoles.Count == 0)
		{
			AnsiConsole.MarkupLine("[yellow]No consoles discovered automatically.[/] You can enter an IP manually.");
		}
		else
		{
			AnsiConsole.MarkupLine($"[green]Discovery complete:[/] found [deepskyblue1]{consoles.Count.ToString(CultureInfo.InvariantCulture)}[/] console(s).");
		}

		return consoles;
	}
}
