namespace Vessel.Logging;

/// <summary>
/// Provides a lightweight, host-agnostic logging abstraction for core storage operations.
/// </summary>
public interface IVesselLogger
{
    /// <summary>
    /// Writes a log entry with the specified severity and message.
    /// </summary>
    /// <param name="level">The severity of the log entry.</param>
    /// <param name="message">The human-readable message to log.</param>
    /// <param name="exception">An optional exception associated with the log entry.</param>
    void Log(VesselLogLevel level, string message, Exception? exception = null);
}
