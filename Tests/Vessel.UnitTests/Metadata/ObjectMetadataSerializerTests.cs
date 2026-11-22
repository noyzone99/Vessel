namespace Vessel.Metadata;

public sealed class ObjectMetadataSerializerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTripPreservesAndReusesEqualIdentifiers(bool sameIdentifier)
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var eTag = sameIdentifier ? hash : "legacy-etag";
        var metadata = new ObjectMetadata("key/È/😀", hash, 10, CompressionType.None, eTag, DateTimeOffset.UnixEpoch, "é.bin");
        var serializer = new ObjectMetadataSerializer();

        var buffer = new byte[serializer.GetMaxByteCount(metadata)];
        Assert.Equal(buffer.Length, serializer.Serialize(metadata, buffer));
        Assert.Equal(serializer.Serialize(metadata).ToArray(), buffer);
        var restored = serializer.Deserialize(buffer);

        Assert.Equal(metadata, restored);
        if (sameIdentifier) Assert.Same(restored.Hash, restored.ETag);
    }
}
