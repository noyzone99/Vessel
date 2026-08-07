namespace Vessel;

/// <summary>
/// Wraps a read-only stream around an underlying payload stream while preserving optional file metadata and checksum information.
/// </summary>
public sealed class VesselStream : Stream
{
    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;

    /// <summary>
    /// Initializes a new wrapper around the supplied stream.
    /// </summary>
    /// <param name="innerStream">The underlying stream that provides the payload bytes.</param>
    /// <param name="checksum">The checksum associated with the payload.</param>
    /// <param name="length">The logical length of the payload stream.</param>
    /// <param name="objectInfo">Optional file metadata to associate with the payload.</param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the wrapped stream is not disposed when this wrapper is disposed.
    /// </param>
    internal VesselStream(Stream innerStream, string checksum, long length, ObjectInfo? objectInfo = null, bool leaveOpen = false)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _leaveOpen = leaveOpen;
        Length = length;
        FileName = objectInfo?.FileName;
        Extension = objectInfo?.Extension;
        MimeType = objectInfo?.MimeType;
        Checksum = checksum;
    }

    /// <summary>
    /// Gets the original file name associated with the payload when available.
    /// </summary>
    public string? FileName { get; }

    /// <summary>
    /// Gets the original file extension associated with the payload when available.
    /// </summary>
    public string? Extension { get; }

    /// <summary>
    /// Gets the MIME type associated with the payload when available.
    /// </summary>
    public string? MimeType { get; }

    /// <summary>
    /// Gets the checksum associated with the underlying payload.
    /// </summary>
    public string Checksum { get; }

    /// <inheritdoc/>
    public override bool CanRead => _innerStream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => _innerStream.CanSeek;

    /// <inheritdoc/>
    public override bool CanTimeout => _innerStream.CanTimeout;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length { get; }

    /// <inheritdoc/>
    public override long Position
    {
        get => CanSeek ? _innerStream.Position : 0;
        set => throw new NotSupportedException("Cannot set the position of a read-only VesselStream.");
    }

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => _innerStream.ReadTimeout;
        set => _innerStream.ReadTimeout = value;
    }

    /// <inheritdoc/>
    public override int WriteTimeout
    {
        get => _innerStream.WriteTimeout;
        set => _innerStream.WriteTimeout = value;
    }

    /// <inheritdoc/>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => _innerStream.BeginRead(buffer, offset, count, callback, state);

    /// <inheritdoc/>
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
        => _innerStream.BeginWrite(buffer, offset, count, callback, state);

    /// <inheritdoc/>
    public override void CopyTo(Stream destination, int bufferSize)
        => _innerStream.CopyTo(destination, bufferSize);

    /// <inheritdoc/>
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => _innerStream.CopyToAsync(destination, bufferSize, cancellationToken);

    /// <inheritdoc/>
    public override int EndRead(IAsyncResult asyncResult)
        => _innerStream.EndRead(asyncResult);

    /// <inheritdoc/>
    public override void EndWrite(IAsyncResult asyncResult)
        => _innerStream.EndWrite(asyncResult);

    /// <inheritdoc/>
    public override void Flush()
        => _innerStream.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
        => _innerStream.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => _innerStream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
        => _innerStream.Read(buffer);

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _innerStream.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc/>
    public override int ReadByte()
        => _innerStream.ReadByte();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
        => _innerStream.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value)
        => throw new NotSupportedException("Cannot set the length of a read-only VesselStream.");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Cannot write to a read-only VesselStream.");

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
        => throw new NotSupportedException("Cannot write to a read-only VesselStream.");

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => throw new NotSupportedException("Cannot write to a read-only VesselStream.");

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Cannot write to a read-only VesselStream.");

    /// <inheritdoc/>
    public override void WriteByte(byte value)
        => throw new NotSupportedException("Cannot write to a read-only VesselStream.");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && !_leaveOpen)
            {
                _innerStream.Dispose();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        try
        {
            if (!_leaveOpen)
            {
                await _innerStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}