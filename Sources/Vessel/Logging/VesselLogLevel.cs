namespace Vessel.Logging;

/// <summary>
/// Represents the severity of a log entry emitted by the logging infrastructure.
/// </summary>
public enum VesselLogLevel
{
    /// <summary>
    /// Indicates a verbose diagnostic message intended for debugging.
    /// </summary>
    Debug,

    /// <summary>
    /// Indicates an informational message about normal operation.
    /// </summary>
    Information,

    /// <summary>
    /// Indicates a warning that does not prevent the operation from continuing.
    /// </summary>
    Warning,

    /// <summary>
    /// Indicates an error that caused the operation to fail or become unusable.
    /// </summary>
    Error
}
