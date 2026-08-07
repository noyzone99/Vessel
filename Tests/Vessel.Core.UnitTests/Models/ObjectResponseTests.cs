namespace Vessel.Core.UnitTests.Models;

public class ObjectResponseTests
{
    [Fact]
    public void Dispose_CalledOnce_DisposesContentStream()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        response.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        response.Dispose();
        var exception = Record.Exception(() => response.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Dispose_CalledConcurrentlyFromMultipleThreads_CompletesSuccessfully()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));
        var tasks = new Task[10];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => response.Dispose(), TestContext.Current.CancellationToken);
        }

        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledOnce_DisposesContentStream()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        await response.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        await response.DisposeAsync();
        var exception = await Record.ExceptionAsync(async () => await response.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledConcurrentlyFromMultipleTasks_CompletesSuccessfully()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));
        var tasks = new Task[10];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () => await response.DisposeAsync(), TestContext.Current.CancellationToken);
        }

        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_WithReadableStream_DisposesStreamCorrectly()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(data);
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        await response.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => stream.Position);
    }

    [Fact]
    public void Dispose_WithReadableStream_DisposesStreamCorrectly()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(data);
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        response.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Position);
    }

    [Fact]
    public void Dispose_WithNonSeekableStream_DisposesStreamCorrectly()
    {
        var stream = new NonSeekableMemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        response.Dispose();

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_WithNonSeekableStream_DisposesStreamCorrectly()
    {
        var stream = new NonSeekableMemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        await response.DisposeAsync();

        Assert.True(stream.IsDisposed);
    }

    [Fact]
    public async Task Dispose_AfterDisposeAsync_DoesNotThrow()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        await response.DisposeAsync();
        var exception = Record.Exception(() => response.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_DoesNotThrow()
    {
        var stream = new MemoryStream();
        var metadata = CreateObjectMetadata();
        var response = new VesselStream(stream, metadata.Hash, metadata.OriginalSize, new ObjectInfo(metadata.FileName, metadata.Extension, metadata.MimeType));

        response.Dispose();
        var exception = await Record.ExceptionAsync(async () => await response.DisposeAsync());

        Assert.Null(exception);
    }

    private sealed class NonSeekableMemoryStream : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        public override bool CanSeek => false;

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static ObjectMetadata CreateObjectMetadata()
    {
        return new ObjectMetadata(
            Key: "test-key",
            Hash: "test-hash",
            OriginalSize: 1024L,
            CompressionType: CompressionType.None,
            ETag: "test-etag",
            CreationDate: DateTimeOffset.UtcNow,
            FileName: "test.txt",
            Extension: ".txt",
            MimeType: "text/plain",
            CompressedSize: 1024L);
    }
}
