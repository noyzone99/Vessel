using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace Vessel.Native;

public static unsafe class NativeExports
{
    private static readonly Lock s_disposeLock = new();

    [UnmanagedCallersOnly(EntryPoint = "vessel_init")]
    public static VesselError Initialize(byte* pathPtr, IntPtr* outHandle)
    {
        if (pathPtr == null || outHandle == null)
            return VesselError.InvalidArgument;

        try
        {
            string path = Marshal.PtrToStringUTF8((IntPtr)pathPtr)
                ?? throw new ArgumentException("Path is null");

            if (string.IsNullOrWhiteSpace(path))
                return VesselError.InvalidArgument;

            var storage = new VesselStorage(path);
            GCHandle handle = GCHandle.Alloc(storage);
            *outHandle = GCHandle.ToIntPtr(handle);
            return VesselError.Success;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_dispose")]
    public static void Dispose(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        lock (s_disposeLock)
        {
            GCHandle gcHandle;
            try
            {
                gcHandle = GCHandle.FromIntPtr(handle);
            }
            catch
            {
                return;
            }

            if (gcHandle.Target is VesselStorage storage)
            {
                try
                {
                    storage.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                gcHandle.Free();
            }
            catch
            {
            }
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_put")]
    public static VesselError Put(IntPtr handle, byte* keyPtr, byte* dataPtr, int dataLen, int overwrite, NativeObjectInfo info, int compType, int compLevel)
    {
        if (handle == IntPtr.Zero || keyPtr == null)
            return VesselError.InvalidArgument;

        if (dataLen < 0 || (dataLen > 0 && dataPtr == null))
            return VesselError.InvalidArgument;

        if (!Enum.IsDefined(typeof(CompressionType), compType) ||
            !Enum.IsDefined(typeof(CompressionLevel), compLevel))
        {
            return VesselError.InvalidArgument;
        }

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        try
        {
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;
            if (string.IsNullOrWhiteSpace(key))
                return VesselError.InvalidArgument;

            var objInfo = new ObjectInfo(
                Marshal.PtrToStringUTF8((IntPtr)info.FileName),
                Marshal.PtrToStringUTF8((IntPtr)info.FileExtension),
                Marshal.PtrToStringUTF8((IntPtr)info.MimeType)
            );

            using var stream = dataLen > 0
                ? new UnmanagedMemoryStream(dataPtr, dataLen)
                : Stream.Null;

            Task.Run(() => storage.PutAsync(
                key,
                stream,
                overwrite != 0,
                objInfo,
                (CompressionType)compType,
                (CompressionLevel)compLevel,
                CancellationToken.None)).GetAwaiter().GetResult();

            return VesselError.Success;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (InvalidOperationException)
        {
            return VesselError.AlreadyExists;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_read")]
    public static VesselError Read(IntPtr handle, byte* keyPtr, NativeVesselObject* outObj)
    {
        if (handle == IntPtr.Zero || keyPtr == null || outObj == null)
            return VesselError.InvalidArgument;

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return VesselError.InvalidArgument;

        byte* nativeBuffer = null;
        byte* checksumPtr = null;
        byte* fileNamePtr = null;
        byte* extensionPtr = null;
        byte* mimeTypePtr = null;
        long length = 0;

        try
        {
            using VesselStream vStream = storage.GetStream(key);
            length = vStream.Length;

            if (length > 0)
            {
                nativeBuffer = (byte*)NativeMemory.Alloc((nuint)length);
                using var nativeStream = new UnmanagedMemoryStream(nativeBuffer, length, length, FileAccess.Write);
                vStream.CopyTo(nativeStream);
            }

            checksumPtr = AllocNativeString(vStream.Checksum);
            fileNamePtr = AllocNativeString(vStream.FileName);
            extensionPtr = AllocNativeString(vStream.Extension);
            mimeTypePtr = AllocNativeString(vStream.MimeType);

            *outObj = new NativeVesselObject
            {
                Data = nativeBuffer,
                Length = length,
                Checksum = checksumPtr,
                FileName = fileNamePtr,
                Extension = extensionPtr,
                MimeType = mimeTypePtr
            };

            nativeBuffer = null;
            checksumPtr = null;
            fileNamePtr = null;
            extensionPtr = null;
            mimeTypePtr = null;

            return VesselError.Success;
        }
        catch (FileNotFoundException)
        {
            return VesselError.NotFound;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
        finally
        {
            if (nativeBuffer != null) NativeMemory.Free(nativeBuffer);
            FreeNativeString(checksumPtr);
            FreeNativeString(fileNamePtr);
            FreeNativeString(extensionPtr);
            FreeNativeString(mimeTypePtr);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_metadata")]
    public static VesselError GetMetadata(IntPtr handle, byte* keyPtr, NativeObjectMetadata* outMetadata)
    {
        if (handle == IntPtr.Zero || keyPtr == null || outMetadata == null)
            return VesselError.InvalidArgument;

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return VesselError.InvalidArgument;

        byte* keyPtrOut = null;
        byte* hashPtr = null;
        byte* eTagPtr = null;
        byte* fileNamePtr = null;
        byte* extensionPtr = null;
        byte* mimeTypePtr = null;

        try
        {
            var md = storage.GetMetadata(key);
            if (md == null)
                return VesselError.NotFound;

            var metadata = md.Value;

            keyPtrOut = AllocNativeString(metadata.Key);
            hashPtr = AllocNativeString(metadata.Hash);
            eTagPtr = AllocNativeString(metadata.ETag);
            fileNamePtr = AllocNativeString(metadata.FileName);
            extensionPtr = AllocNativeString(metadata.Extension);
            mimeTypePtr = AllocNativeString(metadata.MimeType);

            *outMetadata = new NativeObjectMetadata
            {
                OriginalSize = metadata.OriginalSize,
                CompressedSize = metadata.CompressedSize,
                CompressionType = (int)metadata.CompressionType,
                CreationDateUnix = metadata.CreationDate.ToUnixTimeSeconds(),
                Key = keyPtrOut,
                Hash = hashPtr,
                ETag = eTagPtr,
                FileName = fileNamePtr,
                Extension = extensionPtr,
                MimeType = mimeTypePtr
            };

            keyPtrOut = null;
            hashPtr = null;
            eTagPtr = null;
            fileNamePtr = null;
            extensionPtr = null;
            mimeTypePtr = null;

            return VesselError.Success;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
        finally
        {
            FreeNativeString(keyPtrOut);
            FreeNativeString(hashPtr);
            FreeNativeString(eTagPtr);
            FreeNativeString(fileNamePtr);
            FreeNativeString(extensionPtr);
            FreeNativeString(mimeTypePtr);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_delete")]
    public static VesselError Delete(IntPtr handle, byte* keyPtr, int* outDeleted)
    {
        if (handle == IntPtr.Zero || keyPtr == null || outDeleted == null)
            return VesselError.InvalidArgument;

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        try
        {
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;
            if (string.IsNullOrWhiteSpace(key))
                return VesselError.InvalidArgument;

            bool deleted = Task.Run(() => storage.DeleteAsync(key, CancellationToken.None))
                .GetAwaiter().GetResult();

            *outDeleted = deleted ? 1 : 0;
            return VesselError.Success;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_all_keys")]
    public static VesselError GetAllKeys(IntPtr handle, byte* prefixPtr, delegate* unmanaged<byte*, void> callback)
    {
        if (handle == IntPtr.Zero || callback == null)
            return VesselError.InvalidArgument;

        if (prefixPtr == null)
            return VesselError.InvalidArgument;

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        try
        {
            string prefix = Marshal.PtrToStringUTF8((IntPtr)prefixPtr) ?? string.Empty;

            foreach (string key in storage.GetAllKeys(prefix))
            {
                IntPtr keyNative = Marshal.StringToCoTaskMemUTF8(key);
                try
                {
                    callback((byte*)keyNative);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyNative);
                }
            }

            return VesselError.Success;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_open")]
    public static VesselError StreamOpen(IntPtr handle, byte* keyPtr, IntPtr* outStreamHandle)
    {
        if (handle == IntPtr.Zero || keyPtr == null || outStreamHandle == null)
            return VesselError.InvalidArgument;

        if (!TryGetStorage(handle, out VesselStorage? storage))
            return VesselError.InvalidArgument;

        string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            return VesselError.InvalidArgument;

        try
        {
            VesselStream stream = storage.GetStream(key);
            GCHandle gcHandle = GCHandle.Alloc(stream);
            *outStreamHandle = GCHandle.ToIntPtr(gcHandle);
            return VesselError.Success;
        }
        catch (FileNotFoundException)
        {
            return VesselError.NotFound;
        }
        catch (ArgumentException)
        {
            return VesselError.InvalidArgument;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_read")]
    public static VesselError StreamRead(IntPtr streamHandle, byte* buffer, int count, int* outBytesRead)
    {
        if (streamHandle == IntPtr.Zero || buffer == null || outBytesRead == null)
            return VesselError.InvalidArgument;

        if (count < 0)
            return VesselError.InvalidArgument;

        if (!TryGetStream(streamHandle, out VesselStream? stream))
            return VesselError.InvalidArgument;

        try
        {
            int read = stream.Read(new Span<byte>(buffer, count));
            *outBytesRead = read;
            return VesselError.Success;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (NotSupportedException)
        {
            return VesselError.UnknownError;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_length")]
    public static VesselError StreamLength(IntPtr streamHandle, long* outLength)
    {
        if (streamHandle == IntPtr.Zero || outLength == null)
            return VesselError.InvalidArgument;

        if (!TryGetStream(streamHandle, out VesselStream? stream))
            return VesselError.InvalidArgument;

        try
        {
            *outLength = stream.Length;
            return VesselError.Success;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (NotSupportedException)
        {
            return VesselError.UnknownError;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_position")]
    public static VesselError StreamPosition(IntPtr streamHandle, long* outPosition)
    {
        if (streamHandle == IntPtr.Zero || outPosition == null)
            return VesselError.InvalidArgument;

        if (!TryGetStream(streamHandle, out VesselStream? stream))
            return VesselError.InvalidArgument;

        try
        {
            *outPosition = stream.Position;
            return VesselError.Success;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (NotSupportedException)
        {
            return VesselError.UnknownError;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_seek")]
    public static VesselError StreamSeek(IntPtr streamHandle, long offset, int origin)
    {
        if (streamHandle == IntPtr.Zero)
            return VesselError.InvalidArgument;

        if (origin < 0 || origin > 2)
            return VesselError.InvalidArgument;

        if (!TryGetStream(streamHandle, out VesselStream? stream))
            return VesselError.InvalidArgument;

        try
        {
            stream.Seek(offset, (SeekOrigin)origin);
            return VesselError.Success;
        }
        catch (ObjectDisposedException)
        {
            return VesselError.Disposed;
        }
        catch (NotSupportedException)
        {
            return VesselError.UnknownError;
        }
        catch (IOException)
        {
            return VesselError.UnknownError;
        }
        catch (Exception)
        {
            return VesselError.UnknownError;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_stream_close")]
    public static void StreamClose(IntPtr streamHandle)
    {
        if (streamHandle == IntPtr.Zero)
            return;

        try
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(streamHandle);
            if (gcHandle.Target is VesselStream stream)
            {
                try
                {
                    stream.Dispose();
                }
                catch
                {
                }
            }
            gcHandle.Free();
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_object")]
    public static void FreeObject(NativeVesselObject* obj)
    {
        if (obj == null)
            return;

        if (obj->Data != null)
        {
            NativeMemory.Free(obj->Data);
            obj->Data = null;
        }

        FreeAndClear(ref obj->Checksum);
        FreeAndClear(ref obj->FileName);
        FreeAndClear(ref obj->Extension);
        FreeAndClear(ref obj->MimeType);
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_metadata")]
    public static void FreeMetadata(NativeObjectMetadata* metadata)
    {
        if (metadata == null)
            return;

        FreeAndClear(ref metadata->Key);
        FreeAndClear(ref metadata->Hash);
        FreeAndClear(ref metadata->ETag);
        FreeAndClear(ref metadata->FileName);
        FreeAndClear(ref metadata->Extension);
        FreeAndClear(ref metadata->MimeType);
    }

    private static bool TryGetStorage(IntPtr handle, [NotNullWhen(true)] out VesselStorage? storage)
    {
        storage = null;
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(handle);
            if (gcHandle.Target is VesselStorage s)
            {
                storage = s;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool TryGetStream(IntPtr streamHandle, [NotNullWhen(true)] out VesselStream? stream)
    {
        stream = null;
        if (streamHandle == IntPtr.Zero)
            return false;

        try
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(streamHandle);
            if (gcHandle.Target is VesselStream s)
            {
                stream = s;
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static byte* AllocNativeString(string? str)
    {
        if (str == null)
            return null;

        int size = Encoding.UTF8.GetByteCount(str) + 1;
        byte* ptr = (byte*)NativeMemory.Alloc((nuint)size);

        fixed (char* pStr = str)
        {
            Encoding.UTF8.GetBytes(pStr, str.Length, ptr, size - 1);
        }
        ptr[size - 1] = 0;
        return ptr;
    }

    private static void FreeNativeString(byte* ptr)
    {
        if (ptr != null)
            NativeMemory.Free(ptr);
    }

    private static void FreeAndClear(ref byte* ptr)
    {
        if (ptr != null)
        {
            NativeMemory.Free(ptr);
            ptr = null;
        }
    }
}