using System.IO.Pipelines;
using System.Runtime.InteropServices;

namespace Vessel.Native;

internal sealed class NativeUpload : IDisposable
{
    private const int ChunkSize = 64 * 1024;
    private readonly Pipe _pipe = new(new PipeOptions(pauseWriterThreshold: 4 * ChunkSize,
        resumeWriterThreshold: 2 * ChunkSize, useSynchronizationContext: false));
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Lock _writer = new();
    private Task _completion = Task.CompletedTask;
    private Task<FlushResult>? _pendingFlush;
    private int _ended;

    internal void Start(NativeHandles<VesselStorage>.Lease store, string key, bool overwrite, ObjectInfo info,
        CompressionType codec, CompressionLevel level, Action<Task<ObjectMetadata>> complete)
    {
        _completion = Task.Run(() => ObserveAsync(store, key, overwrite, info, codec, level, complete));
    }

    private async Task ObserveAsync(NativeHandles<VesselStorage>.Lease store, string key, bool overwrite,
        ObjectInfo info, CompressionType codec, CompressionLevel level, Action<Task<ObjectMetadata>> complete)
    {
        var result = NativeInput.ConsumeInputAsync(_pipe.Reader, store.Value, key, overwrite, info, codec, level,
            _cancellation.Token);
        try { await result.ConfigureAwait(false); }
        finally
        {
            store.Dispose();
            complete(result);
        }
    }

    internal VesselError Write(ReadOnlySpan<byte> data, out int paused)
    {
        paused = 0;
        lock (_writer)
        {
            if (Volatile.Read(ref _ended) != 0) return VesselError.Disposed;
            if (_pendingFlush is not null) return VesselError.InvalidOperation;
            if (data.IsEmpty) return VesselError.Success;
            data.CopyTo(_pipe.Writer.GetSpan(data.Length));
            _pipe.Writer.Advance(data.Length);
            var flush = _pipe.Writer.FlushAsync(_cancellation.Token);
            if (!flush.IsCompletedSuccessfully)
            {
                _pendingFlush = flush.AsTask();
                paused = 1;
                return VesselError.Success;
            }
            return FlushStatus(flush.Result);
        }
    }

    internal VesselError Drain()
    {
        lock (_writer)
        {
            if (_pendingFlush is null) return VesselError.Success;
            try { return FlushStatus(_pendingFlush.GetAwaiter().GetResult()); }
            finally { _pendingFlush = null; }
        }
    }

    private static VesselError FlushStatus(FlushResult result) => result.IsCanceled ? VesselError.Cancelled
        : result.IsCompleted ? VesselError.InvalidOperation : VesselError.Success;

    internal void End(bool commit)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0 && !commit)
            _cancellation.Cancel();
    }

    public void Dispose()
    {
        End(commit: false);
        _pipe.Writer.Complete();
        _ = DisposeCancellationAsync();
    }

    private async Task DisposeCancellationAsync()
    {
        try { await _completion.ConfigureAwait(false); }
        catch { /* The completion callback has already received the operation status. */ }
        finally { _cancellation.Dispose(); }
    }
}
