using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Vessel.Benchmark;

public class QuickBenchmarks : StorageBenchmarks
{
    [GlobalSetup]
    public Task SetupAsync() => InitializeAsync(128, 4, null, null);
}

[SimpleJob(RuntimeMoniker.Net10_0, baseline: true)]
[SimpleJob(RuntimeMoniker.NativeAot10_0)]
public class FullBenchmarks : StorageBenchmarks
{
    [Params(1024, 64 * 1024, 512 * 1024)]
    public int PayloadSize { get; set; }

    [Params(CompressionType.None, CompressionType.Zstd, CompressionType.LZ4)]
    public CompressionType Codec { get; set; }

    [Params(1, 8)]
    public int Workers { get; set; }

    [GlobalSetup]
    public Task SetupAsync() => InitializeAsync(256, Workers, PayloadSize, Codec);
}

[MemoryDiagnoser]
public abstract class StorageBenchmarks
{
    private const int BatchSize = 32;
    private static readonly byte[] s_document = """{"type":"invoice","customer":"sample","currency":"EUR","total":131.72}"""u8.ToArray();
    private static readonly ObjectInfo s_fileInfo = new("payload.bin", ".bin", "application/octet-stream");

    private Sample[] _writes = [];
    private WorkItem[] _mixed = [];
    private string _root = null!;
    private VesselStorage _store = null!;
    private Sample[] _samples = null!;
    private Random _random = null!;
    private int _workers;
    private long _nextWrite;

    protected async Task InitializeAsync(int count, int workers, int? size, CompressionType? codec)
    {
        var seed = int.TryParse(Environment.GetEnvironmentVariable("VESSEL_BENCH_SEED"), out var configuredSeed)
            ? configuredSeed
            : 42721;
        _random = new Random(seed);
        _workers = workers;
        _root = Path.Combine(Path.GetTempPath(), "Vessel.Benchmarks", Guid.NewGuid().ToString("N"));
        _store = new VesselStorage(_root);
        _samples = new Sample[count];

        for (var index = 0; index < count; index++)
        {
            var length = size ?? (_random.Next(100) switch
            {
                < 48 => 1024,
                < 75 => 8 * 1024,
                < 94 => 64 * 1024,
                _ => 512 * 1024
            });
            var compression = codec ?? (CompressionType)_random.Next(3);
            var bytes = new byte[length];
            if (_random.Next(3) == 0)
                _random.NextBytes(bytes);
            else
                for (var offset = 0; offset < bytes.Length; offset += s_document.Length)
                    s_document.AsSpan(0, Math.Min(s_document.Length, bytes.Length - offset)).CopyTo(bytes.AsSpan(offset));

            BinaryPrimitives.WriteInt64LittleEndian(bytes, index);
            var key = $"corpus/{index / 32:D2}/{index:D4}";
            _samples[index] = new Sample(key, bytes, compression);
            using var input = new MemoryStream(bytes, writable: false);
            await _store.PutAsync(key, input, fileInfo: s_fileInfo, compressionType: compression).ConfigureAwait(false);
        }

        var sample = _samples[^1];
        using var stream = _store.GetStream(sample.Key);
        if (stream.Length != sample.Payload.Length || stream.Metadata?.CompressionType != sample.Codec)
            throw new InvalidOperationException("The read corpus does not match the benchmark scenario.");
    }

    [Benchmark(OperationsPerInvoke = BatchSize, Description = "Read existing objects")]
    public Task Read()
    {
        var reads = SelectSamples();
        return RunAsync(index => ReadAsync(reads[index].Key));
    }

    [IterationSetup(Target = nameof(WriteNew))]
    public void PrepareNew()
        => _writes = SelectSamples().Select(sample => CreateWrite(sample, newKey: true)).ToArray();

    [Benchmark(OperationsPerInvoke = BatchSize, Description = "Put unique payloads")]
    public Task WriteNew() => RunAsync(index => WriteAsync(_writes[index], overwrite: false));

    [IterationSetup(Target = nameof(Overwrite))]
    public void PrepareOverwrites()
    {
        var first = _random.Next(_samples.Length);
        _writes = Enumerable.Range(0, BatchSize)
            .Select(index => CreateWrite(_samples[(first + index) % _samples.Length], newKey: false)).ToArray();
    }

    [Benchmark(OperationsPerInvoke = BatchSize, Description = "Overwrite existing objects")]
    public Task Overwrite() => RunAsync(index => WriteAsync(_writes[index], overwrite: true));

    [IterationSetup(Target = nameof(Mixed))]
    public void PrepareMixed()
    {
        var samples = SelectSamples();
        var first = _random.Next(_samples.Length);
        _mixed = samples.Select((sample, index) => index switch
        {
            < 24 => new WorkItem(Operation.Read, sample),
            < 29 => new WorkItem(Operation.Overwrite, CreateWrite(_samples[(first + index) % _samples.Length], newKey: false)),
            _ => new WorkItem(Operation.Churn, CreateWrite(sample, newKey: true))
        }).ToArray();
        _random.Shuffle(_mixed);
    }

    [Benchmark(OperationsPerInvoke = BatchSize, Description = "Mixed reads, overwrites and churn")]
    public Task Mixed() => RunAsync(async index =>
    {
        var (operation, sample) = _mixed[index];
        switch (operation)
        {
            case Operation.Read:
                await ReadAsync(sample.Key).ConfigureAwait(false);
                break;
            case Operation.Overwrite:
                await WriteAsync(sample, overwrite: true).ConfigureAwait(false);
                break;
            case Operation.Churn:
                await WriteAsync(sample, overwrite: false).ConfigureAwait(false);
                await _store.DeleteAsync(sample.Key).ConfigureAwait(false);
                break;
        }
    });

    [IterationCleanup(Target = nameof(WriteNew))]
    public void RemoveNewObjects()
    {
        foreach (var sample in _writes)
            _store.DeleteAsync(sample.Key).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _store?.Dispose();
        if (_root is not null && Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private Sample[] SelectSamples()
    {
        var selected = new Sample[BatchSize];
        for (var index = 0; index < selected.Length; index++)
        {
            var hot = _random.Next(100) < 75;
            selected[index] = _samples[_random.Next(hot ? _samples.Length / 5 : _samples.Length)];
        }
        return selected;
    }

    private Sample CreateWrite(Sample sample, bool newKey)
    {
        var number = ++_nextWrite;
        var bytes = (byte[])sample.Payload.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(bytes, -number);
        return new Sample(newKey ? $"inserts/{number:D10}" : sample.Key, bytes, sample.Codec);
    }

    private Task RunAsync(Func<int, Task> operation) => Parallel.ForAsync(0, BatchSize,
        new ParallelOptions { MaxDegreeOfParallelism = _workers },
        (index, _) => new ValueTask(operation(index)));

    private async Task ReadAsync(string key)
    {
        using var stream = _store.GetStream(key);
        await stream.CopyToAsync(Stream.Null).ConfigureAwait(false);
    }

    private async Task WriteAsync(Sample sample, bool overwrite)
    {
        using var input = new MemoryStream(sample.Payload, writable: false);
        await _store.PutAsync(sample.Key, input, overwrite, s_fileInfo, sample.Codec).ConfigureAwait(false);
    }

    private sealed record Sample(string Key, byte[] Payload, CompressionType Codec);
    private readonly record struct WorkItem(Operation Operation, Sample Sample);
    private enum Operation { Read, Overwrite, Churn }
}
