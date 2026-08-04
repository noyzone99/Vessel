using Vessel;
using Vessel.Models;
using Vessel.TestZoneTree;
using ZoneTree;
using ZoneTree.Options;

using var zoneTree = new ZoneTreeFactory<string, ObjectMetadata>()
    .SetDataDirectory(".storage")
    .ConfigureDiskSegmentOptions(options =>
    {
        options.CompressionMethod = CompressionMethod.LZ4;
        options.CompressionLevel = CompressionLevels.LZ4Fastest;
        options.CompressionBlockSize = 4 * 1024 * 1024;
    })
    .SetValueSerializer(new ObjectMetadataSerializer())
    .OpenOrCreate();
using var maintainer = zoneTree.CreateMaintainer();

zoneTree.Upsert("example-key", new ObjectMetadata
{
    Key = "example-key",
    Hash = "example-hash",
    Size = 12345,
    CompressedSize = 6789,
    CompressionType = CompressionType.LZ4,
    LastModified = DateTimeOffset.UtcNow,
    ETag = "example-etag",
    FileName = "example-file.txt",
    Extension = ".txt",
    MimeType = "text/plain"
});

if (zoneTree.TryGet("example-key", out var value))
{
    Console.WriteLine(value);
}