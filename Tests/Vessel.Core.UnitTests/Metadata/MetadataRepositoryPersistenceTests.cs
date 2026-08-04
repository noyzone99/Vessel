using Moq;
using System.Text;
using Vessel.Logging;
using Vessel.Metadata;
using Vessel.Models;

namespace Vessel.Core.UnitTests.Metadata;

/// <summary>
/// Tests for MetadataRepository persistence when switching compression modes between runs.
/// Verifies that RocksDB can correctly read data regardless of compression setting changes.
/// </summary>
public sealed class MetadataRepositoryPersistenceTests : IDisposable
{
    private readonly string _testStoragePath;
    private readonly Mock<IVesselLogger> _mockLogger;

    public MetadataRepositoryPersistenceTests()
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
    public async Task SwitchFromUncompressedToCompressed_ReadsExistingData()
    {
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(metadata1, CancellationToken.None);
            await repository.UpsertAsync(metadata2, CancellationToken.None);
            await repository.UpsertAsync(metadata3, CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var result1 = await repository.GetAsync("key1", CancellationToken.None);
            var result2 = await repository.GetAsync("key2", CancellationToken.None);
            var result3 = await repository.GetAsync("key3", CancellationToken.None);

            Assert.NotNull(result1);
            Assert.Equal("key1", result1.Value.Key);
            Assert.Equal("hash1", result1.Value.Hash);
            Assert.NotNull(result2);
            Assert.Equal("key2", result2.Value.Key);
            Assert.NotNull(result3);
            Assert.Equal("key3", result3.Value.Key);
        }
    }

    [Fact]
    public async Task SwitchFromCompressedToUncompressed_ReadsExistingData()
    {
        var metadata1 = CreateTestMetadata("key1", "hash1");
        var metadata2 = CreateTestMetadata("key2", "hash2");
        var metadata3 = CreateTestMetadata("key3", "hash3");

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(metadata1, CancellationToken.None);
            await repository.UpsertAsync(metadata2, CancellationToken.None);
            await repository.UpsertAsync(metadata3, CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var result1 = await repository.GetAsync("key1", CancellationToken.None);
            var result2 = await repository.GetAsync("key2", CancellationToken.None);
            var result3 = await repository.GetAsync("key3", CancellationToken.None);

            Assert.NotNull(result1);
            Assert.Equal("key1", result1.Value.Key);
            Assert.Equal("hash1", result1.Value.Hash);
            Assert.NotNull(result2);
            Assert.Equal("key2", result2.Value.Key);
            Assert.NotNull(result3);
            Assert.Equal("key3", result3.Value.Key);
        }
    }

    [Fact]
    public async Task MultipleCompressionSwitches_AllDataRemainAccessible()
    {
        var uncompressedMetadata = CreateTestMetadata("uncompressed-key", "uncompressed-hash");
        var compressedMetadata = CreateTestMetadata("compressed-key", "compressed-hash");
        var finalMetadata = CreateTestMetadata("final-key", "final-hash");

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(uncompressedMetadata, CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(compressedMetadata, CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(finalMetadata, CancellationToken.None);

            var result1 = await repository.GetAsync("uncompressed-key", CancellationToken.None);
            var result2 = await repository.GetAsync("compressed-key", CancellationToken.None);
            var result3 = await repository.GetAsync("final-key", CancellationToken.None);

            Assert.NotNull(result1);
            Assert.Equal("uncompressed-hash", result1.Value.Hash);
            Assert.NotNull(result2);
            Assert.Equal("compressed-hash", result2.Value.Hash);
            Assert.NotNull(result3);
            Assert.Equal("final-hash", result3.Value.Hash);
        }
    }

    [Fact]
    public async Task SwitchCompression_IterateKeysReturnsAllKeys()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
            await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("key3", "hash3"), CancellationToken.None);
            await repository.UpsertAsync(CreateTestMetadata("key4", "hash4"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var keys = new List<string>();
            await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
            {
                keys.Add(key);
            }

            Assert.Equal(4, keys.Count);
            Assert.Contains("key1", keys);
            Assert.Contains("key2", keys);
            Assert.Contains("key3", keys);
            Assert.Contains("key4", keys);
        }
    }

    [Fact]
    public async Task SwitchCompression_IterationReturnsCurrentKeySet()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
            await repository.UpsertAsync(CreateTestMetadata("key2", "hash2"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("key3", "hash3"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
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
    }

    [Fact]
    public async Task SwitchCompression_IsHashReferencedWorks()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("key1", "hash1"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var isReferenced = await repository.IsHashReferencedAsync("hash1", CancellationToken.None);
            Assert.True(isReferenced);
        }
    }

    [Fact]
    public async Task SwitchCompression_DeleteWorks()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("delete-key", "delete-hash"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var deleted = await repository.DeleteAsync("delete-key", CancellationToken.None);
            Assert.True(deleted);

            var result = await repository.GetAsync("delete-key", CancellationToken.None);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task SwitchCompression_UpdateExistingEntry()
    {
        var originalMetadata = CreateTestMetadata("update-key", "original-hash", 100);
        var updatedMetadata = CreateTestMetadata("update-key", "updated-hash", 200);

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(originalMetadata, CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(updatedMetadata, CancellationToken.None);

            var result = await repository.GetAsync("update-key", CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal("updated-hash", result.Value.Hash);
            Assert.Equal(200, result.Value.Size);
        }
    }

    [Fact]
    public async Task SwitchCompression_LargeDataset_AllEntriesAccessible()
    {
        const int entryCount = 100;

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            for (int i = 0; i < entryCount / 2; i++)
            {
                await repository.UpsertAsync(CreateTestMetadata($"key-{i}", $"hash-{i}"), CancellationToken.None);
            }
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            for (int i = entryCount / 2; i < entryCount; i++)
            {
                await repository.UpsertAsync(CreateTestMetadata($"key-{i}", $"hash-{i}"), CancellationToken.None);
            }
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var keys = new List<string>();
            await foreach (var key in repository.IterateKeysAsync("", CancellationToken.None))
            {
                keys.Add(key);
            }

            Assert.Equal(entryCount, keys.Count);

            for (int i = 0; i < entryCount; i++)
            {
                var result = await repository.GetAsync($"key-{i}", CancellationToken.None);
                Assert.NotNull(result);
                Assert.Equal($"hash-{i}", result.Value.Hash);
            }
        }
    }

    [Fact]
    public async Task SwitchCompression_PrefixIteration_WorksCorrectly()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("users/john", "hash1"), CancellationToken.None);
            await repository.UpsertAsync(CreateTestMetadata("users/jane", "hash2"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            await repository.UpsertAsync(CreateTestMetadata("admins/bob", "hash3"), CancellationToken.None);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var userKeys = new List<string>();
            await foreach (var key in repository.IterateKeysAsync("users/", CancellationToken.None))
            {
                userKeys.Add(key);
            }

            Assert.Equal(2, userKeys.Count);
            Assert.Contains("users/john", userKeys);
            Assert.Contains("users/jane", userKeys);
            Assert.DoesNotContain("admins/bob", userKeys);
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
}
