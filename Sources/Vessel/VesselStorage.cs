using System.Diagnostics;
using System.Text;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Storage;

namespace Vessel;

/// <summary>Stores named objects and their metadata in a local directory.</summary>
/// <remarks>Only one instance may open a directory at a time. Operations on an instance may run concurrently.</remarks>
public sealed class VesselStorage : IVesselStorage
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly IVesselLogger? _logger;
    private readonly FileStream _directoryLock;
    private readonly LocalBlobStorageEngine _blobStorageEngine;
    private readonly MetadataRepository _metadataRepository;
    private readonly object[] _keyLocks = CreateLocks();
    private readonly object[] _blobLocks = CreateLocks();
    private readonly object _lifecycle = new();
    private bool _disposed;
    private int _activeOperations;

    /// <summary>Opens or creates a store in the supplied directory.</summary>
    /// <param name="storageBasePath">The directory containing the store.</param>
    /// <param name="options">Optional metadata compression and diagnostics.</param>
    /// <exception cref="IOException">Another instance holds the directory lock or the store could not be opened.</exception>
    public VesselStorage(string storageBasePath, VesselStorageOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath);
        var path = Path.GetFullPath(storageBasePath);
        Directory.CreateDirectory(path);
        _logger = options?.Logger;
        _directoryLock = new FileStream(Path.Join(path, ".vessel.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _blobStorageEngine = new LocalBlobStorageEngine(path);
            try
            {
                _metadataRepository = new MetadataRepository(path, options?.EnableMetadataCompression ?? false);
                try
                {
                    _blobStorageEngine.CleanupOrphanedTempFiles();
                    _blobStorageEngine.CleanupUnreferencedBlobs(_metadataRepository.IsHashReferenced);
                    if (_logger is not null)
                        Log(VesselLogLevel.Information, $"Opened Vessel store at '{path}'.");
                }
                catch
                {
                    _metadataRepository.Dispose();
                    throw;
                }
            }
            catch
            {
                _blobStorageEngine.Dispose();
                throw;
            }
        }
        catch
        {
            _directoryLock.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ObjectMetadata> PutAsync(string key, Stream contentStream, bool overwrite = false, ObjectInfo fileInfo = default, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Balanced, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation();
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(contentStream);
        ValidateText(fileInfo.FileName);
        ValidateText(fileInfo.Extension);
        ValidateText(fileInfo.MimeType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)compressionType, (uint)CompressionType.LZ4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)compressionLevel, (uint)CompressionLevel.Ultra);
        cancellationToken.ThrowIfCancellationRequested();

        if (overwrite)
            _metadataRepository.ThrowIfUnavailable();
        else if (_metadataRepository.Get(key) is not null)
            throw AlreadyExists(key);

        using var staged = await _blobStorageEngine.StageBlobAsync(contentStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
        ObjectMetadata metadata;
        string? previousHash;
        lock (GetLock(_keyLocks, key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = _metadataRepository.Get(key);
            if (!overwrite && previous is not null)
                throw AlreadyExists(key);

            previousHash = previous?.Hash;
            lock (GetLock(_blobLocks, staged.Info.Hash))
            {
                var (hash, originalSize, compressedSize) = _blobStorageEngine.PublishBlob(staged, cancellationToken);
                var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                metadata = new ObjectMetadata(key, hash, originalSize, compressionType, hash, createdAt, fileInfo.FileName, fileInfo.Extension, fileInfo.MimeType, compressedSize);
                _metadataRepository.Upsert(cancellationToken, metadata);
            }
        }

        if (previousHash is not null && previousHash != metadata.Hash)
            DeleteBlobIfUnreferenced(previousHash);

        if (_logger is not null)
            Log(VesselLogLevel.Information, $"Stored object '{key}' with {metadata.OriginalSize} bytes.");
        return metadata;
    }

    /// <inheritdoc/>
    public VesselStream GetStream(string key)
    {
        using var operation = BeginOperation();
        ValidateKey(key);

        lock (GetLock(_keyLocks, key))
        {
            var metadata = _metadataRepository.Get(key) ?? throw new FileNotFoundException($"Object '{key}' was not found.", key);
            if (!LocalBlobStorageEngine.IsBlobIdentifier(metadata.Hash) || (uint)metadata.CompressionType > (uint)CompressionType.LZ4)
                throw new InvalidDataException($"Object '{key}' has an invalid blob identifier or codec.");

            try
            {
                // Older releases could record the losing writer's compressed size during deduplication.
                var expectedSize = metadata.CompressionType == CompressionType.None ? metadata.OriginalSize : (long?)null;
                var stream = _blobStorageEngine.OpenBlobRead(metadata.Hash, metadata.CompressionType, expectedSize);
                return new VesselStream(stream, metadata.ContentHash, metadata.OriginalSize,
                    new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType), metadata: metadata);
            }
            catch (IOException error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                throw new InvalidDataException($"The blob for object '{key}' is missing.", error);
            }
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            using var operation = BeginOperation();
            ValidateKey(key);
            lock (GetLock(_keyLocks, key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = _metadataRepository.Get(key);
                if (metadata is null || !_metadataRepository.Delete(cancellationToken, key))
                    return Task.FromResult(false);

                DeleteBlobIfUnreferenced(metadata.Value.Hash);
            }

            if (_logger is not null)
                Log(VesselLogLevel.Information, $"Deleted object '{key}'.");
            return Task.FromResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }
        catch (Exception error)
        {
            return Task.FromException<bool>(error);
        }
    }

    /// <inheritdoc/>
    public ObjectMetadata? GetMetadata(string key)
    {
        using var operation = BeginOperation();
        ValidateKey(key);
        lock (GetLock(_keyLocks, key))
            return _metadataRepository.Get(key);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetAllKeys(string prefix = "")
    {
        using var operation = BeginOperation();
        ArgumentNullException.ThrowIfNull(prefix);
        ValidateText(prefix);
        return EnumerateKeys(prefix);
    }

    private IEnumerable<string> EnumerateKeys(string prefix)
    {
        using var operation = BeginOperation();
        foreach (var key in _metadataRepository.IterateKeys(prefix))
            yield return key;
    }

    private void DeleteBlobIfUnreferenced(string hash)
    {
        if (!LocalBlobStorageEngine.IsBlobIdentifier(hash)) return;

        lock (GetLock(_blobLocks, hash))
        {
            if (!_metadataRepository.IsHashReferenced(hash))
                _blobStorageEngine.DeleteBlob(hash);
        }
    }

    private static object[] CreateLocks() => Enumerable.Range(0, 256).Select(_ => new object()).ToArray();

    private static object GetLock(object[] locks, string value)
        => locks[(uint)string.GetHashCode(value, StringComparison.Ordinal) % (uint)locks.Length];

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ValidateText(key);
    }

    private static void ValidateText(string? value)
    {
        if (value is null) return;
        if (value.Contains('\u0000')) throw new ArgumentException("Vessel text cannot contain a NUL character.");
        _ = s_utf8.GetByteCount(value);
    }

    internal static bool IsAlreadyExists(InvalidOperationException error) => error.Data.Contains("Vessel.AlreadyExists");

    private static InvalidOperationException AlreadyExists(string key)
    {
        var error = new InvalidOperationException($"Object '{key}' already exists.");
        error.Data.Add("Vessel.AlreadyExists", true);
        return error;
    }

    private void Log(VesselLogLevel level, string message)
    {
        try { _logger?.Log(level, message); }
        catch (Exception error) { Trace.TraceWarning("Vessel logger failed: {0}", error.Message); }
    }

    private Operation BeginOperation()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeOperations++;
            return new Operation(this);
        }
    }

    private void EndOperation()
    {
        lock (_lifecycle)
        {
            if (--_activeOperations != 0 || !_disposed) return;
        }
        Close();
    }

    /// <summary>Stops accepting operations and releases the directory once operations already running finish.</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _disposed = true;
            if (_activeOperations != 0) return;
        }
        Close();
    }

    private void Close()
    {
        try { _metadataRepository.Dispose(); }
        finally
        {
            _blobStorageEngine.Dispose();
            _directoryLock.Dispose();
        }
    }

    private readonly struct Operation(VesselStorage storage) : IDisposable
    {
        public void Dispose() => storage.EndOperation();
    }
}
