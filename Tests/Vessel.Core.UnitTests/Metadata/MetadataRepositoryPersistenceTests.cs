using Moq;
using Vessel.Logging;
using Vessel.Metadata;

namespace Vessel.Core.UnitTests.Metadata;

/// <summary>
/// Tests for MetadataRepository persistence when switching compression modes between runs. Verifies that RocksDB can
/// correctly read data regardless of compression setting changes.
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
            repository.Upsert(metadata1);
            repository.Upsert(metadata2);
            repository.Upsert(metadata3);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var result1 = repository.Get("key1");
            var result2 = repository.Get("key2");
            var result3 = repository.Get("key3");

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
            repository.Upsert(metadata1);
            repository.Upsert(metadata2);
            repository.Upsert(metadata3);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var result1 = repository.Get("key1");
            var result2 = repository.Get("key2");
            var result3 = repository.Get("key3");

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
            repository.Upsert(uncompressedMetadata);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            repository.Upsert(compressedMetadata);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            repository.Upsert(finalMetadata);

            var result1 = repository.Get("uncompressed-key");
            var result2 = repository.Get("compressed-key");
            var result3 = repository.Get("final-key");

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
            repository.Upsert(CreateTestMetadata("key1", "hash1"));
            repository.Upsert(CreateTestMetadata("key2", "hash2"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            repository.Upsert(CreateTestMetadata("key3", "hash3"));
            repository.Upsert(CreateTestMetadata("key4", "hash4"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var keys = new List<string>();
            keys.AddRange(repository.IterateKeys(""));

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
            repository.Upsert(CreateTestMetadata("key1", "hash1"));
            repository.Upsert(CreateTestMetadata("key2", "hash2"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            repository.Upsert(CreateTestMetadata("key3", "hash3"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var keys = new List<string>();
            keys.AddRange(repository.IterateKeys(""));

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
            repository.Upsert(CreateTestMetadata("key1", "hash1"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var isReferenced = repository.IsHashReferenced("hash1");
            Assert.True(isReferenced);
        }
    }

    [Fact]
    public async Task SwitchCompression_DeleteWorks()
    {
        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            repository.Upsert(CreateTestMetadata("delete-key", "delete-hash"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            var deleted = repository.Delete("delete-key");
            Assert.True(deleted);

            var result = repository.Get("delete-key");
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
            repository.Upsert(originalMetadata);
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            repository.Upsert(updatedMetadata);

            var result = repository.Get("update-key");
            Assert.NotNull(result);
            Assert.Equal("updated-hash", result.Value.Hash);
            Assert.Equal(200, result.Value.OriginalSize);
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
                repository.Upsert(CreateTestMetadata($"key-{i}", $"hash-{i}"));
            }
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            for (int i = entryCount / 2; i < entryCount; i++)
            {
                repository.Upsert(CreateTestMetadata($"key-{i}", $"hash-{i}"));
            }
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var keys = new List<string>();
            keys.AddRange(repository.IterateKeys(""));

            Assert.Equal(entryCount, keys.Count);

            for (int i = 0; i < entryCount; i++)
            {
                var result = repository.Get($"key-{i}");
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
            repository.Upsert(CreateTestMetadata("users/john", "hash1"));
            repository.Upsert(CreateTestMetadata("users/jane", "hash2"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: true, _mockLogger.Object))
        {
            repository.Upsert(CreateTestMetadata("admins/bob", "hash3"));
        }

        using (var repository = new MetadataRepository(_testStoragePath, useCompression: false, _mockLogger.Object))
        {
            var userKeys = new List<string>();
            userKeys.AddRange(repository.IterateKeys("users/"));

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
            OriginalSize: size,
            CompressionType: CompressionType.None,
            ETag: $"etag-{key}",
            CreationDate: DateTimeOffset.UtcNow,
            FileName: $"{key}.txt",
            Extension: ".txt",
            MimeType: "text/plain",
            CompressedSize: size
        );
    }
}
