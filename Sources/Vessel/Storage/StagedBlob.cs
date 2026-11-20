namespace Vessel.Storage;

internal sealed class StagedBlob(string path, FileStream file, BlobInfo info) : IDisposable
{
    public string Path { get; } = path;
    public BlobInfo Info { get; } = info;

    public void FlushAndClose()
    {
        file.Flush(flushToDisk: true);
        file.Dispose();
    }

    public void Dispose()
    {
        try { file.Dispose(); }
        finally { LocalBlobStorageEngine.TryDeleteFile(Path); }
    }
}
