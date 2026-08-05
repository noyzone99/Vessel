using System.Runtime.CompilerServices;
using Vessel.Logging;
using Vessel.Models;
using ZoneTree;
using ZoneTree.Options;

namespace Vessel.Metadata;

/// <summary>
/// Stores metadata in ZoneTree and exposes routines for lookup, updates, deletion, and prefix-based iteration.
/// </summary>
internal sealed class MetadataRepository : IMetadataRepository
{
    private readonly string _databasePath;
    private readonly IZoneTree<string, ObjectMetadata> _database;
    private readonly IMaintainer _maintainer;
    private readonly IVesselLogger? _logger;

    /// <summary>
    /// Initializes the metadata repository against the supplied storage root.
    /// </summary>
    /// <param name="storageBasePath">The root directory where the metadata database is created.</param>
    /// <param name="useCompression">
    /// When <see langword="true"/>, RocksDB uses compression for metadata entries; otherwise metadata is stored
    /// uncompressed.
    /// </param>
    /// <param name="logger">An optional logger used to capture operational diagnostics.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="storageBasePath"/> is null, empty, or whitespace.
    /// </exception>
    public MetadataRepository(string storageBasePath, bool useCompression, IVesselLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath);
        _logger = logger;
        _databasePath = Path.Combine(Path.GetFullPath(storageBasePath), "metadata");
        Directory.CreateDirectory(_databasePath);

        var dataDirectoryPath = Path.GetFullPath(Path.Combine(_databasePath, ".storage"));
        Directory.CreateDirectory(dataDirectoryPath);

        _database = new ZoneTreeFactory<string, ObjectMetadata>()
            .SetDataDirectory(dataDirectoryPath)
            .ConfigureDiskSegmentOptions(options =>
            {
                options.CompressionMethod = useCompression ? CompressionMethod.LZ4 : CompressionMethod.None;
                options.CompressionLevel = CompressionLevels.LZ4Fastest;
                options.CompressionBlockSize = 4 * 1024 * 1024;
            })
            .SetValueSerializer(new ObjectMetadataSerializer())
            // FIX 1: Tipi espliciti per la sintassi delle lambda con modificatori 'in' e 'ref'
            .SetIsDeletedDelegate(static (in _, in value) => IsDeletedMarker(value))
            .SetMarkValueDeletedDelegate(static (ref value) => value = default)
            .OpenOrCreate();

        _maintainer = _database.CreateMaintainer();
    }

    /// <inheritdoc/>
    public async ValueTask<ObjectMetadata?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _database.TryGet(key, out var metadata);
        if (!result)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup miss for key '{key}'.");
            return null;
        }
        _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup hit for key '{key}'.");
        return metadata;
    }

    /// <inheritdoc/>
    public ObjectMetadata? Get(string key)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _database.TryGet(key, out var metadata);
        if (!result || IsDeletedMarker(metadata))
        {
            _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup miss for key '{key}'.");
            return null;
        }

        _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup hit for key '{key}'.");
        return metadata;
    }

    /// <inheritdoc/>
    public ValueTask UpsertAsync(in ObjectMetadata metadata, CancellationToken cancellationToken)
    {
        _database.Upsert(metadata.Key, metadata);
        _logger?.Log(VesselLogLevel.Debug, $"Stored metadata for key '{metadata.Key}' with hash '{metadata.Hash}'.");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public void Upsert(in ObjectMetadata metadata)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);

        _database.Upsert(metadata.Key, metadata);
        _logger?.Log(VesselLogLevel.Debug, $"Stored metadata for key '{metadata.Key}' with hash '{metadata.Hash}'.");
    }

    /// <inheritdoc/>
    public async ValueTask<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var result = _database.TryDelete(key, out _);
        if (!result)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Delete ignored because metadata for key '{key}' does not exist.");
            return false;
        }
        _logger?.Log(VesselLogLevel.Information, $"Deleted metadata for key '{key}'.");
        return true;
    }

    /// <inheritdoc/>
    public bool Delete(string key)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _database.TryDelete(key, out _);
        if (!result)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Delete ignored because metadata for key '{key}' does not exist.");
            return false;
        }

        _logger?.Log(VesselLogLevel.Information, $"Deleted metadata for key '{key}'.");
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsHashReferencedAsync(string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        using var iterator = _database.CreateIterator();
        iterator.SeekFirst();

        while (iterator.Next())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.Log(VesselLogLevel.Debug, $"Cancellation requested while checking hash references for blob hash '{hash}'.");
                return false;
            }
            var metadata = iterator.CurrentValue;

            if (metadata.Hash == hash)
            {
                _logger?.Log(VesselLogLevel.Debug, $"Found metadata reference for blob hash '{hash}'.");
                return true;
            }
        }

        _logger?.Log(VesselLogLevel.Debug, $"No metadata references found for blob hash '{hash}'.");
        return false;
    }

    /// <inheritdoc/>
    public bool IsHashReferenced(string hash)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        using var iterator = _database.CreateIterator();
        iterator.SeekFirst();

        while (iterator.Next())
        {
            var metadata = iterator.CurrentValue;
            if (IsDeletedMarker(metadata))
            {
                continue;
            }

            if (string.Equals(metadata.Hash, hash, StringComparison.Ordinal))
            {
                _logger?.Log(VesselLogLevel.Debug, $"Found metadata reference for blob hash '{hash}'.");
                return true;
            }
        }

        _logger?.Log(VesselLogLevel.Debug, $"No metadata references found for blob hash '{hash}'.");
        return false;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> IterateKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = _database.CreateIterator();
        var enumeratedKeys = 0L;

        _logger?.Log(VesselLogLevel.Debug, $"Iterating keys with prefix '{prefix}'.");
        if (string.IsNullOrWhiteSpace(prefix))
        {
            iterator.SeekFirst();
        }
        else
        {
            iterator.Seek(prefix);
        }
        while (iterator.Next())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = iterator.CurrentKey;
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                break;
            enumeratedKeys++;
            yield return key;
        }

        _logger?.Log(VesselLogLevel.Debug, $"Completed key iteration for prefix '{prefix}' with {enumeratedKeys} keys.");
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IEnumerable<string> IterateKeys(string prefix)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prefix);

        return IterateKeysInternal();

        IEnumerable<string> IterateKeysInternal()
        {
            using var iterator = _database.CreateIterator();
            var enumeratedKeys = 0L;

            _logger?.Log(VesselLogLevel.Debug, $"Iterating keys with prefix '{prefix}'.");

            if (string.IsNullOrEmpty(prefix))
            {
                iterator.SeekFirst();
            }
            else
            {
                iterator.Seek(prefix);
            }

            while (iterator.Next())
            {
                string key = iterator.CurrentKey;
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    break;
                }

                var metadata = iterator.CurrentValue;
                if (IsDeletedMarker(metadata))
                {
                    continue;
                }

                enumeratedKeys++;
                yield return key;
            }

            _logger?.Log(VesselLogLevel.Debug, $"Completed key iteration for prefix '{prefix}' with {enumeratedKeys} keys.");
        }
    }

    private static bool IsDeletedMarker(in ObjectMetadata metadata)
        => string.IsNullOrEmpty(metadata.Key) && string.IsNullOrEmpty(metadata.Hash);

    public void Dispose()
    {
        _maintainer.Dispose();
        _database.Dispose();
    }
}