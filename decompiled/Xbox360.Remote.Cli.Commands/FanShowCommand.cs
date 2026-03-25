using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class FanShowCommand : Command<FanShowCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--json")]
		[LocalizedDescription("Emit JSON output.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.LastFanState == null)
		{
			AnsiConsole.MarkupLine("[yellow]No cached fan state yet. Use `rgh fan set` first.[/]");
			return 1;
		}
		if (settings.Json)
		{
			CliOutput.EmitJson(cliConfig.LastFanState);
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]Field[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
		table.AddRow("[white]Source[/]", "[gold1]Last XeCLI-applied manual setting[/]");
		table.AddRow("[white]Speed[/]", $"[springgreen3_1]{cliConfig.LastFanState.SpeedPercent}%[/]");
		table.AddRow("[white]Channel[/]", "[deepskyblue1]" + Markup.Escape(cliConfig.LastFanState.Channel ?? "both") + "[/]");
		table.AddRow("[white]Updated[/]", CliOutput.FormatTimestamp(cliConfig.LastFanState.UpdatedUtc.UtcDateTime));
		AnsiConsole.Write(table);
		return 0;
	}
}
