using System.Reflection;
using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli;

internal sealed class CliErrorException : Exception {
    public CliErrorException(CliErrorEnvelope error)
        : base(error.Message) {
        Error = error;
    }

    public CliErrorEnvelope Error { get; }
}

internal static class CliErrorReporter {
    private const string GenericUnhandledCode = "CLI_UNHANDLED_EXCEPTION";

    public static Exception Unwrap(Exception ex) {
        while (true) {
            if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1 && aggregate.InnerException != null) {
                ex = aggregate.InnerException;
                continue;
            }

            if ((ex is TargetInvocationException || ex is TypeInitializationException) && ex.InnerException != null) {
                ex = ex.InnerException;
                continue;
            }

            return ex;
        }
    }

    public static CliErrorEnvelope BuildError(string operation, Exception ex, string? targetDisplay = null) {
        Exception root = Unwrap(ex);
        string fallbackMessage = string.IsNullOrWhiteSpace(root.Message) ? root.GetType().Name : root.Message.ReplaceLineEndings(" ").Trim();

        if (root is CliErrorException cliError)
            return cliError.Error;

        if (ConnectionFailureModel.TryBuildFtpError(root, fallbackMessage, out CliErrorEnvelope ftpError))
            return ftpError;

        if (ConnectionFailureModel.TryBuildXbdmError(root, fallbackMessage, targetDisplay, out CliErrorEnvelope xbdmError))
            return xbdmError;

        if (ConnectionFailureModel.TryBuildXbdmReadTimeoutError(root, fallbackMessage, targetDisplay, out CliErrorEnvelope xbdmReadTimeoutError))
            return xbdmReadTimeoutError;

        string redactedMessage = CommandLogRedactor.RedactFreeText(fallbackMessage);
        if (string.IsNullOrWhiteSpace(redactedMessage))
            redactedMessage = $"{operation} failed unexpectedly.";
        else
            redactedMessage = $"{operation} failed: {redactedMessage}";

        return new CliErrorEnvelope(
            $"{operation} failed",
            redactedMessage,
            GenericUnhandledCode,
            new[] {
                "Rerun the command with --json for structured output.",
                "Check the console connection and try again."
            });
    }

    public static CliErrorEnvelope PrepareForJsonOutput(CliErrorEnvelope error) {
        if (!error.Code.Equals(GenericUnhandledCode, StringComparison.Ordinal))
            return error;

        string[] applicableNextSteps = error.NextSteps
            .Where(step => !step.Contains("--json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return applicableNextSteps.Length == error.NextSteps.Count
            ? error
            : error with { NextSteps = applicableNextSteps };
    }

    public static void WriteTextError(string operation, Exception ex, string? targetDisplay = null) {
        CliErrorEnvelope error = BuildError(operation, ex, targetDisplay);
        string message = CompactForConsole(error.Message);

        if (error.Code.Equals(GenericUnhandledCode, StringComparison.Ordinal)) {
            string? jsonRecoveryHint = error.NextSteps
                .FirstOrDefault(step => step.Contains("--json", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(jsonRecoveryHint))
                message = $"{message} {CompactForConsole(jsonRecoveryHint)}";
        }

        Console.Error.WriteLine($"Error: {message}");
    }

    public static void WriteJsonError(string operation, Exception ex, string? targetDisplay = null) {
        CliOutput.EmitJsonError(BuildError(operation, ex, targetDisplay));
    }

    public static void WriteFrameworkExitCodeThree(string[] args) {
        if (ShouldEmitJsonError(args)) {
            CliOutput.EmitJsonError(new CliErrorEnvelope(
                "Command failed",
                "The command failed unexpectedly.",
                GenericUnhandledCode,
                new[] {
                    "Rerun the command with --json to inspect the structured error envelope.",
                    "Check the console connection and try again."
                }));
            return;
        }

        Console.Error.WriteLine("Error: The command failed unexpectedly.");
    }

    public static string? TryGetTargetDisplay(string[] args) {
        string? ip = null;
        int? port = null;

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (arg.Equals("--ip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                ip = args[++i];
                continue;
            }

            if (arg.Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out int parsedPort)) {
                port = parsedPort;
            }
        }

        if (string.IsNullOrWhiteSpace(ip) || !port.HasValue)
            return null;

        return $"{ip}:{port.Value}";
    }

    private static bool ShouldEmitJsonError(string[] args) {
        return args.Any(arg => arg.Equals("--json", StringComparison.OrdinalIgnoreCase));
    }

    private static string CompactForConsole(string message) {
        return string.Join(" ", message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
