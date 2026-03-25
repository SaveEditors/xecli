namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record OriginalXboxCompatibilityInstallResult(string Mode, string SetId, string SetName, string Target, string CompatibilityPath, bool CompatibilityInstalled, bool FixerIncluded, bool FixerInstalled, string? FixerPath, int FileCount, long TotalBytes, string Instructions);
