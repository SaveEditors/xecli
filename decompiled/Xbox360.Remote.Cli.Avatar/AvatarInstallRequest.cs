namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarInstallRequest(AvatarItemRecord Item, string DeviceRoot, AvatarOwnershipPatch Ownership, string? WorkingDirectory = null);
