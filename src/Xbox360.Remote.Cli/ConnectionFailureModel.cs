using System.IO;
using System.Net.Sockets;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal sealed record CliErrorEnvelope(
    string Title,
    string Message,
    string Code,
    IReadOnlyList<string> NextSteps);

internal static class ConnectionFailureModel {
    public const string XbdmConnectionFailedCode = "XBDM_CONNECTION_FAILED";
    public const string XbdmCommandRejectedCode = "XBDM_COMMAND_REJECTED";
    public const string XbdmMemoryVerificationFailedCode = "XBDM_MEMORY_VERIFICATION_FAILED";
    public const string XbdmProtocolViolationCode = "XBDM_PROTOCOL_VIOLATION";
    public const string FtpConnectionFailedCode = "FTP_CONNECTION_FAILED";
    internal const string FtpTimeoutGuidance = "FTP only works when an FTP service is running on Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin; it will not answer from NXE or a game. XBDM/JRPC can still work in games even when FTP is down.";

    internal enum XbdmFailureKind {
        Unknown,
        PortClosedOrRefused,
        ConnectTimeout,
        HandshakeStalled,
        HandshakeRejected
    }

    private enum FtpFailureKind {
        Unknown,
        Unavailable,
        Unreachable,
        Timeout
    }

    public static bool TryBuildXbdmError(Exception ex, string fallbackMessage, string? targetDisplay, out CliErrorEnvelope error) {
        IReadOnlyList<Exception> exceptions = FlattenExceptions(ex).ToList();
        string? effectiveTarget = ExtractXbdmTargetDisplay(fallbackMessage)
            ?? exceptions.Select(inner => ExtractXbdmTargetDisplay(inner.Message)).FirstOrDefault(display => !string.IsNullOrWhiteSpace(display))
            ?? targetDisplay;

        XbdmFailureKind kind = ClassifyXbdmFailure(exceptions, fallbackMessage);
        if (IsGenericCancellationMessage(fallbackMessage) ||
            exceptions.OfType<OperationCanceledException>().Any(operationCanceled => IsGenericCancellationMessage(operationCanceled.Message))) {
            error = BuildXbdmError("XBDM connection timed out.", effectiveTarget);
            return true;
        }

        if (TryBuildXbdmProtocolViolationError(exceptions, effectiveTarget, out error))
            return true;

        if (TryBuildXbdmCommandError(exceptions, fallbackMessage, effectiveTarget, out error))
            return true;

        if (kind == XbdmFailureKind.Unknown) {
            error = EmptyError();
            return false;
        }

        error = kind switch {
            XbdmFailureKind.ConnectTimeout => BuildXbdmError("XBDM connection timed out.", effectiveTarget),
            XbdmFailureKind.HandshakeStalled => BuildXbdmError(
                "XBDM handshake stalled.",
                effectiveTarget,
                "The TCP connection was accepted, but no XBDM greeting arrived before the timeout expired."),
            XbdmFailureKind.HandshakeRejected => BuildXbdmError(
                "XBDM handshake rejected.",
                effectiveTarget,
                GetXbdmHandshakeDetail(exceptions)),
            XbdmFailureKind.PortClosedOrRefused => BuildXbdmError("XBDM connection refused.", effectiveTarget),
            _ => EmptyError()
        };

        return true;
    }

