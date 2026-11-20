using System.Buffers.Binary;
using System.Security.Cryptography;
using Vessel.Metadata;
using ZoneTree;
using ZoneTree.Options;

namespace Vessel.Tests;

public sealed class StorageReliabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VesselReliability-{Guid.NewGuid():N}");

    [Fact]
    public async Task SharedBlobsRemainConsistentDuringMixedTrafficAndReopen()
    {
        var token = TestContext.Current.CancellationToken;
        var random = new Random(724087);
        var shared = new byte[65_537];
        random.NextBytes(shared);
        var workload = Enumerable.Range(0, 96).Select(index =>
        {
            var payload = index % 3 == 0 ? shared : new byte[random.Next(1, 130_000)];
            if (payload != shared) random.NextBytes(payload);
            return new Sample($"objects/{index:D3}", payload, (CompressionType)(index / 3 % 3));
        }).ToArray();
        var expected = new Dictionary<string, Sample>();

        using (var storage = new VesselStorage(_root))
        {
            await Parallel.ForEachAsync(workload, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = token },
                async (sample, cancellation) => await Put(storage, sample, false, cancellation));

            using var snapshot = storage.GetStream(workload[0].Key);
            var operations = workload.Select((sample, index) =>
            {
                if (index % 4 == 1) return (Sample: sample, Delete: true, Overwrite: false);
                if (index % 4 == 0)
                {
                    sample = sample with { Bytes = [(byte)index, 0, 255], Codec = CompressionType.Zstd };
                    expected.Add(sample.Key, sample);
                    return (Sample: sample, Delete: false, Overwrite: true);
                }

                expected.Add(sample.Key, sample);
                return (Sample: sample, Delete: false, Overwrite: false);
            }).ToArray();
            random.Shuffle(operations);
            await Parallel.ForEachAsync(operations, new ParallelOptions { MaxDegreeOfParallelism = 12, CancellationToken = token },
                async (operation, cancellation) =>
                {
                    if (operation.Delete)
                        Assert.True(await storage.DeleteAsync(operation.Sample.Key, cancellation));
                    else if (operation.Overwrite)
                        await Put(storage, operation.Sample, true, cancellation);
                    else
                        await Verify(storage, operation.Sample, cancellation);
                });

            using var oldBytes = new MemoryStream();
            await snapshot.CopyToAsync(oldBytes, token);
            Assert.Equal(shared, oldBytes.ToArray());
        }

        using var reopened = new VesselStorage(_root, new VesselStorageOptions(EnableMetadataCompression: true));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), reopened.GetAllKeys("objects/"));
        await Parallel.ForEachAsync(expected.Values, token, async (sample, cancellation) => await Verify(reopened, sample, cancellation));
        var distinctBlobs = expected.Keys.Select(key => reopened.GetMetadata(key)!.Value.Hash).Distinct().Count();
        Assert.Equal(distinctBlobs, Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.blob", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task CancelledOverwriteKeepsPreviousVersionAndLetsOtherKeysChange()
    {
        var token = TestContext.Current.CancellationToken;
        using var storage = new VesselStorage(_root);
        var previous = new Sample("existing", [1, 23, 4], CompressionType.None);
        await Put(storage, previous, false, token);
        await Put(storage, new Sample("other", [17], CompressionType.None), false, token);
        using var source = new CancellationTokenSource();
        using var input = new WaitingStream();
        var pending = storage.PutAsync(previous.Key, input, overwrite: true, cancellationToken: source.Token);
        await input.Started.Task.WaitAsync(token);
        try
        {
            Assert.True(await storage.DeleteAsync("other", token).WaitAsync(TimeSpan.FromSeconds(5), token));
            await Verify(storage, previous, token);
        }
        finally { await source.CancelAsync(); }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Verify(storage, previous, token);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "tmp")));
    }

    [Fact]
    public async Task OpenRecoversOrphansAndLocksDirectory()
    {
        var token = TestContext.Current.CancellationToken;
        var sample = new Sample("legacy/item", [19, 28, 29], CompressionType.None);
        using (var storage = new VesselStorage(_root))
        {
            await Put(storage, sample, false, token);
            Assert.ThrowsAny<IOException>(() => new VesselStorage(_root));
        }

        var temporaryFile = Path.Combine(_root, "tmp", "interrupted.tmp");
        await File.WriteAllTextAsync(temporaryFile, "incomplete", token);
        var orphanHash = new string('b', 64);
        var orphanDirectory = Directory.CreateDirectory(Path.Combine(_root, "blobs", "bb", "bb")).FullName;
        var orphanFile = Path.Combine(orphanDirectory, $"{orphanHash}.blob");
        await File.WriteAllBytesAsync(orphanFile, [98, 97], token);
        var unrelatedFile = Path.Combine(orphanDirectory, "unrelated.blob");
        await File.WriteAllTextAsync(unrelatedFile, "unrelated", token);

        using var reopened = new VesselStorage(_root);
        Assert.False(File.Exists(temporaryFile));
        Assert.False(File.Exists(orphanFile));
        Assert.True(File.Exists(unrelatedFile));
        await Verify(reopened, sample, token);
    }

    [Fact]
    public async Task ReopensLegacyMetadataWalAndPayload()
    {
        var token = TestContext.Current.CancellationToken;
        var sample = new Sample("legacy/€uro", [50, 20, 13], CompressionType.None);
        var hash = Convert.ToHexStringLower(SHA256.HashData(sample.Bytes));
        var blobPath = Directory.CreateDirectory(Path.Combine(_root, "blobs", hash[..2], hash[2..4])).FullName;
        await File.WriteAllBytesAsync(Path.Combine(blobPath, $"{hash}.blob"), sample.Bytes, token);
        var store = new ZoneTreeFactory<string, ObjectMetadata>()
            .SetDataDirectory(Path.Combine(_root, "metadata"))
            .ConfigureWriteAheadLogOptions(options => options.WriteAheadLogMode = WriteAheadLogMode.AsyncCompressed)
            .ConfigureDiskSegmentOptions(options => options.CompressionMethod = CompressionMethod.None)
            .SetValueSerializer(new ObjectMetadataSerializer())
            .SetIsDeletedDelegate(static (in _, in value) => string.IsNullOrEmpty(value.Key))
            .SetMarkValueDeletedDelegate(static (ref value) => value = default);
        using (var legacy = store.OpenOrCreate())
        {
            legacy.Upsert(sample.Key, new ObjectMetadata(sample.Key, hash, sample.Bytes.Length, sample.Codec, hash,
                DateTimeOffset.UtcNow, CompressedSize: sample.Bytes.Length));
            legacy.Upsert("legacy/invalid", new ObjectMetadata("legacy/invalid", hash + "-99", 10,
                (CompressionType)99, hash, DateTimeOffset.UtcNow));
        }

        using var reopened = new VesselStorage(_root);
        await Verify(reopened, sample, token);
        Assert.Throws<InvalidDataException>(() => reopened.GetStream("legacy/invalid"));
        Assert.True(await reopened.DeleteAsync("legacy/invalid", token));
        Assert.Equal([sample.Key], reopened.GetAllKeys("legacy/"));
    }

    [Theory]
    [InlineData(512, CompressionType.None)]
    [InlineData(65_537, CompressionType.Zstd)]
    [InlineData(0, CompressionType.LZ4)]
    [InlineData(0, CompressionType.Zstd)]
    public async Task ConsumesStreamFromItsCurrentPosition(int length, CompressionType codec)
    {
        var token = TestContext.Current.CancellationToken;
        var payload = new byte[length];
        new Random(length).NextBytes(payload);
        var offset = Math.Min(11, length);
        using var storage = new VesselStorage(_root);
        using var input = new MemoryStream(payload) { Position = offset };
        var metadata = await storage.PutAsync("test", input, compressionType: codec, cancellationToken: token);
        Assert.Equal(length - offset, metadata.OriginalSize);
        await Verify(storage, new Sample("test", payload[offset..], codec), token);
    }

    [Fact]
    public async Task RejectsInvalidCodecAndReportsCorruptBlob()
    {
        var token = TestContext.Current.CancellationToken;
        using var storage = new VesselStorage(_root);
        using var input = new MemoryStream([23, 41, 17]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            storage.PutAsync("invalid", input, compressionType: (CompressionType)99, cancellationToken: token));
        var metadata = await storage.PutAsync("truncated", input, cancellationToken: token);
        var path = Directory.EnumerateFiles(Path.Combine(_root, "blobs"), $"{metadata.Hash}.blob", SearchOption.AllDirectories).Single();
        await File.WriteAllBytesAsync(path, [23], token);
        Assert.Throws<InvalidDataException>(() => storage.GetStream("truncated"));
        Assert.Null(storage.GetMetadata("invalid"));
    }

    [Fact]
    public void InvalidMetadataLengthHasAnExplicitError()
    {
        var serializer = new ObjectMetadataSerializer();
        var record = serializer.Serialize(default);
        BinaryPrimitives.WriteInt32LittleEndian(record.Span[28..], int.MaxValue);
        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(record));
    }

    private static async Task Put(VesselStorage storage, Sample sample, bool overwrite, CancellationToken token)
    {
        using var input = new MemoryStream(sample.Bytes, writable: false);
        await storage.PutAsync(sample.Key, input, overwrite, compressionType: sample.Codec, cancellationToken: token);
    }

    private static async Task Verify(VesselStorage storage, Sample sample, CancellationToken token)
    {
        using var stream = storage.GetStream(sample.Key);
        var hash = Convert.ToHexStringLower(SHA256.HashData(sample.Bytes));
        Assert.Equal(hash, stream.Checksum);
        Assert.Equal(sample.Codec, stream.Metadata?.CompressionType);
        Assert.Equal(sample.Bytes.Length, stream.Length);
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination, token);
        Assert.Equal(sample.Bytes, destination.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record Sample(string Key, byte[] Bytes, CompressionType Codec);

    private sealed class WaitingStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
