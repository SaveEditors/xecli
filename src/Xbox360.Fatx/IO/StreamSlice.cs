namespace Xbox360.Fatx.IO;

public sealed class StreamSlice : Stream
{
    private readonly Stream _baseStream;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _leaveOpen;
    private long _position;

    public StreamSlice(Stream baseStream, long start, long length, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(baseStream);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        _baseStream = baseStream;
        _start = start;
        _length = length;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _baseStream.CanRead;

    public override bool CanSeek => _baseStream.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var allowed = (int)Math.Min(buffer.Length, _length - _position);
        lock (_baseStream)
        {
            _baseStream.Position = _start + _position;
            var read = _baseStream.Read(buffer[..allowed]);
            _position += read;
            return read;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _length)
        {
            return 0;
        }

        var allowed = (int)Math.Min(buffer.Length, _length - _position);
        _baseStream.Position = _start + _position;
        var read = await _baseStream.ReadAsync(buffer[..allowed], cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var next = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (next < 0)
        {
            throw new IOException("Attempted to seek before the beginning of the slice.");
        }

        _position = Math.Min(next, _length);
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _baseStream.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _baseStream.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
