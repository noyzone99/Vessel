using Moq;
using System.Text;
using Vessel.Logging;
using Vessel.Models;

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
        _storage.DisposeAsync().AsTask().Wait();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PutAsync_ValidKey_StoresObjectAndReturnsMetadata()
    {
        // Arrange
        const string key = "test-key";
        const string content = "Hello, World!";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Act
        var result = await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(key, result.Key);
        Assert.NotNull(result.Hash);
        Assert.True(result.Size > 0);
    }

    [Fact]
    public async Task PutAsync_NullKey_ThrowsArgumentNullException()
    {
        // Arrange
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.PutAsync(null!, stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_EmptyKey_ThrowsArgumentException()
    {
        // Arrange
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.PutAsync("", stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_WhitespaceKey_ThrowsArgumentException()
    {
        // Arrange
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.PutAsync("   ", stream, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_NullStream_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.PutAsync("key", null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutAsync_ExistingKeyWithoutOverwrite_ThrowsInvalidOperationException()
    {
        // Arrange
        const string key = "duplicate-key";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("content1"));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("content2"));
        await _storage.PutAsync(key, stream1, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _storage.PutAsync(key, stream2, overwrite: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public async Task PutAsync_ExistingKeyWithOverwrite_UpdatesMetadata()
    {
        // Arrange
        const string key = "overwrite-key";
        const string content1 = "original content";
        const string content2 = "updated content";
        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

        var original = await _storage.PutAsync(key, stream1, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var updated = await _storage.PutAsync(key, stream2, overwrite: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(key, updated.Key);
        Assert.NotEqual(original.Hash, updated.Hash);
        Assert.NotEqual(original.Size, updated.Size);
    }

    [Fact]
    public async Task PutAsync_WithFileName_StoresFileName()
    {
        // Arrange
        const string key = "test-with-filename";
        const string fileName = "document.txt";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act
        var result = await _storage.PutAsync(key, stream, fileName: fileName, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(fileName, result.FileName);
    }

    [Fact]
    public async Task PutAsync_WithWhitespaceFileName_StoresNull()
    {
        // Arrange
        const string key = "test-whitespace-filename";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act
        var result = await _storage.PutAsync(key, stream, fileName: "   ", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.FileName);
    }

    [Fact]
    public async Task PutAsync_WithExtension_StoresExtension()
    {
        // Arrange
        const string key = "test-with-extension";
        const string extension = ".json";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        // Act
        var result = await _storage.PutAsync(key, stream, extension: extension, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(extension, result.Extension);
    }

    [Fact]
    public async Task PutAsync_WithExtensionWithoutDot_AddsDot()
    {
        // Arrange
        const string key = "test-extension-no-dot";
        const string extension = "txt";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act
        var result = await _storage.PutAsync(key, stream, extension: extension, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(".txt", result.Extension);
    }

    [Fact]
    public async Task PutAsync_WithMimeType_StoresMimeType()
    {
        // Arrange
        const string key = "test-with-mimetype";
        const string mimeType = "application/json";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}"));

        // Act
        var result = await _storage.PutAsync(key, stream, mimeType: mimeType, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(mimeType, result.MimeType);
    }

    [Fact]
    public async Task PutAsync_WithWhitespaceMimeType_StoresNull()
    {
        // Arrange
        const string key = "test-whitespace-mimetype";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act
        var result = await _storage.PutAsync(key, stream, mimeType: "   ", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.MimeType);
    }

    [Fact]
    public async Task PutAsync_WithCompressionType_StoresCompressedData()
    {
        // Arrange
        const string key = "test-compressed";
        var content = new string('a', 10000);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Act
        var result = await _storage.PutAsync(key, stream, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CompressionType.LZ4, result.CompressionType);
        Assert.True(result.CompressedSize < result.Size);
    }

    [Fact]
    public async Task PutAsync_ConcurrentSameKey_SerializesWrites()
    {
        // Arrange
        const string key = "concurrent-key";
        var tasks = new List<Task<ObjectMetadata>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var content = $"content-{i}";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tasks.Add(_storage.PutAsync(key, stream, overwrite: true, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.Equal(key, r.Key));

        // Verify final state
        var metadata = await _storage.GetMetadataAsync(key, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(metadata);
    }

    [Fact]
    public async Task PutAsync_ConcurrentDifferentKeys_ExecutesInParallel()
    {
        // Arrange
        var tasks = new List<Task<ObjectMetadata>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var key = $"parallel-key-{i}";
            var content = $"content-{i}";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            tasks.Add(_storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, results.Length);
        Assert.All(results, r => Assert.NotNull(r.Hash));
    }

    [Fact]
    public async Task GetAsync_ExistingKey_ReturnsObjectResponse()
    {
        // Arrange
        const string key = "get-test-key";
        const string content = "Hello, World!";
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await _storage.GetAsync(key, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(key, response.Metadata.Key);
        Assert.NotNull(response.ContentStream);

        using var reader = new StreamReader(response.ContentStream);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task GetAsync_NonExistingKey_ThrowsFileNotFoundException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _storage.GetAsync("non-existing", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("non-existing", exception.FileName);
    }

    [Fact]
    public async Task GetAsync_NullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.GetAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_EmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.GetAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_WhitespaceKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.GetAsync("   ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_CompressedData_ReturnsDecompressedContent()
    {
        // Arrange
        const string key = "compressed-get-test";
        var content = new string('x', 10000);
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, compressionType: CompressionType.LZ4, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var response = await _storage.GetAsync(key, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        using var reader = new StreamReader(response.ContentStream);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task GetAsync_ConcurrentReads_ReturnsCorrectData()
    {
        // Arrange
        const string key = "concurrent-read-key";
        const string content = "concurrent content";
        await using var putStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, putStream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _storage.GetAsync(key, cancellationToken: TestContext.Current.CancellationToken))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, responses.Length);
        foreach (var response in responses)
        {
            using var reader = new StreamReader(response.ContentStream);
            var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.Equal(content, retrievedContent);
        }
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesObjectAndReturnsTrue()
    {
        // Arrange
        const string key = "delete-test-key";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var result = await _storage.DeleteAsync(key, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);

        // Verify object is gone
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _storage.GetAsync(key, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_NonExistingKey_ReturnsFalse()
    {
        // Act
        var result = await _storage.DeleteAsync("non-existing", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_NullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.DeleteAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_EmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.DeleteAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhitespaceKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.DeleteAsync("   ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_DuplicateContent_RemovesMetadataButPreservesBlob()
    {
        // Arrange
        const string key1 = "dup-key1";
        const string key2 = "dup-key2";
        const string content = "duplicate content";

        await using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var metadata1 = await _storage.PutAsync(key1, stream1, cancellationToken: TestContext.Current.CancellationToken);
        var metadata2 = await _storage.PutAsync(key2, stream2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(metadata1.Hash, metadata2.Hash);

        // Act
        var deleted = await _storage.DeleteAsync(key1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleted);

        // Verify key2 can still be retrieved
        var response = await _storage.GetAsync(key2, cancellationToken: TestContext.Current.CancellationToken);
        using var reader = new StreamReader(response.ContentStream);
        var retrievedContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(content, retrievedContent);
    }

    [Fact]
    public async Task DeleteAsync_LastReference_DeletesBlob()
    {
        // Arrange
        const string key = "last-ref-key";
        const string content = "unique content for deletion";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var deleted = await _storage.DeleteAsync(key, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleted);
        var metadata = await _storage.GetMetadataAsync(key, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(metadata);
    }

    [Fact]
    public async Task DeleteAsync_ConcurrentSameKey_SerializesDeletes()
    {
        // Arrange
        const string key = "concurrent-delete-key";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => _storage.DeleteAsync(key, cancellationToken: TestContext.Current.CancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.Single(results, r => r);
        Assert.Equal(4, results.Count(r => !r));
    }

    [Fact]
    public async Task GetMetadataAsync_ExistingKey_ReturnsMetadata()
    {
        // Arrange
        const string key = "metadata-test-key";
        const string fileName = "test.txt";
        const string mimeType = "text/plain";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
        await _storage.PutAsync(key, stream, fileName: fileName, mimeType: mimeType, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var metadata = await _storage.GetMetadataAsync(key, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(key, metadata.Value.Key);
        Assert.Equal(fileName, metadata.Value.FileName);
        Assert.Equal(mimeType, metadata.Value.MimeType);
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistingKey_ReturnsNull()
    {
        // Act
        var metadata = await _storage.GetMetadataAsync("non-existing", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(metadata);
    }

    [Fact]
    public async Task GetMetadataAsync_NullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _storage.GetMetadataAsync(null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMetadataAsync_EmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.GetMetadataAsync("", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMetadataAsync_WhitespaceKey_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storage.GetMetadataAsync("   ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllKeysAsync_EmptyStorage_ReturnsEmpty()
    {
        // Act
        var keys = new List<string>();
        await foreach (var key in _storage.GetAllKeysAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            keys.Add(key);
        }

        // Assert
        Assert.Empty(keys);
    }

    [Fact]
    public async Task GetAllKeysAsync_WithMultipleKeys_ReturnsAllKeys()
    {
        // Arrange
        var expectedKeys = new[] { "key1", "key2", "key3" };
        foreach (var key in expectedKeys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act
        var keys = new List<string>();
        await foreach (var key in _storage.GetAllKeysAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            keys.Add(key);
        }

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.All(expectedKeys, k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task GetAllKeysAsync_WithPrefix_ReturnsMatchingKeys()
    {
        // Arrange
        var keys = new[] { "user/1", "user/2", "admin/1", "admin/2" };
        foreach (var key in keys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act
        var userKeys = new List<string>();
        await foreach (var key in _storage.GetAllKeysAsync("user/", cancellationToken: TestContext.Current.CancellationToken))
        {
            userKeys.Add(key);
        }

        // Assert
        Assert.Equal(2, userKeys.Count);
        Assert.Contains("user/1", userKeys);
        Assert.Contains("user/2", userKeys);
    }

    [Fact]
    public async Task GetAllKeysAsync_NullPrefix_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var key in _storage.GetAllKeysAsync(null!, cancellationToken: TestContext.Current.CancellationToken))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task GetAllKeysAsync_EmptyPrefix_ReturnsAllKeys()
    {
        // Arrange
        var expectedKeys = new[] { "key1", "key2" };
        foreach (var key in expectedKeys)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"content-{key}"));
            await _storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act
        var keys = new List<string>();
        await foreach (var key in _storage.GetAllKeysAsync("", cancellationToken: TestContext.Current.CancellationToken))
        {
            keys.Add(key);
        }

        // Assert
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task DisposeAsync_WithLogger_LogsInformationMessage()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions
        {
            Logger = mockLogger.Object,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Act
        await storage.DisposeAsync();

        // Assert
        mockLogger.Verify(l => l.Log(VesselLogLevel.Information, "Disposing embedded object storage."), Times.Once);

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_WithoutLogger_CompletesSuccessfully()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var options = new VesselStorageOptions
        {
            Logger = null,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Act
        await storage.DisposeAsync();

        // Assert - successful completion without exception
        Assert.True(true);

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_HandlesGracefully()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions
        {
            Logger = mockLogger.Object,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Act
        await storage.DisposeAsync();
        await storage.DisposeAsync();
        await storage.DisposeAsync();

        // Assert
        mockLogger.Verify(l => l.Log(VesselLogLevel.Information, "Disposing embedded object storage."), Times.Exactly(3));

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_ReturnsCompletedTask()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var options = new VesselStorageOptions
        {
            Logger = null,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Act
        var result = storage.DisposeAsync();

        // Assert
        Assert.True(result.IsCompleted);
        await result;

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_AfterStoreOperations_DisposesCorrectly()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions
        {
            Logger = mockLogger.Object,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Perform some operations
        const string key = "test-key";
        const string content = "test content";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await storage.PutAsync(key, stream, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await storage.DisposeAsync();

        // Assert
        mockLogger.Verify(l => l.Log(VesselLogLevel.Information, "Disposing embedded object storage."), Times.Once);

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_HandlesGracefully()
    {
        // Arrange
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"VesselTests_{Guid.NewGuid():N}");
        var mockLogger = new Mock<IVesselLogger>();
        var options = new VesselStorageOptions
        {
            Logger = mockLogger.Object,
            EnableMetadataCompression = false
        };
        var storage = new VesselStorage(tempDirectory, options);

        // Act
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => storage.DisposeAsync().AsTask())
            .ToList();

        await Task.WhenAll(tasks);

        // Assert
        mockLogger.Verify(l => l.Log(VesselLogLevel.Information, "Disposing embedded object storage."), Times.Exactly(5));

        // Cleanup
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
