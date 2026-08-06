namespace Vessel.Models;

/// <summary>
/// Represents the storage identity and size information for a persisted blob payload.
/// </summary>
/// <param name="Hash">The content hash that uniquely identifies the blob.</param>
/// <param name="Size">The uncompressed size of the payload in bytes.</param>
/// <param name="CompressedSize">The size of the stored payload after compression, if any.</param>
internal record struct BlobInfo(string Hash, long Size, long CompressedSize);