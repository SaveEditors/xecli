using Xbox360.Fatx.Exceptions;

namespace Xbox360.Fatx.Reading;

public sealed class FatxClusterChainReader
{
    private readonly FatxVolume _volume;

    public FatxClusterChainReader(FatxVolume volume)
    {
        _volume = volume;
    }

    public async Task<Stream> OpenChainAsync(uint firstCluster, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (firstCluster == 0)
        {
            return Stream.Null;
        }

        var clusters = new List<uint>();
        var visited = new HashSet<uint>();
        var current = firstCluster;

        while (true)
        {
            if (!visited.Add(current))
            {
                throw new FatxException($"Detected a loop in the FATX cluster chain starting at {firstCluster}.");
            }

            clusters.Add(current);
            var next = await _volume.AllocationTable.ReadNextClusterAsync(current, cancellationToken).ConfigureAwait(false);
            if (next is not uint nextCluster)
            {
                break;
            }

            current = nextCluster;
        }

        return new FatxClusterChainStream(_volume, clusters);
    }
}
