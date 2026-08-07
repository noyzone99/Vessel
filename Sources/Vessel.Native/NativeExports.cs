using System.Runtime.InteropServices;
using System.Text;

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
    public static VesselError Read(IntPtr handle, byte* keyPtr, NativeVesselObject* outObj)
    {
        byte* nativeBuffer = null;
        try
        {
            var storage = GetStorage(handle);
            string key = Marshal.PtrToStringUTF8((IntPtr)keyPtr)!;

            using VesselStream vStream = storage.GetStream(key);

            if (vStream.Length > 0)
            {
                nativeBuffer = (byte*)NativeMemory.Alloc((nuint)vStream.Length);
                using var nativeStream = new UnmanagedMemoryStream(nativeBuffer, vStream.Length, vStream.Length, FileAccess.Write);
                vStream.CopyTo(nativeStream);
            }

            *outObj = new NativeVesselObject
            {
                Data = nativeBuffer,
                Length = vStream.Length,
                Checksum = AllocNativeString(vStream.Checksum),
                FileName = vStream.FileName != null ? AllocNativeString(vStream.FileName) : null,
                Extension = vStream.Extension != null ? AllocNativeString(vStream.Extension) : null,
                MimeType = vStream.MimeType != null ? AllocNativeString(vStream.MimeType) : null
            };

            return VesselError.Success;
        }
        catch (FileNotFoundException)
        {
            if (nativeBuffer != null) NativeMemory.Free(nativeBuffer);
            return VesselError.NotFound;
        }
        catch (Exception)
        {
            if (nativeBuffer != null) NativeMemory.Free(nativeBuffer);
            return VesselError.UnknownError;
        }
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

    [UnmanagedCallersOnly(EntryPoint = "vessel_free_object")]
    public static void FreeObject(NativeVesselObject obj)
    {
        if (obj.Data != null) NativeMemory.Free(obj.Data);

        FreeNativeString(obj.Checksum);
        FreeNativeString(obj.FileName);
        FreeNativeString(obj.Extension);
        FreeNativeString(obj.MimeType);
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