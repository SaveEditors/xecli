using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LedStateCommand : Command<LedStateCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--json")]
		[Description("Emit JSON output.")]
		public bool Json { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.LastLedState == null)
		{
			AnsiConsole.MarkupLine("[yellow]No cached LED state yet. Use `rgh led set` first.[/]");
			return 1;
		}
		if (settings.Json)
		{
			CliOutput.EmitJson(cliConfig.LastLedState);
			return 0;
		}
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]Field[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]Value[/]"));
		table.AddRow("[white]Source[/]", "[gold1]Last XeCLI-applied ring-light state[/]");
		table.AddRow("[white]Preset[/]", "[deepskyblue1]" + Markup.Escape(cliConfig.LastLedState.Preset ?? "custom") + "[/]");
		table.AddRow("[white]Top Left[/]", "[springgreen3_1]" + Markup.Escape(cliConfig.LastLedState.TopLeft ?? "unknown") + "[/]");
		table.AddRow("[white]Top Right[/]", "[springgreen3_1]" + Markup.Escape(cliConfig.LastLedState.TopRight ?? "unknown") + "[/]");
		table.AddRow("[white]Bottom Left[/]", "[springgreen3_1]" + Markup.Escape(cliConfig.LastLedState.BottomLeft ?? "unknown") + "[/]");
		table.AddRow("[white]Bottom Right[/]", "[springgreen3_1]" + Markup.Escape(cliConfig.LastLedState.BottomRight ?? "unknown") + "[/]");
		table.AddRow("[white]Updated[/]", CliOutput.FormatTimestamp(cliConfig.LastLedState.UpdatedUtc.UtcDateTime));
		AnsiConsole.Write(table);
		return 0;
	}
}
