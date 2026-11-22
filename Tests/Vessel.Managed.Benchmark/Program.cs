using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vessel;

if (args.Length != 5 ||
    !int.TryParse(args[0], out var objects) || objects <= 0 ||
    !int.TryParse(args[1], out var operations) || operations <= 0 ||
    !int.TryParse(args[2], out var workers) || workers <= 0 || workers > operations ||
    !int.TryParse(args[3], out var payloadSize) || payloadSize < sizeof(int) ||
    !Enum.TryParse<CompressionType>(args[4], ignoreCase: true, out var codec) || !Enum.IsDefined(codec))
{
    Console.Error.WriteLine("Usage: Vessel.Managed.Benchmark <objects> <ops-per-phase> <workers> <payload-bytes> <none|lz4|zstd>");
    return 2;
}

var root = Path.Combine(Path.GetTempPath(), $"vessel-managed-benchmark-{Guid.NewGuid():N}");
var existing = Enumerable.Range(0, objects).Select(i => $"existing/{i:D8}").ToArray();
var newKeys = Enumerable.Range(0, operations).Select(i => $"new/{i:D8}").ToArray();
var payload = new byte[payloadSize];
new Random(42721).NextBytes(payload.AsSpan(0, payloadSize / 2));
var buffers = Enumerable.Range(0, workers).Select(_ => payload.ToArray()).ToArray();
var drive = new DriveInfo(Path.GetPathRoot(root)!);
Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription} os={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture} logical_cpus={Environment.ProcessorCount} filesystem={drive.DriveFormat}");
Console.WriteLine($"objects={objects} operations={operations} workers={workers} bytes={payloadSize} codec={codec} seed=42721");

try
{
    CollectGarbage();
    using (var seed = MeasureOpen("first-empty-open", root))
    {
        // A bounded set of distinct blobs allows measuring large metadata corpora.
        var distinct = Math.Min(64, objects);
        for (var i = 0; i < existing.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload, i % distinct);
            await PutAsync(seed, existing[i], payload, overwrite: false, codec);
        }
        Console.WriteLine($"distinct_seed_blobs={distinct}");
    }

    CollectGarbage();
    using var store = MeasureOpen("warm-reopen", root);
    if (store.GetMetadata(existing[^1]) is null) throw new InvalidOperationException("The corpus did not reopen.");

    await MeasureAsync("metadata-mixed-hit-miss", operations, workers, (_, index) =>
    {
        var present = index % 2 == 0;
        var metadata = store.GetMetadata(present ? existing[index % objects] : newKeys[index]);
        if (metadata.HasValue != present) throw new InvalidOperationException("Unexpected key visibility.");
        return Task.CompletedTask;
    });
    await MeasureAsync("stream-read", operations, workers, async (_, index) =>
    {
        using var stream = store.GetStream(existing[index % objects]);
        if (stream.Length != payloadSize) throw new InvalidOperationException("Unexpected payload length.");
        await stream.CopyToAsync(Stream.Null);
    });
    await MeasureAsync("put-distinct", operations, workers, (worker, index) =>
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffers[worker], ~index);
        return PutAsync(store, newKeys[index], buffers[worker], overwrite: false, codec);
    });
    await MeasureAsync("overwrite-dedup", operations, workers, (_, index) =>
        PutAsync(store, newKeys[index], payload, overwrite: true, codec));
    await MeasureAsync("delete", operations, workers, async (_, index) =>
    {
        if (!await store.DeleteAsync(newKeys[index])) throw new InvalidOperationException("The inserted key is missing.");
    });
    using var process = Process.GetCurrentProcess();
    Console.WriteLine($"peak_working_set_bytes={process.PeakWorkingSet64} private_bytes={process.PrivateMemorySize64}");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static async Task PutAsync(VesselStorage store, string key, byte[] payload, bool overwrite, CompressionType codec)
{
    using var input = new MemoryStream(payload, writable: false);
    await store.PutAsync(key, input, overwrite, compressionType: codec);
}

static VesselStorage MeasureOpen(string phase, string root)
{
    using var process = Process.GetCurrentProcess();
    var beforePrivate = process.PrivateMemorySize64;
    var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
    var start = Stopwatch.GetTimestamp();
    var store = new VesselStorage(root);
    var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;
    process.Refresh();
    var privateDelta = process.PrivateMemorySize64 - beforePrivate;
    CollectGarbage();
    var loh = GC.GetGCMemoryInfo().GenerationInfo[3].SizeAfterBytes;
    Console.WriteLine(FormattableString.Invariant($"{phase}: ms={milliseconds:F3} allocated_bytes={allocated} private_delta_bytes={privateDelta} loh_after_gc_bytes={loh}"));
    return store;
}

static async Task MeasureAsync(string phase, int operations, int workers, Func<int, int, Task> operation)
{
    var samples = new long[operations];
    var beforeAllocated = GC.GetTotalAllocatedBytes(precise: true);
    var beforeGen2 = GC.CollectionCount(2);
    var start = Stopwatch.GetTimestamp();
    await Parallel.ForAsync(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, async (worker, _) =>
    {
        for (var index = worker; index < operations; index += workers)
        {
            var opStart = Stopwatch.GetTimestamp();
            await operation(worker, index);
            samples[index] = Stopwatch.GetTimestamp() - opStart;
        }
    });
    var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - beforeAllocated;
    var gen2 = GC.CollectionCount(2) - beforeGen2;
    Array.Sort(samples);
    Console.WriteLine(FormattableString.Invariant($"{phase}: ops_per_sec={operations / elapsed:F1} p50_us={Percentile(0.50):F1} p95_us={Percentile(0.95):F1} p99_us={Percentile(0.99):F1} allocated_bytes_per_op={allocated / (double)operations:F1} gen2={gen2}"));

    double Percentile(double percentile) => samples[(int)Math.Ceiling(samples.Length * percentile) - 1] * (1_000_000.0 / Stopwatch.Frequency);
}

static void CollectGarbage()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}
