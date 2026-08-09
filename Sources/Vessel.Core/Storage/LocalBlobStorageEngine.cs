using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using System.Buffers;
using System.Security.Cryptography;
using System.Threading.Channels;
using Vessel.Logging;
using ZstdSharp;

namespace Vessel.Storage;

/// <summary>
/// Persists and retrieves object payload blobs from a local filesystem directory.
/// </summary>
internal sealed class LocalBlobStorageEngine : IBlobStorageEngine, IDisposable
{
    private const int BufferSize = 64 * 1024;
    private readonly string _blobsPath;
    private readonly string _tmpPath;
    private readonly Channel<string> _deleteChannel;
    private readonly Task _deleteWorker;
    private readonly CancellationTokenSource _cts = new();
    private readonly IVesselLogger? _logger;
    private int _disposed;

    /// <summary>
    /// Initializes the local blob storage engine under the supplied storage root.
    /// </summary>
    /// <param name="storageBasePath">The root directory used to host the blob and temporary-file folders.</param>
    /// <param name="logger">An optional logger used to record blob storage diagnostics.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="storageBasePath"/> is null, empty, or whitespace.
    /// </exception>
    public LocalBlobStorageEngine(string storageBasePath, IVesselLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath);
        _logger = logger;
        var rootPath = Path.GetFullPath(storageBasePath);
        _blobsPath = Path.Join(rootPath, "blobs");
        _tmpPath = Path.Join(rootPath, "tmp");
        Directory.CreateDirectory(_blobsPath);
        Directory.CreateDirectory(_tmpPath);

