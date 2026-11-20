namespace Vessel;

/// <summary>
/// Specifies the compression codec that can be applied to stored object payloads.
/// </summary>
public enum CompressionType
{
    /// <summary>Stores the payload without applying any compression.</summary>
    None = 0,
    /// <summary>Uses the Zstandard compression codec.</summary>
    Zstd = 1,
    /// <summary>Uses the LZ4 compression codec.</summary>
    LZ4 = 2
}