    public static bool TryBuildXbdmReadTimeoutError(Exception ex, string fallbackMessage, string? targetDisplay, out CliErrorEnvelope error) {
        IReadOnlyList<Exception> exceptions = FlattenExceptions(ex).ToList();
        bool hasReadTimeout = exceptions.Any(inner =>
            inner is TimeoutException &&
            LooksLikeXbdmReadTimeout(inner.Message));

        if (!hasReadTimeout && !LooksLikeXbdmReadTimeout(fallbackMessage)) {
            error = EmptyError();
            return false;
        }

        string? effectiveTarget = ExtractXbdmTargetDisplay(fallbackMessage)
            ?? exceptions.Select(inner => ExtractXbdmTargetDisplay(inner.Message)).FirstOrDefault(display => !string.IsNullOrWhiteSpace(display))
            ?? targetDisplay;

        string targetLabel = string.IsNullOrWhiteSpace(effectiveTarget)
            ? "the XBDM endpoint"
            : $"the XBDM endpoint at {effectiveTarget}";
        IReadOnlyList<string> nextSteps = string.IsNullOrWhiteSpace(effectiveTarget)
            ? new[] {
                "Check that XBDM is running and responsive.",
                "Confirm the console did not freeze after the TCP connection was accepted.",
                "Retry with a higher --timeout if the console is just slow."
            }
            : new[] {
                $"Check that {targetLabel} is running and responsive.",
                "Confirm the console did not freeze after the TCP connection was accepted.",
                "Retry with a higher --timeout if the console is just slow."
            };

        error = new CliErrorEnvelope(
            "XBDM response timed out",
            string.IsNullOrWhiteSpace(effectiveTarget)
                ? "XBDM accepted the connection, but no response arrived before the read timeout expired. Retry after confirming the console is responsive; increase --timeout if the console is just slow."
                : $"XBDM at {effectiveTarget} accepted the connection, but no response arrived before the read timeout expired. Retry after confirming the console is responsive; increase --timeout if the console is just slow.",
            XbdmConnectionFailedCode,
            nextSteps);
        return true;
    }

    public static bool TryBuildXbdmCommandError(IReadOnlyList<Exception> exceptions, string? fallbackMessage, string? targetDisplay, out CliErrorEnvelope error) {
        bool hasVerificationFailure = exceptions.Any(inner => LooksLikeXbdmMemoryVerificationFailure(inner.Message))
                                     || LooksLikeXbdmMemoryVerificationFailure(fallbackMessage);

        string message = !string.IsNullOrWhiteSpace(fallbackMessage)
            ? fallbackMessage
            : exceptions.Select(inner => inner.Message).FirstOrDefault(msg => !string.IsNullOrWhiteSpace(msg))
              ?? string.Empty;

        if (hasVerificationFailure) {
            string? failedOperation = ExtractXbdmFailedOperation(message)
                ?? exceptions.Select(inner => ExtractXbdmFailedOperation(inner.Message))
                    .FirstOrDefault(op => !string.IsNullOrWhiteSpace(op));

            error = BuildXbdmMemoryVerificationError(failedOperation, targetDisplay, message);
            return true;
        }

        if (!exceptions.Any(inner => LooksLikeXbdmCommandFailure(inner.Message)) &&
            !LooksLikeXbdmCommandFailure(message)) {
            error = EmptyError();
            return false;
        }

        string? failedOperationForCommand = ExtractXbdmFailedOperation(message)
            ?? exceptions.Select(inner => ExtractXbdmFailedOperation(inner.Message))
                .FirstOrDefault(op => !string.IsNullOrWhiteSpace(op))
            ?? "xbdm command";

        error = BuildXbdmCommandFailureError(failedOperationForCommand, message, targetDisplay);
        return true;
    }

    public static bool TryBuildXbdmProtocolViolationError(IReadOnlyList<Exception> exceptions, string? targetDisplay, out CliErrorEnvelope error) {
        XbdmProtocolViolationException? violation = exceptions.OfType<XbdmProtocolViolationException>().FirstOrDefault();
        if (violation == null) {
            error = EmptyError();
            return false;
        }

        string targetLabel = string.IsNullOrWhiteSpace(targetDisplay)
            ? "the XBDM endpoint"
            : $"the XBDM endpoint at {targetDisplay}";
        string raw = string.IsNullOrWhiteSpace(violation.RawResponse)
            ? "<empty>"
            : violation.RawResponse;

        error = new CliErrorEnvelope(
            "XBDM protocol violation",
            $"XBDM returned an invalid response for {violation.Command}: expected {violation.ExpectedDescription}, got '{raw}' from {targetLabel}. This is a backend or plugin failure, not a successful command.",
            XbdmProtocolViolationCode,
            new[] {
                $"Confirm {targetLabel} is responsive and running the expected XBDM/JRPC plugin.",
                "Retry the command after restarting the dashboard or plugin if the response stays empty.",
                "Capture the raw command and response when reporting this failure."
            });
        return true;
    }

    public static bool IsXbdmConnectionFailure(Exception ex) {
        return ClassifyXbdmFailure(FlattenExceptions(ex).ToList(), ex.Message) != XbdmFailureKind.Unknown;
    }

