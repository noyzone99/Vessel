namespace Vessel;

/// <summary>
/// Specifies the trade-off between compression strength and CPU cost for a compression codec.
/// </summary>
public enum CompressionLevel
{
    /// <summary>Prefers the lowest CPU overhead and the fastest compression speed.</summary>
    Fastest = 0,
    /// <summary>Uses a balanced trade-off between compression ratio and speed.</summary>
    Balanced = 1,
    /// <summary>Prioritizes a stronger compression ratio over speed.</summary>
    High = 2,
    /// <summary>Uses the strongest available compression level, at the highest CPU cost.</summary>
    Ultra = 3
}
