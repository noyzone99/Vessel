using RocksDbSharp;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Vessel.Logging;
using Vessel.Models;

namespace Vessel.Metadata;

/// <summary>
/// Stores metadata in RocksDB and exposes routines for lookup, updates, deletion, and prefix-based iteration.
/// </summary>
internal sealed class MetadataRepository : IMetadataRepository
{
    private readonly string _databasePath;
    private readonly DbOptions _options;
    private readonly RocksDb _database;
    private readonly ReadOptions _readOptions;
    private readonly IVesselLogger? _logger;

    /// <summary>
    /// Initializes the metadata repository against the supplied storage root.
    /// </summary>
    /// <param name="storageBasePath">The root directory where the metadata database is created.</param>
    /// <param name="useCompression">When <see langword="true"/>, RocksDB uses compression for metadata entries; otherwise metadata is stored uncompressed.</param>
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
        _options = new DbOptions().SetCreateIfMissing(true).IncreaseParallelism(Environment.ProcessorCount);
        _options.SetCompression(useCompression ? Compression.Lz4 : Compression.No);
        _database = RocksDb.Open(_options, _databasePath);
        _readOptions = new ReadOptions();
    }

    /// <inheritdoc/>
    public Task<ObjectMetadata?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var bytes = _database.Get(Encoding.UTF8.GetBytes(key), null, _readOptions);
        if (bytes is null)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup miss for key '{key}'.");
            return Task.FromResult<ObjectMetadata?>(null);
        }

        var metadata = JsonSerializer.Deserialize<ObjectMetadata>(bytes);
        _logger?.Log(VesselLogLevel.Debug, $"Metadata lookup hit for key '{key}'.");
        return Task.FromResult<ObjectMetadata?>(metadata);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(in ObjectMetadata metadata, CancellationToken cancellationToken)
    {
        var keyBytes = Encoding.UTF8.GetBytes(metadata.Key);
        var valueBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
        _database.Put(keyBytes, valueBytes);
        _logger?.Log(VesselLogLevel.Debug, $"Stored metadata for key '{metadata.Key}' with hash '{metadata.Hash}'.");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existing = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            _logger?.Log(VesselLogLevel.Debug, $"Delete ignored because metadata for key '{key}' does not exist.");
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        _database.Remove(keyBytes);
        _logger?.Log(VesselLogLevel.Information, $"Deleted metadata for key '{key}'.");
        return true;
    }

    /// <inheritdoc/>
    public Task<bool> IsHashReferencedAsync(string hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        using var iterator = _database.NewIterator(null, _readOptions);
        iterator.SeekToFirst();

        while (iterator.Valid())
        {
            var valueBytes = iterator.Value();
            if (valueBytes is not null)
            {
                var metadata = JsonSerializer.Deserialize<ObjectMetadata>(valueBytes);
                if (metadata.Hash == hash)
                {
                    _logger?.Log(VesselLogLevel.Debug, $"Found metadata reference for blob hash '{hash}'.");
                    return Task.FromResult(true);
                }
            }

            iterator.Next();
        }

        _logger?.Log(VesselLogLevel.Debug, $"No metadata references found for blob hash '{hash}'.");
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<long> GetCountAsync(CancellationToken cancellationToken)
    {
        using var iterator = _database.NewIterator(null, _readOptions);
        iterator.SeekToFirst();

        long count = 0;
        while (iterator.Valid())
        {
            count++;
            iterator.Next();
        }

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task WriteBatchAsync(Action<WriteBatch> batchAction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batchAction);

        using var batch = new WriteBatch();
        batchAction(batch);
        _database.Write(batch);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> IterateKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = _database.NewIterator(null, _readOptions);
        byte[] prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var enumeratedKeys = 0L;

        _logger?.Log(VesselLogLevel.Debug, $"Iterating keys with prefix '{prefix}'.");

        if (prefixBytes.Length > 0)
        {
            iterator.Seek(prefixBytes);
        }
        else
        {
            iterator.SeekToFirst();
        }

        while (iterator.Valid())
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte[] keyBytes = iterator.Key();
            string key = Encoding.UTF8.GetString(keyBytes);

            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            enumeratedKeys++;
            yield return key;
            iterator.Next();
        }

        _logger?.Log(VesselLogLevel.Debug, $"Completed key iteration for prefix '{prefix}' with {enumeratedKeys} keys.");
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _database.Dispose();
    }
}
