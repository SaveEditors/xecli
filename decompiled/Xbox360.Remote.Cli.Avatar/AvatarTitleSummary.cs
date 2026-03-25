using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarTitleSummary(uint TitleId, string TitleName, int ItemCount, long TotalBytes, IReadOnlyList<string> Publishers);
