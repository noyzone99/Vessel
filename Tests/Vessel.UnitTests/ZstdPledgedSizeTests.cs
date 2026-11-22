using ZstdSharp;

namespace Vessel.Tests;

public sealed class ZstdPledgedSizeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VesselZstd-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4_096, 17)]
    [InlineData(65_536, 0)]
    [InlineData(16, 20)]
    public async Task ReadOnlyMemoryStreamPledgesItsRemainingBytes(int length, int offset)
    {
        var token = TestContext.Current.CancellationToken;
        var payload = new byte[length];
        new Random(42721).NextBytes(payload);
        var expected = payload[Math.Min(offset, length)..];

        using (var storage = new VesselStorage(_root))
        {
            using var input = new MemoryStream(payload, writable: false) { Position = offset };
            var stored = await storage.PutAsync("blob", input, compressionType: CompressionType.Zstd,
                compressionLevel: CompressionLevel.Ultra, cancellationToken: token);
            Assert.Equal(expected.Length, stored.OriginalSize);

            var hash = stored.Hash;
            var blob = Path.Combine(_root, "blobs", hash[..2], hash[2..4], $"{hash}.blob");
            var frame = await File.ReadAllBytesAsync(blob, token);
            Assert.Equal((ulong)expected.Length, Decompressor.GetDecompressedSize(frame.AsSpan()));
        }

        using var reopened = new VesselStorage(_root);
        await VerifyRead(reopened, expected, token);
    }

    [Fact]
    public async Task CustomSeekableStreamDoesNotNeedToReportLength()
    {
        var token = TestContext.Current.CancellationToken;
        byte[] payload = [17, 18, 19];
        using var storage = new VesselStorage(_root);
        using var input = new CustomMemoryStream(payload);
        await storage.PutAsync("blob", input, compressionType: CompressionType.Zstd, cancellationToken: token);
        await VerifyRead(storage, payload, token);
    }

    private static async Task VerifyRead(VesselStorage storage, byte[] expected, CancellationToken token)
    {
        using var reader = storage.GetStream("blob");
        using var copy = new MemoryStream();
        await reader.CopyToAsync(copy, token);
        Assert.Equal(expected, copy.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CustomMemoryStream(byte[] payload) : MemoryStream(payload, writable: false)
    {
        public override long Length => throw new NotSupportedException();
    }
}
