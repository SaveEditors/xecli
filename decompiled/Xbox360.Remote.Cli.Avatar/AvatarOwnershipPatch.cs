using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarOwnershipPatch(ulong PrimaryXuid, IReadOnlyList<ulong>? AdditionalXuids = null);