        _deleteChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = true
        });

        _deleteWorker = Task.Run(ProcessDeletionsAsync);

        _logger?.Log(VesselLogLevel.Information, $"Initialized blob storage at '{rootPath}'.");
    }

    /// <summary>
    /// Processes queued blob deletion requests from the background worker.
    /// </summary>
    private async Task ProcessDeletionsAsync()
    {
        await foreach (var hash in _deleteChannel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                var path = GetBlobPath(hash);
                TryDeleteFile(path);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.Log(VesselLogLevel.Error, $"Background delete failed for {hash}: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public async Task<BlobInfo> WriteBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(inputStream);

        if (inputStream.CanSeek && inputStream.Length <= BufferSize)
        {
            return await WriteSmallBlobAsync(inputStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
        }

        return await WriteLargeBlobAsync(inputStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a small payload directly to disk after optionally compressing it.
    /// </summary>
    /// <param name="inputStream">The readable source stream that contains the payload bytes.</param>
    /// <param name="compressionType">The compression codec to apply to the payload.</param>
    /// <param name="compressionLevel">The desired compression strength for the selected codec.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>Metadata describing the resulting blob file and its effective storage size.</returns>
    private async Task<BlobInfo> WriteSmallBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        int originalSize = (int)inputStream.Length;
        byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(originalSize);
        byte[]? compressedBuffer = null;

        try
        {
            Memory<byte> sourceMemory = sourceBuffer.AsMemory(0, originalSize);
            await inputStream.ReadExactlyAsync(sourceMemory, cancellationToken).ConfigureAwait(false);

            string hash;
            {
                Span<byte> hashBytes = stackalloc byte[32];
                SHA256.HashData(sourceMemory.Span, hashBytes);
#if NET10_0_OR_GREATER
                hash = Convert.ToHexStringLower(hashBytes);
#else
                hash = ToLowerHexString(hashBytes);
#endif
                var blobIdentifier = GetBlobIdentifier(hash, compressionType);
                var destinationPath = GetBlobPath(blobIdentifier);

                if (File.Exists(destinationPath))
                {
                    return new BlobInfo(blobIdentifier, originalSize, new FileInfo(destinationPath).Length);
                }

                ReadOnlyMemory<byte> dataToWrite;

                if (compressionType == CompressionType.None)
                {
                    dataToWrite = sourceMemory;
                }
                else
                {
                    compressedBuffer = ArrayPool<byte>.Shared.Rent(originalSize + 1024);
                    int compressedSize = CompressBuffer(sourceMemory.Span, compressedBuffer, compressionType, compressionLevel);
                    dataToWrite = compressedBuffer.AsMemory(0, compressedSize);
                }

                var tempFilePath = GetTempFilePath();
                try
                {
#if NET10_0_OR_GREATER
                    await File.WriteAllBytesAsync(tempFilePath, dataToWrite, cancellationToken).ConfigureAwait(false);
                    var dir = Path.GetDirectoryName(destinationPath)!;
                    Directory.CreateDirectory(dir);
#else
                    await using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1, FileOptions.Asynchronous))
                    {
                        await fs.WriteAsync(dataToWrite, cancellationToken).ConfigureAwait(false);
                    }

                    var dir = Path.GetDirectoryName(destinationPath)!;
                    Directory.CreateDirectory(dir);
#endif
                    File.Move(tempFilePath, destinationPath, overwrite: false);
                }
                catch (IOException) when (File.Exists(destinationPath)) { }
                finally { TryDeleteFile(tempFilePath); }

                return new BlobInfo(blobIdentifier, originalSize, dataToWrite.Length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer);
            if (compressedBuffer != null) ArrayPool<byte>.Shared.Return(compressedBuffer);
        }
    }

    /// <summary>
    /// Writes a large payload to a temporary file and then atomically moves it into the final blob location.
    /// </summary>
    /// <param name="inputStream">The readable source stream that contains the payload bytes.</param>
    /// <param name="compressionType">The compression codec to apply to the payload.</param>
    /// <param name="compressionLevel">The desired compression strength for the selected codec.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>Metadata describing the resulting blob file and its effective storage size.</returns>
    private async Task<BlobInfo> WriteLargeBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        var tempFilePath = GetTempFilePath();
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long originalSize = 0;
        long compressedSize;

        try
        {
            await using (var fileDestination = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                Stream targetStream = WrapStream(fileDestination, compressionType, compressionLevel);
                try
                {
                    int bytesRead;
                    var memoryBuffer = buffer.AsMemory(0, BufferSize);

                    while ((bytesRead = await inputStream.ReadAsync(memoryBuffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        originalSize += bytesRead;
                        hasher.AppendData(buffer.AsSpan(0, bytesRead));
                        await targetStream.WriteAsync(memoryBuffer[..bytesRead], cancellationToken).ConfigureAwait(false);
                    }

                    await targetStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (targetStream != fileDestination)
                        await targetStream.DisposeAsync().ConfigureAwait(false);
                }

                compressedSize = fileDestination.Length;
            }

            Span<byte> hashBytes = stackalloc byte[32];
            hasher.GetHashAndReset(hashBytes);
#if NET10_0_OR_GREATER
            string hash = Convert.ToHexStringLower(hashBytes);
#else
            string hash = ToLowerHexString(hashBytes);
#endif
            var blobIdentifier = GetBlobIdentifier(hash, compressionType);

            var destinationPath = GetBlobPath(blobIdentifier);
            if (File.Exists(destinationPath))
            {
                TryDeleteFile(tempFilePath);
                return new BlobInfo(blobIdentifier, originalSize, new FileInfo(destinationPath).Length);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            try
            {
                File.Move(tempFilePath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                TryDeleteFile(tempFilePath);
            }

            return new BlobInfo(blobIdentifier, originalSize, compressedSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            TryDeleteFile(tempFilePath);
        }
    }

    /// <inheritdoc/>
    public Stream OpenBlobRead(string hash, CompressionType compressionType)
    {
        ThrowIfDisposed();
        var path = GetBlobPath(hash);
        var fileSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return compressionType switch
        {
            CompressionType.Zstd => new DecompressionStream(fileSource, bufferSize: BufferSize, leaveOpen: false),
            CompressionType.LZ4 => LZ4Stream.Decode(fileSource, leaveOpen: false),
            CompressionType.None => fileSource,
            _ => throw new ArgumentOutOfRangeException(nameof(compressionType))
        };
    }

    /// <inheritdoc/>
    public void QueueDeleteBlob(string hash)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        if (hash.Length < 4)
        {
            throw new ArgumentException("Blob hash must contain at least 4 characters.", nameof(hash));
        }

        _deleteChannel.Writer.TryWrite(hash);
    }

    /// <inheritdoc/>
    public void CleanupOrphanedTempFiles()
    {
        ThrowIfDisposed();
        if (!Directory.Exists(_tmpPath)) return;

        foreach (var tempFile in Directory.EnumerateFiles(_tmpPath, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(tempFile);
        }
    }

    /// <summary>
    /// Compresses the supplied bytes with the requested codec and compression level.
    /// </summary>
    /// <param name="source">The uncompressed source bytes.</param>
    /// <param name="destination">The buffer that receives the compressed bytes.</param>
    /// <param name="type">The compression codec to use.</param>
    /// <param name="level">The requested compression strength.</param>
    /// <returns>The number of compressed bytes written into the destination buffer.</returns>
    private static int CompressBuffer(ReadOnlySpan<byte> source, byte[] destination, CompressionType type, CompressionLevel level)
    {
        if (type == CompressionType.Zstd)
        {
            using var compressor = new Compressor(GetZstdLevel(level));
            return compressor.Wrap(source, destination);
        }

        if (type == CompressionType.LZ4)
        {
            using var memStream = new MemoryStream(destination);
            using (var lz4Stream = LZ4Stream.Encode(memStream, new LZ4EncoderSettings { CompressionLevel = GetLZ4Level(level) }, leaveOpen: true))
            {
                lz4Stream.Write(source);
            }
            return (int)memStream.Position;
        }

        return 0;
    }

    /// <summary>
    /// Best-effort deletes a file if it exists and can be removed without raising an exception.
    /// </summary>
    /// <param name="path">The full path of the file to delete.</param>
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    /// <summary>
    /// Resolves the on-disk path for a blob identified by a content hash.
    /// </summary>
    /// <param name="hash">The content hash used as the blob identifier.</param>
    /// <returns>The fully qualified path to the stored blob.</returns>
    private string GetBlobPath(string hash)
    {
        return Path.Join(_blobsPath, hash.AsSpan(0, 2), hash.AsSpan(2, 2), $"{hash}.blob");
    }

    /// <summary>
    /// Builds a stable blob identifier that includes the compression codec when the payload is stored compressed.
    /// </summary>
    /// <param name="contentHash">The content hash of the original payload.</param>
    /// <param name="compressionType">The codec used to persist the blob.</param>
    /// <returns>The blob identifier used for the on-disk path.</returns>
    private static string GetBlobIdentifier(string contentHash, CompressionType compressionType)
    {
        if (compressionType == CompressionType.None)
        {
            return contentHash;
        }
        return $"{contentHash}-{(int)compressionType}";
    }

    /// <summary>
    /// Wraps a base stream with the appropriate compression stream for the requested codec and level.
    /// </summary>
    /// <param name="baseStream">The destination stream that receives the compressed bytes.</param>
    /// <param name="type">The compression codec requested by the caller.</param>
    /// <param name="level">The compression strength selected for the operation.</param>
    /// <returns>A stream that writes compressed data to the supplied base stream.</returns>
    private static Stream WrapStream(Stream baseStream, CompressionType type, CompressionLevel level)
    {
        return type switch
        {
            CompressionType.Zstd => new CompressionStream(baseStream, GetZstdLevel(level), leaveOpen: true),
            CompressionType.LZ4 => LZ4Stream.Encode(baseStream, new LZ4EncoderSettings { CompressionLevel = GetLZ4Level(level) }, leaveOpen: true),
            CompressionType.None => baseStream,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    /// <summary>
    /// Maps the generic compression level to the Zstandard compression settings used by the implementation.
    /// </summary>
    /// <param name="level">The requested compression level.</param>
    /// <returns>The corresponding Zstandard compression level.</returns>
    private static int GetZstdLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.High => 7,
        CompressionLevel.Ultra => 15,
        _ => 3
    };

    /// <summary>
    /// Maps the generic compression level to the LZ4 compression settings used by the implementation.
    /// </summary>
    /// <param name="level">The requested compression level.</param>
    /// <returns>The corresponding LZ4 compression level.</returns>
    private static LZ4Level GetLZ4Level(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => LZ4Level.L00_FAST,
        CompressionLevel.High => LZ4Level.L04_HC,
        CompressionLevel.Ultra => LZ4Level.L09_HC,
        _ => LZ4Level.L00_FAST
    };

    /// <summary>
    /// Creates a unique temporary file path inside the engine's temporary directory.
    /// </summary>
    /// <returns>The path of a new temporary file to use for a pending blob write.</returns>
    private string GetTempFilePath()
    {
        Span<char> guidChars = stackalloc char[32];
        Guid.NewGuid().TryFormat(guidChars, out _, "N");
        return Path.Join(_tmpPath, $"{guidChars}.tmp");
    }

#if !NET10_0_OR_GREATER
    /// <summary>
    /// Formats the supplied bytes as a lowercase hexadecimal string suitable for blob identifiers.
    /// </summary>
    /// <param name="bytes">The binary payload to encode.</param>
    /// <returns>A lowercase hexadecimal representation of the supplied bytes.</returns>
    private static unsafe string ToLowerHexString(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* pBytes = bytes)
        {
            return string.Create(bytes.Length * 2, (IntPtr)pBytes, (chars, ptrState) =>
            {
                byte* b = (byte*)ptrState;
                const string hexAlphabet = "0123456789abcdef";
                for (int i = 0; i < chars.Length / 2; i++)
                {
                    chars[i * 2] = hexAlphabet[b[i] >> 4];
                    chars[(i * 2) + 1] = hexAlphabet[b[i] & 0xF];
                }
            });
        }
    }
#endif

    /// <summary>
    /// Stops the background delete worker and releases the engine's disposable resources.
    /// </summary>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) != 0, this);
        _cts.Cancel();
        _deleteChannel.Writer.TryComplete();
    }

    /// <summary>
    /// Throws an exception when the storage engine has already been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the engine can no longer be used.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

}
