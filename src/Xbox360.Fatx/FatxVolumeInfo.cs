namespace Xbox360.Fatx;

public sealed class FatxVolumeInfo
{
    public required string Magic { get; init; }

    public required string Label { get; init; }

    public required long PartitionOffset { get; init; }

    public required long PartitionLength { get; init; }

    public required uint RootDirectoryCluster { get; init; }

    public required uint ClusterSize { get; init; }

    public required uint TotalClusters { get; init; }
}
