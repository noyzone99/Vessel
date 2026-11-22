using Vessel.Logging;

namespace Vessel;

/// <summary>
/// Provides optional configuration for a <see cref="VesselStorage"/> instance.
/// </summary>
/// <param name="EnableMetadataCompression">When <see langword="true"/>, compresses metadata disk segments with Zstd.</param>
/// <param name="Logger">An optional logger used to emit diagnostics during storage operations.</param>
public record VesselStorageOptions(bool EnableMetadataCompression = false, IVesselLogger? Logger = null);
