// -----------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.VSDiagnostics;

namespace Vessel.Benchmark;

// For more information on the VS BenchmarkDotNet Diagnosers see https://learn.microsoft.com/visualstudio/profiling/profiling-with-benchmark-dotnet
[CPUUsageDiagnoser]
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, baseline: true)]
//[SimpleJob(RuntimeMoniker.NativeAot10_0)]
public class Benchmarks
{
    private const string Key = "benchmark-json";

    private VesselStorage? _storage;
    private byte[] _payloadBytes = [];
    private string _storageRoot = string.Empty;

    [Params(CompressionType.None, CompressionType.Zstd, CompressionType.LZ4)]
    public CompressionType CompressionType { get; set; }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), "Vessel.Benchmarks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
        _payloadBytes = await File.ReadAllBytesAsync("sample.json");
        _storage = new VesselStorage(_storageRoot, new VesselStorageOptions());

        await _storage.PutAsync(
            Key,
            new MemoryStream(_payloadBytes, writable: false),
            overwrite: true,
            fileName: "benchmark.json",
            extension: ".json",
            mimeType: "application/json",
            compressionType: CompressionType,
            compressionLevel: CompressionLevel.Balanced);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _storage?.Dispose();

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Write")]
    public async Task WriteJsonPayload()
    {
        ArgumentNullException.ThrowIfNull(_storage);

        await using var payloadStream = new MemoryStream(_payloadBytes, writable: false);
        await _storage.PutAsync(
            Key,
            payloadStream,
            overwrite: true,
            fileName: "benchmark.json",
            extension: ".json",
            mimeType: "application/json",
            compressionType: CompressionType,
            compressionLevel: CompressionLevel.Balanced);
    }

    [Benchmark]
    [BenchmarkCategory("ReadAsync")]
    public async Task ReadJsonPayloadAsync()
    {
        ArgumentNullException.ThrowIfNull(_storage);

        await using var response = await _storage.GetAsync(Key);
        await response.ContentStream.CopyToAsync(Stream.Null);
    }

    [Benchmark]
    [BenchmarkCategory("ReadSync")]
    public void ReadJsonPayloadSync()
    {
        ArgumentNullException.ThrowIfNull(_storage);

        using var response = _storage.Get(Key);
        response.ContentStream.CopyTo(Stream.Null);
    }
}
