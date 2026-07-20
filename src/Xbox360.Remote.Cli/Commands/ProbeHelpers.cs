using Xbox360.Remote.Cli.Logging;

namespace Xbox360.Remote.Cli.Commands;

internal static class ProbeHelpers {
    internal sealed record ProbeResult<T>(T? Value, bool TimedOut, bool Failed) where T : class {
        public bool Unavailable => TimedOut || Failed;
    }

    internal static int GetProbeTimeoutMs(int timeoutMs) {
        return Math.Clamp(timeoutMs / 4, 250, 1500);
    }

    internal static async Task<ProbeResult<T>> RunProbeAsync<T>(
        string label,
        Func<CancellationToken, Task<T?>> probe,
        int timeoutMs,
        Action<string>? addWarning = null,
        T? fallback = null) where T : class {
        using CancellationTokenSource probeCts = CancellationTokenSource.CreateLinkedTokenSource(CliHelpers.ConsoleCancellationToken);
        probeCts.CancelAfter(timeoutMs);
        try {
            T? value = await probe(probeCts.Token);
            return new ProbeResult<T>(value, TimedOut: false, Failed: false);
        }
        catch (OperationCanceledException) when (probeCts.IsCancellationRequested && !CliHelpers.ConsoleCancellationToken.IsCancellationRequested) {
            addWarning?.Invoke(CommandLogRedactor.RedactFreeText($"{label} timed out after {timeoutMs} ms."));
            return new ProbeResult<T>(fallback, TimedOut: true, Failed: false);
        }
        catch (TimeoutException) {
            addWarning?.Invoke(CommandLogRedactor.RedactFreeText($"{label} timed out after {timeoutMs} ms."));
            return new ProbeResult<T>(fallback, TimedOut: true, Failed: false);
        }
        catch (Exception ex) {
            addWarning?.Invoke(CommandLogRedactor.RedactFreeText($"{label} failed: {ex.Message}"));
            return new ProbeResult<T>(fallback, TimedOut: false, Failed: true);
        }
    }
}
