// -----------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using System.Reflection;
using Vessel.Logging;

namespace Vessel.Benchmark;

// For more information on the VS BenchmarkDotNet Diagnosers see https://learn.microsoft.com/visualstudio/profiling/profiling-with-benchmark-dotnet
[CPUUsageDiagnoser]
[MemoryDiagnoser]
public class Benchmarks
{
    private const string Key = "benchmark-json";
    private static readonly Type StorageImplementationType = typeof(IVesselStorage).Assembly.GetType("Vessel.VesselStorage")
        ?? throw new InvalidOperationException("Could not locate the Vessel storage implementation.");

    private static readonly ConstructorInfo StorageConstructor = StorageImplementationType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        [typeof(string), typeof(bool), typeof(IVesselLogger)],
        modifiers: null)
        ?? throw new InvalidOperationException("Could not locate a suitable Vessel storage constructor.");

    private IVesselStorage? _storage;
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
        _storage = CreateStorage(_storageRoot);

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
    public async Task CleanupAsync()
    {
        if (_storage is not null)
        {
            await _storage.DisposeAsync();
        }

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
    [BenchmarkCategory("Read")]
    public async Task ReadJsonPayload()
    {
        ArgumentNullException.ThrowIfNull(_storage);

        await using var response = await _storage.GetAsync(Key);
        await response.ContentStream.CopyToAsync(Stream.Null);
    }

    private static IVesselStorage CreateStorage(string storageRoot)
    {
        return (IVesselStorage)StorageConstructor.Invoke([storageRoot, false, null]);
    }
}
