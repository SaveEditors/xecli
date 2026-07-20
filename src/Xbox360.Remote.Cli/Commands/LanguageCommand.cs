using Spectre.Console;
using Spectre.Console.Cli;
using XeCli.Localization;

namespace Xbox360.Remote.Cli.Commands;

public sealed class LanguageCommand : Command<LanguageCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandOption("--set <LANG>")]
        [LocalizedDescription("Save the default UI language (en or es).")]
        public string? Set { get; init; }

        [CommandOption("--clear")]
        [LocalizedDescription("Clear the saved UI language and fall back to --lang, XECLI_LANG, or the system language.")]
        public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (settings.Clear && !string.IsNullOrWhiteSpace(settings.Set)) {
            OperationFeedback.WriteFailure("UI language settings invalid", "Use --set <LANG> or --clear, not both.");
            return 1;
        }

        if (!CliConfig.TryLoad(out CliConfig config)) {
            OperationFeedback.WriteFailure(
                "UI language unavailable",
                "Config file is present but could not be parsed. Fix or remove config.json, then run rgh language again.");
            return 1;
        }

        if (settings.Clear) {
            config.UiLanguage = null;
            config.Save();
            OperationFeedback.WriteSuccess("UI language cleared", "XeCLI will fall back to --lang, XECLI_LANG, or the system language.");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.Set)) {
            string languageCode = settings.Set.Trim().ToLowerInvariant();
            if (languageCode is not ("en" or "es")) {
                OperationFeedback.WriteFailure("UI language settings invalid", "Use --set en|es or --clear.");
                return 1;
            }

            config.UiLanguage = languageCode;
            config.Save();
            OperationFeedback.WriteSuccess("UI language updated", $"Saved UI language: [cyan]{Markup.Escape(FormatLanguage(languageCode))}[/]");
            return 0;
        }

        Table table = CliOutput.CreateTable();
        table.AddColumn(new TableColumn("[white]Field[/]"));
        table.AddColumn(new TableColumn("[white]Value[/]"));
        table.AddRow("[white]Saved UI language[/]", $"[cyan]{Markup.Escape(FormatLanguage(config.UiLanguage))}[/]");
        table.AddRow("[white]Effective language[/]", $"[cyan]{Markup.Escape(FormatLanguage(LocalizedText.CurrentLanguageCode))}[/]");
        table.AddRow("[white]CLI override[/]", "[grey70]Use --lang en|es for one command[/]");
        AnsiConsole.Write(table);
        return 0;
    }

    private static string FormatLanguage(string? languageCode) {
        return LocalizedText.NormalizeLanguageCode(languageCode) == "es"
            ? "Español (es)"
            : "English (en)";
    }
}
