namespace Vessel.Storage;

internal sealed class StagedBlob(string path, FileStream file, BlobInfo info) : IDisposable
{
    private bool _published;

    public BlobInfo Info { get; } = info;

    public void FlushAndClose()
    {
        file.Flush(flushToDisk: true);
        file.Dispose();
    }

    public void MoveTo(string destination)
    {
        File.Move(path, destination);
        _published = true;
    }

    public void Dispose()
    {
        if (_published) return;
        try { file.Dispose(); }
        finally { LocalBlobStorageEngine.TryDeleteFile(path); }
    }
}
