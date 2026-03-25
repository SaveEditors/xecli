using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarInstallPlan(AvatarItemRecord Item, string PatchedLocalPath, string RemoteDirectory, string RemoteFilePath, ulong PrimaryXuid, IReadOnlyList<ulong> OwnershipTable);
