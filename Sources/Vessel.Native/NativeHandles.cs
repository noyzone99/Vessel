using System.Collections.Concurrent;
using System.Diagnostics;

namespace Vessel.Native;

internal static class NativeHandleIds
{
    private static long s_last;

    internal static nint Next()
    {
        var id = Interlocked.Increment(ref s_last);
        if (id <= 0 || id > nint.MaxValue) throw new InvalidOperationException("Vessel handle limit reached.");
        return (nint)id;
    }
}

internal sealed class NativeHandles<T> where T : class, IDisposable
{
    private readonly ConcurrentDictionary<nint, Entry> _entries = new();

    public nint Add(T value)
    {
        try
        {
            var id = NativeHandleIds.Next();
            if (!_entries.TryAdd(id, new Entry(value))) throw new InvalidOperationException("Duplicate Vessel handle.");
            return id;
        }
        catch
        {
            DisposeValue(value);
            throw;
        }
    }

    public bool TryRent(nint id, out Lease lease)
    {
        if (_entries.TryGetValue(id, out var entry) && entry.TryRent())
        {
            lease = new Lease(entry);
            return true;
        }
        lease = default;
        return false;
    }

    public void Close(nint id)
    {
        if (_entries.TryRemove(id, out var entry)) entry.Close();
    }

    internal readonly struct Lease(Entry entry) : IDisposable
    {
        public T Value => entry.Value;
        public void Dispose() => entry.Release();
    }

    internal sealed class Entry(T value)
    {
        private readonly Lock _gate = new();
        private int _active;
        private bool _closed;

        public T Value { get; } = value;

        public bool TryRent()
        {
            lock (_gate)
            {
                if (_closed) return false;
                _active++;
                return true;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                if (_active != 0) return;
            }
            DisposeValue(Value);
        }

        public void Release()
        {
            lock (_gate)
            {
                if (--_active != 0 || !_closed) return;
            }
            DisposeValue(Value);
        }
    }

    private static void DisposeValue(T value)
    {
        try { value.Dispose(); }
        catch (Exception error) { Trace.TraceWarning("Vessel could not close a handle: {0}", error.Message); }
    }
}
