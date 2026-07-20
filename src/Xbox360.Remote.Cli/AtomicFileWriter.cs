using System.Text;

namespace Xbox360.Remote.Cli;

internal static class AtomicFileWriter {
    private const int MaxWriteAttempts = 8;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(10);

    public static void WriteAllText(string path, string contents, Encoding? encoding = null) {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory ?? Path.GetTempPath(), $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        Encoding actualEncoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++) {
            try {
                File.WriteAllText(tempPath, contents, actualEncoding);
                File.Move(tempPath, fullPath, overwrite: true);

                return;
            }
            catch (Exception ex) when (IsTransientWriteConflict(ex) && attempt < MaxWriteAttempts) {
                Thread.Sleep(GetRetryDelay(attempt));
            }
            finally {
                if (File.Exists(tempPath)) {
                    try {
                        File.Delete(tempPath);
                    }
                    catch {
                        // Best-effort cleanup only.
                    }
                }
            }
        }
    }

    public static async Task WriteAsync(string path, Func<FileStream, Task> writeAsync) {
        ArgumentNullException.ThrowIfNull(writeAsync);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory ?? Path.GetTempPath(), $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try {
            await using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 0x10000,
                FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                await writeAsync(stream);
                await stream.FlushAsync();
            }

            for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++) {
                try {
                    File.Move(tempPath, fullPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (IsTransientWriteConflict(ex) && attempt < MaxWriteAttempts) {
                    await Task.Delay(GetRetryDelay(attempt));
                }
            }
        }
        finally {
            if (File.Exists(tempPath)) {
                try {
                    File.Delete(tempPath);
                }
                catch {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static bool IsTransientWriteConflict(Exception ex) {
        if (ex is UnauthorizedAccessException)
            return true;

        if (ex is IOException ioException) {
            int errorCode = ioException.HResult & 0xFFFF;
            return errorCode is 32 or 33;
        }

        return false;
    }

    private static TimeSpan GetRetryDelay(int attempt) {
        return TimeSpan.FromMilliseconds(InitialRetryDelay.TotalMilliseconds * attempt);
    }
}
