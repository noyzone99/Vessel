using RocksDbSharp;
using Vessel.Models;

namespace Vessel.Metadata;

/// <summary>
/// Defines the contract for persisting and querying object metadata entries in a backing store.
/// </summary>
internal interface IMetadataRepository : IDisposable
{
    /// <summary>
    /// Retrieves metadata associated with the specified logical key.
    /// </summary>
    /// <param name="key">The logical key to look up.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored metadata when the key exists; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    Task<ObjectMetadata?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Persists or updates the metadata for a stored object.
    /// </summary>
    /// <param name="metadata">The metadata to write to the repository.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metadata"/> is <see langword="null"/>.</exception>
    Task UpsertAsync(in ObjectMetadata metadata, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes metadata for the specified logical key when present.
    /// </summary>
    /// <param name="key">The logical key to remove.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns><see langword="true"/> when the key existed and was removed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether any metadata entry still references the supplied blob hash.
    /// </summary>
    /// <param name="hash">The blob hash to evaluate.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns><see langword="true"/> when the hash is still referenced; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hash"/> is null, empty, or whitespace.</exception>
    Task<bool> IsHashReferencedAsync(string hash, CancellationToken cancellationToken);

    /// <summary>
    /// Streams logical keys matching the supplied prefix in ascending order.
    /// </summary>
    /// <param name="prefix">An optional prefix used to filter the enumeration.</param>
    /// <param name="cancellationToken">A token that can cancel the enumeration.</param>
    /// <returns>An asynchronous enumeration of matching keys.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prefix"/> is <see langword="null"/>.</exception>
    IAsyncEnumerable<string> IterateKeysAsync(string prefix, CancellationToken cancellationToken);
}
