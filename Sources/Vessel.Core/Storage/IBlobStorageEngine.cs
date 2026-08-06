using Vessel.Models;

namespace Vessel.Storage;

/// <summary>
/// Defines the contract for persisting and reading object payload blobs in the backing storage engine.
/// </summary>
internal interface IBlobStorageEngine
{
    /// <summary>
    /// Writes a payload stream to the backing store, optionally applying compression and returning the resulting blob metadata.
    /// </summary>
    /// <param name="inputStream">The readable stream containing the uncompressed payload.</param>
    /// <param name="compressionType">The compression codec to use when persisting the blob.</param>
    /// <param name="compressionLevel">The compression strength to use for the selected codec.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A tuple containing the generated hash, the uncompressed size, and the compressed blob size.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputStream"/> is <see langword="null"/>.</exception>
    Task<BlobInfo> WriteBlobAsync(Stream inputStream, CompressionType compressionType, CompressionLevel compressionLevel, CancellationToken cancellationToken);

    /// <summary>
    /// Opens the blob identified by the supplied hash for reading.
    /// </summary>
    /// <param name="hash">The content hash that identifies the blob file.</param>
    /// <param name="compressionType">The compression codec used to encode the blob.</param>
    /// <returns>A readable stream that exposes the blob contents.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hash"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the blob does not exist.</exception>
    Stream OpenBlobRead(string hash, CompressionType compressionType);

    /// <summary>
    /// Deletes the blob identified by the supplied hash when it exists.
    /// </summary>
    /// <param name="hash">The content hash identifying the blob to remove.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="hash"/> is null, empty, or whitespace.</exception>
    void DeleteBlob(string hash);

    /// <summary>
    /// Removes temporary files that may have been left behind by interrupted writes.
    /// </summary>
    void CleanupOrphanedTempFiles();
}
