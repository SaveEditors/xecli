using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarItemRecord(uint TitleId, string TitleName, string? ContentType, string ContentId, string RelativeStorePath, string RelativePath, string SourcePath, long SizeBytes, AvatarPackageMagic Magic, string DisplayName, string? GameName, string? Publisher, IReadOnlyList<string> Tags, string? Sha256 = null, string? DownloadUrl = null);
