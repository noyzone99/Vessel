using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.VSDiagnostics;
using Vessel.Models;

namespace Vessel.Benchmark;

[CPUUsageDiagnoser]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, baseline: true)]
//[SimpleJob(RuntimeMoniker.NativeAot10_0)]
public class Benchmarks
{
    private VesselStorage? _storage;
    private byte[] _payloadBytes = [];
    private string _storageRoot = string.Empty;
    private int _iteration = 0;

    //[Params(CompressionType.None, CompressionType.Zstd, CompressionType.LZ4)]
    public CompressionType CompressionType { get; set; } = CompressionType.None;

    [Params(1024, 1024 * 512)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "Vessel.Benchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);

        _payloadBytes = new byte[PayloadSize];
        Random.Shared.NextBytes(_payloadBytes);

        _storage = new VesselStorage(_storageRoot, new VesselStorageOptions());

        for (int i = 0; i < 100; i++)
        {
            await _storage.PutAsync($"init-{i}", new MemoryStream(new byte[100]));
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _storage?.Dispose();
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    [Benchmark(Description = "Writing of new object (I/O Complete)")]
    public async Task WriteNewObject()
    {
        _payloadBytes[0] = (byte)(_iteration++ % 255);

        var key = $"key-{_iteration}";
        await using var ms = new MemoryStream(_payloadBytes);
        await _storage!.PutAsync(key, ms, false, new ObjectInfo("data.bin"), CompressionType);
    }

    [Benchmark(Description = "Overwriting existing object (Update Metadata)")]
    public async Task OverwriteExisting()
    {
        _payloadBytes[0] = (byte)(_iteration++ % 255);

        await using var ms = new MemoryStream(_payloadBytes);
        await _storage!.PutAsync("constant-key", ms, true, new ObjectInfo("data.bin"), CompressionType);
    }

    [Benchmark(Description = "Random read (Simulates web traffic)")]
    public void ReadRandom()
    {
        var key = $"init-{Random.Shared.Next(0, 100)}";
        using var response = _storage!.Get(key);
        response.ContentStream.CopyTo(Stream.Null);
    }
}