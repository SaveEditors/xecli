namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarPackageMetadata(AvatarPackageMagic Magic, uint TitleId, string? DisplayName, string? Publisher, string? GameName);
