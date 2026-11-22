namespace Vessel.Tests;

public sealed class PutDeduplicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VesselPutDeduplication-{Guid.NewGuid():N}");

    [Fact]
    public async Task PutRejectsTruncatedUncompressedBlobDuringDeduplication()
    {
        var token = TestContext.Current.CancellationToken;
        byte[] duplicate = [17, 18, 19];
        byte[] previousBytes = [21, 22, 23, 24];
        ObjectMetadata previousMetadata;

        using (var storage = new VesselStorage(_root))
        {
            using var source = new MemoryStream(duplicate);
            using var previous = new MemoryStream(previousBytes);
            var shared = await storage.PutAsync("shared", source, cancellationToken: token);
            previousMetadata = await storage.PutAsync("existing", previous, cancellationToken: token);
            var hash = shared.Hash;
            var blob = Path.Combine(_root, "blobs", hash[..2], hash[2..4], $"{hash}.blob");
            await File.WriteAllBytesAsync(blob, [duplicate[0]], token);

            using var alias = new MemoryStream(duplicate);
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.PutAsync("alias", alias, cancellationToken: token));
            Assert.Null(storage.GetMetadata("alias"));

            using var replacement = new MemoryStream(duplicate);
            await Assert.ThrowsAsync<InvalidDataException>(() => storage.PutAsync("existing", replacement, overwrite: true,
                cancellationToken: token));
            Assert.Equal(previousMetadata, storage.GetMetadata("existing"));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "tmp")));
        }

        using var reopened = new VesselStorage(_root);
        Assert.Null(reopened.GetMetadata("alias"));
        using var reader = reopened.GetStream("existing");
        using var content = new MemoryStream();
        await reader.CopyToAsync(content, token);
        Assert.Equal(previousBytes, content.ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
