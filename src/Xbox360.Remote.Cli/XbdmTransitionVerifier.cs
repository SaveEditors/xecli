using System.Diagnostics;
using Xbox360.Remote;

namespace Xbox360.Remote.Cli;

internal sealed record XbdmTransitionObservation(
    bool Succeeded,
    bool DisconnectObserved,
    uint? PreviousProcessId,
    uint? CurrentProcessId,
    string? PreviousRunningXex,
    string? RunningXex,
    string? Evidence,
    long ElapsedMilliseconds,
    string? LastError);

internal static class XbdmTransitionVerifier {
    private const int PollDelayMilliseconds = 1000;

    public static async Task<XbdmTransitionObservation> WaitForProcessChangeAsync(
        (string Ip, int Port, int TimeoutMs) target,
        uint? previousProcessId,
        string? previousRunningXex,
        string? expectedXex,
        int timeoutSeconds,
        CancellationToken cancellationToken) {
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool disconnectObserved = false;
        string? lastError = null;
        uint? lastProcessId = null;
        string? lastRunningXex = null;
        int attemptTimeout = Math.Clamp(target.TimeoutMs, 500, 3000);

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds)) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                using XbdmClient client = await CliHelpers.ConnectResolvedAsync(
                    target.Ip,
                    target.Port,
                    attemptTimeout,
                    cancellationToken);
                uint? currentProcessId = await client.GetCurrentProcessIdAsync(cancellationToken);
                string? runningXex = expectedXex == null
                    ? null
                    : await client.GetRunningXexPathAsync(null, cancellationToken);
                lastProcessId = currentProcessId;
                lastRunningXex = runningXex;
                bool processChanged = previousProcessId.HasValue &&
                    currentProcessId.HasValue &&
                    previousProcessId.Value != currentProcessId.Value;
                bool pathMatches = expectedXex == null || RunningPathMatches(expectedXex, runningXex);
                bool activePathChanged = expectedXex != null &&
                    !string.IsNullOrWhiteSpace(previousRunningXex) &&
                    !RunningPathMatches(expectedXex, previousRunningXex);
                bool reconnectObserved = disconnectObserved && currentProcessId.HasValue;
                bool transitionObserved = expectedXex == null
                    ? processChanged || reconnectObserved
                    : pathMatches && (processChanged || reconnectObserved || activePathChanged);
                if (transitionObserved) {
                    return new XbdmTransitionObservation(
                        true,
                        disconnectObserved,
                        previousProcessId,
                        currentProcessId,
                        previousRunningXex,
                        runningXex,
                        DescribeEvidence(processChanged, reconnectObserved, activePathChanged),
                        stopwatch.ElapsedMilliseconds,
                        lastError);
                }

                if (expectedXex != null && !pathMatches) {
                    lastError = $"The active XEX was '{runningXex ?? "unknown"}' instead of '{expectedXex}'.";
                }
                else if (expectedXex != null) {
                    lastError = "The requested XEX is active, but XBDM exposed no process, path, or reconnect transition from the pre-launch state.";
                }
                else {
                    lastError = "XBDM remained continuously available and the active process ID did not change.";
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;
                disconnectObserved = true;
                lastError = ex.Message;
            }

            TimeSpan remaining = TimeSpan.FromSeconds(timeoutSeconds) - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Min(PollDelayMilliseconds, remaining.TotalMilliseconds)),
                cancellationToken);
        }

        return new XbdmTransitionObservation(
            false,
            disconnectObserved,
            previousProcessId,
            lastProcessId,
            previousRunningXex,
            lastRunningXex,
            null,
            stopwatch.ElapsedMilliseconds,
            lastError);
    }

    private static string DescribeEvidence(bool processChanged, bool reconnectObserved, bool activePathChanged) {
        List<string> evidence = new(3);
        if (processChanged)
            evidence.Add("process-id-changed");
        if (reconnectObserved)
            evidence.Add("disconnect-reconnect");
        if (activePathChanged)
            evidence.Add("active-xex-changed");
        return string.Join("+", evidence);
    }

    public static async Task<bool> TryResumeAsync(
        (string Ip, int Port, int TimeoutMs) target,
        CancellationToken cancellationToken) {
        try {
            using XbdmClient client = await CliHelpers.ConnectResolvedAsync(
                target.Ip,
                target.Port,
                Math.Clamp(target.TimeoutMs, 500, 3000),
                cancellationToken);
            await client.SendCommandExpectOkAsync("go", cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            return false;
        }
    }

    internal static bool RunningPathMatches(string expectedXex, string? runningXex) {
        if (string.IsNullOrWhiteSpace(runningXex))
            return false;

        string expected = NormalizePath(expectedXex);
        string actual = NormalizePath(runningXex);
        if (expected.StartsWith("\\device\\", StringComparison.OrdinalIgnoreCase))
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        int colon = expected.IndexOf(':');
        if (colon >= 0)
            expected = expected[(colon + 1)..];
        else if (expected.StartsWith('\\')) {
            string[] segments = expected.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            expected = segments.Length > 1 ? "\\" + string.Join('\\', segments.Skip(1)) : expected;
        }

        if (!expected.StartsWith('\\'))
            expected = "\\" + expected;
        return actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) {
        return path.Trim().Replace('/', '\\').TrimEnd('\\');
    }
}
