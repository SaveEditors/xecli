namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record InstalledHomebrewPackage(string Id, string DisplayName, string InstallFolderName, string ArchivePath, string InstallPath, int FileCount, long TotalBytes);
