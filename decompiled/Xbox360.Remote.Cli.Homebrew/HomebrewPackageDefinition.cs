namespace Xbox360.Remote.Cli.Homebrew;

internal sealed record HomebrewPackageDefinition(string Id, string DisplayName, string Description, string InstallFolderName, string PrimaryUrl, string? MirrorUrl);
