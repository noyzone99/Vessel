using K4os.Compression.LZ4.Streams;
using System.Buffers;
using System.Security.Cryptography;
using Vessel.Logging;
using ZstdSharp;

namespace Vessel.Storage;

internal sealed class LocalBlobStorageEngine : IBlobStorageEngine
{
    private const int BufferSize = 64 * 1024;
    private readonly string _blobsPath;
    private readonly string _tmpPath;
    private readonly IVesselLogger? _logger;

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

    public async Task<(string Hash, long Size, long CompressedSize)> WriteBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputStream);

        Span<char> guidChars = stackalloc char[32];
        Guid.NewGuid().TryFormat(guidChars, out _, "N");

        Span<char> tempFileName = stackalloc char[36];
        guidChars.CopyTo(tempFileName);
        ".tmp".AsSpan().CopyTo(tempFileName[32..]);

        var tempFilePath = Path.Join(_tmpPath, tempFileName);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long originalSize = 0;
        long compressedSize = 0;

        try
        {
            await using (var fileDestination = new FileStream(
                tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                BufferSize, FileOptions.Asynchronous))
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

            var dir = Path.GetDirectoryName(destinationPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (File.Exists(destinationPath))
            {
                TryDeleteFile(tempFilePath);
            }
            else
            {
                try { File.Move(tempFilePath, destinationPath, overwrite: false); }
                catch (IOException) { TryDeleteFile(tempFilePath); }
            }

            return (hash, originalSize, compressedSize);
        }
        catch (Exception ex)
        {
            _logger?.Log(VesselLogLevel.Error, $"Failed to write blob: {ex.Message}");
            TryDeleteFile(tempFilePath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

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

    public void DeleteBlob(string hash)
    {
        var path = GetBlobPath(hash);
        if (File.Exists(path)) File.Delete(path);
    }

    public void CleanupOrphanedTempFiles()
    {
        if (!Directory.Exists(_tmpPath)) return;

        foreach (var tempFile in Directory.EnumerateFiles(_tmpPath, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            TryDeleteFile(tempFile);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private string GetBlobPath(string hash)
    {
        return Path.Join(_blobsPath, hash.AsSpan(0, 2), hash.AsSpan(2, 2), $"{hash}.blob");
    }

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

    private static int GetZstdLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.High => 7,
        CompressionLevel.Ultra => 15,
        _ => 3
    };

    private static K4os.Compression.LZ4.LZ4Level GetLZ4Level(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => K4os.Compression.LZ4.LZ4Level.L00_FAST,
        CompressionLevel.High => K4os.Compression.LZ4.LZ4Level.L04_HC,
        CompressionLevel.Ultra => K4os.Compression.LZ4.LZ4Level.L09_HC,
        _ => K4os.Compression.LZ4.LZ4Level.L00_FAST
    };

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