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
    /// <returns>The stored metadata when the key exists; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is null, empty, or whitespace.
    /// </exception>
    ObjectMetadata? Get(string key);

    /// <summary>
    /// Persists or updates the metadata for a stored object.
    /// </summary>
    /// <param name="metadata">The metadata to write to the repository.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="metadata"/> is <see langword="null"/>.
    /// </exception>
    void Upsert(in ObjectMetadata metadata);

    /// <summary>
    /// Deletes metadata for the specified logical key when present.
    /// </summary>
    /// <param name="key">The logical key to remove.</param>
    /// <returns>
    /// <see langword="true"/> when the key existed and was removed; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is null, empty, or whitespace.
    /// </exception>
    bool Delete(string key);

    /// <summary>
    /// Determines whether any metadata entry still references the supplied blob hash.
    /// </summary>
    /// <param name="hash">The blob hash to evaluate.</param>
    /// <returns><see langword="true"/> when the hash is still referenced; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="hash"/> is null, empty, or whitespace.
    /// </exception>
    bool IsHashReferenced(string hash);

    /// <summary>
    /// Streams logical keys matching the supplied prefix in ascending order.
    /// </summary>
    /// <param name="prefix">An optional prefix used to filter the enumeration.</param>
    /// <returns>A synchronous enumeration of matching keys.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="prefix"/> is <see langword="null"/>.
    /// </exception>
    IEnumerable<string> IterateKeys(string prefix);
}
