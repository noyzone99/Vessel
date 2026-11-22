using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Vessel.Logging;

namespace Vessel.Native;

public static unsafe class NativeExports
{
    private const int ChunkSize = 64 * 1024;
    private const int WholeReadLimit = 64 * 1024 * 1024;
    // SHA-256 in hexadecimal, an optional two-character codec suffix, and NUL.
    private const int BlobIdentifierCapacity = SHA256.HashSizeInBytes * 2 + 3;
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private static readonly NativeHandles<VesselStorage> s_stores = new();
    private static readonly NativeHandles<VesselStream> s_streams = new();
    private static readonly NativeHandles<NativeUpload> s_uploads = new();

    [UnmanagedCallersOnly(EntryPoint = "vessel_abi_version", CallConvs = [typeof(CallConvCdecl)])]
    public static int AbiVersion() => 1;

    [UnmanagedCallersOnly(EntryPoint = "vessel_init", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError Initialize(byte* path, nint* output) => OpenStore(path, null, output);

    [UnmanagedCallersOnly(EntryPoint = "vessel_init_ex", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError InitializeEx(byte* path, NativeOptions* options, nint* output) => OpenStore(path, options, output);

    private static VesselError OpenStore(byte* path, NativeOptions* options, nint* output)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = 0;
        if (path == null || (options != null && options->EnableMetadataCompression is not (0 or 1)))
            return VesselError.InvalidArgument;

        try
        {
            var configuration = options == null ? null : new VesselStorageOptions(
                options->EnableMetadataCompression != 0,
                options->Log == null ? null : new NativeLogger(options->Log, options->LogContext));
            *output = s_stores.Add(new VesselStorage(Decode(path), configuration));
            return VesselError.Success;
        }
        catch (Exception error) { return MapError(error); }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_dispose", CallConvs = [typeof(CallConvCdecl)])]
    public static void Dispose(nint handle) => s_stores.Close(handle);

    [UnmanagedCallersOnly(EntryPoint = "vessel_put", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError Put(nint handle, byte* key, byte* data, int length, int overwrite, NativeObjectInfo info, int codec, int level)
        => PutBuffer(handle, key, data, length, overwrite, info, codec, level, null);

    [UnmanagedCallersOnly(EntryPoint = "vessel_put_ex", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError PutEx(nint handle, byte* key, byte* data, long length, int overwrite, NativeObjectInfo info, int codec, int level, NativeMetadataResult* output)
        => PutBuffer(handle, key, data, length, overwrite, info, codec, level, output);

    private static VesselError PutBuffer(nint handle, byte* key, byte* data, long length, int overwrite, NativeObjectInfo info, int codec, int level, NativeMetadataResult* output)
    {
        if (output != null) *output = default;
        if (key == null || length < 0 || (ulong)length > (ulong)nint.MaxValue || (length > 0 && data == null))
            return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            NativeMetadataResult prepared = default;
            try
            {
                var decodedKey = Decode(key);
                var decodedInfo = Decode(info);
                if (output != null) prepared = PrepareWriteResult(decodedKey, decodedInfo);
                using Stream input = length == 0 ? Stream.Null : new UnmanagedMemoryStream(data, length);
                var metadata = store.Value.PutAsync(decodedKey, input, overwrite != 0, decodedInfo, (CompressionType)codec, (CompressionLevel)level)
                    .GetAwaiter().GetResult();
                if (output != null) TransferWriteResult(metadata, ref prepared, output);
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
            finally
            {
                if (prepared.Metadata.Key != null) ClearMetadata(&prepared.Metadata);
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_put_stream", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError PutStream(nint handle, byte* key, delegate* unmanaged[Cdecl]<void*, byte*, int, int> read, void* context,
        int overwrite, NativeObjectInfo info, int codec, int level, NativeMetadataResult* output)
    {
        if (output != null) *output = default;
        if (key == null || read == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            NativeMetadataResult prepared = default;
            try
            {
                var decodedKey = Decode(key);
                var decodedInfo = Decode(info);
                if (output != null) prepared = PrepareWriteResult(decodedKey, decodedInfo);
                var metadata = ReadInput(store.Value, decodedKey, overwrite != 0, decodedInfo, (CompressionType)codec, (CompressionLevel)level, read, context);
                if (output != null) TransferWriteResult(metadata, ref prepared, output);
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
            finally
            {
                if (prepared.Metadata.Key != null) ClearMetadata(&prepared.Metadata);
            }
        }
    }

    private static ObjectMetadata ReadInput(VesselStorage store, string key, bool overwrite, ObjectInfo info, CompressionType codec,
        CompressionLevel level, delegate* unmanaged[Cdecl]<void*, byte*, int, int> read, void* context)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 4 * ChunkSize, resumeWriterThreshold: 2 * ChunkSize, useSynchronizationContext: false));
        using var cancellation = new CancellationTokenSource();
        var consumer = NativeInput.ConsumeInputAsync(pipe.Reader, store, key, overwrite, info, codec, level, cancellation.Token);
        ExceptionDispatchInfo? producerError = null;
        try
        {
            while (!consumer.IsCompleted)
            {
                var buffer = pipe.Writer.GetMemory(ChunkSize)[..ChunkSize];
                int count;
                fixed (byte* data = buffer.Span)
                    count = read(context, data, buffer.Length);

                if (count == 0) break;
                if (count == (int)VesselError.Cancelled) throw new OperationCanceledException();
                if (count < 0) throw new IOException("The input callback reported an error.");
                if (count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(read), "The input callback returned more bytes than requested.");

                pipe.Writer.Advance(count);
                var flush = pipe.Writer.FlushAsync();
                var result = flush.IsCompletedSuccessfully ? flush.Result : flush.AsTask().GetAwaiter().GetResult();
                if (result.IsCompleted) break;
            }
        }
        catch (Exception error)
        {
            producerError = ExceptionDispatchInfo.Capture(error);
            cancellation.Cancel();
        }
        finally
        {
            pipe.Writer.Complete();
        }

        try { return consumer.GetAwaiter().GetResult(); }
        finally { producerError?.Throw(); }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_upload_begin", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError UploadBegin(nint handle, byte* key, int overwrite, NativeObjectInfo info, int codec,
        int level, delegate* unmanaged[Cdecl]<void*, VesselError, NativeMetadataResult*, void> complete,
        void* context, nint* output)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = 0;
        if (key == null || complete == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);

        nint uploadHandle = 0;
        NativeMetadataResult prepared = default;
        try
        {
            var decodedKey = Decode(key);
            var decodedInfo = Decode(info);
            prepared = PrepareWriteResult(decodedKey, decodedInfo);
            var upload = new NativeUpload();
            uploadHandle = s_uploads.Add(upload);
            // The worker can complete before this call returns. It takes ownership once queued.
            var callbackResult = prepared;
            upload.Start(store, decodedKey, overwrite != 0, decodedInfo, (CompressionType)codec,
                (CompressionLevel)level, result => CompleteUpload(result, complete, context, callbackResult));
            *output = uploadHandle;
            return VesselError.Success;
        }
        catch (Exception error)
        {
            if (prepared.Metadata.Key != null) ClearMetadata(&prepared.Metadata);
            s_uploads.Close(uploadHandle);
            store.Dispose();
            return MapError(error);
        }
    }

    private static void CompleteUpload(Task<ObjectMetadata> result,
        delegate* unmanaged[Cdecl]<void*, VesselError, NativeMetadataResult*, void> complete, void* context,
        NativeMetadataResult metadata)
    {
        try
        {
            try { FillWriteResult(result.GetAwaiter().GetResult(), ref metadata); }
            catch (Exception error)
            {
                complete(context, MapError(error), null);
                return;
            }
            complete(context, VesselError.Success, &metadata);
        }
        finally { ClearMetadata(&metadata.Metadata); }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_upload_write", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError UploadWrite(nint handle, byte* data, int count, int* paused)
    {
        if (paused == null) return VesselError.InvalidArgument;
        *paused = 0;
        if (count < 0 || count > ChunkSize || (count != 0 && data == null)) return VesselError.InvalidArgument;
        if (!s_uploads.TryRent(handle, out var upload)) return HandleError(handle);
        using (upload)
        {
            try { return upload.Value.Write(new ReadOnlySpan<byte>(data, count), out *paused); }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_upload_drain", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError UploadDrain(nint handle)
    {
        if (!s_uploads.TryRent(handle, out var upload)) return HandleError(handle);
        using (upload)
        {
            try { return upload.Value.Drain(); }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_upload_end", CallConvs = [typeof(CallConvCdecl)])]
    public static void UploadEnd(nint handle, int commit)
    {
        if (!s_uploads.TryRent(handle, out var upload)) return;
        using (upload)
        {
            try { upload.Value.End(commit != 0); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Vessel upload cancellation failed: {0}", error.Message); }
            finally { s_uploads.Close(handle); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_read", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError Read(nint handle, byte* key, NativeVesselObject* output)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = default;
        if (key == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            NativeVesselObject result = default;
            try
            {
                using var stream = store.Value.GetStream(Decode(key), synchronous: OperatingSystem.IsWindows());
                if (stream.Length > WholeReadLimit) return VesselError.LimitExceeded;
                result.Length = stream.Length;
                if (stream.Length != 0)
                {
                    result.Data = (byte*)NativeMemory.Alloc((nuint)stream.Length);
                    stream.ReadExactly(new Span<byte>(result.Data, (int)stream.Length));
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("The object exceeds its declared length.");

                var payload = new ReadOnlySpan<byte>(result.Data, (int)stream.Length);
                Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
                Span<char> checksum = stackalloc char[SHA256.HashSizeInBytes * 2];
                SHA256.HashData(payload, digest);
                if (!Convert.TryToHexStringLower(digest, checksum, out _) || !checksum.SequenceEqual(stream.Checksum))
                    throw new InvalidDataException("The object does not match its content checksum.");

                result.Checksum = AllocateString(stream.Checksum);
                result.FileName = AllocateString(stream.FileName);
                result.Extension = AllocateString(stream.Extension);
                result.MimeType = AllocateString(stream.MimeType);
                *output = result;
                result = default;
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
            finally { ClearObject(&result); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_metadata", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError GetMetadata(nint handle, byte* key, NativeObjectMetadata* output)
        => FindMetadata(handle, key, output, null);

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_metadata_ex", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError GetMetadataEx(nint handle, byte* key, NativeMetadataResult* output)
        => output == null ? VesselError.InvalidArgument : FindMetadata(handle, key, &output->Metadata, &output->CreationDateUnixMs);

    private static VesselError FindMetadata(nint handle, byte* key, NativeObjectMetadata* output, long* createdMs)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = default;
        if (createdMs != null) *createdMs = 0;
        if (key == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            try
            {
                if (store.Value.GetMetadata(Decode(key)) is not { } metadata) return VesselError.NotFound;
                *output = ExportMetadata(metadata);
                if (createdMs != null) *createdMs = metadata.CreationDate.ToUnixTimeMilliseconds();
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_delete", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError Delete(nint handle, byte* key, int* output)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = 0;
        if (key == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            try
            {
                *output = store.Value.DeleteAsync(Decode(key)).GetAwaiter().GetResult() ? 1 : 0;
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_all_keys", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError GetAllKeys(nint handle, byte* prefix, delegate* unmanaged[Cdecl]<byte*, void> callback)
        => EnumerateKeys(handle, prefix, callback, null, null);

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_all_keys_ex", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError GetAllKeysEx(nint handle, byte* prefix, delegate* unmanaged[Cdecl]<void*, byte*, int> callback, void* context)
        => EnumerateKeys(handle, prefix, null, callback, context);

    private static VesselError EnumerateKeys(nint handle, byte* prefix, delegate* unmanaged[Cdecl]<byte*, void> legacy,
        delegate* unmanaged[Cdecl]<void*, byte*, int> callback, void* context)
    {
        if (legacy == null && callback == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            try
            {
                foreach (var key in store.Value.GetAllKeys(prefix == null ? "" : Decode(prefix)))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(checked(s_utf8.GetByteCount(key) + 1));
                    try
                    {
                        buffer[s_utf8.GetBytes(key, buffer)] = 0;
                        fixed (byte* text = buffer)
                        {
                            if (legacy != null)
                            {
                                legacy(text);
                                continue;
                            }
                            var status = callback(context, text);
                            if (status > 0) break;
                            if (status < 0) return VesselError.Cancelled;
                        }
                    }
                    finally { ArrayPool<byte>.Shared.Return(buffer); }
                }
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_open", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamOpen(nint handle, byte* key, nint* output)
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = 0;
        if (key == null) return VesselError.InvalidArgument;
        if (!s_stores.TryRent(handle, out var store)) return HandleError(handle);
        using (store)
        {
            try
            {
                *output = s_streams.Add(store.Value.GetStream(Decode(key), synchronous: OperatingSystem.IsWindows()));
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_read", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamRead(nint handle, byte* buffer, int count, int* bytesRead)
    {
        if (bytesRead == null) return VesselError.InvalidArgument;
        *bytesRead = 0;
        if (count < 0 || (count > 0 && buffer == null)) return VesselError.InvalidArgument;
        if (!s_streams.TryRent(handle, out var stream)) return HandleError(handle);
        using (stream)
        {
            try
            {
                lock (stream.Value)
                    *bytesRead = stream.Value.Read(new Span<byte>(buffer, count));
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_length", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamLength(nint handle, long* length) => GetStreamValue(handle, length, static stream => stream.Length);

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_position", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamPosition(nint handle, long* position) => GetStreamValue(handle, position, static stream => stream.Position);

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_can_seek", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamCanSeek(nint handle, int* output)
        => GetStreamValue(handle, output, static stream => stream.CanSeek ? 1 : 0);

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_get_metadata", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamGetMetadata(nint handle, NativeMetadataResult* output)
        => GetStreamValue(handle, output, static stream => ExportResult(stream.Metadata!.Value));

    private static VesselError GetStreamValue<T>(nint handle, T* output, Func<VesselStream, T> read) where T : unmanaged
    {
        if (output == null) return VesselError.InvalidArgument;
        *output = default;
        if (!s_streams.TryRent(handle, out var stream)) return HandleError(handle);
        using (stream)
        {
            try
            {
                lock (stream.Value) *output = read(stream.Value);
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_seek", CallConvs = [typeof(CallConvCdecl)])]
    public static VesselError StreamSeek(nint handle, long offset, int origin)
    {
        if ((uint)origin > (uint)SeekOrigin.End) return VesselError.InvalidArgument;
        if (!s_streams.TryRent(handle, out var stream)) return HandleError(handle);
        using (stream)
        {
            try
            {
                lock (stream.Value) stream.Value.Seek(offset, (SeekOrigin)origin);
                return VesselError.Success;
            }
            catch (Exception error) { return MapError(error); }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_close", CallConvs = [typeof(CallConvCdecl)])]
    public static void StreamClose(nint handle) => s_streams.Close(handle);

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_object", CallConvs = [typeof(CallConvCdecl)])]
    public static void FreeObject(NativeVesselObject* output) => ClearObject(output);

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_metadata", CallConvs = [typeof(CallConvCdecl)])]
    public static void FreeMetadata(NativeObjectMetadata* output) => ClearMetadata(output);

    private static NativeMetadataResult ExportResult(ObjectMetadata metadata)
        => new() { Metadata = ExportMetadata(metadata), CreationDateUnixMs = metadata.CreationDate.ToUnixTimeMilliseconds() };

    private static NativeMetadataResult PrepareWriteResult(string key, ObjectInfo info)
    {
        NativeMetadataResult result = default;
        try
        {
            result.Metadata.Key = AllocateString(key);
            result.Metadata.Hash = (byte*)NativeMemory.Alloc(BlobIdentifierCapacity);
            result.Metadata.ETag = (byte*)NativeMemory.Alloc(BlobIdentifierCapacity);
            result.Metadata.FileName = AllocateString(info.FileName);
            result.Metadata.Extension = AllocateString(info.Extension);
            result.Metadata.MimeType = AllocateString(info.MimeType);
            return result;
        }
        catch
        {
            ClearMetadata(&result.Metadata);
            throw;
        }
    }

    private static void TransferWriteResult(ObjectMetadata metadata, ref NativeMetadataResult result, NativeMetadataResult* output)
    {
        FillWriteResult(metadata, ref result);
        *output = result;
        result = default;
    }

    private static void FillWriteResult(ObjectMetadata metadata, ref NativeMetadataResult result)
    {
        // PutAsync derives both strings from the staged blob identifier.
        WriteBlobIdentifier(metadata.Hash, result.Metadata.Hash);
        WriteBlobIdentifier(metadata.ETag, result.Metadata.ETag);
        FillMetadataNumbers(metadata, ref result.Metadata);
        result.CreationDateUnixMs = metadata.CreationDate.ToUnixTimeMilliseconds();
    }

    private static void WriteBlobIdentifier(string identifier, byte* output)
    {
        var buffer = new Span<byte>(output, BlobIdentifierCapacity);
        buffer[s_utf8.GetBytes(identifier, buffer)] = 0;
    }

    private static void FillMetadataNumbers(ObjectMetadata metadata, ref NativeObjectMetadata result)
    {
        result.OriginalSize = metadata.OriginalSize;
        result.CompressedSize = metadata.CompressedSize;
        result.CompressionType = (int)metadata.CompressionType;
        result.CreationDateUnix = metadata.CreationDate.ToUnixTimeSeconds();
    }

    private static NativeObjectMetadata ExportMetadata(ObjectMetadata metadata)
    {
        NativeObjectMetadata result = default;
        FillMetadataNumbers(metadata, ref result);

        try
        {
            result.Key = AllocateString(metadata.Key);
            result.Hash = AllocateString(metadata.Hash);
            result.ETag = AllocateString(metadata.ETag);
            result.FileName = AllocateString(metadata.FileName);
            result.Extension = AllocateString(metadata.Extension);
            result.MimeType = AllocateString(metadata.MimeType);
            return result;
        }
        catch
        {
            ClearMetadata(&result);
            throw;
        }
    }

    private static void ClearObject(NativeVesselObject* output)
    {
        if (output == null) return;
        NativeMemory.Free(output->Data);
        NativeMemory.Free(output->Checksum);
        NativeMemory.Free(output->FileName);
        NativeMemory.Free(output->Extension);
        NativeMemory.Free(output->MimeType);
        *output = default;
    }

    private static void ClearMetadata(NativeObjectMetadata* output)
    {
        if (output == null) return;
        NativeMemory.Free(output->Key);
        NativeMemory.Free(output->Hash);
        NativeMemory.Free(output->ETag);
        NativeMemory.Free(output->FileName);
        NativeMemory.Free(output->Extension);
        NativeMemory.Free(output->MimeType);
        *output = default;
    }

    private static string Decode(byte* text) => s_utf8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text));
    private static string? DecodeOptional(byte* text) => text == null ? null : Decode(text);
    private static ObjectInfo Decode(NativeObjectInfo info)
        => new(DecodeOptional(info.FileName), DecodeOptional(info.FileExtension), DecodeOptional(info.MimeType));

    private static byte* AllocateString(string? value)
    {
        if (value is null) return null;
        var size = checked(s_utf8.GetByteCount(value) + 1);
        var data = (byte*)NativeMemory.Alloc((nuint)size);
        s_utf8.GetBytes(value, new Span<byte>(data, size - 1));
        data[size - 1] = 0;
        return data;
    }

    private static VesselError HandleError(nint handle) => handle == 0 ? VesselError.InvalidArgument : VesselError.Disposed;

    private static VesselError MapError(Exception error) => error switch
    {
        OperationCanceledException => VesselError.Cancelled,
        OutOfMemoryException => VesselError.OutOfMemory,
        ArgumentException => VesselError.InvalidArgument,
        FileNotFoundException => VesselError.NotFound,
        ObjectDisposedException => VesselError.Disposed,
        InvalidOperationException invalid when VesselStorage.IsAlreadyExists(invalid) => VesselError.AlreadyExists,
        InvalidOperationException or NotSupportedException => VesselError.InvalidOperation,
        IOException or UnauthorizedAccessException or InvalidDataException => VesselError.IoError,
        _ => VesselError.UnknownError
    };

    private sealed class NativeLogger(delegate* unmanaged[Cdecl]<void*, int, byte*, void> callback, void* context) : IVesselLogger
    {
        public void Log(VesselLogLevel level, string message, Exception? error)
        {
            var text = AllocateString(message);
            try { callback(context, (int)level, text); }
            finally { NativeMemory.Free(text); }
        }
    }
}

internal static class NativeInput
{
    internal static async Task<ObjectMetadata> ConsumeInputAsync(PipeReader reader, VesselStorage store, string key, bool overwrite,
        ObjectInfo info, CompressionType codec, CompressionLevel level, CancellationToken cancellationToken)
    {
        try
        {
            using var input = reader.AsStream(leaveOpen: true);
            return await store.PutAsync(key, input, overwrite, info, codec, level, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }
}
