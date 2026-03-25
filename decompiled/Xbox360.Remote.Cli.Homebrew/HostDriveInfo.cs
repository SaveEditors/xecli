namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HostDriveInfo(string Key, string RootPath, string DisplayName, string? VolumeLabel, string? FileSystem, long TotalBytes, long FreeBytes, bool IsRemovable);
