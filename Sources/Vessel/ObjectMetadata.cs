namespace Vessel;

/// <summary>
/// Describes a stored object identified by its logical key, blob identity, size, entity tag, and optional file attributes.
/// </summary>
/// <param name="Key">The logical key used by the caller.</param>
/// <param name="Hash">The physical blob identifier, including a codec suffix for compressed data.</param>
/// <param name="OriginalSize">The size of the stored object in bytes.</param>
/// <param name="CompressionType">The compression algorithm applied to the stored blob.</param>
/// <param name="ETag">The entity tag derived from the blob hash.</param>
/// <param name="CreationDate">The UTC timestamp of the last metadata update.</param>
/// <param name="FileName">The original file name supplied by the caller, when available.</param>
/// <param name="Extension">The file extension derived from the original name, when available.</param>
/// <param name="MimeType">The MIME type provided for the upload, when available.</param>
/// <param name="CompressedSize">The size of the compressed blob payload in bytes.</param>
public readonly record struct ObjectMetadata(
    string Key,
    string Hash,
    long OriginalSize,
    CompressionType CompressionType,
    string ETag,
    DateTimeOffset CreationDate,
    string? FileName = null,
    string? Extension = null,
    string? MimeType = null,
    long CompressedSize = 0)
{
    /// <summary>Gets the SHA-256 hash of the uncompressed payload.</summary>
    public string ContentHash => Hash is { Length: 66 } ? Hash[..64] : Hash;
}
