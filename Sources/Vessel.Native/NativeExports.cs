using System.Runtime.InteropServices;
using System.Text;
using Vessel.Models;

namespace Vessel.Native;

public static unsafe class NativeExports
{
    [UnmanagedCallersOnly(EntryPoint = "vessel_init")]
    public static VesselError Initialize(byte* pathPtr, IntPtr* outHandle)
    {
        try
        {
            string path = Marshal.PtrToStringUTF8((IntPtr)pathPtr) ?? throw new ArgumentException("Path is null");
            var storage = new VesselStorage(path);
            GCHandle handle = GCHandle.Alloc(storage);
            *outHandle = GCHandle.ToIntPtr(handle);
            return VesselError.Success;
        }
        catch (ArgumentException) { return VesselError.InvalidArgument; }
        catch (Exception) { return VesselError.UnknownError; }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_put")]
    public static VesselError Put(IntPtr handle, byte* keyPtr, byte* dataPtr, int dataLen,
        int overwrite, NativeObjectInfo info, int compType, int compLevel)
    {
        try
        {
            var storage = GetStorage(handle);
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;

            var objInfo = new ObjectInfo(
                Marshal.PtrToStringUTF8((IntPtr)info.FileName),
                Marshal.PtrToStringUTF8((IntPtr)info.FileExtension),
                Marshal.PtrToStringUTF8((IntPtr)info.MimeType)
            );

            using var stream = new UnmanagedMemoryStream(dataPtr, dataLen);

            storage.PutAsync(key, stream, overwrite != 0, objInfo,
                (CompressionType)compType, (CompressionLevel)compLevel)
                .GetAwaiter().GetResult();

            return VesselError.Success;
        }
        catch (ArgumentException) { return VesselError.InvalidArgument; }
        catch (ObjectDisposedException) { return VesselError.Disposed; }
        catch (InvalidOperationException) { return VesselError.AlreadyExists; }
        catch (Exception) { return VesselError.UnknownError; }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_read")]
    public static VesselError Read(IntPtr handle, byte* keyPtr, byte** outData, int* outLen, NativeObjectMetadata* outMetadata)
    {
        try
        {
            var storage = GetStorage(handle);
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;

            using var response = storage.GetStream(key);

            *outMetadata = MapMetadata(response.Metadata);

            int length = (int)response.ContentStream.Length;
            byte* nativeBuffer = (byte*)NativeMemory.Alloc((nuint)length);

            using var nativeStream = new UnmanagedMemoryStream(nativeBuffer, length, length, FileAccess.Write);
            response.ContentStream.CopyTo(nativeStream);

            *outData = nativeBuffer;
            *outLen = length;

            return VesselError.Success;
        }
        catch (FileNotFoundException) { return VesselError.NotFound; }
        catch (ObjectDisposedException) { return VesselError.Disposed; }
        catch (Exception) { return VesselError.UnknownError; }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_delete")]
    public static VesselError Delete(IntPtr handle, byte* keyPtr, int* outDeleted)
    {
        try
        {
            var storage = GetStorage(handle);
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;
            bool deleted = storage.DeleteAsync(key).GetAwaiter().GetResult();
            *outDeleted = deleted ? 1 : 0;
            return VesselError.Success;
        }
        catch (Exception) { return VesselError.UnknownError; }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_get_all_keys")]
    public static VesselError GetAllKeys(IntPtr handle, byte* prefixPtr, delegate* unmanaged<byte*, void> callback)
    {
        try
        {
            var storage = GetStorage(handle);
            string prefix = Marshal.PtrToStringUTF8((IntPtr)prefixPtr) ?? string.Empty;

            foreach (string key in storage.GetAllKeys(prefix))
            {
                IntPtr keyNative = Marshal.StringToCoTaskMemUTF8(key);
                callback((byte*)keyNative);
                Marshal.FreeCoTaskMem(keyNative);
            }

            return VesselError.Success;
        }
        catch (ObjectDisposedException) { return VesselError.Disposed; }
        catch (Exception) { return VesselError.UnknownError; }
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_metadata")]
    public static void FreeMetadata(NativeObjectMetadata metadata)
    {
        FreeNativeString(metadata.Key);
        FreeNativeString(metadata.Hash);
        FreeNativeString(metadata.ETag);
        FreeNativeString(metadata.FileName);
        FreeNativeString(metadata.Extension);
        FreeNativeString(metadata.MimeType);
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_buffer")]
    public static void FreeBuffer(byte* buffer)
    {
        if (buffer != null) NativeMemory.Free(buffer);
    }

    [UnmanagedCallersOnly(EntryPoint = "vessel_dispose")]
    public static void Dispose(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        GCHandle gcHandle = GCHandle.FromIntPtr(handle);
        if (gcHandle.Target is VesselStorage storage) storage.Dispose();
        gcHandle.Free();
    }

    private static VesselStorage GetStorage(IntPtr handle) => (VesselStorage)GCHandle.FromIntPtr(handle).Target!;

    private static NativeObjectMetadata MapMetadata(ObjectMetadata md)
    {
        return new NativeObjectMetadata
        {
            Size = md.Size,
            CompressedSize = md.CompressedSize,
            CompressionType = (int)md.CompressionType,
            LastModifiedUnix = md.LastModified.ToUnixTimeSeconds(),
            Key = AllocNativeString(md.Key),
            Hash = AllocNativeString(md.Hash),
            ETag = AllocNativeString(md.ETag),
            FileName = md.FileName != null ? AllocNativeString(md.FileName) : null,
            Extension = md.Extension != null ? AllocNativeString(md.Extension) : null,
            MimeType = md.MimeType != null ? AllocNativeString(md.MimeType) : null
        };
    }

    private static byte* AllocNativeString(string str)
    {
        int size = Encoding.UTF8.GetByteCount(str) + 1;
        byte* ptr = (byte*)NativeMemory.Alloc((nuint)size);
        fixed (char* pStr = str)
        {
            Encoding.UTF8.GetBytes(pStr, str.Length, ptr, size - 1);
        }
        ptr[size - 1] = 0;
        return ptr;
    }

    private static void FreeNativeString(byte* ptr) { if (ptr != null) NativeMemory.Free(ptr); }
}