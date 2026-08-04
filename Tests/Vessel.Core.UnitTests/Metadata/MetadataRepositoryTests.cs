using Moq;
using System.Text;
using System.Text.Json;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Models;

namespace Vessel.Core.UnitTests.Metadata;

public sealed class MetadataRepositoryTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly Mock<IVesselLogger> _mockLogger;

    public MetadataRepositoryTests()
    {
        _testStoragePath = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid()}");
        _mockLogger = new Mock<IVesselLogger>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStoragePath))
        {
            try
            {
                Directory.Delete(_testStoragePath, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Constructor_WithValidPath_CreatesRepository()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.NotNull(repository);
        Assert.True(Directory.Exists(Path.Combine(_testStoragePath, "metadata")));
    }

    [Fact]
    public void Constructor_WithNullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataRepository(null!, false, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithEmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MetadataRepository(string.Empty, false, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithWhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MetadataRepository("   ", false, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithoutLogger_CreatesRepository()
    {
        using var repository = new MetadataRepository(_testStoragePath, false);

        Assert.NotNull(repository);
        Assert.True(Directory.Exists(Path.Combine(_testStoragePath, "metadata")));
    }

    [Fact]
    public void Constructor_WithRelativePath_CreatesRepository()
    {
        var relativePath = Path.Combine(_testStoragePath, "relative");

        using var repository = new MetadataRepository(relativePath, false, _mockLogger.Object);

        Assert.NotNull(repository);
        Assert.True(Directory.Exists(Path.Combine(Path.GetFullPath(relativePath), "metadata")));
    }

    [Fact]
    public async Task GetAsync_WithExistingKey_ReturnsMetadata()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("test-key", "test-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);

        var result = await repository.GetAsync("test-key", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("test-key", result.Value.Key);
        Assert.Equal("test-hash", result.Value.Hash);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Metadata lookup hit"))), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WithNonExistingKey_ReturnsNull()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = await repository.GetAsync("non-existing-key", CancellationToken.None);

        Assert.Null(result);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Metadata lookup miss"))), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WithWhitespaceKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_ConcurrentReads_ReturnCorrectData()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");
        await repository.UpsertAsync(metadata1, CancellationToken.None);
        await repository.UpsertAsync(metadata2, CancellationToken.None);
        await repository.UpsertAsync(metadata3, CancellationToken.None);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var result1 = await repository.GetAsync("key1", CancellationToken.None);
            var result2 = await repository.GetAsync("key2", CancellationToken.None);
            var result3 = await repository.GetAsync("key3", CancellationToken.None);
            return (result1, result2, result3);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result =>
        {
            Assert.NotNull(result.result1);
            Assert.Equal("key1", result.result1.Value.Key);
            Assert.Equal("hash1", result.result1.Value.Hash);
            Assert.NotNull(result.result2);
            Assert.Equal("key2", result.result2.Value.Key);
            Assert.Equal("hash2", result.result2.Value.Hash);
            Assert.NotNull(result.result3);
            Assert.Equal("key3", result.result3.Value.Key);
            Assert.Equal("hash3", result.result3.Value.Hash);
        });
    }

    [Fact]
    public async Task UpsertAsync_WithNewMetadata_StoresMetadata()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("new-key", "new-hash");

        await repository.UpsertAsync(metadata, CancellationToken.None);

        var result = await repository.GetAsync("new-key", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("new-key", result.Value.Key);
        Assert.Equal("new-hash", result.Value.Hash);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Stored metadata"))), Times.Once);
    }

    [Fact]
    public async Task UpsertAsync_WithExistingKey_UpdatesMetadata()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var originalMetadata = CreateTestMetadata("update-key", "original-hash", 100);
        var updatedMetadata = CreateTestMetadata("update-key", "updated-hash", 200);
        await repository.UpsertAsync(originalMetadata, CancellationToken.None);

        await repository.UpsertAsync(updatedMetadata, CancellationToken.None);

        var result = await repository.GetAsync("update-key", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("update-key", result.Value.Key);
        Assert.Equal("updated-hash", result.Value.Hash);
        Assert.Equal(200, result.Value.Size);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Stored metadata"))), Times.Exactly(2));
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentWrites_AllSucceed()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 20).Select(i => CreateTestMetadata($"concurrent-key-{i}", $"hash-{i}")).ToList();

        var tasks = metadataList.Select(m => repository.UpsertAsync(m, CancellationToken.None)).ToArray();
        await Task.WhenAll(tasks);

        foreach (var metadata in metadataList)
        {
            var result = await repository.GetAsync(metadata.Key, CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(metadata.Key, result.Value.Key);
            Assert.Equal(metadata.Hash, result.Value.Hash);
        }
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentWritesToSameKey_LastWriteWins()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        const string key = "same-key";

        var tasks = Enumerable.Range(1, 10).Select(i =>
            repository.UpsertAsync(CreateTestMetadata(key, $"hash-{i}", i * 100), CancellationToken.None)
        ).ToArray();
        await Task.WhenAll(tasks);

        var result = await repository.GetAsync(key, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(key, result.Value.Key);
        Assert.Contains(result.Value.Hash, Enumerable.Range(1, 10).Select(i => $"hash-{i}"));
    }

    [Fact]
    public async Task DeleteAsync_WithExistingKey_DeletesAndReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("delete-key", "delete-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);

        var result = await repository.DeleteAsync("delete-key", CancellationToken.None);

        Assert.True(result);
        var getResult = await repository.GetAsync("delete-key", CancellationToken.None);
        Assert.Null(getResult);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Information, It.Is<string>(s => s.Contains("Deleted metadata"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingKey_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = await repository.DeleteAsync("non-existing-key", CancellationToken.None);

        Assert.False(result);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Delete ignored"))), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithNullKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.DeleteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.DeleteAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WithWhitespaceKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.DeleteAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("delete-twice-key", "delete-twice-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);
        await repository.DeleteAsync("delete-twice-key", CancellationToken.None);

        var result = await repository.DeleteAsync("delete-twice-key", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithExistingHash_ReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("key-with-hash", "unique-hash-123");
        await repository.UpsertAsync(metadata, CancellationToken.None);

        var result = await repository.IsHashReferencedAsync("unique-hash-123", CancellationToken.None);

        Assert.True(result);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Found metadata reference"))), Times.Once);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithNonExistingHash_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = await repository.IsHashReferencedAsync("non-existing-hash", CancellationToken.None);

        Assert.False(result);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("No metadata references found"))), Times.Once);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithMultipleMetadataDifferentHashes_FindsCorrectHash()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");
        await repository.UpsertAsync(metadata1, CancellationToken.None);
        await repository.UpsertAsync(metadata2, CancellationToken.None);
        await repository.UpsertAsync(metadata3, CancellationToken.None);

        var result = await repository.IsHashReferencedAsync("hash2", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithMultipleMetadataSameHash_ReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "shared-hash");
        var metadata2 = CreateTestMetadata("key2", "shared-hash");
        var metadata3 = CreateTestMetadata("key3", "shared-hash");
        await repository.UpsertAsync(metadata1, CancellationToken.None);
        await repository.UpsertAsync(metadata2, CancellationToken.None);
        await repository.UpsertAsync(metadata3, CancellationToken.None);

        var result = await repository.IsHashReferencedAsync("shared-hash", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_AfterDelete_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("temp-key", "temp-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);
        await repository.DeleteAsync("temp-key", CancellationToken.None);

        var result = await repository.IsHashReferencedAsync("temp-hash", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithNullHash_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.IsHashReferencedAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithEmptyHash_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.IsHashReferencedAsync(string.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithWhitespaceHash_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.IsHashReferencedAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task IsHashReferencedAsync_ConcurrentReads_ReturnConsistentResults()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "concurrent-hash-1");
        var metadata2 = CreateTestMetadata("key2", "concurrent-hash-2");
        await repository.UpsertAsync(metadata1, CancellationToken.None);
        await repository.UpsertAsync(metadata2, CancellationToken.None);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var result1 = await repository.IsHashReferencedAsync("concurrent-hash-1", CancellationToken.None);
            var result2 = await repository.IsHashReferencedAsync("concurrent-hash-2", CancellationToken.None);
            var result3 = await repository.IsHashReferencedAsync("non-existing", CancellationToken.None);
            return (result1, result2, result3);
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result =>
        {
            Assert.True(result.result1);
            Assert.True(result.result2);
            Assert.False(result.result3);
        });
    }

    [Fact]
    public async Task Integration_UpsertGetDeleteGetIsHashReferenced_WorksCorrectly()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("integration-key", "integration-hash");

        await repository.UpsertAsync(metadata, CancellationToken.None);

        var getResult = await repository.GetAsync("integration-key", CancellationToken.None);
        Assert.NotNull(getResult);
        Assert.Equal("integration-key", getResult.Value.Key);

        var isReferencedBefore = await repository.IsHashReferencedAsync("integration-hash", CancellationToken.None);
        Assert.True(isReferencedBefore);

        var deleteResult = await repository.DeleteAsync("integration-key", CancellationToken.None);
        Assert.True(deleteResult);

        var getAfterDelete = await repository.GetAsync("integration-key", CancellationToken.None);
        Assert.Null(getAfterDelete);

        var isReferencedAfter = await repository.IsHashReferencedAsync("integration-hash", CancellationToken.None);
        Assert.False(isReferencedAfter);
    }

    [Fact]
    public async Task Integration_ConcurrentWritesAndReads_MaintainDataIntegrity()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        const int writeCount = 50;
        const int readCount = 50;

        var writeTasks = Enumerable.Range(1, writeCount).Select(i =>
            repository.UpsertAsync(CreateTestMetadata($"mixed-key-{i}", $"mixed-hash-{i}"), CancellationToken.None)
        ).ToArray();

        await Task.WhenAll(writeTasks);

        var readTasks = Enumerable.Range(1, readCount).Select(i =>
            repository.GetAsync($"mixed-key-{i}", CancellationToken.None)
        ).ToArray();

        var results = await Task.WhenAll(readTasks);

        Assert.Equal(readCount, results.Length);
        for (int i = 0; i < readCount; i++)
        {
            Assert.NotNull(results[i]);
            Assert.Equal($"mixed-key-{i + 1}", results[i]!.Value.Key);
            Assert.Equal($"mixed-hash-{i + 1}", results[i]!.Value.Hash);
        }
    }

    private static ObjectMetadata CreateTestMetadata(string key, string hash, long size = 1000)
    {
        return new ObjectMetadata(
            Key: key,
            Hash: hash,
            Size: size,
            CompressionType: CompressionType.None,
            ETag: $"etag-{key}",
            LastModified: DateTimeOffset.UtcNow,
            FileName: $"{key}.txt",
            Extension: ".txt",
            MimeType: "text/plain",
            CompressedSize: size
        );
    }

    [Fact]
    public async Task GetCountAsync_WithEmptyRepository_ReturnsZero()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var count = await repository.GetCountAsync(CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetCountAsync_WithSingleEntry_ReturnsOne()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("single-key", "single-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);

        var count = await repository.GetCountAsync(CancellationToken.None);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetCountAsync_WithMultipleEntries_ReturnsCorrectCount()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 10).Select(i => CreateTestMetadata($"key-{i}", $"hash-{i}")).ToList();
        foreach (var metadata in metadataList)
        {
            await repository.UpsertAsync(metadata, CancellationToken.None);
        }

        var count = await repository.GetCountAsync(CancellationToken.None);

        Assert.Equal(10, count);
    }

    [Fact]
    public async Task GetCountAsync_AfterDelete_ReturnsCorrectCount()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key3", "hash3"), CancellationToken.None);
        await repository.DeleteAsync("key2", CancellationToken.None);

        var count = await repository.GetCountAsync(CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountAsync_WithUpdatedEntry_ReturnsCorrectCount()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key2", "hash2-updated"), CancellationToken.None);

        var count = await repository.GetCountAsync(CancellationToken.None);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetCountAsync_ConcurrentReads_ReturnConsistentCount()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 5).Select(i => CreateTestMetadata($"key-{i}", $"hash-{i}")).ToList();
        foreach (var metadata in metadataList)
        {
            await repository.UpsertAsync(metadata, CancellationToken.None);
        }

        var tasks = Enumerable.Range(0, 10).Select(_ => repository.GetCountAsync(CancellationToken.None)).ToArray();
        var counts = await Task.WhenAll(tasks);

        Assert.All(counts, count => Assert.Equal(5, count));
    }

    [Fact]
    public async Task WriteBatchAsync_WithNullBatchAction_ThrowsArgumentNullException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.WriteBatchAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task WriteBatchAsync_WithSingleWrite_StoresData()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("batch-key", "batch-hash");

        await repository.WriteBatchAsync(batch =>
        {
            var keyBytes = Encoding.UTF8.GetBytes(metadata.Key);
            var valueBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
            batch.Put(keyBytes, valueBytes);
        }, CancellationToken.None);

        var result = await repository.GetAsync("batch-key", CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal("batch-key", result.Value.Key);
        Assert.Equal("batch-hash", result.Value.Hash);
    }

    [Fact]
    public async Task WriteBatchAsync_WithMultipleWrites_StoresAllData()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 5).Select(i => CreateTestMetadata($"batch-key-{i}", $"batch-hash-{i}")).ToList();

        await repository.WriteBatchAsync(batch =>
        {
            foreach (var metadata in metadataList)
            {
                var keyBytes = Encoding.UTF8.GetBytes(metadata.Key);
                var valueBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
                batch.Put(keyBytes, valueBytes);
            }
        }, CancellationToken.None);

        foreach (var metadata in metadataList)
        {
            var result = await repository.GetAsync(metadata.Key, CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(metadata.Key, result.Value.Key);
            Assert.Equal(metadata.Hash, result.Value.Hash);
        }
    }

    [Fact]
    public async Task WriteBatchAsync_WithDelete_DeletesData()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("batch-delete-key", "batch-delete-hash");
        await repository.UpsertAsync(metadata, CancellationToken.None);

        await repository.WriteBatchAsync(batch =>
        {
            var keyBytes = Encoding.UTF8.GetBytes(metadata.Key);
            batch.Delete(keyBytes);
        }, CancellationToken.None);

        var result = await repository.GetAsync("batch-delete-key", CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task WriteBatchAsync_WithMixedOperations_AppliesAllOperations()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("existing-key", "existing-hash"), CancellationToken.None);
        var newMetadata = CreateTestMetadata("new-key", "new-hash");

        await repository.WriteBatchAsync(batch =>
        {
            batch.Delete(Encoding.UTF8.GetBytes("existing-key"));
            var keyBytes = Encoding.UTF8.GetBytes(newMetadata.Key);
            var valueBytes = JsonSerializer.SerializeToUtf8Bytes(newMetadata);
            batch.Put(keyBytes, valueBytes);
        }, CancellationToken.None);

        var existingResult = await repository.GetAsync("existing-key", CancellationToken.None);
        Assert.Null(existingResult);
        var newResult = await repository.GetAsync("new-key", CancellationToken.None);
        Assert.NotNull(newResult);
        Assert.Equal("new-key", newResult.Value.Key);
    }

    [Fact]
    public async Task WriteBatchAsync_ConcurrentBatches_AllSucceed()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var tasks = Enumerable.Range(1, 10).Select(i => repository.WriteBatchAsync(batch =>
        {
            var metadata = CreateTestMetadata($"concurrent-batch-key-{i}", $"concurrent-batch-hash-{i}");
            var keyBytes = Encoding.UTF8.GetBytes(metadata.Key);
            var valueBytes = JsonSerializer.SerializeToUtf8Bytes(metadata);
            batch.Put(keyBytes, valueBytes);
        }, CancellationToken.None)).ToArray();
        await Task.WhenAll(tasks);

        for (int i = 1; i <= 10; i++)
        {
            var result = await repository.GetAsync($"concurrent-batch-key-{i}", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal($"concurrent-batch-key-{i}", result.Value.Key);
        }
    }

    [Fact]
    public async Task IterateKeysAsync_WithEmptyRepository_ReturnsNoKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Empty(keys);
    }

    [Fact]
    public async Task IterateKeysAsync_WithEmptyPrefix_ReturnsAllKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key3", "hash3"), CancellationToken.None);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Equal(3, keys.Count);
        Assert.Contains("key1", keys);
        Assert.Contains("key2", keys);
        Assert.Contains("key3", keys);
    }

    [Fact]
    public async Task IterateKeysAsync_WithMatchingPrefix_ReturnsMatchingKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("prefix-1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("prefix-2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("other-1", "hash3"), CancellationToken.None);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("prefix-", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Equal(2, keys.Count);
        Assert.Contains("prefix-1", keys);
        Assert.Contains("prefix-2", keys);
        Assert.DoesNotContain("other-1", keys);
    }

    [Fact]
    public async Task IterateKeysAsync_WithNonMatchingPrefix_ReturnsNoKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("nonexistent-", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Empty(keys);
    }

    [Fact]
    public async Task IterateKeysAsync_WithCancellationToken_ThrowsOnCancellation()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 100).Select(i => CreateTestMetadata($"cancel-key-{i}", $"hash-{i}")).ToList();
        foreach (var metadata in metadataList)
        {
            await repository.UpsertAsync(metadata, CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var keys = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var key in repository.IterateKeysAsync("cancel-", cts.Token))
            {
                keys.Add(key);
                if (keys.Count >= 5)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(keys.Count >= 5);
    }

    [Fact]
    public async Task IterateKeysAsync_LogsDebugMessages_ForIterationStartAndCompletion()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("test-key", "test-hash"), CancellationToken.None);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("test-", CancellationToken.None))
        {
            keys.Add(key);
        }

        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Iterating keys with prefix"))), Times.AtLeastOnce);
        _mockLogger.Verify(l => l.Log(VesselLogLevel.Debug, It.Is<string>(s => s.Contains("Completed key iteration"))), Times.AtLeastOnce);
    }

    [Fact]
    public async Task IterateKeysAsync_WithPartialPrefix_ReturnsMatchingKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("user/john/file1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("user/john/file2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("user/jane/file1", "hash3"), CancellationToken.None);

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("user/john/", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Equal(2, keys.Count);
        Assert.Contains("user/john/file1", keys);
        Assert.Contains("user/john/file2", keys);
        Assert.DoesNotContain("user/jane/file1", keys);
    }

    [Fact]
    public async Task IterateKeysAsync_ConcurrentIterations_EachReturnsCorrectKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("a-key-1", "hash1"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("a-key-2", "hash2"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("b-key-1", "hash3"), CancellationToken.None);
        await repository.UpsertAsync(CreateTestMetadata("b-key-2", "hash4"), CancellationToken.None);

        var tasks = new[]
        {
            Task.Run(async () =>
            {
                var keys = new List<string>();
                await foreach (var key in repository.IterateKeysAsync("a-", CancellationToken.None))
                {
                    keys.Add(key);
                }
                return keys;
            }),
            Task.Run(async () =>
            {
                var keys = new List<string>();
                await foreach (var key in repository.IterateKeysAsync("b-", CancellationToken.None))
                {
                    keys.Add(key);
                }
                return keys;
            })
        };

        var results = await Task.WhenAll(tasks);

        Assert.Equal(2, results[0].Count);
        Assert.Contains("a-key-1", results[0]);
        Assert.Contains("a-key-2", results[0]);
        Assert.Equal(2, results[1].Count);
        Assert.Contains("b-key-1", results[1]);
        Assert.Contains("b-key-2", results[1]);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        repository.Dispose();
        repository.Dispose();
        repository.Dispose();
    }

    [Fact]
    public async Task Dispose_AfterOperations_CompletesWithoutError()
    {
        var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        await repository.UpsertAsync(CreateTestMetadata("dispose-key", "dispose-hash"), CancellationToken.None);
        var result = await repository.GetAsync("dispose-key", CancellationToken.None);
        Assert.NotNull(result);

        repository.Dispose();
    }
}

