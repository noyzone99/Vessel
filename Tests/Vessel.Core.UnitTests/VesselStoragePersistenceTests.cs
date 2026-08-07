using Moq;
using System.Text;
using Vessel.Logging;

namespace Vessel.Core.UnitTests;

/// <summary>
/// Tests for VesselStorage persistence when switching metadata compression between runs. Verifies that the storage
/// layer correctly handles compression mode changes.
/// </summary>
public sealed class VesselStoragePersistenceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IVesselLogger> _mockLogger;

    public VesselStoragePersistenceTests()
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
    public async Task SwitchFromUncompressedToCompressed_RetrievesExistingObjects()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content 1"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content 2"));
            await storage.PutAsync("key1", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("key2", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var response1 = storage.GetStream("key1");
            var response2 = storage.GetStream("key2");

            using var reader1 = new StreamReader(response1);
            using var reader2 = new StreamReader(response2);
            Assert.Equal("Content 1", await reader1.ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Content 2", await reader2.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task SwitchFromCompressedToUncompressed_RetrievesExistingObjects()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content A"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content B"));
            await storage.PutAsync("keyA", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("keyB", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            var response1 = storage.GetStream("keyA");
            var response2 = storage.GetStream("keyB");

            using var reader1 = new StreamReader(response1);
            using var reader2 = new StreamReader(response2);
            Assert.Equal("Content A", await reader1.ReadToEndAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Content B", await reader2.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task MultipleCompressionSwitches_GetAllKeysReturnsAllObjects()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Uncompressed content"));
            await storage.PutAsync("uncompressed-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Compressed content"));
            await storage.PutAsync("compressed-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Final content"));
            await storage.PutAsync("final-key", stream, cancellationToken: TestContext.Current.CancellationToken);

            var keys = new List<string>();
            keys.AddRange(storage.GetAllKeys());

            Assert.Equal(3, keys.Count);
            Assert.Contains("uncompressed-key", keys);
            Assert.Contains("compressed-key", keys);
            Assert.Contains("final-key", keys);
        }
    }

    [Fact]
    public async Task SwitchCompression_DeleteWorks()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Content to delete"));
            await storage.PutAsync("delete-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var deleted = await storage.DeleteAsync("delete-key", TestContext.Current.CancellationToken);
            Assert.True(deleted);

            Assert.Throws<FileNotFoundException>(() =>
                storage.GetStream("delete-key"));
        }
    }

    [Fact]
    public async Task SwitchCompression_UpdateExistingObject()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Original content"));
            await storage.PutAsync("update-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Updated content"));
            var updated = await storage.PutAsync("update-key", stream, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

            var response = storage.GetStream("update-key");
            using var reader = new StreamReader(response);
            var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Updated content", content);
        }
    }

    [Fact]
    public async Task SwitchCompression_GetMetadataWorks()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Test content"));
            await storage.PutAsync("metadata-key", stream, fileInfo: new ObjectInfo(FileName: "test.txt", Extension: ".txt", MimeType: "text/plain"), cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var metadata = storage.GetMetadata("metadata-key");

            Assert.NotNull(metadata);
            Assert.Equal("metadata-key", metadata.Value.Key);
            Assert.Equal("test.txt", metadata.Value.FileName);
            Assert.Equal(".txt", metadata.Value.Extension);
            Assert.Equal("text/plain", metadata.Value.MimeType);
        }
    }

    [Fact]
    public async Task SwitchCompression_LargeDataset_AllObjectsAccessible()
    {
        const int objectCount = 50;

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            for (int i = 0; i < objectCount / 2; i++)
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"Content {i}"));
                await storage.PutAsync($"key-{i}", stream, cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            for (int i = objectCount / 2; i < objectCount; i++)
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"Content {i}"));
                await storage.PutAsync($"key-{i}", stream, cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            var keys = new List<string>();
            keys.AddRange(storage.GetAllKeys());

            Assert.Equal(objectCount, keys.Count);

            for (int i = 0; i < objectCount; i++)
            {
                var response = storage.GetStream($"key-{i}");

                using var reader = new StreamReader(response);
                var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                Assert.Equal($"Content {i}", content);
            }
        }
    }

    [Fact]
    public async Task SwitchCompression_PrefixQuery_WorksCorrectly()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content 1"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content 2"));
            await storage.PutAsync("users/john", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("users/jane", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Admin content"));
            await storage.PutAsync("admins/alice", stream, cancellationToken: TestContext.Current.CancellationToken);

            var userKeys = new List<string>();
            userKeys.AddRange(storage.GetAllKeys(prefix: "users/"));

            Assert.Equal(2, userKeys.Count);
            Assert.Contains("users/john", userKeys);
            Assert.Contains("users/jane", userKeys);
            Assert.DoesNotContain("admins/alice", userKeys);
        }
    }

    [Fact]
    public async Task SwitchCompression_BlobDeduplication_StillWorks()
    {
        const string duplicateContent = "Duplicate content for testing";

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(duplicateContent));
            await storage.PutAsync("key1", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(duplicateContent));
            var metadata2 = await storage.PutAsync("key2", stream, cancellationToken: TestContext.Current.CancellationToken);

            var metadata1 = storage.GetMetadata("key1");
            Assert.NotNull(metadata1);
            Assert.Equal(metadata1.Value.Hash, metadata2.Hash);

            var blobsPath = Path.Combine(_tempDirectory, "blobs");
            if (Directory.Exists(blobsPath))
            {
                var blobFiles = Directory.GetFiles(blobsPath, "*", SearchOption.AllDirectories);
                Assert.Single(blobFiles);
            }
        }
    }

    [Theory]
    [InlineData(CompressionType.Zstd)]
    [InlineData(CompressionType.LZ4)]
    public async Task PutAsync_SameContentDifferentCompressionTypes_StoresSeparateBlobsAndReadsCorrectly(CompressionType compressionType)
    {
        const string content = "Shared content for compression collision testing";
        ObjectMetadata? uncompressedMetadata = null;

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                uncompressedMetadata = await storage.PutAsync("shared-content", stream, cancellationToken: TestContext.Current.CancellationToken);
            }

            await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                var compressedMetadata = await storage.PutAsync("shared-content-compressed", stream, compressionType: compressionType, cancellationToken: TestContext.Current.CancellationToken);
                Assert.Equal(compressionType, compressedMetadata.CompressionType);
                Assert.NotNull(uncompressedMetadata);
                Assert.NotEqual(uncompressedMetadata.Value.Hash, compressedMetadata.Hash);
            }
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var uncompressedResponse = storage.GetStream("shared-content");
            var compressedResponse = storage.GetStream("shared-content-compressed");

            using var uncompressedReader = new StreamReader(uncompressedResponse);
            using var compressedReader = new StreamReader(compressedResponse);

            var uncompressedText = await uncompressedReader.ReadToEndAsync(TestContext.Current.CancellationToken);
            var compressedText = await compressedReader.ReadToEndAsync(TestContext.Current.CancellationToken);

            Assert.Equal(content, uncompressedText);
            Assert.Equal(content, compressedText);
        }
    }

    [Fact]
    public async Task SwitchCompression_WithDifferentBlobCompressionTypes_AllRetrievable()
    {
        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(new string('A', 5000)));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(new string('B', 5000)));
            await storage.PutAsync("key1", stream1, compressionType: CompressionType.Zstd, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("key2", stream2, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);
        }

        using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var response1 = storage.GetStream("key1");
            var response2 = storage.GetStream("key2");

            using var reader1 = new StreamReader(response1);
            using var reader2 = new StreamReader(response2);

            var content1 = await reader1.ReadToEndAsync(TestContext.Current.CancellationToken);
            var content2 = await reader2.ReadToEndAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new string('A', 5000), content1);
            Assert.Equal(new string('B', 5000), content2);
        }
    }
}
