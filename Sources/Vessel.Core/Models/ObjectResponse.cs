namespace Vessel.Models;

/// <summary>
/// Wraps an object response with metadata and an open content stream returned by the storage engine.
/// </summary>
/// <param name="Metadata">The metadata associated with the object.</param>
/// <param name="ContentStream">The readable stream that exposes the object content.</param>
public sealed record ObjectResponse(ObjectMetadata Metadata, Stream ContentStream) : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Disposes the underlying content stream when the response is no longer needed.
    /// </summary>
    public void Dispose() => ContentStream.Dispose();

    /// <summary>
    /// Asynchronously disposes the underlying content stream when the response is no longer needed.
    /// </summary>
    public async ValueTask DisposeAsync() => await ContentStream.DisposeAsync();

}
