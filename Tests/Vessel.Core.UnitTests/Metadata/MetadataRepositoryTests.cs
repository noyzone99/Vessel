using Moq;
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
        repository.Upsert(metadata);

        var result = repository.Get("test-key");

        Assert.NotNull(result);
        Assert.Equal("test-key", result.Value.Key);
        Assert.Equal("test-hash", result.Value.Hash);
    }

    [Fact]
    public async Task GetAsync_WithNonExistingKey_ReturnsNull()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = repository.Get("non-existing-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithNullKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.Throws<ArgumentNullException>(() => repository.Get(null!));
    }

    [Fact]
    public async Task GetAsync_WithEmptyKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.Throws<ArgumentException>(() => repository.Get(string.Empty));
    }

    [Fact]
    public async Task GetAsync_WithWhitespaceKey_ThrowsArgumentException()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.Throws<ArgumentException>(() => repository.Get("   "));
    }

    [Fact]
    public async Task GetAsync_ConcurrentReads_ReturnCorrectData()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");
        repository.Upsert(metadata1);
        repository.Upsert(metadata2);
        repository.Upsert(metadata3);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var result1 = repository.Get("key1");
            var result2 = repository.Get("key2");
            var result3 = repository.Get("key3");
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

        repository.Upsert(metadata);

        var result = repository.Get("new-key");
        Assert.NotNull(result);
        Assert.Equal("new-key", result.Value.Key);
        Assert.Equal("new-hash", result.Value.Hash);
    }

    [Fact]
    public async Task UpsertAsync_WithExistingKey_UpdatesMetadata()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var originalMetadata = CreateTestMetadata("update-key", "original-hash", 100);
        var updatedMetadata = CreateTestMetadata("update-key", "updated-hash", 200);
        repository.Upsert(originalMetadata);

        repository.Upsert(updatedMetadata);

        var result = repository.Get("update-key");
        Assert.NotNull(result);
        Assert.Equal("update-key", result.Value.Key);
        Assert.Equal("updated-hash", result.Value.Hash);
        Assert.Equal(200, result.Value.Size);
    }

    [Fact]
    public async Task UpsertAsync_ConcurrentWrites_AllSucceed()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 20).Select(i => CreateTestMetadata($"concurrent-key-{i}", $"hash-{i}")).ToList();

        var tasks = metadataList.Select(m => Task.Run(() => repository.Upsert(m))).ToArray();
        await Task.WhenAll(tasks);

        foreach (var metadata in metadataList)
        {
            var result = repository.Get(metadata.Key);
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
            Task.Run(() => repository.Upsert(CreateTestMetadata(key, $"hash-{i}", i * 100)))
        ).ToArray();
        await Task.WhenAll(tasks);

        var result = repository.Get(key);
        Assert.NotNull(result);
        Assert.Equal(key, result.Value.Key);
        Assert.Contains(result.Value.Hash, Enumerable.Range(1, 10).Select(i => $"hash-{i}"));
    }

    [Fact]
    public async Task DeleteAsync_WithExistingKey_DeletesAndReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("delete-key", "delete-hash");
        repository.Upsert(metadata);

        var result = repository.Delete("delete-key");

        Assert.True(result);
        var getResult = repository.Get("delete-key");
        Assert.Null(getResult);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingKey_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = repository.Delete("non-existing-key");

        Assert.False(result);
    }

    [Fact]
    public void DeleteAsync_WithNullKey_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.Delete(null!));
    }

    [Fact]
    public void DeleteAsync_WithEmptyKey_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.Delete(string.Empty));
    }

    [Fact]
    public void DeleteAsync_WithWhitespaceKey_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.Delete("   "));
    }

    [Fact]
    public async Task DeleteAsync_AlreadyDeleted_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("delete-twice-key", "delete-twice-hash");
        repository.Upsert(metadata);
        repository.Delete("delete-twice-key");

        var result = repository.Delete("delete-twice-key");

        Assert.False(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithExistingHash_ReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("key-with-hash", "unique-hash-123");
        repository.Upsert(metadata);

        var result = repository.IsHashReferenced("unique-hash-123");

        Assert.True(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithNonExistingHash_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        var result = repository.IsHashReferenced("non-existing-hash");

        Assert.False(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithMultipleMetadataDifferentHashes_FindsCorrectHash()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");
        repository.Upsert(metadata1);
        repository.Upsert(metadata2);
        repository.Upsert(metadata3);

        var result = repository.IsHashReferenced("hash2");

        Assert.True(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithMultipleMetadataSameHash_ReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "shared-hash");
        var metadata2 = CreateTestMetadata("key2", "shared-hash");
        var metadata3 = CreateTestMetadata("key3", "shared-hash");
        repository.Upsert(metadata1);
        repository.Upsert(metadata2);
        repository.Upsert(metadata3);

        var result = repository.IsHashReferenced("shared-hash");

        Assert.True(result);
    }

    [Fact]
    public async Task IsHashReferencedAsync_AfterDelete_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata = CreateTestMetadata("temp-key", "temp-hash");
        repository.Upsert(metadata);
        repository.Delete("temp-key");

        var result = repository.IsHashReferenced("temp-hash");

        Assert.False(result);
    }

    [Fact]
    public void IsHashReferencedAsync_WithNullHash_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.IsHashReferenced(null!));
    }

    [Fact]
    public void IsHashReferencedAsync_WithEmptyHash_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.IsHashReferenced(string.Empty));
    }

    [Fact]
    public void IsHashReferencedAsync_WithWhitespaceHash_ReturnsFalse()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);

        Assert.False(repository.IsHashReferenced("   "));
    }

    [Fact]
    public async Task IsHashReferencedAsync_ConcurrentReads_ReturnConsistentResults()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        var metadata1 = CreateTestMetadata("key1", "concurrent-hash-1");
        var metadata2 = CreateTestMetadata("key2", "concurrent-hash-2");
        repository.Upsert(metadata1);
        repository.Upsert(metadata2);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var result1 = repository.IsHashReferenced("concurrent-hash-1");
            var result2 = repository.IsHashReferenced("concurrent-hash-2");
            var result3 = repository.IsHashReferenced("non-existing");
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

        repository.Upsert(metadata);

        var getResult = repository.Get("integration-key");
        Assert.NotNull(getResult);
        Assert.Equal("integration-key", getResult.Value.Key);

        var isReferencedBefore = repository.IsHashReferenced("integration-hash");
        Assert.True(isReferencedBefore);

        var deleteResult = repository.Delete("integration-key");
        Assert.True(deleteResult);

        var getAfterDelete = repository.Get("integration-key");
        Assert.Null(getAfterDelete);

        var isReferencedAfter = repository.IsHashReferenced("integration-hash");
        Assert.False(isReferencedAfter);
    }

    [Fact]
    public async Task Integration_ConcurrentWritesAndReads_MaintainDataIntegrity()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        const int writeCount = 50;
        const int readCount = 50;

        var writeTasks = Enumerable.Range(1, writeCount).Select(i =>
            Task.Run(() => repository.Upsert(CreateTestMetadata($"mixed-key-{i}", $"mixed-hash-{i}")))
        ).ToArray();

        await Task.WhenAll(writeTasks);

        var readTasks = Enumerable.Range(1, readCount).Select(i =>
            Task.Run(() => repository.Get($"mixed-key-{i}"))
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
    public async Task IterateKeysAsync_AfterDeleteAndUpdate_ReturnsCurrentKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        repository.Upsert(CreateTestMetadata("key1", "hash1"));
        repository.Upsert(CreateTestMetadata("key2", "hash2"));
        repository.Upsert(CreateTestMetadata("key2", "hash2-updated"));
        repository.Delete("key1");

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Single(keys);
        Assert.Equal("key2", keys[0]);
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
        repository.Upsert(CreateTestMetadata("key1", "hash1"));
        repository.Upsert(CreateTestMetadata("key2", "hash2"));
        repository.Upsert(CreateTestMetadata("key3", "hash3"));

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
        repository.Upsert(CreateTestMetadata("prefix-1", "hash1"));
        repository.Upsert(CreateTestMetadata("prefix-2", "hash2"));
        repository.Upsert(CreateTestMetadata("other-1", "hash3"));

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
        repository.Upsert(CreateTestMetadata("key1", "hash1"));
        repository.Upsert(CreateTestMetadata("key2", "hash2"));

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
            repository.Upsert(metadata);
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
        repository.Upsert(CreateTestMetadata("test-key", "test-hash"));

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("test-", CancellationToken.None))
        {
            keys.Add(key);
        }
    }

    [Fact]
    public async Task IterateKeysAsync_WithPartialPrefix_ReturnsMatchingKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        repository.Upsert(CreateTestMetadata("user/john/file1", "hash1"));
        repository.Upsert(CreateTestMetadata("user/john/file2", "hash2"));
        repository.Upsert(CreateTestMetadata("user/jane/file1", "hash3"));

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
        repository.Upsert(CreateTestMetadata("a-key-1", "hash1"));
        repository.Upsert(CreateTestMetadata("a-key-2", "hash2"));
        repository.Upsert(CreateTestMetadata("b-key-1", "hash3"));
        repository.Upsert(CreateTestMetadata("b-key-2", "hash4"));

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
        Assert.Throws<ObjectDisposedException>(() => repository.Dispose());
    }

    [Fact]
    public async Task Dispose_AfterOperations_CompletesWithoutError()
    {
        var repository = new MetadataRepository(_testStoragePath, false, _mockLogger.Object);
        repository.Upsert(CreateTestMetadata("dispose-key", "dispose-hash"));
        var result = repository.Get("dispose-key");
        Assert.NotNull(result);

        repository.Dispose();
    }
}

