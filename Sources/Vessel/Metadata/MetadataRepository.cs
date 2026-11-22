using Vessel.Logging;
using System.Collections.Concurrent;
using ZoneTree;
using ZoneTree.AbstractFileStream;
using ZoneTree.Options;

namespace Vessel.Metadata;

/// <summary>
/// Stores metadata in ZoneTree and exposes routines for lookup, updates, deletion, and prefix-based iteration.
/// </summary>
internal sealed class MetadataRepository : IMetadataRepository
{
    private readonly IZoneTree<string, ObjectMetadata> _metadataDb;
    private readonly IMaintainer _metadataDbMaintainer;
    private readonly ConcurrentDictionary<string, int> _refCounts = new(StringComparer.Ordinal);
    private readonly object _commitGate = new();
    private readonly IVesselLogger? _logger;
    private Exception? _mutationFailure;
    private int _disposed;

    /// <summary>
    /// Initializes the metadata repository against the supplied storage root.
    /// </summary>
    /// <param name="storageBasePath">The root directory where the metadata database is created.</param>
    /// <param name="useCompression">
    /// When <see langword="true"/>, ZoneTree uses compression for metadata entries; otherwise metadata is stored
    /// uncompressed.
    /// </param>
    /// <param name="logger">An optional logger used to capture operational diagnostics.</param>
    /// <param name="fileStreamProvider">An optional provider for the current metadata database's files.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="storageBasePath"/> is null, empty, or whitespace.
    /// </exception>
    public MetadataRepository(string storageBasePath, bool useCompression, IVesselLogger? logger = null, IFileStreamProvider? fileStreamProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath);
        _logger = logger;
        var fullPath = Path.GetFullPath(storageBasePath);

        var currentPath = Path.Join(fullPath, "metadata-v1");
        var legacyPath = Path.Join(fullPath, "metadata");
        if (!Directory.Exists(currentPath) && Directory.Exists(legacyPath))
            MigrateLegacyMetadata(legacyPath, currentPath, useCompression);

