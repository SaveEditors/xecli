using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.IO;
using Xbox360.Fatx.Layout;
using Xbox360.Fatx.Reading;

namespace Xbox360.Fatx;

public sealed class FatxVolume : IAsyncDisposable, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private bool _disposed;

    private FatxVolume(Stream stream, FatxPartition partition, FatxVolumeHeader header, bool leaveOpen)
    {
        _stream = stream;
        Partition = partition;
        Header = header;
        _leaveOpen = leaveOpen;
        PathResolver = new FatxPathResolver(this);
        DirectoryReader = new FatxDirectoryReader(this);
        AllocationTable = new FatxAllocationTableReader(this);
        ClusterChains = new FatxClusterChainReader(this);
    }

    public FatxPartition Partition { get; }

    public FatxVolumeHeader Header { get; }

    public FatxPathResolver PathResolver { get; }

    public FatxDirectoryReader DirectoryReader { get; }

    public FatxAllocationTableReader AllocationTable { get; }

    public FatxClusterChainReader ClusterChains { get; }

    internal long ChainMapOffset => Header.ChainMapOffset;

    internal long DataRegionOffset => Header.DataRegionOffset;

    internal int ClusterSizeBytes => checked((int)Header.ClusterSize);

    internal int ChainEntrySizeBytes => Header.ChainEntrySize;

    internal uint RootDirectoryCluster => Header.RootDirectoryCluster;

    internal Stream BaseStream => _stream;

    public FatxVolumeInfo Info => new()
    {
        Magic = Header.Magic,
        Label = Header.Label,
        PartitionOffset = Partition.Offset,
        PartitionLength = Partition.Length,
        RootDirectoryCluster = Header.RootDirectoryCluster,
        ClusterSize = Header.ClusterSize,
        TotalClusters = Header.TotalClusters,
    };

    public static async Task<FatxVolume> OpenAsync(Stream baseStream, FatxPartition partition, bool leaveOpen, CancellationToken cancellationToken = default)
    {
        var slice = new StreamSlice(baseStream, partition.Offset, partition.Length, leaveOpen: true);
        var header = await FatxVolumeHeader.ReadAsync(slice, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(header.Magic, "FATX", StringComparison.Ordinal) &&
            !string.Equals(header.Magic, "XTAF", StringComparison.Ordinal))
        {
            throw new FatxUnsupportedLayoutException($"Partition '{partition.Name}' does not contain a readable FATX/XTAF header.");
        }

        return new FatxVolume(slice, partition, header, leaveOpen);
    }

    public Task<IReadOnlyList<Entries.FatxEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return DirectoryReader.ReadDirectoryAsync(path, cancellationToken);
    }

    public Task<Entries.FatxEntry> ResolvePathAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return PathResolver.ResolveAsync(path, cancellationToken);
    }

    public async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var entry = await ResolvePathAsync(path, cancellationToken).ConfigureAwait(false);
        if (entry is not Entries.FatxFileEntry fileEntry)
        {
            throw new FatxException($"Path '{path}' does not reference a FATX file.");
        }

        if (fileEntry.Size == 0 || !fileEntry.FirstCluster.HasValue || fileEntry.FirstCluster.Value == 0)
        {
            return Array.Empty<byte>();
        }

        await using Stream source = await ClusterChains.OpenChainAsync(fileEntry.FirstCluster.Value, cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(fileEntry.Size > int.MaxValue ? 0 : (int)fileEntry.Size);
        byte[] buffer = new byte[0x10000];
        long remaining = fileEntry.Size;
        while (remaining > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }

        return destination.ToArray();
    }

    internal async Task<byte[]> ReadBytesAtAsync(long offset, int count, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = offset;
            return await FatxBinaryReader.ReadBytesAsync(_stream, count, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    internal long GetClusterOffset(uint cluster)
    {
        if (cluster == 0)
        {
            throw new FatxException("Cluster index 0 is reserved and cannot be read.");
        }

        return checked(DataRegionOffset + ((long)cluster - 1L) * ClusterSizeBytes);
    }

    internal Task<byte[]> ReadClusterBytesAsync(uint cluster, CancellationToken cancellationToken = default)
        => ReadBytesAtAsync(GetClusterOffset(cluster), ClusterSizeBytes, cancellationToken);

    internal async Task WriteBytesAtAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_stream.CanWrite)
        {
            throw new FatxException("This FATX volume was opened read-only.");
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = offset;
            await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    internal Task WriteClusterAsync(uint cluster, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (data.Length > ClusterSizeBytes)
        {
            throw new FatxException($"Cluster payload exceeds FATX cluster size ({ClusterSizeBytes} bytes).");
        }

        byte[] buffer = new byte[ClusterSizeBytes];
        data.CopyTo(buffer);
        return WriteBytesAtAsync(GetClusterOffset(cluster), buffer, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ioLock.Dispose();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ioLock.Dispose();
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
