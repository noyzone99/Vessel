using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using System.Buffers;
using System.Security.Cryptography;
using Vessel.Logging;
using Vessel.Models;
using ZstdSharp;

namespace Vessel.Storage;

/// <summary>
/// Persists and retrieves object payload blobs from a local filesystem directory.
/// </summary>
internal sealed class LocalBlobStorageEngine : IBlobStorageEngine
{
    private const int BufferSize = 64 * 1024;
    private readonly string _blobsPath;
    private readonly string _tmpPath;
    private readonly IVesselLogger? _logger;

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

        if (!Directory.Exists(_blobsPath)) Directory.CreateDirectory(_blobsPath);
        if (!Directory.Exists(_tmpPath)) Directory.CreateDirectory(_tmpPath);

        _logger?.Log(VesselLogLevel.Information, $"Initialized blob storage at '{rootPath}'.");
    }

    /// <inheritdoc/>
    public async Task<BlobInfo> WriteBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        if (inputStream.CanSeek && inputStream.Length <= BufferSize)
        {
            return await WriteSmallBlobAsync(inputStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
        }

        return await WriteLargeBlobAsync(inputStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
    }

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
                hash = ToLowerHexString(hashBytes);
            }

            var destinationPath = GetBlobPath(hash);
            if (File.Exists(destinationPath))
            {
                return new BlobInfo(hash, originalSize, new FileInfo(destinationPath).Length);
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
                await using (var fs = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
                {
                    await fs.WriteAsync(dataToWrite, cancellationToken).ConfigureAwait(false);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Move(tempFilePath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath)) { }
            finally { TryDeleteFile(tempFilePath); }

            return new BlobInfo(hash, originalSize, dataToWrite.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer);
            if (compressedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(compressedBuffer);
            }
        }
    }

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
            string hash = ToLowerHexString(hashBytes);

            var destinationPath = GetBlobPath(hash);
            if (File.Exists(destinationPath))
            {
                TryDeleteFile(tempFilePath);
                return new BlobInfo(hash, originalSize, new FileInfo(destinationPath).Length);
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

            return new BlobInfo(hash, originalSize, compressedSize);
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
    public void DeleteBlob(string hash)
    {
        var path = GetBlobPath(hash);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <inheritdoc/>
    public void CleanupOrphanedTempFiles()
    {
        if (!Directory.Exists(_tmpPath)) return;

        foreach (var tempFile in Directory.EnumerateFiles(_tmpPath, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(tempFile);
        }
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
        CompressionLevel.Fastest => K4os.Compression.LZ4.LZ4Level.L00_FAST,
        CompressionLevel.High => K4os.Compression.LZ4.LZ4Level.L04_HC,
        CompressionLevel.Ultra => K4os.Compression.LZ4.LZ4Level.L09_HC,
        _ => K4os.Compression.LZ4.LZ4Level.L00_FAST
    };

    private static int CompressBuffer(ReadOnlySpan<byte> source, byte[] destination, CompressionType type, CompressionLevel level)
    {
        if (type == CompressionType.Zstd)
        {
            using var compressor = new Compressor(GetZstdLevel(level));
            return compressor.Wrap(source, destination);
        }

        if (type == CompressionType.LZ4)
        {
            var lz4Level = GetLZ4Level(level);
            return LZ4Codec.Encode(source, destination, lz4Level);
        }

        return 0;
    }

    private string GetTempFilePath()
    {
        Span<char> guidChars = stackalloc char[32];
        Guid.NewGuid().TryFormat(guidChars, out _, "N");
        return Path.Join(_tmpPath, $"{guidChars}.tmp");
    }

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
}
