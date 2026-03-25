using Xbox360.Fatx.Exceptions;
using Xbox360.Fatx.IO;

namespace Xbox360.Fatx.Reading;

internal sealed class FatxClusterChainStream : Stream
{
    private readonly FatxVolume _volume;
    private readonly IReadOnlyList<uint> _clusters;
    private readonly long _clusterSize;
    private readonly long _dataRegionOffset;
    private int _clusterIndex;
    private long _position;
    private StreamSlice? _currentSlice;
    private bool _disposed;

    public FatxClusterChainStream(FatxVolume volume, IReadOnlyList<uint> clusters)
    {
        _volume = volume;
        _clusters = clusters;
        _clusterSize = volume.Header.ClusterSize;
        _dataRegionOffset = volume.Header.DataRegionOffset;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _clusters.Count * _clusterSize;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("Cluster chain streams do not support seeking.");
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_disposed || buffer.IsEmpty)
        {
            return 0;
        }

        return ReadCore(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed || buffer.IsEmpty)
        {
            return 0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Task.FromResult(ReadCore(buffer.Span)).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Cluster chain streams do not support seeking.");

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentSlice?.Dispose();
        _currentSlice = null;
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_currentSlice is not null)
        {
            await _currentSlice.DisposeAsync().ConfigureAwait(false);
            _currentSlice = null;
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private int ReadCore(Span<byte> buffer)
    {
        if (_position >= Length)
        {
            return 0;
        }

        var totalRead = 0;
        while (!buffer.IsEmpty && _position < Length)
        {
            var clusterIndex = (int)(_position / _clusterSize);
            if (clusterIndex >= _clusters.Count)
            {
                break;
            }

            if (_currentSlice is null || _clusterIndex != clusterIndex)
            {
                OpenClusterSlice(clusterIndex);
            }

            var clusterOffset = _position % _clusterSize;
            _currentSlice!.Position = clusterOffset;
            var remainingInCluster = (int)Math.Min(_clusterSize - clusterOffset, buffer.Length);
            var read = _currentSlice.Read(buffer[..remainingInCluster]);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
            _position += read;
            buffer = buffer[read..];

            if (clusterOffset + read >= _clusterSize)
            {
                AdvanceClusterSlice();
            }
        }

        return totalRead;
    }

    private void OpenClusterSlice(int clusterIndex)
    {
        AdvanceClusterSlice();
        _clusterIndex = clusterIndex;
        var cluster = _clusters[clusterIndex];
        var absoluteOffset = _dataRegionOffset + ((long)cluster - 1L) * _clusterSize;
        _currentSlice = new StreamSlice(_volume.BaseStream, absoluteOffset, _clusterSize, leaveOpen: true);
    }

    private void AdvanceClusterSlice()
    {
        _currentSlice?.Dispose();
        _currentSlice = null;
    }
}