    public static bool IsTransientXbdmConnectionFailure(Exception ex) {
        XbdmFailureKind kind = ClassifyXbdmFailure(FlattenExceptions(ex).ToList(), ex.Message);
        return kind is XbdmFailureKind.ConnectTimeout or XbdmFailureKind.HandshakeStalled or XbdmFailureKind.PortClosedOrRefused;
    }

    public static CliErrorEnvelope BuildXbdmError(string summary, string? targetDisplay) {
        return BuildXbdmError(summary, targetDisplay, null);
    }

    private static CliErrorEnvelope BuildXbdmError(string summary, string? targetDisplay, string? detail) {
        string targetLabel = string.IsNullOrWhiteSpace(targetDisplay)
            ? "the XBDM endpoint"
            : $"the XBDM endpoint at {targetDisplay}";
        string preamble = string.IsNullOrWhiteSpace(detail) ? summary : $"{summary} {detail}";
        IReadOnlyList<string> nextSteps = string.IsNullOrWhiteSpace(targetDisplay)
            ? new[] {
                "Check that the XBDM endpoint is powered on and not frozen.",
                "Confirm the XBDM endpoint is reachable on the local network.",
                "Run rgh connect to set or refresh the target."
            }
            : new[] {
                $"Check that {targetLabel} is powered on and not frozen.",
                    "Confirm the XBDM endpoint is reachable on the local network.",
                    "Re-run rgh connect if the target changed."
                };

        return new CliErrorEnvelope(
            GetXbdmErrorTitle(summary),
            $"{preamble} Check that {targetLabel} is powered on, not frozen, and reachable, then try again or re-run rgh connect if the target changed.",
            XbdmConnectionFailedCode,
            nextSteps);
    }

    private static string GetXbdmErrorTitle(string summary) {
        string title = summary.Trim();
        if (title.EndsWith(".", StringComparison.Ordinal))
            title = title[..^1];

        return string.IsNullOrWhiteSpace(title)
            ? "XBDM connection failed"
            : title;
    }

    public static CliErrorEnvelope BuildFtpError(string? targetDisplay) {
        return BuildFtpError(targetDisplay, null);
    }

    public static CliErrorEnvelope BuildFtpError(string? targetDisplay, Exception? lastError) {
        return BuildFtpError(targetDisplay, ClassifyFtpFailure(lastError));
    }

