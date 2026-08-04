using K4os.Compression.LZ4.Streams;
using System.Buffers;
using System.Security.Cryptography;
using Vessel.Logging;
using ZstdSharp;

namespace Vessel.Storage;

/// <summary>
/// Persists object payloads as files on disk and exposes read/write helpers for the core storage engine.
/// </summary>
internal sealed class LocalBlobStorageEngine : IBlobStorageEngine
{
    private const int BufferSize = 64 * 1024;
    private readonly string _rootPath;
    private readonly string _blobsPath;
    private readonly string _tmpPath;
    private readonly IVesselLogger? _logger;

    /// <summary>
    /// Initializes the blob storage engine against the supplied storage root.
    /// </summary>
    /// <param name="storageBasePath">The root directory used for blob and temporary files.</param>
    /// <param name="logger">An optional logger used to record operational diagnostics.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="storageBasePath"/> is null, empty, or whitespace.</exception>
    public LocalBlobStorageEngine(string storageBasePath, IVesselLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath);
        _logger = logger;
        _rootPath = Path.GetFullPath(storageBasePath);
        _blobsPath = Path.Combine(_rootPath, "blobs");
        _tmpPath = Path.Combine(_rootPath, "tmp");

        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(_blobsPath);
        Directory.CreateDirectory(_tmpPath);

        _logger?.Log(VesselLogLevel.Information, $"Initialized blob storage at '{_rootPath}'.");
    }

    /// <summary>
    /// Writes a payload stream to the backing store and returns the resulting blob identity and sizes.
    /// </summary>
    /// <param name="inputStream">The readable stream containing the payload to persist.</param>
    /// <param name="compressionType">The compression codec to apply when serializing the blob.</param>
    /// <param name="compressionLevel">The compression strength to use for the selected codec.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The generated blob hash, the original payload size, and the size of the stored blob.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputStream"/> is <see langword="null"/>.</exception>
    public async Task<(string Hash, long Size, long CompressedSize)> WriteBlobAsync(
        Stream inputStream,
        CompressionType compressionType,
        CompressionLevel compressionLevel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        var tempFilePath = Path.Combine(_tmpPath, $"{Guid.NewGuid():N}.tmp");
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long originalSize = 0;

        try
        {
            await using (var fileDestination = new FileStream(
                tempFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous))
            {
                Stream targetStream = fileDestination;

                switch (compressionType)
                {
                    case CompressionType.Zstd:
                        int zstdLevel = compressionLevel switch
                        {
                            CompressionLevel.Fastest => 1,
                            CompressionLevel.Balanced => 3,
                            CompressionLevel.High => 7,
                            CompressionLevel.Ultra => 15,
                            _ => 3
                        };
                        targetStream = new CompressionStream(fileDestination, zstdLevel, leaveOpen: true);
                        break;

                    case CompressionType.LZ4:
                        var lz4Level = compressionLevel switch
                        {
                            CompressionLevel.Fastest => K4os.Compression.LZ4.LZ4Level.L00_FAST,
                            CompressionLevel.Balanced => K4os.Compression.LZ4.LZ4Level.L00_FAST,
                            CompressionLevel.High => K4os.Compression.LZ4.LZ4Level.L04_HC,
                            CompressionLevel.Ultra => K4os.Compression.LZ4.LZ4Level.L09_HC,
                            _ => K4os.Compression.LZ4.LZ4Level.L00_FAST
                        };
                        targetStream = LZ4Stream.Encode(fileDestination, new LZ4EncoderSettings { CompressionLevel = lz4Level }, leaveOpen: true);
                        break;

                    case CompressionType.None:
                        break;
                }

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
                    {
                        await targetStream.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }

            Span<byte> hashBytes = stackalloc byte[32];
            hasher.GetHashAndReset(hashBytes);
            var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

            var destinationPath = GetBlobPath(hash);
            var destinationDirectory = Path.GetDirectoryName(destinationPath)!;
            Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationPath))
            {
                long existingSize = new FileInfo(destinationPath).Length;
                _logger?.Log(VesselLogLevel.Information, $"Blob '{hash}' already exists; reusing existing file.");
                TryDeleteFile(tempFilePath);
                return (hash, originalSize, existingSize);
            }

            try
            {
                File.Move(tempFilePath, destinationPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                TryDeleteFile(tempFilePath);
                long existingSize = new FileInfo(destinationPath).Length;
                return (hash, originalSize, existingSize);
            }

            long compressedSize = new FileInfo(destinationPath).Length;
            _logger?.Log(VesselLogLevel.Information, $"Wrote blob '{hash}' to '{destinationPath}' ({originalSize} bytes).");
            return (hash, originalSize, compressedSize);
        }
        catch (Exception exception)
        {
            _logger?.Log(VesselLogLevel.Error, $"Failed to write blob to temporary file '{tempFilePath}': {exception.Message}");
            TryDeleteFile(tempFilePath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Opens a previously stored blob for reading with the requested compression semantics.
    /// </summary>
    /// <param name="hash">The content hash identifying the blob.</param>
    /// <param name="compressionType">The compression codec used when the blob was stored.</param>
    /// <returns>A readable stream that exposes the blob contents.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hash"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the blob does not exist.</exception>
    public Stream OpenBlobRead(string hash, CompressionType compressionType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var path = GetBlobPath(hash);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Blob '{hash}' was not found.", path);
        }

        var fileSource = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            return compressionType switch
            {
                CompressionType.Zstd => new DecompressionStream(fileSource, bufferSize: BufferSize, leaveOpen: false),
                CompressionType.LZ4 => LZ4Stream.Decode(fileSource, leaveOpen: false),
                CompressionType.None => fileSource,
                _ => throw new ArgumentOutOfRangeException(nameof(compressionType), $"Unsupported compression type: {compressionType}")
            };
        }
        catch (Exception exception)
        {
            _logger?.Log(VesselLogLevel.Error, $"Failed to initialize decompression stream for blob '{hash}' using {compressionType}: {exception.Message}");
            fileSource.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Deletes the blob identified by the supplied hash when it exists.
    /// </summary>
    /// <param name="hash">The content hash identifying the blob to remove.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hash"/> is null, empty, or whitespace.</exception>
    public void DeleteBlob(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var path = GetBlobPath(hash);
        if (File.Exists(path))
        {
            _logger?.Log(VesselLogLevel.Information, $"Deleting blob '{hash}' from '{path}'.");
            File.Delete(path);
        }
    }

    /// <summary>
    /// Deletes temporary files left behind by interrupted or failed writes.
    /// </summary>
    public void CleanupOrphanedTempFiles()
    {
        if (!Directory.Exists(_tmpPath))
        {
            return;
        }

        _logger?.Log(VesselLogLevel.Information, $"Cleaning up orphaned temp files under '{_tmpPath}'.");

        foreach (var tempFile in Directory.EnumerateFiles(_tmpPath, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception exception)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Unable to delete orphaned temp file '{tempFile}': {exception.Message}");
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private string GetBlobPath(string hash)
    {
        if (hash.Length < 4)
        {
            throw new ArgumentException("Blob hash must contain at least 4 characters.", nameof(hash));
        }

        return Path.Combine(_blobsPath, hash[..2], hash[2..4], $"{hash}.blob");
    }
}