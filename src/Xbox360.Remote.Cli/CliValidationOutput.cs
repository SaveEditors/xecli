using Spectre.Console;

namespace Xbox360.Remote.Cli;

internal static class CliValidationOutput {
    public static void Write(
        ConnectionSettings settings,
        string title,
        string message,
        string code,
        params string[] nextSteps) {
        Write(settings.Json, title, message, code, nextSteps);
    }

    public static void Write(
        bool json,
        string title,
        string message,
        string code,
        params string[] nextSteps) {
        if (json) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(title, message, code, nextSteps));
            return;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }
}
