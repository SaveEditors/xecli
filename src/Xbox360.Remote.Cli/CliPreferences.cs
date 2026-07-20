namespace Xbox360.Remote.Cli;

internal static class CliPreferences {
    public const int DefaultConnectionTimeoutMs = 5000;
    public const int DefaultReconnectAttempts = 3;
    public const int DefaultReconnectDelaySeconds = 10;
    public const string DefaultScreenshotAction = "notification";
    public const string DefaultFtpConflictBehavior = "ask";

    public static int GetConnectionTimeoutMs(CliConfig config) =>
        Math.Clamp(config.ConnectionTimeoutMs ?? DefaultConnectionTimeoutMs, 1000, 60000);

    public static int GetReconnectAttempts(CliConfig config) =>
        Math.Clamp(config.ReconnectAttempts ?? DefaultReconnectAttempts, 1, 10);

    public static int GetReconnectDelaySeconds(CliConfig config) =>
        Math.Clamp(config.ReconnectDelaySeconds ?? DefaultReconnectDelaySeconds, 1, 60);

    public static string NormalizeScreenshotAction(string? value) =>
        value?.Trim().ToLowerInvariant() switch {
            "preview" => "preview",
            "folder" => "folder",
            _ => DefaultScreenshotAction
        };

    public static string NormalizeFtpConflictBehavior(string? value) =>
        value?.Trim().ToLowerInvariant() switch {
            "keep-both" => "keep-both",
            "skip" => "skip",
            _ => DefaultFtpConflictBehavior
        };

    public static string GetDefaultLocalDirectory() {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path;
    }

    public static string GetDefaultScreenshotDirectory() {
        if (CliPaths.IsPortable || CliPaths.HasRootOverride)
            return Path.Combine(CliPaths.DataRoot, "captures");

        string path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.CurrentDirectory;
        return Path.Combine(path, "XeCLI", "Captures");
    }
}
