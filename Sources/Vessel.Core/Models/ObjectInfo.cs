namespace Vessel.Models;

/// <summary>
/// Carries optional file metadata that can be persisted alongside an object entry.
/// </summary>
/// <param name="FileName">The original file name supplied by the caller, when available.</param>
/// <param name="FileExtension">The file extension associated with the original file name, when available.</param>
/// <param name="MimeType">The MIME type supplied for the upload, when available.</param>
public readonly record struct ObjectInfo(string? FileName = null, string? FileExtension = null, string? MimeType = null);