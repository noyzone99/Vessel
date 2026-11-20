using Moq;
using System.Collections.Concurrent;
using System.Text;
using Vessel.Logging;

namespace Vessel.Core.UnitTests;

public sealed class VesselStorageTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IVesselLogger> _mockLogger;
    private readonly VesselStorage _storage;

    public VesselStorageTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        _mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions(EnableMetadataCompression: false, Logger: _mockLogger.Object);
        _storage = new VesselStorage(_tempDirectory, options);
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PutAsync_ValidKey_StoresObjectAndReturnsMetadata()
    {
        const string key = "test-key";
        const string content = "Hello, World!";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(key, result.Key);
        Assert.NotNull(result.Hash);
        Assert.True(result.OriginalSize > 0);
    }

    [Fact]
    public async Task PutAsync_NullKey_ThrowsArgumentNullException()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.PutAsync(null!, stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_EmptyKey_ThrowsArgumentException()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.PutAsync("", stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_WhitespaceKey_ThrowsArgumentException()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.PutAsync("   ", stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_NullStream_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.PutAsync("key", null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_ExistingKeyWithoutOverwrite_ThrowsInvalidOperationException()
    {
        const string key = "duplicate-key";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("content1"));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("content2"));
        await _storage.PutAsync(key, stream1, cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storage.PutAsync(key, stream2, overwrite: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public async Task PutAsync_ExistingKeyWithOverwrite_UpdatesMetadata()
    {
        const string key = "overwrite-key";
        const string content1 = "original content";
        const string content2 = "updated content";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

        var original = await _storage.PutAsync(key, stream1, cancellationToken: TestContext.Current.CancellationToken);

        var updated = await _storage.PutAsync(key, stream2, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(key, updated.Key);
        Assert.NotEqual(original.Hash, updated.Hash);
        Assert.NotEqual(original.OriginalSize, updated.OriginalSize);
    }

    [Fact]
    public async Task PutAsync_WithFileName_StoresFileName()
    {
        const string key = "test-with-filename";
        const string fileName = "document.txt";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(FileName: fileName), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(fileName, result.FileName);
    }

    [Fact]
    public async Task PutAsync_WithWhitespaceFileName_PreservesValue()
    {
        const string key = "test-whitespace-filename";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(FileName: "   "), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("   ", result.FileName);
    }

    [Fact]
    public async Task PutAsync_WithExtension_PreservesExtensionValue()
    {
        const string key = "test-with-extension";
        const string extension = ".json";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(Extension: extension), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(extension, result.Extension);
    }

    [Fact]
    public async Task PutAsync_WithExtensionWithoutDot_PreservesValue()
    {
        const string key = "test-extension-no-dot";
        const string extension = "txt";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(Extension: extension), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(extension, result.Extension);
    }

    [Fact]
    public async Task PutAsync_WithMimeType_StoresMimeType()
    {
        const string key = "test-with-mimetype";
        const string mimeType = "application/json";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(MimeType: mimeType), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(mimeType, result.MimeType);
    }

    [Fact]
    public async Task PutAsync_WithWhitespaceMimeType_PreservesValue()
    {
        const string key = "test-whitespace-mimetype";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        var result = await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(MimeType: "   "), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("   ", result.MimeType);
    }

    [Fact]
    public async Task PutAsync_WithCompressionType_StoresCompressedData()
    {
        const string key = "test-compressed";
        var content = new string('a', 10000);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var result = await _storage.PutAsync(key, stream, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CompressionType.LZ4, result.CompressionType);
        Assert.True(result.CompressedSize < result.OriginalSize);
    }

    [Fact]
    public async Task PutAsync_ConcurrentSameKey_SerializesWrites()
    {
        const string key = "concurrent-key";
        var tasks = new List<Task<ObjectMetadata>>();

        for (int i = 0; i < 10; i++)
        {
            var content = $"content-{i}";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tasks.Add(_storage.PutAsync(key, stream, overwrite: true, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(key, r.Key));

        var metadata = _storage.GetMetadata(key);
        Assert.NotNull(metadata);
    }

    [Fact]
    public async Task PutAsync_ConcurrentDifferentKeys_ExecutesInParallel()
    {
        var tasks = new List<Task<ObjectMetadata>>();

        for (int i = 0; i < 10; i++)
        {
            var key = $"parallel-key-{i}";
            var content = $"content-{i}";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tasks.Add(_storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.NotNull(r.Hash));
    }

    [Fact]
    public async Task GetAsync_ExistingKey_ReturnsObjectResponse()
    {
        const string key = "get-test-key";
        const string content = "Hello, World!";
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, cancellationToken: TestContext.Current.CancellationToken);

        var response = _storage.GetStream(key);

        Assert.NotNull(response);
        Assert.NotNull(response);

        using var reader = new StreamReader(response);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task GetAsync_NonExistingKey_ThrowsFileNotFoundException()
    {
        var exception = Assert.Throws<FileNotFoundException>(() =>
            _storage.GetStream("non-existing"));
        Assert.Contains("non-existing", exception.FileName);
    }

    [Fact]
    public async Task GetAsync_NullKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _storage.GetStream(null!));
    }

    [Fact]
    public async Task GetAsync_EmptyKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _storage.GetStream(""));
    }

    [Fact]
    public async Task GetAsync_WhitespaceKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _storage.GetStream("   "));
    }

    [Fact]
    public async Task GetAsync_CompressedData_ReturnsDecompressedContent()
    {
        const string key = "compressed-get-test";
        var content = new string('x', 10000);
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);

        var response = _storage.GetStream(key);

        using var reader = new StreamReader(response);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task GetAsync_ConcurrentReads_ReturnsCorrectData()
    {
        const string key = "concurrent-read-key";
        const string content = "concurrent content";
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, cancellationToken: TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.FromResult(_storage.GetStream(key)))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(10, responses.Length);
        foreach (var response in responses)
        {
            using var reader = new StreamReader(response);
            var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.Equal(content, retrievedContent);
        }
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesObjectAndReturnsTrue()
    {
        const string key = "delete-test-key";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        var result = await _storage.DeleteAsync(key, TestContext.Current.CancellationToken);

        Assert.True(result);

        Assert.Throws<FileNotFoundException>(() =>
            _storage.GetStream(key));
    }

    [Fact]
    public async Task DeleteAsync_NonExistingKey_ReturnsFalse()
    {
        var result = await _storage.DeleteAsync("non-existing", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_NullKey_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.DeleteAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_EmptyKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.DeleteAsync("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhitespaceKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.DeleteAsync("   ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_DuplicateContent_RemovesMetadataButPreservesBlob()
    {
        const string key1 = "dup-key1";
        const string key2 = "dup-key2";
        const string content = "duplicate content";

        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var metadata1 = await _storage.PutAsync(key1, stream1, cancellationToken: TestContext.Current.CancellationToken);
        var metadata2 = await _storage.PutAsync(key2, stream2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(metadata1.Hash, metadata2.Hash);

        var deleted = await _storage.DeleteAsync(key1, TestContext.Current.CancellationToken);

        Assert.True(deleted);

        var response = _storage.GetStream(key2);
        using var reader = new StreamReader(response);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task DeleteAsync_LastReference_DeletesBlob()
    {
        const string key = "last-ref-key";
        const string content = "unique content for deletion";
        await using var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var metadata = await _storage.PutAsync(key, inputStream, cancellationToken: TestContext.Current.CancellationToken);
        using var openStream = _storage.GetStream(key);

        var deleted = await _storage.DeleteAsync(key, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Null(_storage.GetMetadata(key));
        using var reader = new StreamReader(openStream);
        Assert.Equal(content, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(
            _tempDirectory,
            "blobs",
            metadata.Hash[..2],
            metadata.Hash.Substring(2, 2),
            $"{metadata.Hash}.blob")));
    }

    [Fact]
    public async Task DeleteAsync_ConcurrentSameKey_SerializesDeletes()
    {
        const string key = "concurrent-delete-key";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _storage.DeleteAsync(key, TestContext.Current.CancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r);
        Assert.Equal(4, results.Count(r => !r));
    }

    [Fact]
    public async Task GetMetadataAsync_ExistingKey_ReturnsMetadata()
    {
        const string key = "metadata-test-key";
        const string fileName = "test.txt";
        const string mimeType = "text/plain";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, fileInfo: new ObjectInfo(FileName: fileName, MimeType: mimeType), cancellationToken: TestContext.Current.CancellationToken);

        var metadata = _storage.GetMetadata(key);

        Assert.NotNull(metadata);
        Assert.Equal(key, metadata.Value.Key);
        Assert.Equal(fileName, metadata.Value.FileName);
        Assert.Equal(mimeType, metadata.Value.MimeType);
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistingKey_ReturnsNull()
    {
        var metadata = _storage.GetMetadata("non-existing");

        Assert.Null(metadata);
    }

    [Fact]
    public async Task GetMetadataAsync_NullKey_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _storage.GetMetadata(null!));
    }

    [Fact]
    public async Task GetMetadataAsync_EmptyKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _storage.GetMetadata(""));
    }

    [Fact]
    public async Task GetMetadataAsync_WhitespaceKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _storage.GetMetadata("   "));
    }

    [Fact]
    public async Task GetAllKeysAsync_EmptyStorage_ReturnsEmpty()
    {
        var keys = new List<string>();
        keys.AddRange(_storage.GetAllKeys());

        Assert.Empty(keys);
    }

    [Fact]
    public async Task GetAllKeysAsync_WithMultipleKeys_ReturnsAllKeys()
    {
        var expectedKeys = new[] { "key1", "key2", "key3" };
        foreach (var key in expectedKeys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        var keys = new List<string>();
        keys.AddRange(_storage.GetAllKeys());

        Assert.Equal(3, keys.Count);
        Assert.All(expectedKeys, k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task GetAllKeysAsync_WithPrefix_ReturnsMatchingKeys()
    {
        var keys = new[] { "user/1", "user/2", "admin/1", "admin/2" };
        foreach (var key in keys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        var userKeys = new List<string>();
        userKeys.AddRange(_storage.GetAllKeys("user/"));

        Assert.Equal(2, userKeys.Count);
        Assert.Contains("user/1", userKeys);
        Assert.Contains("user/2", userKeys);
    }

    [Fact]
    public async Task GetAllKeysAsync_NullPrefix_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            foreach (var key in _storage.GetAllKeys(null!))
            {
            }
        });
    }

    [Fact]
    public async Task GetAllKeysAsync_EmptyPrefix_ReturnsAllKeys()
    {
        var expectedKeys = new[] { "key1", "key2" };
        foreach (var key in expectedKeys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        var keys = new List<string>();
        keys.AddRange(_storage.GetAllKeys(""));

        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task DisposeAsync_WithLogger_DoesNotThrow()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions
        {
            Logger = mockLogger.Object,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        storage.Dispose();

        mockLogger.Verify(l => l.Log(VesselLogLevel.Information, It.IsAny<string>()), Times.AtLeastOnce);

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_ThrowsObjectDisposedException()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var options = new VesselStorageOptions
        {
            Logger = null,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        storage.Dispose();

        Assert.Throws<ObjectDisposedException>(() => storage.Dispose());

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_AfterStoreOperations_DisposesCorrectly()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var options = new VesselStorageOptions
        {
            Logger = null,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        const string key = "test-key";
        const string content = "test content";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        storage.Dispose();

        Assert.Throws<ObjectDisposedException>(() => storage.Dispose());

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_ThrowsAtLeastOnce()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var options = new VesselStorageOptions
        {
            Logger = null,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);
        var exceptions = new ConcurrentQueue<Exception>();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() =>
            {
                try
                {
                    storage.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }))
            .ToList();

        await Task.WhenAll(tasks);

        Assert.NotEmpty(exceptions);

        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
