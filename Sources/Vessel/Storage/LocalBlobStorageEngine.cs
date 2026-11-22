using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using Vessel.Logging;
using ZstdSharp;

namespace Vessel.Storage;

/// <summary>
/// Persists and retrieves object payload blobs from a local filesystem directory.
/// </summary>
internal sealed class LocalBlobStorageEngine : IBlobStorageEngine, IDisposable
{
    private const int BufferSize = 64 * 1024;
    private const int StagingFileBufferSize = 4 * 1024;
    private const int ContentHashHexLength = 64;
    private readonly string _blobsPath;
    private readonly string _tmpPath;
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

        _logger?.Log(VesselLogLevel.Information, $"Initialized blob storage at '{rootPath}'.");
    }

    /// <inheritdoc/>
    public async Task<BlobInfo> WriteBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        using var staged = await StageBlobAsync(inputStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
        return PublishBlob(staged, cancellationToken);
    }

    public async Task<StagedBlob> StageBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(inputStream);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)compressionType, (uint)CompressionType.LZ4);
        cancellationToken.ThrowIfCancellationRequested();

        var tempPath = GetTempFilePath();
        var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, StagingFileBufferSize, FileOptions.Asynchronous);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var pledgedSize = compressionType == CompressionType.Zstd ? GetPledgedSize(inputStream) : null;
            var target = WrapStream(file, compressionType, compressionLevel, pledgedSize);
            long originalSize = 0;
            var copyComplete = false;
            try
            {
                if (target is CompressionStream zstd && pledgedSize is ulong size)
                    zstd.SetPledgedSrcSize(size);

                var memory = buffer.AsMemory(0, BufferSize);
                int bytesRead;
                while ((bytesRead = await inputStream.ReadAsync(memory, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    checked { originalSize += bytesRead; }
                    hasher.AppendData(memory.Span[..bytesRead]);
                    await target.WriteAsync(memory[..bytesRead], cancellationToken).ConfigureAwait(false);
                }
                copyComplete = true;
            }
            finally
            {
                if (target != file)
                {
                    try { await target.DisposeAsync().ConfigureAwait(false); }
                    catch (ZstdException) when (pledgedSize.HasValue && !copyComplete)
                    {
                        // Ending a partial frame can fail the size check. Preserve the copy error.
                    }
                }
            }

            Span<byte> digest = stackalloc byte[32];
            hasher.GetHashAndReset(digest);
#if NET10_0_OR_GREATER
            var hash = Convert.ToHexStringLower(digest);
#else
            var hash = ToLowerHexString(digest);
#endif
            return new StagedBlob(tempPath, file, new BlobInfo(GetBlobIdentifier(hash, compressionType), originalSize, file.Position));
        }
        catch
        {
            try { await file.DisposeAsync().ConfigureAwait(false); }
            finally { TryDeleteFile(tempPath); }
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // The caller holds the blob's lock across publication and the metadata commit.
    public BlobInfo PublishBlob(StagedBlob staged, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetBlobPath(staged.Info.Hash);
        var existing = new FileInfo(path);
        if (existing.Exists)
            return ReuseExistingBlob(staged.Info, existing.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        staged.FlushAndClose();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            staged.MoveTo(path);
            return staged.Info;
        }
        catch (IOException) when (File.Exists(path))
        {
            return ReuseExistingBlob(staged.Info, new FileInfo(path).Length);
        }
    }

    private static BlobInfo ReuseExistingBlob(BlobInfo blob, long storedLength)
    {
        // Only uncompressed blob identifiers have no codec suffix.
        if (blob.Hash.Length == ContentHashHexLength)
            ValidateBlobLength(blob.Hash, storedLength, blob.Size);

        return blob with { CompressedSize = storedLength };
    }

    private static void ValidateBlobLength(string hash, long storedLength, long expectedLength)
    {
        if (storedLength != expectedLength)
            throw new InvalidDataException($"Blob '{hash}' has an unexpected stored length.");
    }

    /// <inheritdoc/>
    public Stream OpenBlobRead(string hash, CompressionType compressionType, long? expectedSize = null)
    {
        ThrowIfDisposed();
        var path = GetBlobPath(hash);
        var fileSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            if (expectedSize is long size)
                ValidateBlobLength(hash, fileSource.Length, size);

            return compressionType switch
            {
                CompressionType.Zstd => new DecompressionStream(fileSource, bufferSize: BufferSize, leaveOpen: false),
                CompressionType.LZ4 => LZ4Stream.Decode(fileSource, leaveOpen: false),
                CompressionType.None => fileSource,
                _ => throw new ArgumentOutOfRangeException(nameof(compressionType))
            };
        }
        catch
        {
            fileSource.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public void DeleteBlob(string hash)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        if (hash.Length < 4)
        {
            throw new ArgumentException("Blob hash must contain at least 4 characters.", nameof(hash));
        }

        TryDeleteFile(GetBlobPath(hash));
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

    public void CleanupUnreferencedBlobs(Func<string, bool> isReferenced)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var path in Directory.EnumerateFiles(_blobsPath, "*.blob", options))
        {
            var hash = Path.GetFileNameWithoutExtension(path);
            if (IsBlobIdentifier(hash) && !isReferenced(hash) && path.Equals(GetBlobPath(hash), StringComparison.Ordinal))
                TryDeleteFile(path);
        }
    }

    internal static bool IsBlobIdentifier(string hash) =>
        (hash.Length == ContentHashHexLength || (hash.Length == ContentHashHexLength + 2 && hash[^2] == '-' && hash[^1] is '1' or '2')) &&
        hash.AsSpan(0, ContentHashHexLength).IndexOfAnyExcept("0123456789abcdef") < 0;

    internal static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException error) { Trace.TraceWarning("Vessel could not remove '{0}': {1}", path, error.Message); }
        catch (UnauthorizedAccessException error) { Trace.TraceWarning("Vessel could not remove '{0}': {1}", path, error.Message); }
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
    /// <param name="pledgedSize">The exact remaining input size, when known.</param>
    /// <returns>A stream that writes compressed data to the supplied base stream.</returns>
    private static Stream WrapStream(Stream baseStream, CompressionType type, CompressionLevel level, ulong? pledgedSize)
    {
        return type switch
        {
            CompressionType.Zstd => new CompressionStream(baseStream, GetZstdLevel(level),
                bufferSize: pledgedSize is <= BufferSize ? BufferSize : 0, leaveOpen: true),
            CompressionType.LZ4 => LZ4Stream.Encode(baseStream, new LZ4EncoderSettings { CompressionLevel = GetLZ4Level(level) }, leaveOpen: true),
            CompressionType.None => baseStream,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static ulong? GetPledgedSize(Stream stream)
    {
        if (ReferenceEquals(stream, Stream.Null)) return 0;

        // Only these BCL read-only streams guarantee length and position reflect what Read returns.
        var type = stream.GetType();
        if ((type == typeof(MemoryStream) || type == typeof(UnmanagedMemoryStream)) && stream.CanRead && !stream.CanWrite)
            return (ulong)Math.Max(0, stream.Length - stream.Position);

        return null;
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
    /// Releases the engine's disposable resources.
    /// </summary>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) != 0, this);
    }

    /// <summary>
    /// Throws an exception when the storage engine has already been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the engine can no longer be used.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

}
