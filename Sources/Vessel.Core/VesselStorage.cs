using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Models;
using Vessel.Storage;

namespace Vessel;

/// <summary>
/// Coordinates the metadata repository and the blob storage engine for the public storage API.
/// </summary>
/// <remarks>
/// This implementation keeps logical object metadata and physical blob payloads in sync while protecting concurrent access to the same key with a small set of per-key locks.
/// </remarks>
/// <param name="storageBasePath">The root directory used to store metadata, blobs, and temporary files.</param>
/// <param name="options">Optional configuration for the storage instance, including diagnostics and behavior toggles that influence metadata handling.</param>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="storageBasePath"/> is <see langword="null"/>, empty, or whitespace.
/// </exception>
internal sealed class VesselStorage(string storageBasePath, VesselStorageOptions options = default!) : IVesselStorage
{
    private readonly IVesselLogger? _logger = options.Logger;
    private readonly LocalBlobStorageEngine _blobStorageEngine = new(storageBasePath, options.Logger);
    private readonly MetadataRepository _metadataRepository = new(storageBasePath, options.EnableMetadataCompression, options.Logger);
    private readonly SemaphoreSlim[] _keyLocks = [.. Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1))];

    /// <inheritdoc/>
    public async Task<ObjectMetadata> PutAsync(string key, Stream contentStream, bool overwrite = false, string? fileName = null, string? extension = null, string? mimeType = null, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Balanced, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(contentStream);

        _logger?.Log(VesselLogLevel.Information, $"Starting put for object '{key}' with overwrite={overwrite}.");

        var keyLock = GetKeyLock(key);
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var existingMetadata = await _metadataRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (existingMetadata is not null && !overwrite)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Refusing to overwrite existing object '{key}'.");
                throw new InvalidOperationException($"Object '{key}' already exists.");
            }
            var (hash, size, compressedSize) = await _blobStorageEngine.WriteBlobAsync(contentStream, compressionType, compressionLevel, cancellationToken).ConfigureAwait(false);

            var normalizedFileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
            var normalizedExtension = ResolveExtension(normalizedFileName, extension);
            var normalizedMimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType.Trim();
            var metadata = new ObjectMetadata(key, hash, size, compressionType, hash, DateTimeOffset.UtcNow, normalizedFileName, normalizedExtension, normalizedMimeType, compressedSize);

            await _metadataRepository.UpsertAsync(metadata, cancellationToken).ConfigureAwait(false);

            if (existingMetadata is not null && !string.Equals(existingMetadata.Value.Hash, hash, StringComparison.Ordinal))
            {
                var isStillReferenced = await _metadataRepository.IsHashReferencedAsync(existingMetadata.Value.Hash, cancellationToken).ConfigureAwait(false);
                if (!isStillReferenced)
                {
                    _logger?.Log(VesselLogLevel.Information, $"Deleting superseded blob '{existingMetadata.Value.Hash}' for object '{key}'.");
                    _blobStorageEngine.DeleteBlob(existingMetadata.Value.Hash);
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
    public async Task<ObjectResponse> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var metadata = await _metadataRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
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
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _logger?.Log(VesselLogLevel.Information, $"Deleting object '{key}'.");

        var keyLock = GetKeyLock(key);
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var metadata = await _metadataRepository.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (metadata is null)
            {
                _logger?.Log(VesselLogLevel.Information, $"Delete request ignored because object '{key}' was not found.");
                return false;
            }

            var deleted = await _metadataRepository.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            if (!deleted)
            {
                _logger?.Log(VesselLogLevel.Warning, $"Delete operation reported failure for object '{key}'.");
                return false;
            }

            var isStillReferenced = await _metadataRepository.IsHashReferencedAsync(metadata.Value.Hash, cancellationToken).ConfigureAwait(false);
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
    public Task<ObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _logger?.Log(VesselLogLevel.Debug, $"Fetching metadata for object '{key}'.");
        return _metadataRepository.GetAsync(key, cancellationToken);
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

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _logger?.Log(VesselLogLevel.Information, "Disposing embedded object storage.");
        _metadataRepository.Dispose();
        return ValueTask.CompletedTask;
    }

    private SemaphoreSlim GetKeyLock(string key)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = BitConverter.ToInt32(hashBytes, 0);
        if (hash == int.MinValue)
        {
            hash = 0;
        }

        return _keyLocks[Math.Abs(hash) % _keyLocks.Length];
    }

    private static string? ResolveExtension(string? fileName, string? extension)
    {
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.StartsWith('.') ? extension : $".{extension}";
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return Path.GetExtension(fileName);
    }
}
