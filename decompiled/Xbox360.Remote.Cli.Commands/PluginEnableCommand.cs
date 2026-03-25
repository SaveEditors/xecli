using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class PluginEnableCommand : AsyncCommand<PluginEnableCommand.Settings>
{
	public sealed class Settings : FtpConnectionSettings
	{
		[CommandOption("--slot <N>")]
		[LocalizedDescription("Plugin slot number, usually 1-5.")]
		public int? Slot { get; init; }

		[CommandOption("--path <PATH>")]
		[LocalizedDescription("Plugin XEX path.")]
		public string? PluginPath { get; init; }

		[CommandOption("--ini <PATH>")]
		[LocalizedDescription("DashLaunch config path (default: /Hdd1/launch.ini).")]
		public string? IniPath { get; init; }

		[CommandOption("--backup")]
		[LocalizedDescription("Create a .bak copy before writing.")]
		public bool Backup { get; init; }
	}

	public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
	{
		if (!settings.Slot.HasValue || settings.Slot.Value <= 0 || string.IsNullOrWhiteSpace(settings.PluginPath))
		{
			AnsiConsole.MarkupLine("[red]--slot and --path are required.[/]");
			return 1;
		}
		(string, int, string, string, int) tuple = await FtpHelpers.ResolveAsync(settings, CancellationToken.None);
		string ip = tuple.Item1;
		int port = tuple.Item2;
		string user = tuple.Item3;
		string pass = tuple.Item4;
		int timeout = tuple.Item5;
		PluginHelpers.PluginConfig pluginConfig = await PluginHelpers.LoadAsync(ip, port, user, pass, timeout, settings.IniPath);
		pluginConfig.SetSlot(settings.Slot.Value, settings.PluginPath);
		await PluginHelpers.SaveAsync(ip, port, user, pass, timeout, pluginConfig, settings.Backup);
		AnsiConsole.MarkupLine($"[green]Enabled[/] plugin{settings.Slot.Value} = {Markup.Escape(settings.PluginPath)}");
		return 0;
	}
}