        _metadataDb = CreateFactory(currentPath, useCompression, WriteAheadLogMode.Sync, fileStreamProvider).OpenOrCreate();
        try
        {
            RebuildReferenceCounts();
            _metadataDbMaintainer = _metadataDb.CreateMaintainer();
        }
        catch
        {
            _metadataDb.Dispose();
            throw;
        }
    }

    private static ZoneTreeFactory<string, ObjectMetadata> CreateFactory(string path, bool useCompression, WriteAheadLogMode logMode, IFileStreamProvider? fileStreamProvider = null)
        => new ZoneTreeFactory<string, ObjectMetadata>(fileStreamProvider)
            .SetDataDirectory(path)
            .ConfigureWriteAheadLogOptions(options => options.WriteAheadLogMode = logMode)
            .ConfigureDiskSegmentOptions(options =>
            {
                options.CompressionMethod = useCompression ? CompressionMethod.LZ4 : CompressionMethod.None;
                options.CompressionLevel = CompressionLevels.LZ4Fastest;
                options.CompressionBlockSize = 4 * 1024 * 1024;
            })
            .SetValueSerializer(new ObjectMetadataSerializer())
            .SetIsDeletedDelegate(static (in _, in value) => string.IsNullOrEmpty(value.Key))
            .SetMarkValueDeletedDelegate(static (ref value) => value = default);

    private static void MigrateLegacyMetadata(string legacyPath, string currentPath, bool useCompression)
    {
        var stagingPath = $"{currentPath}.migration-{Guid.NewGuid():N}";
        // ZoneTree preserves the WAL encoding of an existing segment. Copy into a new sync store.
        using (var legacy = CreateFactory(legacyPath, useCompression, WriteAheadLogMode.AsyncCompressed).OpenOrCreate())
        using (var target = CreateFactory(stagingPath, useCompression, WriteAheadLogMode.Sync).OpenOrCreate())
        using (var iterator = legacy.CreateIterator())
        {
            iterator.SeekFirst();
            while (iterator.Next())
                target.Upsert(iterator.CurrentKey, iterator.CurrentValue);
        }

        Directory.Move(stagingPath, currentPath);
    }

    /// <inheritdoc/>
    public ObjectMetadata? Get(string key)
    {
        ThrowIfUnavailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = _metadataDb.TryGet(key, out var metadata);
        ThrowIfUnavailable();
        if (!result)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup miss for key '{key}'.");
            return null;
        }

        _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup hit for key '{key}'.");
        return metadata;
    }

    /// <inheritdoc/>
    public void Upsert(in ObjectMetadata metadata) => Upsert(default, metadata);

    /// <summary>Persists metadata unless cancellation is requested before the mutation begins.</summary>
    internal void Upsert(CancellationToken cancellationToken, in ObjectMetadata metadata)
    {
        lock (_commitGate)
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            var hadPrevious = _metadataDb.TryGet(metadata.Key, out var previous);
            cancellationToken.ThrowIfCancellationRequested();
            var changedHash = !hadPrevious || previous.Hash != metadata.Hash;
            try
            {
                if (changedHash) IncrementRefCount(metadata.Hash);
                _metadataDb.Upsert(metadata.Key, metadata);
                if (changedHash && hadPrevious) DecrementRefCount(previous.Hash);
            }
            catch (Exception error)
            {
                // ZoneTree can change its in-memory tree before appending the WAL record.
                // A failed append must remain at the end of the log until reopen.
                Volatile.Write(ref _mutationFailure, error);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool Delete(string key) => Delete(default, key);

    /// <summary>Deletes metadata unless cancellation is requested before the mutation begins.</summary>
    internal bool Delete(CancellationToken cancellationToken, string key)
    {
        lock (_commitGate)
        {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_metadataDb.TryGet(key, out var metadata)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!_metadataDb.TryDelete(key, out _)) return false;
                DecrementRefCount(metadata.Hash);
                return true;
            }
            catch (Exception error)
            {
                Volatile.Write(ref _mutationFailure, error);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsHashReferenced(string hash)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(hash)) return false;
        // Keep payloads when the on-disk outcome of a metadata write is uncertain.
        return (_refCounts.TryGetValue(hash, out var count) && count > 0) || Volatile.Read(ref _mutationFailure) is not null;
    }

    /// <inheritdoc/>
    public IEnumerable<string> IterateKeys(string prefix)
    {
        ThrowIfUnavailable();
        // L'iteratore di ZoneTree è un oggetto disposable che punta ai segmenti in memoria/disco
        using var iterator = _metadataDb.CreateIterator();
        var enumeratedKeys = 0L;

        _logger?.Log(VesselLogLevel.Debug, $"Iterating keys (sync) with prefix '{prefix}'.");

        if (prefix.Length == 0)
        {
            iterator.SeekFirst();
        }
        else
        {
            iterator.Seek(prefix);
        }

        while (true)
        {
            var hasNext = iterator.Next();
            ThrowIfUnavailable();
            if (!hasNext) break;

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
        _refCounts.AddOrUpdate(hash, 1, static (_, count) => checked(count + 1));
    }

    /// <summary>
    /// Decreases the reference count for the supplied blob hash without allowing it to become negative.
    /// </summary>
    /// <param name="hash">The blob hash for which the reference count should be decremented.</param>
    private void DecrementRefCount(string hash)
    {
        while (_refCounts.TryGetValue(hash, out var count))
        {
            if (count == 1)
            {
                if (_refCounts.TryRemove(new KeyValuePair<string, int>(hash, count))) return;
            }
            else if (_refCounts.TryUpdate(hash, count - 1, count))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Reconstructs the derived reference counts from durable object metadata.
    /// </summary>
    private void RebuildReferenceCounts()
    {
        using var iterator = _metadataDb.CreateIterator();
        iterator.SeekFirst();
        while (iterator.Next())
        {
            IncrementRefCount(iterator.CurrentValue.Hash);
        }
    }

    /// <summary>
    /// Releases the resources held by the metadata repository and its backing databases.
    /// </summary>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(Interlocked.Exchange(ref _disposed, 1) != 0, this);
        try { _metadataDbMaintainer.Dispose(); }
        finally { _metadataDb.Dispose(); }
    }

    /// <summary>
    /// Throws an exception when the repository has already been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the repository is no longer usable.</exception>
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    internal void ThrowIfUnavailable()
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _mutationFailure) is { } failure)
            throw new IOException("A metadata write failed. Dispose and reopen the Vessel store before using it again.", failure);
    }
}
