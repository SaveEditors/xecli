namespace Xbox360.Remote.Cli.Avatar;

internal sealed record AvatarCollectionFingerprint(string RootPath, int FileCount, long TotalBytes, long LatestWriteTimeUtcTicks);
