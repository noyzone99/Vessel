using Vessel.Logging;

namespace Vessel;

/// <summary>
/// Provides optional configuration for a <see cref="VesselStorage"/> instance.
/// </summary>
/// <param name="EnableMetadataCompression">When <see langword="true"/>, metadata-related operations can opt into compression-aware handling.</param>
/// <param name="Logger">An optional logger used to emit diagnostics during storage operations.</param>
public record VesselStorageOptions(bool EnableMetadataCompression = false, IVesselLogger? Logger = null);