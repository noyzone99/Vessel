using Moq;
using System.Text;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Models;

namespace Vessel.Core.UnitTests.Metadata;

/// <summary>
/// Tests for MetadataRepository with metadata compression enabled.
/// Verifies that the repository works correctly when using RocksDB's LZ4 compression.
/// </summary>
public sealed class MetadataRepositoryCompressionTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly Mock<IVesselLogger> _mockLogger;

    public MetadataRepositoryCompressionTests()
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
    public async Task GetAsync_WithCompression_StoresAndRetrievesMetadata()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var metadata = CreateTestMetadata("compressed-key", "compressed-hash");

        repository.Upsert(metadata);
        var result = repository.Get("compressed-key");

        Assert.NotNull(result);
        Assert.Equal("compressed-key", result.Value.Key);
        Assert.Equal("compressed-hash", result.Value.Hash);
    }

    [Fact]
    public async Task UpsertAsync_WithCompression_StoresMultipleEntries()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 100)
            .Select(i => CreateTestMetadata($"compressed-key-{i}", $"hash-{i}"))
            .ToList();

        foreach (var metadata in metadataList)
        {
            repository.Upsert(metadata);
        }

        foreach (var metadata in metadataList)
        {
            var result = repository.Get(metadata.Key);
            Assert.NotNull(result);
            Assert.Equal(metadata.Key, result.Value.Key);
            Assert.Equal(metadata.Hash, result.Value.Hash);
        }
    }

    [Fact]
    public async Task DeleteAsync_WithCompression_DeletesAndReturnsTrue()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var metadata = CreateTestMetadata("delete-compressed-key", "delete-compressed-hash");
        repository.Upsert(metadata);

        var deleteResult = repository.Delete("delete-compressed-key");

        Assert.True(deleteResult);
        var getResult = repository.Get("delete-compressed-key");
        Assert.Null(getResult);
    }

    [Fact]
    public async Task IsHashReferencedAsync_WithCompression_FindsHash()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var metadata = CreateTestMetadata("hash-ref-key", "unique-compressed-hash");
        repository.Upsert(metadata);

        var result = repository.IsHashReferenced("unique-compressed-hash");

        Assert.True(result);
    }

    [Fact]
    public async Task IterateKeysAsync_WithCompression_ReturnsAllKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
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
    }

    [Fact]
    public async Task IterateKeysAsync_WithCompression_ReturnsExpectedKeys()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var metadataList = Enumerable.Range(1, 50)
            .Select(i => CreateTestMetadata($"count-key-{i}", $"hash-{i}"))
            .ToList();

        foreach (var metadata in metadataList)
        {
            repository.Upsert(metadata);
        }

        var keys = new List<string>();
        await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
        {
            keys.Add(key);
        }

        Assert.Equal(50, keys.Count);
        Assert.Contains("count-key-1", keys);
        Assert.Contains("count-key-50", keys);
    }

    [Fact]
    public async Task ConcurrentOperations_WithCompression_MaintainDataIntegrity()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        const int operationCount = 50;

        var writeTasks = Enumerable.Range(1, operationCount)
            .Select(i => Task.Run(() => repository.Upsert(CreateTestMetadata($"concurrent-{i}", $"hash-{i}"))))
            .ToArray();
        await Task.WhenAll(writeTasks);

        var readTasks = Enumerable.Range(1, operationCount)
            .Select(i => Task.Run(() => repository.Get($"concurrent-{i}")))
            .ToArray();
        var results = await Task.WhenAll(readTasks);

        Assert.All(results, result => Assert.NotNull(result));
        for (int i = 0; i < operationCount; i++)
        {
            Assert.Equal($"concurrent-{i + 1}", results[i]!.Value.Key);
        }
    }

    [Fact]
    public async Task LargeMetadata_WithCompression_StoresAndRetrievesCorrectly()
    {
        using var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object);
        var largeFileName = new string('A', 1000);
        var metadata = new ObjectMetadata(
            Key: "large-metadata-key",
            Hash: "large-hash",
            Size: 10_000_000,
            CompressionType: CompressionType.None,
            ETag: "large-etag",
            LastModified: DateTimeOffset.UtcNow,
            FileName: largeFileName,
            Extension: ".bin",
            MimeType: "application/octet-stream",
            CompressedSize: 10_000_000
        );

        repository.Upsert(metadata);
        var result = repository.Get("large-metadata-key");

        Assert.NotNull(result);
        Assert.Equal(largeFileName, result.Value.FileName);
        Assert.Equal(10_000_000, result.Value.Size);
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
}
