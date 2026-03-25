using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using XeCli.Localization;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LanguageCommand : Command<LanguageCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--set <LANG>")]
		[LocalizedDescription("Save the default UI language (`en` or `es`).")]
		public string? SetLanguage { get; init; }

		[CommandOption("--clear")]
		[LocalizedDescription("Clear the saved UI language and fall back to --lang, XECLI_LANG, or the system language.")]
		public bool Clear { get; init; }

		[CommandOption("--quiet")]
		[LocalizedDescription("Suppress non-error output.")]
		public bool Quiet { get; init; }
	}

	public override int Execute(CommandContext context, Settings settings)
	{
		if (settings.Clear && !string.IsNullOrWhiteSpace(settings.SetLanguage))
		{
			AnsiConsole.MarkupLine("[red]Choose either --set or --clear, not both.[/]");
			return 1;
		}

		CliConfig config = CliConfig.Load();
		if (settings.Clear)
		{
			config.UiLanguage = null;
			config.Save();
			if (!settings.Quiet)
			{
				AnsiConsole.MarkupLine("[green]Saved UI language cleared.[/]");
				AnsiConsole.MarkupLine("[grey]XeCLI will now use --lang, XECLI_LANG, or the system language.[/]");
			}
			return 0;
		}

		if (!string.IsNullOrWhiteSpace(settings.SetLanguage))
		{
			if (!TryNormalizeLanguage(settings.SetLanguage, out string normalized))
			{
				AnsiConsole.MarkupLine("[red]Unsupported language.[/] Use [white]en[/] or [white]es[/].");
				return 1;
			}

			config.UiLanguage = normalized;
			config.Save();
			if (!settings.Quiet)
			{
				AnsiConsole.MarkupLine("[green]Saved UI language set to[/] " + DescribeLanguage(normalized));
			}
			return 0;
		}

		string? saved = config.UiLanguage;
		string effective = LocalizedText.CurrentLanguageCode;
		Table table = CliOutput.CreateTable();
		table.AddColumn("[bold white]Field[/]");
		table.AddColumn("[bold white]Value[/]");
		table.AddRow("[white]Saved UI language[/]", string.IsNullOrWhiteSpace(saved) ? "[grey]Not set[/]" : "[springgreen3_1]" + DescribeLanguage(saved) + "[/]");
		table.AddRow("[white]Effective language[/]", "[deepskyblue1]" + DescribeLanguage(effective) + "[/]");
		table.AddRow("[white]CLI override[/]", "[grey]Use --lang en|es for one command[/]");
		AnsiConsole.Write(table);
		return 0;
	}

	private static bool TryNormalizeLanguage(string value, out string normalized)
	{
		string trimmed = value.Trim();
		if (trimmed.Equals("en", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("en-us", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("english", StringComparison.OrdinalIgnoreCase))
		{
			normalized = "en";
			return true;
		}

		if (trimmed.Equals("es", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("es-es", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("es-mx", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("spanish", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("espanol", StringComparison.OrdinalIgnoreCase) ||
			trimmed.Equals("español", StringComparison.OrdinalIgnoreCase))
		{
			normalized = "es";
			return true;
		}

		normalized = string.Empty;
		return false;
	}

	private static string DescribeLanguage(string code)
	{
		return LocalizedText.NormalizeLanguageCode(code) == "es" ? "Español (es)" : "English (en)";
	}
}
