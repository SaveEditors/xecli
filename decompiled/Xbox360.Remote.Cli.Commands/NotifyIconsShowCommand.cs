using System.ComponentModel;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class NotifyIconsShowCommand : Command<NotifyIconsShowCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "<value>")]
		[Description("A built-in icon name, preset alias, decimal id, or 0x hex id.")]
		public string? Value { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Value))
		{
			AnsiConsole.MarkupLine("[red]A logo value is required.[/]");
			return 1;
		}
		CliConfig cliConfig = CliConfig.Load();
		if (cliConfig.NotifyIcons != null && cliConfig.NotifyIcons.TryGetValue(settings.Value, out var value))
		{
			RenderResolvedTable(settings.Value, value, "preset");
			return 0;
		}
		if (!NotifyHelpers.TryResolveLogo(null, settings.Value, out int logo, out string error))
		{
			AnsiConsole.MarkupLine("[red]" + Markup.Escape(error ?? "Unknown notify icon.") + "[/]");
			return 1;
		}
		RenderResolvedTable(settings.Value, logo, "built-in/raw");
		return 0;
	}

	private static void RenderResolvedTable(string input, int logo, string source)
	{
		NotifyCatalog.TryGet(logo, out NotifyLogoDefinition definition);
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[white]Field[/]"));
		table.AddColumn(new TableColumn("[deepskyblue1]Value[/]"));
		table.AddRow("[white]Input[/]", "[deepskyblue1]" + Markup.Escape(input) + "[/]");
		table.AddRow("[white]Source[/]", "[deepskyblue1]" + Markup.Escape(source) + "[/]");
		table.AddRow("[white]Logo ID[/]", "[deepskyblue1]" + logo.ToString(CultureInfo.InvariantCulture) + "[/]");
		table.AddRow("[white]Name[/]", "[deepskyblue1]" + Markup.Escape(definition?.Key ?? "-") + "[/]");
		table.AddRow("[white]Label[/]", "[deepskyblue1]" + Markup.Escape(definition?.Label ?? "Unknown / custom") + "[/]");
		table.AddRow("[white]Notes[/]", "[deepskyblue1]" + Markup.Escape(definition?.Notes ?? "-") + "[/]");
		AnsiConsole.Write(table);
	}
}
