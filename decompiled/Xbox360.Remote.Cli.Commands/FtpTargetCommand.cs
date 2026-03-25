using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FtpTargetCommand : Command<FtpTargetCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--set <IP>")]
		[LocalizedDescription("Set the default FTP IP.")]
		public string? Ip { get; init; }

		[CommandOption("--port <PORT>")]
		[LocalizedDescription("Set the default FTP port (default: 21).")]
		public int? Port { get; init; }

		[CommandOption("--user <USER>")]
		[LocalizedDescription("Set the default FTP username.")]
		public string? User { get; init; }

		[CommandOption("--pass <PASS>")]
		[LocalizedDescription("Set the default FTP password.")]
		public string? Pass { get; init; }

		[CommandOption("--clear")]
		[LocalizedDescription("Clear saved FTP target settings.")]
		public bool Clear { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (settings.Clear)
		{
			cliConfig.DefaultFtpPort = null;
			cliConfig.DefaultFtpUser = null;
			cliConfig.DefaultFtpPassword = null;
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]FTP target cleared.[/]");
			return 0;
		}
		bool flag = false;
		if (!string.IsNullOrWhiteSpace(settings.Ip))
		{
			cliConfig.DefaultIp = settings.Ip;
			flag = true;
		}
		if (settings.Port.HasValue)
		{
			cliConfig.DefaultFtpPort = settings.Port;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.User))
		{
			cliConfig.DefaultFtpUser = settings.User;
			flag = true;
		}
		if (!string.IsNullOrWhiteSpace(settings.Pass))
		{
			cliConfig.DefaultFtpPassword = settings.Pass;
			flag = true;
		}
		if (flag)
		{
			cliConfig.Save();
			AnsiConsole.MarkupLine("[green]FTP target updated.[/]");
			return 0;
		}
		string value = cliConfig.DefaultIp ?? "unknown";
		string value2 = (cliConfig.DefaultFtpPort ?? 21).ToString(CultureInfo.InvariantCulture);
		string value3 = cliConfig.DefaultFtpUser ?? "xboxftp";
		string value4 = (string.IsNullOrWhiteSpace(cliConfig.DefaultFtpPassword) ? "unknown" : "********");
		AnsiConsole.MarkupLine($"[green]FTP target:[/] {value}:{value2} user={value3} pass={value4}");
		return 0;
	}
}
