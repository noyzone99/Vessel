using Vessel.Models;

namespace Vessel;

/// <summary>
/// Defines the public contract for asynchronously storing and retrieving objects backed by a local blob store and metadata indexes.
/// </summary>
/// <remarks>
/// Implementations are expected to manage logical key-based object identity, store object metadata independently from payload bytes, and provide basic diagnostics and key enumeration capabilities.
/// </remarks>
public interface IVesselStorage : IDisposable
{
    /// <summary>
    /// Stores a new object payload under the specified logical key or replaces an existing object when overwrite is enabled.
    /// </summary>
    /// <param name="key">The logical key under which the object should be stored. Keys are used as the stable identifier for metadata lookups.</param>
    /// <param name="contentStream">The readable stream that contains the payload to persist. The stream is consumed during the write operation.</param>
    /// <param name="overwrite">When <see langword="true"/>, an existing object with the same key is replaced; otherwise, the operation fails when the key already exists.</param>
    /// <param name="fileInfo">Optional file metadata such as the original file name, extension, and MIME type to persist with the object.</param>
    /// <param name="compressionType">The compression algorithm to apply to the underlying blob payload.</param>
    /// <param name="compressionLevel">The compression strength to use for the selected compression codec.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>Metadata describing the stored object, including the blob hash, logical sizes, and timestamps.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contentStream"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the object already exists and <paramref name="overwrite"/> is <see langword="false"/>.</exception>
    Task<ObjectMetadata> PutAsync(string key, Stream contentStream, bool overwrite = false, ObjectInfo fileInfo = default, CompressionType compressionType = CompressionType.None, CompressionLevel compressionLevel = CompressionLevel.Balanced, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the object payload and metadata for the specified logical key.
    /// </summary>
    /// <param name="key">The logical key of the object to read.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>An <see cref="ObjectResponse"/> that exposes the content stream and metadata.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when no object exists for the requested key.</exception>
    ValueTask<ObjectResponse> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the object payload and metadata for the specified logical key.
    /// </summary>
    /// <param name="key">The logical key of the object to read.</param>
    /// <returns>An <see cref="ObjectResponse"/> that exposes the content stream and metadata.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="FileNotFoundException">Thrown when no object exists for the requested key.</exception>
    ObjectResponse Get(string key);

    /// <summary>
    /// Deletes the object associated with the specified logical key.
    /// </summary>
    /// <param name="key">The logical key of the object to delete.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns><see langword="true"/> when an object was deleted; otherwise <see langword="false"/> when no object existed for the key.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for the specified object without opening the underlying blob content.
    /// </summary>
    /// <param name="key">The logical key of the object whose metadata should be retrieved.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>Metadata for the object, or <see langword="null"/> when the key is unknown.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    ValueTask<ObjectMetadata?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for the specified object without opening the underlying blob content.
    /// </summary>
    /// <param name="key">The logical key of the object whose metadata should be retrieved.</param>
    /// <returns>Metadata for the object, or <see langword="null"/> when the key is unknown.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is null, empty, or whitespace.</exception>
    ObjectMetadata? GetMetadata(string key);

    /// <summary>
    /// Enumerates logical object keys whose names match the supplied prefix.
    /// </summary>
    /// <param name="prefix">An optional prefix used to filter the set of keys.</param>
    /// <param name="cancellationToken">A token used to cancel the enumeration.</param>
    /// <returns>An asynchronous enumeration of matching keys.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prefix"/> is <see langword="null"/>.</exception>
    IAsyncEnumerable<string> GetAllKeysAsync(string prefix = "", CancellationToken cancellationToken = default);
}
