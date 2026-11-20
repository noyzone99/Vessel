using Moq;
using System.Text;
using Vessel.Logging;
using Vessel.Storage;

namespace Vessel.UnitTests.Storage;

public sealed class LocalBlobStorageEngineTests : IDisposable
{
    private readonly string _testRootPath;
    private readonly Mock<IVesselLogger> _mockLogger;

    public LocalBlobStorageEngineTests()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "LocalBlobStorageEngineTests", Guid.NewGuid().ToString("N"));
        _mockLogger = new Mock<IVesselLogger>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRootPath))
        {
            try
            {
                Directory.Delete(_testRootPath, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Constructor_ValidPath_CreatesDirectories()
    {
        _ = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.True(Directory.Exists(_testRootPath));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "tmp")));
    }

    [Fact]
    public void Constructor_ValidPathWithoutLogger_CreatesDirectories()
    {
        _ = new LocalBlobStorageEngine(_testRootPath);

        Assert.True(Directory.Exists(_testRootPath));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "tmp")));
    }

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalBlobStorageEngine(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LocalBlobStorageEngine(string.Empty, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new LocalBlobStorageEngine("   ", _mockLogger.Object));
    }

    [Fact]
    public void Constructor_RelativePath_ExpandsToFullPath()
    {
        const string relativePath = "test_relative";
        var expectedFullPath = Path.GetFullPath(relativePath);

        _ = new LocalBlobStorageEngine(relativePath, _mockLogger.Object);

        Assert.True(Directory.Exists(expectedFullPath));

        if (Directory.Exists(expectedFullPath))
        {
            Directory.Delete(expectedFullPath, recursive: true);
        }
    }

    [Fact]
    public async Task WriteBlobAsync_NoCompression_WritesBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Hello, World!"u8.ToArray();
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(testData.Length, size);
        Assert.Equal(testData.Length, compressedSize);
    }

    [Fact]
    public async Task WriteBlobAsync_ZstdCompression_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('A', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize < size);
    }

    [Fact]
    public async Task WriteBlobAsync_ZstdCompressionFastest_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('B', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            CompressionLevel.Fastest,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize < size);
    }

    [Fact]
    public async Task WriteBlobAsync_ZstdCompressionHigh_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('C', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            CompressionLevel.High,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize < size);
    }

    [Fact]
    public async Task WriteBlobAsync_ZstdCompressionUltra_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('D', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            CompressionLevel.Ultra,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize < size);
    }

    [Fact]
    public async Task WriteBlobAsync_LZ4Compression_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('E', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.LZ4,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize <= size);
    }

    [Fact]
    public async Task WriteBlobAsync_LZ4CompressionFastest_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('F', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.LZ4,
            CompressionLevel.Fastest,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize <= size);
    }

    [Fact]
    public async Task WriteBlobAsync_LZ4CompressionHigh_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('G', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.LZ4,
            CompressionLevel.High,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize <= size);
    }

    [Fact]
    public async Task WriteBlobAsync_LZ4CompressionUltra_WritesCompressedBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('H', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.LZ4,
            CompressionLevel.Ultra,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize <= size);
    }

    [Fact]
    public async Task WriteBlobAsync_NullStream_ThrowsArgumentNullException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            engine.WriteBlobAsync(null!, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None));
    }

    [Fact]
    public async Task WriteBlobAsync_EmptyStream_WritesEmptyBlob()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        await using var inputStream = new MemoryStream();

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(0, size);
    }

    [Fact]
    public async Task WriteBlobAsync_DuplicateBlob_ReuseExisting()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Duplicate test"u8.ToArray();

        await using var inputStream1 = new MemoryStream(testData);
        var (hash1, size1, compressedSize1) = await engine.WriteBlobAsync(
            inputStream1,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        await using var inputStream2 = new MemoryStream(testData);
        var (hash2, size2, compressedSize2) = await engine.WriteBlobAsync(
            inputStream2,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.Equal(hash1, hash2);
        Assert.Equal(size1, size2);
        Assert.Equal(compressedSize1, compressedSize2);
    }

    [Fact]
    public async Task WriteBlobAsync_LargeData_WritesSuccessfully()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var largeData = new byte[1024 * 1024];
        new Random(42).NextBytes(largeData);
        await using var inputStream = new MemoryStream(largeData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(largeData.Length, size);
        Assert.True(compressedSize > 0);
    }

    [Fact]
    public async Task WriteBlobAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var largeData = new byte[1024 * 1024 * 10];
        await using var inputStream = new SlowStream(largeData);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.WriteBlobAsync(inputStream, CompressionType.None, CompressionLevel.Balanced, cts.Token));
    }

    [Fact]
    public async Task WriteBlobAsync_ConcurrentWrites_BothSucceed()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData1 = "Data 1"u8.ToArray();
        var testData2 = "Data 2"u8.ToArray();

        var task1 = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(testData1);
            return await engine.WriteBlobAsync(stream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(testData2);
            return await engine.WriteBlobAsync(stream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.NotEqual(results[0].Hash, results[1].Hash);
        Assert.Equal(testData1.Length, results[0].Size);
        Assert.Equal(testData2.Length, results[1].Size);
    }

    [Fact]
    public async Task WriteBlobAsync_ConcurrentWritesSameData_HandleDeduplication()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Same data"u8.ToArray();

        var task1 = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(testData);
            return await engine.WriteBlobAsync(stream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            await using var stream = new MemoryStream(testData);
            return await engine.WriteBlobAsync(stream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(results[0].Hash, results[1].Hash);
        Assert.Equal(testData.Length, results[0].Size);
        Assert.Equal(testData.Length, results[1].Size);
    }

    [Fact]
    public async Task OpenBlobRead_NoCompression_ReturnsReadableStream()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Test data for reading"u8.ToArray();
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        await using var readStream = engine.OpenBlobRead(hash, CompressionType.None);
        await using var memoryStream = new MemoryStream();
        readStream.CopyTo(memoryStream);
        var actualData = memoryStream.ToArray();

        Assert.Equal(testData, actualData);
    }

    [Fact]
    public async Task OpenBlobRead_ZstdCompression_ReturnsDecompressedStream()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('Z', 1000));
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.Zstd, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        await using var readStream = engine.OpenBlobRead(hash, CompressionType.Zstd);
        await using var memoryStream = new MemoryStream();
        readStream.CopyTo(memoryStream);
        var actualData = memoryStream.ToArray();

        Assert.Equal(testData, actualData);
    }

    [Fact]
    public async Task OpenBlobRead_LZ4Compression_ReturnsDecompressedStream()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('L', 1000));
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.LZ4, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        await using var readStream = engine.OpenBlobRead(hash, CompressionType.LZ4);
        await using var memoryStream = new MemoryStream();
        readStream.CopyTo(memoryStream);
        var actualData = memoryStream.ToArray();

        Assert.Equal(testData, actualData);
    }

    [Fact]
    public void OpenBlobRead_NonExistentBlob_ThrowsDirectoryNotFoundException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        const string nonExistentHash = "0000000000000000000000000000000000000000000000000000000000000000";

        Assert.Throws<DirectoryNotFoundException>(() => engine.OpenBlobRead(nonExistentHash, CompressionType.None));
    }

    [Fact]
    public void OpenBlobRead_NullHash_ThrowsArgumentOutOfRangeException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => engine.OpenBlobRead(null!, CompressionType.None));
    }

    [Fact]
    public void OpenBlobRead_EmptyHash_ThrowsArgumentOutOfRangeException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => engine.OpenBlobRead(string.Empty, CompressionType.None));
    }

    [Fact]
    public void OpenBlobRead_WhitespaceHash_ThrowsArgumentOutOfRangeException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => engine.OpenBlobRead("   ", CompressionType.None));
    }

    [Fact]
    public async Task OpenBlobRead_InvalidCompressionType_ThrowsArgumentOutOfRangeException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Test data"u8.ToArray();
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.OpenBlobRead(hash, (CompressionType)999));
    }

    [Fact]
    public async Task OpenBlobRead_ConcurrentReads_BothSucceed()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Concurrent read data"u8.ToArray();
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        var task1 = Task.Run(() =>
        {
            using var readStream = engine.OpenBlobRead(hash, CompressionType.None);
            using var memoryStream = new MemoryStream();
            readStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });

        var task2 = Task.Run(() =>
        {
            using var readStream = engine.OpenBlobRead(hash, CompressionType.None);
            using var memoryStream = new MemoryStream();
            readStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        });

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(testData, results[0]);
        Assert.Equal(testData, results[1]);
    }

    [Fact]
    public async Task DeleteBlob_ExistingBlob_DeletesSuccessfully()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Data to delete"u8.ToArray();
        string hash;

        await using (var inputStream = new MemoryStream(testData))
        {
            var (Hash, Size, CompressedSize) = await engine.WriteBlobAsync(inputStream, CompressionType.None, CompressionLevel.Balanced, CancellationToken.None);
            hash = Hash;
        }

        engine.DeleteBlob(hash);

        Assert.Throws<FileNotFoundException>(() => engine.OpenBlobRead(hash, CompressionType.None));
    }

    [Fact]
    public void DeleteBlob_NonExistentBlob_DoesNotThrow()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        const string nonExistentHash = "0000000000000000000000000000000000000000000000000000000000000000";

        engine.DeleteBlob(nonExistentHash);
    }

    [Fact]
    public void DeleteBlob_NullHash_ThrowsArgumentException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentException>(() => engine.DeleteBlob(null!));
    }

    [Fact]
    public void DeleteBlob_EmptyHash_ThrowsArgumentException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentException>(() => engine.DeleteBlob(string.Empty));
    }

    [Fact]
    public void DeleteBlob_WhitespaceHash_ThrowsArgumentException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        Assert.ThrowsAny<ArgumentException>(() => engine.DeleteBlob("   "));
    }

    [Fact]
    public void CleanupOrphanedTempFiles_WithTempFiles_DeletesAllTempFiles()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var tmpPath = Path.Combine(_testRootPath, "tmp");

        var tempFile1 = Path.Combine(tmpPath, "orphan1.tmp");
        var tempFile2 = Path.Combine(tmpPath, "orphan2.tmp");
        File.WriteAllText(tempFile1, "orphaned content 1");
        File.WriteAllText(tempFile2, "orphaned content 2");

        engine.CleanupOrphanedTempFiles();

        Assert.False(File.Exists(tempFile1));
        Assert.False(File.Exists(tempFile2));
    }

    [Fact]
    public void CleanupOrphanedTempFiles_WithNoTempFiles_DoesNotThrow()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);

        engine.CleanupOrphanedTempFiles();
    }

    [Fact]
    public void CleanupOrphanedTempFiles_TmpDirectoryDoesNotExist_DoesNotThrow()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var tmpPath = Path.Combine(_testRootPath, "tmp");

        Directory.Delete(tmpPath);

        engine.CleanupOrphanedTempFiles();
    }

    [Fact]
    public void CleanupOrphanedTempFiles_OnlyDeletesTmpFiles_IgnoresOtherFiles()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var tmpPath = Path.Combine(_testRootPath, "tmp");

        var tempFile = Path.Combine(tmpPath, "orphan.tmp");
        var nonTempFile = Path.Combine(tmpPath, "keepme.txt");
        File.WriteAllText(tempFile, "orphaned content");
        File.WriteAllText(nonTempFile, "keep this content");

        engine.CleanupOrphanedTempFiles();

        Assert.False(File.Exists(tempFile));
        Assert.True(File.Exists(nonTempFile));
    }

    [Fact]
    public async Task ConcurrentCompressionLevelsReportTheSizeOfThePublishedBlob()
    {
        var token = TestContext.Current.CancellationToken;
        using var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var data = new byte[130_017];
        new Random(8311).NextBytes(data);
        for (var offset = 0; offset < data.Length - 64; offset += 173)
            data.AsSpan(offset, 64).Fill((byte)(offset % 13));
        using var fast = new MemoryStream(data, writable: false);
        using var slow = new MemoryStream(data, writable: false);
        var writes = await Task.WhenAll(
            engine.WriteBlobAsync(fast, CompressionType.Zstd, CompressionLevel.Fastest, token),
            engine.WriteBlobAsync(slow, CompressionType.Zstd, CompressionLevel.Ultra, token));
        var blob = Directory.EnumerateFiles(Path.Combine(_testRootPath, "blobs"), "*.blob", SearchOption.AllDirectories).Single();
        Assert.Equal(writes[0].Hash, writes[1].Hash);
        Assert.All(writes, result => Assert.Equal(new FileInfo(blob).Length, result.CompressedSize));
    }

    [Fact]
    public async Task WriteBlobAsync_InvalidCompressionLevel_UsesDefaultLevel()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('X', 1000));
        await using var inputStream = new MemoryStream(testData);

        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.Zstd,
            (CompressionLevel)999,
            CancellationToken.None);

        Assert.NotNull(hash);
        Assert.Equal(testData.Length, size);
        Assert.True(compressedSize < size);
    }

    [Fact]
    public async Task WriteBlobAsync_ShortHash_CanBeRetrieved()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Data with short hash test"u8.ToArray();

        await using var inputStream = new MemoryStream(testData);
        var (hash, size, compressedSize) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.True(hash.Length >= 4);

        await using var readStream = engine.OpenBlobRead(hash, CompressionType.None);
        Assert.NotNull(readStream);
    }

    [Fact]
    public async Task WriteBlobAsync_MultipleCompressionLevelsZstd_AllSucceed()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('M', 10000));

        foreach (CompressionLevel level in Enum.GetValues<CompressionLevel>())
        {
            await using var inputStream = new MemoryStream(testData);
            var (hash, size, compressedSize) = await engine.WriteBlobAsync(
                inputStream,
                CompressionType.Zstd,
                level,
                CancellationToken.None);

            Assert.NotNull(hash);
            Assert.Equal(testData.Length, size);
        }
    }

    [Fact]
    public async Task WriteBlobAsync_MultipleCompressionLevelsLZ4_AllSucceed()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = Encoding.UTF8.GetBytes(new string('N', 10000));

        foreach (CompressionLevel level in Enum.GetValues<CompressionLevel>())
        {
            await using var inputStream = new MemoryStream(testData);
            var (hash, size, compressedSize) = await engine.WriteBlobAsync(
                inputStream,
                CompressionType.LZ4,
                level,
                CancellationToken.None);

            Assert.NotNull(hash);
            Assert.Equal(testData.Length, size);
        }
    }

    [Fact]
    public void CleanupOrphanedTempFiles_WithLockedFile_PreservesTheOpenStream()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var tmpPath = Path.Combine(_testRootPath, "tmp");
        var tempFile = Path.Combine(tmpPath, "locked.tmp");

        File.WriteAllText(tempFile, "locked content");
        using var fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        engine.CleanupOrphanedTempFiles();

        Assert.Equal(OperatingSystem.IsWindows(), File.Exists(tempFile));
        Assert.Equal((int)'l', fileStream.ReadByte());
    }

    [Fact]
    public void OpenBlobRead_ShortHash_ThrowsArgumentOutOfRangeException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        const string shortHash = "abc";

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => engine.OpenBlobRead(shortHash, CompressionType.None));
    }

    [Fact]
    public void DeleteBlob_ShortHash_ThrowsArgumentException()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        const string shortHash = "ab";

        Assert.Throws<ArgumentException>(() => engine.DeleteBlob(shortHash));
    }

    [Fact]
    public async Task WriteBlobAsync_VerifyHashIsLowercase()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Verify hash format"u8.ToArray();
        await using var inputStream = new MemoryStream(testData);

        var (hash, _, _) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public async Task WriteBlobAsync_VerifyHashIsSHA256()
    {
        var engine = new LocalBlobStorageEngine(_testRootPath, _mockLogger.Object);
        var testData = "Verify hash length"u8.ToArray();
        await using var inputStream = new MemoryStream(testData);

        var (hash, _, _) = await engine.WriteBlobAsync(
            inputStream,
            CompressionType.None,
            CompressionLevel.Balanced,
            CancellationToken.None);

        Assert.Equal(64, hash.Length);
    }

    private sealed class SlowStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= data.Length)
                return 0;

            var toRead = Math.Min(count, data.Length - _position);
            Array.Copy(data, _position, buffer, offset, toRead);
            _position += toRead;
            Thread.Sleep(10);
            return toRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_position >= data.Length)
                return 0;

            var toRead = Math.Min(buffer.Length, data.Length - _position);
            data.AsMemory(_position, toRead).CopyTo(buffer);
            _position += toRead;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
