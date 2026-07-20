namespace Xbox360.Remote.Cli;

internal static class CliPaths {
    private const string RootOverrideEnvVar = "XECLI_HOME";
    internal const string TitleDatabaseEnvironmentVariable = "XECLI_TITLE_DATABASE";
    internal const string PortableMarkerFileName = "xecli.portable";

    public static bool IsPortable => File.Exists(PortableMarkerPath);

    public static bool HasRootOverride => RootOverride != null;

    public static string StorageModeDisplayName => HasRootOverride
        ? "Custom (XECLI_HOME)"
        : IsPortable
            ? "Portable - package local"
            : "Installed - Windows profile";

    public static string DataRoot => RootOverride
        ?? (IsPortable
            ? PortableDataRoot
            : Path.Combine(GetLocalAppDataRoot(), "XeCLI"));

    public static string ConfigPath => Path.Combine(
        ConfigDirectory,
        "config.json");

    public static string CachePath => Path.Combine(DataRoot, "cache");

    public static string DefaultTitleDatabasePath => Path.Combine(ConfigDirectory, "titleids.csv");

    public static string LegacyTitleDatabasePath => Path.Combine(ConfigDirectory, "titleids.local.csv");

    public static string DiscoveredTitleDatabasePath => Path.Combine(ConfigDirectory, "titleids.discovered.csv");

    public static string ConfigDirectory => Path.Combine(
        RootOverride
        ?? (IsPortable
            ? PortableDataRoot
            : Path.Combine(GetAppDataRoot(), "XeCLI")));

    public static string PortableMarkerPath => Path.Combine(AppContext.BaseDirectory, PortableMarkerFileName);

    public static string ResolveTitleDatabasePath(string path) {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Title Database path cannot be empty.", nameof(path));

        string trimmed = Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(trimmed, ConfigDirectory);
    }

    public static string GetStoredTitleDatabasePath(string path) {
        string fullPath = Path.GetFullPath(path);
        string relativePath = Path.GetRelativePath(ConfigDirectory, fullPath);
        if (!Path.IsPathRooted(relativePath) &&
            !relativePath.Equals("..", StringComparison.Ordinal) &&
            !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)) {
            return relativePath;
        }

        return fullPath;
    }

    private static string PortableDataRoot => Path.Combine(AppContext.BaseDirectory, "UserData");

    private static string? RootOverride {
        get {
            string? root = Environment.GetEnvironmentVariable(RootOverrideEnvVar);
            return string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
        }
    }

    private static string GetAppDataRoot() {
        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        return string.IsNullOrWhiteSpace(appData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : appData;
    }

    private static string GetLocalAppDataRoot() {
        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        return string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
    }
}
