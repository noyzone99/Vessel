using System.Runtime.CompilerServices;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Models;
using Vessel.Storage;

namespace Vessel;

/// <summary>
/// Coordinates the metadata repository and the blob storage engine for the public storage API.
/// </summary>
/// <remarks>
/// This implementation keeps logical object metadata and physical blob payloads in sync while protecting concurrent
/// access to the same key with a small set of per-key locks.
/// </remarks>
/// <param name="storageBasePath">The root directory used to store metadata, blobs, and temporary files.</param>
/// <param name="options">
/// Optional configuration for the storage instance, including diagnostics and behavior toggles that influence metadata
/// handling.
/// </param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="storageBasePath"/> is <see langword="null"/>, empty, or whitespace.
/// </exception>
public sealed class VesselStorage(string storageBasePath, VesselStorageOptions? options = null) : IVesselStorage
{
    private readonly IVesselLogger? _logger = options?.Logger;
    private readonly LocalBlobStorageEngine _blobStorageEngine = new(storageBasePath, options?.Logger);
    private readonly MetadataRepository _metadataRepository = new(storageBasePath, options?.EnableMetadataCompression ?? false, options?.Logger);
    private readonly SemaphoreSlim[] _keyLocks = [.. Enumerable.Range(0, 1024).Select(_ => new SemaphoreSlim(1, 1))];

    /// <inheritdoc/>
    public async Task<ObjectMetadata> PutAsync(string key, Stream contentStream, bool overwrite = false, ObjectInfo fileInfo = default, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Balanced, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contentStream);

        _logger?.Log(VesselLogLevel.Information, $"Starting put for object '{key}' with overwrite={overwrite}.");

        var keyLock = GetKeyLock(key);
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var existingMetadata = _metadataRepository.Get(key);
            if (existingMetadata is not null && !overwrite)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Refusing to overwrite existing object '{key}'.");
                throw new InvalidOperationException($"Object '{key}' already exists.");
            }

            var (hash, size, compressedSize) = await _blobStorageEngine.WriteBlobAsync(contentStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);
            var metadata = new ObjectMetadata(key, hash, size, compressionType, hash, DateTimeOffset.UtcNow, fileInfo.FileName, fileInfo.FileExtension, fileInfo.MimeType, compressedSize);

            _metadataRepository.Upsert(metadata);

            if (existingMetadata is not null && !string.Equals(existingMetadata.Value.Hash, hash, StringComparison.Ordinal))
            {
                var isStillReferenced = _metadataRepository.IsHashReferenced(existingMetadata.Value.Hash);
                if (!isStillReferenced)
                {
                    _logger?.Log(VesselLogLevel.Information, $"Deleting superseded blob '{existingMetadata.Value.Hash}' for object '{key}'.");
                    _blobStorageEngine.DeleteBlob(existingMetadata.Value.Hash);
                    //TODO: try with Task.Run(() => _blobStorageEngine.DeleteBlob(existingMetadata.Value.Hash));
                }
            }

            _logger?.Log(VesselLogLevel.Information, $"Stored object '{key}' with hash '{hash}' and size={size} bytes.");
            return metadata;
        }
        catch (Exception exception)
        {
            _logger?.Log(VesselLogLevel.Error, $"Failed to store object '{key}': {exception.Message}");
            throw;
        }
        finally
        {
            keyLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ObjectResponse> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var metadata = _metadataRepository.Get(key);
        if (metadata is null)
        {
            _logger?.Log(VesselLogLevel.Warning, $"Attempted to read missing object '{key}'.");
            throw new FileNotFoundException($"Object '{key}' was not found.", key);
        }

        var objectMetadata = metadata.Value;
        _logger?.Log(VesselLogLevel.Debug, $"Opening blob '{objectMetadata.Hash}' for object '{key}'.");
        var contentStream = _blobStorageEngine.OpenBlobRead(objectMetadata.Hash, objectMetadata.CompressionType);
        return new ObjectResponse(objectMetadata, contentStream);
    }

    /// <inheritdoc/>
    public ObjectResponse Get(string key)
    {
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var metadata = _metadataRepository.Get(key);
            if (metadata is null)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Attempted to read missing object '{key}'.");
                throw new FileNotFoundException($"Object '{key}' was not found.", key);
            }

            var objectMetadata = metadata.Value;
            _logger?.Log(VesselLogLevel.Debug, $"Opening blob '{objectMetadata.Hash}' for object '{key}'.");
            var contentStream = _blobStorageEngine.OpenBlobRead(objectMetadata.Hash, objectMetadata.CompressionType);
            return new ObjectResponse(objectMetadata, contentStream);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _logger?.Log(VesselLogLevel.Information, $"Deleting object '{key}'.");

        var keyLock = GetKeyLock(key);
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var metadata = _metadataRepository.Get(key);
            if (metadata is null)
            {
                _logger?.Log(VesselLogLevel.Information, $"Delete request ignored because object '{key}' was not found.");
                return false;
            }

            var deleted = _metadataRepository.Delete(key);
            if (!deleted)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Delete operation reported failure for object '{key}'.");
                return false;
            }

            var isStillReferenced = _metadataRepository.IsHashReferenced(metadata.Value.Hash);
            if (!isStillReferenced)
            {
                _logger?.Log(VesselLogLevel.Information, $"Deleting unreferenced blob '{metadata.Value.Hash}' after removing object '{key}'.");
                _blobStorageEngine.DeleteBlob(metadata.Value.Hash);
            }

            _logger?.Log(VesselLogLevel.Information, $"Deleted object '{key}'.");
            return true;
        }
        catch (Exception exception)
        {
            _logger?.Log(VesselLogLevel.Error, $"Failed to delete object '{key}': {exception.Message}");
            throw;
        }
        finally
        {
            keyLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _logger?.Log(VesselLogLevel.Debug, $"Fetching metadata for object '{key}'.");
        return _metadataRepository.Get(key);
    }

    /// <inheritdoc/>
    public ObjectMetadata? GetMetadata(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _logger?.Log(VesselLogLevel.Debug, $"Fetching metadata for object '{key}'.");
        return _metadataRepository.Get(key);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> GetAllKeysAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        return GetAllKeysAsyncInternal(cancellationToken);

        async IAsyncEnumerable<string> GetAllKeysAsyncInternal([EnumeratorCancellation] CancellationToken localToken)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Enumerating keys with prefix '{prefix}'.");
            await foreach (var key in _metadataRepository.IterateKeysAsync(prefix, localToken).ConfigureAwait(false))
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// Selects the per-key synchronization primitive for the supplied logical key.
    /// </summary>
    /// <param name="key">The logical key for which a lock should be resolved.</param>
    /// <returns>The semaphore used to serialize concurrent access for the specified key.</returns>
    private SemaphoreSlim GetKeyLock(string key)
    {
        uint hash = (uint)string.GetHashCode(key, StringComparison.Ordinal);
        return _keyLocks[hash % _keyLocks.Length];
    }

    /// <summary>
    /// Releases the resources owned by the storage instance.
    /// </summary>
    public void Dispose() => _metadataRepository.Dispose();
}
