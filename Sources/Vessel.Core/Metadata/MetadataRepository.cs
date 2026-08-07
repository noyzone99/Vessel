using Vessel.Logging;
using ZoneTree;
using ZoneTree.Options;

namespace Vessel.Metadata;

/// <summary>
/// Stores metadata in ZoneTree and exposes routines for lookup, updates, deletion, and prefix-based iteration.
/// </summary>
internal sealed class MetadataRepository : IMetadataRepository
{
    private readonly IZoneTree<string, ObjectMetadata> _metadataDb;
    private readonly IZoneTree<string, int> _refCountDb;
    private readonly IMaintainer _metadataDbMaintainer;
    private readonly IMaintainer _refCountDbMaintainer;
    private readonly IVesselLogger? _logger;
    private int _disposed;

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
        _logger = logger;
        var fullPath = Path.GetFullPath(storageBasePath);

        _metadataDb = new ZoneTreeFactory<string, ObjectMetadata>()
            .SetDataDirectory(Path.Join(fullPath, "metadata"))
            .ConfigureDiskSegmentOptions(options =>
            {
                options.CompressionMethod = useCompression ? CompressionMethod.LZ4 : CompressionMethod.None;
                options.CompressionLevel = CompressionLevels.LZ4Fastest;
                options.CompressionBlockSize = 4 * 1024 * 1024;
            })
            .SetValueSerializer(new ObjectMetadataSerializer())
            .SetIsDeletedDelegate(static (in _, in value) => string.IsNullOrEmpty(value.Key))
            .SetMarkValueDeletedDelegate(static (ref value) => value = default)
            .OpenOrCreate();
        _metadataDbMaintainer = _metadataDb.CreateMaintainer();

        _refCountDb = new ZoneTreeFactory<string, int>()
            .SetDataDirectory(Path.Join(fullPath, "refcounts"))
            .ConfigureDiskSegmentOptions(options => options.CompressionMethod = CompressionMethod.None)
            .OpenOrCreate();
        _refCountDbMaintainer = _refCountDb.CreateMaintainer();
    }

    /// <inheritdoc/>
    public ObjectMetadata? Get(string key)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _metadataDb.TryGet(key, out var metadata);
        if (!result)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup miss for key '{key}'.");
            return null;
        }

        _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup hit for key '{key}'.");
        return metadata;
    }

    /// <inheritdoc/>
    public void Upsert(in ObjectMetadata metadata)
    {
        ThrowIfDisposed();
        if (_metadataDb.TryGet(metadata.Key, out var oldMetadata))
        {
            if (oldMetadata.Hash == metadata.Hash)
            {
                _metadataDb.Upsert(metadata.Key, metadata);
                return;
            }
            DecrementRefCount(oldMetadata.Hash);
        }

        IncrementRefCount(metadata.Hash);
        _metadataDb.Upsert(metadata.Key, metadata);
    }

    /// <inheritdoc/>
    public bool Delete(string key)
    {
        ThrowIfDisposed();
        if (!_metadataDb.TryGet(key, out var metadata)) return false;
        DecrementRefCount(metadata.Hash);
        return _metadataDb.TryDelete(key, out _);
    }

    /// <inheritdoc/>
    public bool IsHashReferenced(string hash)
    {
        ThrowIfDisposed();
        return _refCountDb.TryGet(hash, out var count) && count > 0;
    }

    /// <inheritdoc/>
    public IEnumerable<string> IterateKeys(string prefix)
    {
        ThrowIfDisposed();
        // L'iteratore di ZoneTree è un oggetto disposable che punta ai segmenti in memoria/disco
        using var iterator = _metadataDb.CreateIterator();
        var enumeratedKeys = 0L;

        _logger?.Log(VesselLogLevel.Debug, $"Iterating keys (sync) with prefix '{prefix}'.");

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
            // Nessun CancellationToken qui (opzionale in sync, o puoi passarlo)
            string key = iterator.CurrentKey;

            // Se usciamo dal prefisso, interrompiamo subito l'iterazione (O(log n) + m)
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                break;

            enumeratedKeys++;
            yield return key;
        }

        _logger?.Log(VesselLogLevel.Debug, $"Completed sync key iteration for prefix '{prefix}' with {enumeratedKeys} keys.");
    }

    /// <summary>
    /// Increases the reference count for the supplied blob hash.
    /// </summary>
    /// <param name="hash">The blob hash for which the reference count should be incremented.</param>
    private void IncrementRefCount(string hash)
    {
        _refCountDb.Upsert(hash, _refCountDb.TryGet(hash, out var count) ? count + 1 : 1);
    }

    /// <summary>
    /// Decreases the reference count for the supplied blob hash and removes the entry when no references remain.
    /// </summary>
    /// <param name="hash">The blob hash for which the reference count should be decremented.</param>
    private void DecrementRefCount(string hash)
    {
        if (_refCountDb.TryGet(hash, out var count))
        {
            if (count <= 1) _refCountDb.TryDelete(hash, out _);
            else _refCountDb.Upsert(hash, count - 1);
        }
    }

    /// <summary>
    /// Releases the resources held by the metadata repository and its backing databases.
    /// </summary>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) != 0, this);
        _metadataDbMaintainer.Dispose();
        _metadataDb.Dispose();
        _refCountDbMaintainer.Dispose();
        _refCountDb.Dispose();
    }

    /// <summary>
    /// Throws an exception when the repository has already been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the repository is no longer usable.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}