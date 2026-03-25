using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class TargetCommand : Command<TargetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--set <IP>")]
		[Description("Set the default console IP.")]
		public string? Ip { get; init; }

		[CommandOption("--port <PORT>")]
		[Description("Set the default port (default: 730).")]
		public int? Port { get; init; }

		[CommandOption("--clear")]
		[Description("Clear the saved target.")]
		public bool Clear { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (settings.Clear)
		{
			cliConfig.DefaultIp = null;
			cliConfig.DefaultPort = null;
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]Default target cleared.[/]");
			return 0;
		}
		if (!string.IsNullOrWhiteSpace(settings.Ip))
		{
			cliConfig.DefaultIp = settings.Ip;
			if (settings.Port.HasValue)
			{
				cliConfig.DefaultPort = settings.Port;
			}
			else
			{
				CliConfig cliConfig2 = cliConfig;
				int? defaultPort = cliConfig2.DefaultPort;
				int valueOrDefault = defaultPort.GetValueOrDefault();
				if (!defaultPort.HasValue)
				{
					valueOrDefault = 730;
					int? defaultPort2 = valueOrDefault;
					cliConfig2.DefaultPort = defaultPort2;
				}
			}
			cliConfig.Save();
			AnsiConsole.MarkupLine($"[green]Default target set to[/] {settings.Ip}:{cliConfig.DefaultPort}");
			return 0;
		}
		if (string.IsNullOrWhiteSpace(cliConfig.DefaultIp))
		{
			AnsiConsole.MarkupLine("[yellow]No default target set. Use --set or `rgh connect`.[/]");
			return 1;
		}
		AnsiConsole.MarkupLine($"[green]Default target:[/] {cliConfig.DefaultIp}:{cliConfig.DefaultPort ?? 730}");
		return 0;
	}
}
