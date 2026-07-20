using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal static class ConfirmationHelpers {
    public const string NonInteractiveDetail = "Interactive confirmation is required. Re-run with --auto-confirm or --yes in a terminal.";

    public static bool CanPromptForConfirmation() {
        if (!AnsiConsole.Console.Profile.Capabilities.Interactive)
            return false;

        if (!AnsiConsole.Console.Profile.Out.IsTerminal)
            return false;

        try {
            return !Console.IsInputRedirected;
        }
        catch (IOException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    public static bool TryConfirm(
        string title,
        string prompt,
        bool autoConfirm,
        bool defaultValue = false,
        string? nonInteractiveDetail = null,
        string cancelledDetail = "No changes were made.",
        bool emitWarning = true) {
        if (autoConfirm)
            return true;

        if (!CanPromptForConfirmation()) {
            if (emitWarning)
                OperationFeedback.WriteWarning(title, nonInteractiveDetail ?? NonInteractiveDetail);
            return false;
        }

        if (!AnsiConsole.Confirm(prompt, defaultValue)) {
            if (emitWarning)
                OperationFeedback.WriteWarning(title, cancelledDetail);
            return false;
        }

        return true;
    }

    public static void ThrowIfCannotPrompt(string message) {
        if (!CanPromptForConfirmation())
            throw new OperationCanceledException(message);
    }
}
