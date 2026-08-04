using Moq;
using System.Text;
using Vessel.Logging;
using Vessel.Models;

namespace Vessel.Core.UnitTests;

/// <summary>
/// Tests for VesselStorage persistence when switching metadata compression between runs.
/// Verifies that the storage layer correctly handles compression mode changes.
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
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content 1"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content 2"));
            await storage.PutAsync("key1", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("key2", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var response1 = await storage.GetAsync("key1", TestContext.Current.CancellationToken);
            var response2 = await storage.GetAsync("key2", TestContext.Current.CancellationToken);

            Assert.Equal("key1", response1.Metadata.Key);
            Assert.Equal("key2", response2.Metadata.Key);

            using var reader1 = new StreamReader(response1.ContentStream);
            using var reader2 = new StreamReader(response2.ContentStream);
            Assert.Equal("Content 1", await reader1.ReadToEndAsync());
            Assert.Equal("Content 2", await reader2.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task SwitchFromCompressedToUncompressed_RetrievesExistingObjects()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content A"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content B"));
            await storage.PutAsync("keyA", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("keyB", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            var response1 = await storage.GetAsync("keyA", TestContext.Current.CancellationToken);
            var response2 = await storage.GetAsync("keyB", TestContext.Current.CancellationToken);

            Assert.Equal("keyA", response1.Metadata.Key);
            Assert.Equal("keyB", response2.Metadata.Key);

            using var reader1 = new StreamReader(response1.ContentStream);
            using var reader2 = new StreamReader(response2.ContentStream);
            Assert.Equal("Content A", await reader1.ReadToEndAsync());
            Assert.Equal("Content B", await reader2.ReadToEndAsync());
        }
    }

    [Fact]
    public async Task MultipleCompressionSwitches_GetAllKeysReturnsAllObjects()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Uncompressed content"));
            await storage.PutAsync("uncompressed-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Compressed content"));
            await storage.PutAsync("compressed-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Final content"));
            await storage.PutAsync("final-key", stream, cancellationToken: TestContext.Current.CancellationToken);

            var keys = new List<string>();
            await foreach (var key in storage.GetAllKeysAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                keys.Add(key);
            }

            Assert.Equal(3, keys.Count);
            Assert.Contains("uncompressed-key", keys);
            Assert.Contains("compressed-key", keys);
            Assert.Contains("final-key", keys);
        }
    }

    [Fact]
    public async Task SwitchCompression_DeleteWorks()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Content to delete"));
            await storage.PutAsync("delete-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var deleted = await storage.DeleteAsync("delete-key", TestContext.Current.CancellationToken);
            Assert.True(deleted);

            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                storage.GetAsync("delete-key", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task SwitchCompression_UpdateExistingObject()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Original content"));
            await storage.PutAsync("update-key", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Updated content"));
            var updated = await storage.PutAsync("update-key", stream, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

            var response = await storage.GetAsync("update-key", TestContext.Current.CancellationToken);
            using var reader = new StreamReader(response.ContentStream);
            var content = await reader.ReadToEndAsync();

            Assert.Equal("Updated content", content);
        }
    }

    [Fact]
    public async Task SwitchCompression_GetMetadataWorks()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Test content"));
            await storage.PutAsync("metadata-key", stream, fileName: "test.txt", extension: ".txt", mimeType: "text/plain", cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var metadata = await storage.GetMetadataAsync("metadata-key", TestContext.Current.CancellationToken);

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

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            for (int i = 0; i < objectCount / 2; i++)
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"Content {i}"));
                await storage.PutAsync($"key-{i}", stream, cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            for (int i = objectCount / 2; i < objectCount; i++)
            {
                await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"Content {i}"));
                await storage.PutAsync($"key-{i}", stream, cancellationToken: TestContext.Current.CancellationToken);
            }
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            var keys = new List<string>();
            await foreach (var key in storage.GetAllKeysAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                keys.Add(key);
            }

            Assert.Equal(objectCount, keys.Count);

            for (int i = 0; i < objectCount; i++)
            {
                var response = await storage.GetAsync($"key-{i}", TestContext.Current.CancellationToken);
                Assert.Equal($"key-{i}", response.Metadata.Key);

                using var reader = new StreamReader(response.ContentStream);
                var content = await reader.ReadToEndAsync();
                Assert.Equal($"Content {i}", content);
            }
        }
    }

    [Fact]
    public async Task SwitchCompression_PrefixQuery_WorksCorrectly()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("Content 1"));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("Content 2"));
            await storage.PutAsync("users/john", stream1, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("users/jane", stream2, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Admin content"));
            await storage.PutAsync("admins/alice", stream, cancellationToken: TestContext.Current.CancellationToken);

            var userKeys = new List<string>();
            await foreach (var key in storage.GetAllKeysAsync(prefix: "users/", cancellationToken: TestContext.Current.CancellationToken))
            {
                userKeys.Add(key);
            }

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

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(duplicateContent));
            await storage.PutAsync("key1", stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(duplicateContent));
            var metadata2 = await storage.PutAsync("key2", stream, cancellationToken: TestContext.Current.CancellationToken);

            var metadata1 = await storage.GetMetadataAsync("key1", TestContext.Current.CancellationToken);
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

    [Fact]
    public async Task SwitchCompression_WithDifferentBlobCompressionTypes_AllRetrievable()
    {
        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object)))
        {
            await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(new string('A', 5000)));
            await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(new string('B', 5000)));
            await storage.PutAsync("key1", stream1, compressionType: CompressionType.Zstd, cancellationToken: TestContext.Current.CancellationToken);
            await storage.PutAsync("key2", stream2, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);
        }

        await using (var storage = new VesselStorage(_tempDirectory, new VesselStorageOptions(EnableMetadataCompression: true, Logger: _mockLogger.Object)))
        {
            var response1 = await storage.GetAsync("key1", TestContext.Current.CancellationToken);
            var response2 = await storage.GetAsync("key2", TestContext.Current.CancellationToken);

            using var reader1 = new StreamReader(response1.ContentStream);
            using var reader2 = new StreamReader(response2.ContentStream);

            var content1 = await reader1.ReadToEndAsync();
            var content2 = await reader2.ReadToEndAsync();

            Assert.Equal(new string('A', 5000), content1);
            Assert.Equal(new string('B', 5000), content2);
        }
    }
}
