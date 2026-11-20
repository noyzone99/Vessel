using Moq;
using System.Text;
using Vessel.Logging;

namespace Vessel.UnitTests;

/// <summary>
/// Tests for VesselStorage with metadata compression enabled. Verifies end-to-end storage functionality with compressed
/// metadata.
/// </summary>
public sealed class VesselStorageCompressionTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IVesselLogger> _mockLogger;

    public VesselStorageCompressionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        _mockLogger = new Mock<IVesselLogger>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task PutAsync_WithMetadataCompression_StoresAndRetrievesObject()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        const string key = "test-key";
        const string content = "Hello, World!";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var metadata = await storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(key, metadata.Key);
        Assert.NotNull(metadata.Hash);
        Assert.True(metadata.OriginalSize > 0);

        var response = storage.GetStream(key);
        Assert.Equal(metadata.Hash, response.Checksum);
    }

    [Fact]
    public async Task GetAllKeysAsync_WithMetadataCompression_ReturnsAllKeys()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("content1"));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("content2"));
        await using var stream3 = new MemoryStream(Encoding.UTF8.GetBytes("content3"));

        await storage.PutAsync("key1", stream1, cancellationToken: TestContext.Current.CancellationToken);
        await storage.PutAsync("key2", stream2, cancellationToken: TestContext.Current.CancellationToken);
        await storage.PutAsync("key3", stream3, cancellationToken: TestContext.Current.CancellationToken);

        var keys = new List<string>();
        keys.AddRange(storage.GetAllKeys());

        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public async Task DeleteAsync_WithMetadataCompression_DeletesObject()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        await storage.PutAsync("delete-key", stream, cancellationToken: TestContext.Current.CancellationToken);

        var deleted = await storage.DeleteAsync("delete-key", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Throws<FileNotFoundException>(() =>
            storage.GetStream("delete-key"));
    }

    [Fact]
    public async Task PutAsync_WithMetadataCompressionAndOverwrite_UpdatesObject()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        const string key = "overwrite-key";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("original content"));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("updated content"));

        var original = await storage.PutAsync(key, stream1, cancellationToken: TestContext.Current.CancellationToken);
        var updated = await storage.PutAsync(key, stream2, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(original.Hash, updated.Hash);
        Assert.NotEqual(original.OriginalSize, updated.OriginalSize);
    }

    [Fact]
    public async Task GetMetadataAsync_WithMetadataCompression_ReturnsMetadata()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        const string key = "metadata-key";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test content"));
        var stored = await storage.PutAsync(key, stream, fileInfo: new ObjectInfo(FileName: "test.txt", Extension: ".txt", MimeType: "text/plain"), cancellationToken: TestContext.Current.CancellationToken);

        var metadata = storage.GetMetadata(key);

        Assert.NotNull(metadata);
        Assert.Equal(key, metadata.Value.Key);
        Assert.Equal("test.txt", metadata.Value.FileName);
        Assert.Equal(".txt", metadata.Value.Extension);
        Assert.Equal("text/plain", metadata.Value.MimeType);
    }

    [Fact]
    public async Task MultipleOperations_WithMetadataCompression_MaintainConsistency()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        const int operationCount = 50;
        var keys = new List<string>();

        for (int i = 0; i < operationCount; i++)
        {
            var key = $"concurrent-key-{i}";
            keys.Add(key);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{i}"));
            await storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        foreach (var key in keys)
        {
            var metadata = storage.GetMetadata(key);
            Assert.NotNull(metadata);
            Assert.Equal(key, metadata.Value.Key);
        }

        var allKeys = new List<string>();
        allKeys.AddRange(storage.GetAllKeys());
        Assert.Equal(operationCount, allKeys.Count);
    }

    [Fact]
    public async Task PutAsync_WithMetadataCompressionAndDifferentCompressionTypes_StoresCorrectly()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(new string('A', 10000)));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(new string('B', 10000)));
        await using var stream3 = new MemoryStream(Encoding.UTF8.GetBytes(new string('C', 10000)));

        var metadata1 = await storage.PutAsync("key1", stream1, compressionType: CompressionType.None, cancellationToken: TestContext.Current.CancellationToken);
        var metadata2 = await storage.PutAsync("key2", stream2, compressionType: CompressionType.Zstd, cancellationToken: TestContext.Current.CancellationToken);
        var metadata3 = await storage.PutAsync("key3", stream3, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CompressionType.None, metadata1.CompressionType);
        Assert.Equal(CompressionType.Zstd, metadata2.CompressionType);
        Assert.Equal(CompressionType.LZ4, metadata3.CompressionType);

        Assert.True(metadata2.CompressedSize < metadata2.OriginalSize);
        Assert.True(metadata3.CompressedSize < metadata3.OriginalSize);
    }

    [Fact]
    public async Task PrefixQuery_WithMetadataCompression_ReturnsMatchingKeys()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("content1"));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("content2"));
        await using var stream3 = new MemoryStream(Encoding.UTF8.GetBytes("content3"));

        await storage.PutAsync("users/john/file1", stream1, cancellationToken: TestContext.Current.CancellationToken);
        await storage.PutAsync("users/john/file2", stream2, cancellationToken: TestContext.Current.CancellationToken);
        await storage.PutAsync("admins/alice/file1", stream3, cancellationToken: TestContext.Current.CancellationToken);

        var userKeys = new List<string>();
        userKeys.AddRange(storage.GetAllKeys(prefix: "users/"));

        Assert.Equal(2, userKeys.Count);
        Assert.Contains("users/john/file1", userKeys);
        Assert.Contains("users/john/file2", userKeys);
        Assert.DoesNotContain("admins/alice/file1", userKeys);
    }

    [Fact]
    public async Task DuplicateContent_WithMetadataCompression_DeduplicatesBlobs()
    {
        var options = new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object);
        using var storage = new VesselStorage(_tempDirectory, options);

        const string content = "Duplicate content for testing";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var metadata1 = await storage.PutAsync("key1", stream1, cancellationToken: TestContext.Current.CancellationToken);
        var metadata2 = await storage.PutAsync("key2", stream2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(metadata1.Hash, metadata2.Hash);

        var blobsPath = Path.Combine(_tempDirectory, "blobs");
        if (Directory.Exists(blobsPath))
        {
            var blobFiles = Directory.GetFiles(blobsPath, "*", SearchOption.AllDirectories);
            Assert.Single(blobFiles);
        }
    }
}