    private static CliErrorEnvelope BuildFtpError(string? targetDisplay, FtpFailureKind kind) {
        string targetSuffix = string.IsNullOrWhiteSpace(targetDisplay) ? string.Empty : $" at {targetDisplay}";
        return kind switch {
            FtpFailureKind.Timeout => new CliErrorEnvelope(
                "FTP service unreachable",
                $"FTP connection{targetSuffix} timed out after retries. Check the console network path, then retry after starting an FTP service on Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin. {FtpTimeoutGuidance}",
                FtpConnectionFailedCode,
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? new[] {
                        "Check the console network path.",
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }
                    : new[] {
                        $"Check the console network path for {targetDisplay}.",
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }),
            FtpFailureKind.Unreachable => new CliErrorEnvelope(
                "FTP service unreachable",
                $"FTP connection{targetSuffix} could not reach the console. Check the console network path, then launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin. {FtpTimeoutGuidance}",
                FtpConnectionFailedCode,
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? new[] {
                        "Check the console network path.",
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }
                    : new[] {
                        $"Check the console network path for {targetDisplay}.",
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }),
            FtpFailureKind.Unavailable => new CliErrorEnvelope(
                "FTP service unavailable",
                $"FTP connection{targetSuffix} failed after retries. Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin, then retry. {FtpTimeoutGuidance}",
                FtpConnectionFailedCode,
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? new[] {
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Confirm the FTP port, user, and password are correct.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }
                    : new[] {
                        $"Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin on the console at {targetDisplay}.",
                        "Confirm the FTP port, user, and password are correct.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    }),
            _ => new CliErrorEnvelope(
                "FTP connection failed",
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? "FTP connection failed after retries. Confirm the console is powered on, an FTP service is running, credentials are correct, and the FTP port is reachable."
                    : $"FTP connection at {targetDisplay} failed after retries. Confirm the console is powered on, an FTP service is running, credentials are correct, and the FTP port is reachable.",
                FtpConnectionFailedCode,
                string.IsNullOrWhiteSpace(targetDisplay)
                    ? new[] {
                        "Confirm the console is powered on and not frozen.",
                        "Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin.",
                        "Run rgh ftp target to refresh FTP host, port, user, and password."
                    }
                    : new[] {
                        $"Launch Aurora, FreestyleDash, XexMenu, or a DashLaunch FTP plugin on the console at {targetDisplay}.",
                        "Confirm the FTP port, user, and password are correct.",
                        "Run rgh ftp target if the FTP service or credentials changed."
                    })
        };
    }

    public static bool TryBuildFtpError(Exception ex, string fallbackMessage, out CliErrorEnvelope error) {
        string? message = !string.IsNullOrWhiteSpace(fallbackMessage)
            ? fallbackMessage
            : ex.Message;
        IReadOnlyList<Exception> exceptions = FlattenExceptions(ex).ToList();
        string? targetDisplay = ExtractFtpTargetDisplay(message)
            ?? exceptions
                .Select(inner => ExtractFtpTargetDisplay(inner.Message))
                .FirstOrDefault(display => !string.IsNullOrWhiteSpace(display));

        if (!LooksLikeFtpConnectionFailure(message) &&
            !exceptions.Any(inner => LooksLikeFtpConnectionFailure(inner.Message))) {
            error = EmptyError();
            return false;
        }

        FtpFailureKind kind = ClassifyFtpFailure(ex);
        if (kind == FtpFailureKind.Unknown &&
            !LooksLikeFtpConnectionFailure(message) &&
            !exceptions.Any(inner => LooksLikeFtpConnectionFailure(inner.Message))) {
            error = EmptyError();
            return false;
        }

        error = BuildFtpError(targetDisplay, kind);
        return true;
    }

    internal static XbdmFailureKind ClassifyXbdmFailure(Exception ex) {
        return ClassifyXbdmFailure(FlattenExceptions(ex).ToList(), ex.Message);
    }

    private static XbdmFailureKind ClassifyXbdmFailure(IReadOnlyList<Exception> exceptions, string? fallbackMessage) {
        string? connectionMessage = exceptions
            .Select(inner => inner.Message)
            .FirstOrDefault(message => IsXbdmConnectionContextMessage(message));

        if (exceptions.Any(inner => LooksLikeXbdmHandshakeRejected(inner.Message)))
            return XbdmFailureKind.HandshakeRejected;

        if (exceptions.Any(inner => LooksLikeXbdmHandshakeStalled(inner.Message)))
            return XbdmFailureKind.HandshakeStalled;

        if (exceptions.Any(inner => LooksLikeXbdmConnectTimeout(inner.Message)))
            return XbdmFailureKind.ConnectTimeout;

        if (exceptions.Any(inner => IsXbdmPortClosedOrRefused(inner)))
            return XbdmFailureKind.PortClosedOrRefused;

        if (LooksLikeXbdmConnectTimeout(fallbackMessage) || LooksLikeXbdmHandshakeStalled(fallbackMessage) || LooksLikeXbdmHandshakeRejected(fallbackMessage))
            return ClassifyXbdmMessage(fallbackMessage);

        if (IsXbdmConnectionContextMessage(connectionMessage)) {
            if (LooksLikeXbdmRejectedText(connectionMessage))
                return XbdmFailureKind.HandshakeRejected;
            if (LooksLikeXbdmStalledText(connectionMessage))
                return XbdmFailureKind.HandshakeStalled;
            if (LooksLikeXbdmTimeoutText(connectionMessage))
                return XbdmFailureKind.ConnectTimeout;
            return XbdmFailureKind.PortClosedOrRefused;
        }

        return XbdmFailureKind.Unknown;
    }

    private static XbdmFailureKind ClassifyXbdmMessage(string? message) {
        if (LooksLikeXbdmPortClosedOrRefused(message))
            return XbdmFailureKind.PortClosedOrRefused;
        if (LooksLikeXbdmHandshakeRejected(message))
            return XbdmFailureKind.HandshakeRejected;
        if (LooksLikeXbdmHandshakeStalled(message))
            return XbdmFailureKind.HandshakeStalled;
        if (LooksLikeXbdmConnectTimeout(message))
            return XbdmFailureKind.ConnectTimeout;
        return XbdmFailureKind.Unknown;
    }

    private static bool IsGenericCancellationMessage(string? message) {
        return string.IsNullOrWhiteSpace(message) ||
               message.Equals("The operation was canceled.", StringComparison.OrdinalIgnoreCase) ||
               message.Equals("A task was canceled.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeXbdmReadTimeout(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("XBDM read timed out", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXbdmPortClosedOrRefused(Exception ex) {
        return (ex is SocketException socketException && socketException.SocketErrorCode == SocketError.ConnectionRefused) ||
               LooksLikeXbdmPortClosedOrRefused(ex.Message);
    }

    private static bool IsXbdmConnectionContextMessage(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("XBDM connection to", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("XBDM handshake", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("XBDM TCP connection", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Console connection", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("accepted the TCP connection", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("did not return the greeting", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("returned an unexpected handshake response", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeXbdmConnectTimeout(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return IsXbdmConnectionContextMessage(message) &&
               (message.Contains("did not complete", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeXbdmHandshakeStalled(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return IsXbdmConnectionContextMessage(message) &&
               (message.Contains("stalled", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("silent", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("greeting", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("read timed out", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeXbdmHandshakeRejected(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return IsXbdmConnectionContextMessage(message) &&
               (message.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unexpected", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("closed the connection before the greeting arrived", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeXbdmPortClosedOrRefused(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("actively refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeXbdmRejectedText(string? message) {
        return LooksLikeXbdmHandshakeRejected(message);
    }

    private static bool LooksLikeXbdmStalledText(string? message) {
        return LooksLikeXbdmHandshakeStalled(message);
    }

    private static bool LooksLikeXbdmTimeoutText(string? message) {
        return LooksLikeXbdmConnectTimeout(message);
    }

    private static bool LooksLikeXbdmCommandFailure(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (!message.Contains(" failed:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (LooksLikeXbdmReadTimeout(message) ||
            LooksLikeXbdmConnectTimeout(message) ||
            IsXbdmConnectionContextMessage(message))
            return false;

        return true;
    }

    private static bool LooksLikeXbdmMemoryVerificationFailure(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("memory verification failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractXbdmFailedOperation(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        int failedToken = message.IndexOf(" failed:", StringComparison.OrdinalIgnoreCase);
        if (failedToken <= 0)
            return null;

        string operation = message[..failedToken].Trim();
        if (string.IsNullOrWhiteSpace(operation))
            return null;

        int lastSpace = operation.LastIndexOf(' ');
        if (lastSpace > 0)
            operation = operation[(lastSpace + 1)..];

        return operation;
    }

    private static CliErrorEnvelope BuildXbdmCommandFailureError(string operation, string message, string? targetDisplay) {
        string targetLabel = string.IsNullOrWhiteSpace(targetDisplay)
            ? "the XBDM endpoint"
            : $"the XBDM endpoint at {targetDisplay}";
        string detail = string.IsNullOrWhiteSpace(message)
            ? $"XBDM command {operation} failed at {targetLabel}."
            : message;
        string rejectionSuffix = detail.Contains("rejected", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" rejected by XBDM at {targetLabel}.";

        return new CliErrorEnvelope(
            "XBDM command rejected",
            detail.Contains("XBDM", StringComparison.OrdinalIgnoreCase)
                ? detail + rejectionSuffix
                : $"{detail} at {targetLabel}.{rejectionSuffix}",
            XbdmCommandRejectedCode,
            new[] {
                $"Check that {targetLabel} is running and allows the {operation} command.",
                "Confirm no debugger or security policy is blocking the request.",
                "Retry the command to confirm whether the failure is deterministic."
            });
    }

    private static CliErrorEnvelope BuildXbdmMemoryVerificationError(string? operation, string? targetDisplay, string message) {
        string normalizedOperation = string.IsNullOrWhiteSpace(operation) ? "memory write" : operation;
        string targetLabel = string.IsNullOrWhiteSpace(targetDisplay)
            ? "the XBDM endpoint"
            : $"the XBDM endpoint at {targetDisplay}";

        return new CliErrorEnvelope(
            "XBDM memory verification failed",
            $"{(string.IsNullOrWhiteSpace(message) ? $"Memory verification failed for {normalizedOperation} at {targetLabel}." : message)} Verify writable memory and that no debugger is restoring values.",
            XbdmMemoryVerificationFailedCode,
            new[] {
                $"Check that {targetLabel} permits writes to the requested address range.",
                "Confirm that no debug/protection feature is reverting writes.",
                "Retry the command after restoring or resetting memory protections."
            });
    }

    private static string? GetXbdmHandshakeDetail(IEnumerable<Exception> exceptions) {
        foreach (Exception ex in exceptions) {
            string? message = ex.Message;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (!IsXbdmConnectionContextMessage(message))
                continue;

            int detailStart = message.LastIndexOf(": ", StringComparison.Ordinal);
            if (detailStart < 0 || detailStart + 2 >= message.Length)
                continue;

            string detail = message[(detailStart + 2)..].Trim();
            if (!string.IsNullOrWhiteSpace(detail))
                return detail;
        }

        return null;
    }

    private static bool LooksLikeFtpTimeout(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("did not complete", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("cancelled read from socket stream", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("canceled read from socket stream", StringComparison.OrdinalIgnoreCase);
    }

    private static FtpFailureKind ClassifyFtpFailure(Exception? ex) {
        if (ex == null)
            return FtpFailureKind.Unknown;

        IReadOnlyList<Exception> exceptions = FlattenExceptions(ex).ToList();
        if (exceptions.Any(inner => inner is TimeoutException || LooksLikeFtpTimeout(inner.Message) || IsSocketTimeout(inner)))
            return FtpFailureKind.Timeout;

        if (exceptions.Any(inner => IsSocketUnreachable(inner) || LooksLikeFtpUnreachable(inner.Message)))
            return FtpFailureKind.Unreachable;

        if (exceptions.Any(inner => IsSocketUnavailable(inner) || LooksLikeFtpUnavailable(inner.Message)))
            return FtpFailureKind.Unavailable;

        return FtpFailureKind.Unknown;
    }

    private static bool IsSocketTimeout(Exception ex) {
        return ex is SocketException socketException && socketException.SocketErrorCode == SocketError.TimedOut;
    }

    private static bool IsSocketUnavailable(Exception ex) {
        return ex is SocketException socketException &&
               (socketException.SocketErrorCode == SocketError.ConnectionRefused || LooksLikeFtpUnavailable(socketException.Message));
    }

    private static bool IsSocketUnreachable(Exception ex) {
        return ex is SocketException socketException &&
               (socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.HostUnreachable or SocketError.NetworkDown or SocketError.NetworkReset or SocketError.NetworkUnreachable or SocketError.NoData or SocketError.TryAgain or SocketError.AddressNotAvailable ||
                LooksLikeFtpUnreachable(socketException.Message));
    }

    private static bool LooksLikeFtpUnreachable(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeFtpUnavailable(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("refused", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception ex) {
        yield return ex;

        if (ex is AggregateException aggregate) {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions) {
                yield return inner;
                if (inner.InnerException != null) {
                    foreach (Exception nested in FlattenExceptions(inner.InnerException))
                        yield return nested;
                }
            }
        }
        else if (ex.InnerException != null) {
            foreach (Exception inner in FlattenExceptions(ex.InnerException))
                yield return inner;
        }
    }

    private static bool LooksLikeFtpConnectionFailure(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("FTP connection", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("FTP service unavailable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("FTP service unreachable", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("failed after retries", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractFtpTargetDisplay(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        foreach (string prefix in new[] { "FTP connection to ", "FTP connection at ", "FTP service unavailable at ", "FTP service unreachable at " }) {
            int start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                continue;

            start += prefix.Length;
            int end = message.IndexOf(" failed after retries", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                end = message.IndexOf(" timed out after retries", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                end = message.IndexOf('.', start);
            if (end <= start)
                end = message.Length;

            return message.Substring(start, end - start);
        }

        return null;
    }

    private static string? ExtractXbdmTargetDisplay(string? message) {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        foreach (string prefix in new[] {
            "XBDM connection to ",
            "XBDM TCP connection to ",
            "XBDM handshake at ",
            "Console connection to "
        }) {
            int start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                continue;

            start += prefix.Length;
            int end = message.IndexOf(" did not complete", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                end = message.IndexOf(" stalled", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                end = message.IndexOf(" rejected", start, StringComparison.OrdinalIgnoreCase);
            if (end <= start)
                continue;

            return message.Substring(start, end - start);
        }

        return null;
    }

    private static CliErrorEnvelope EmptyError() {
        return new CliErrorEnvelope(string.Empty, string.Empty, string.Empty, Array.Empty<string>());
    }
}
