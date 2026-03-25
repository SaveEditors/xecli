using Xbox360.Fatx.Layout;
using Xbox360.Fatx.IO;

namespace Xbox360.Fatx;

public sealed class FatxDisk : IAsyncDisposable, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    private FatxDisk(string sourcePath, Stream stream, IReadOnlyList<FatxPartition> partitions, bool leaveOpen)
    {
        SourcePath = sourcePath;
        _stream = stream;
        Partitions = partitions;
        _leaveOpen = leaveOpen;
    }

    public string SourcePath { get; }

    public IReadOnlyList<FatxPartition> Partitions { get; }

    public static Task<FatxDisk> OpenReadAsync(string sourcePath, FatxOpenOptions? options = null, CancellationToken cancellationToken = default)
        => OpenAsync(sourcePath, options ?? new FatxOpenOptions(), cancellationToken);

    public static async Task<FatxDisk> OpenAsync(string sourcePath, FatxOpenOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        options ??= new FatxOpenOptions();

        var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            options.ReadOnly ? FileAccess.Read : FileAccess.ReadWrite,
            options.ReadOnly ? FileShare.ReadWrite : FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            var partitions = await Xbox360StorageLayoutDetector.DetectAsync(stream, options, cancellationToken).ConfigureAwait(false);
            return new FatxDisk(sourcePath, stream, partitions, options.LeaveOpen);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<FatxVolume> OpenVolumeAsync(FatxPartition partition, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(partition);
        return FatxVolume.OpenAsync(_stream, partition, leaveOpen: true, cancellationToken);
    }

    public Stream OpenPartitionStream(FatxPartition partition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(partition);
        return new StreamSlice(_stream, partition.Offset, partition.Length, leaveOpen: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
        if (!_leaveOpen)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
