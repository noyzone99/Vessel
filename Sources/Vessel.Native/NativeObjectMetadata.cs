using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeObjectMetadata
{
    public long Size;
    public long CompressedSize;
    public int CompressionType;
    public long LastModifiedUnix;
    public byte* Key;
    public byte* Hash;
    public byte* ETag;
    public byte* FileName;
    public byte* Extension;
    public byte* MimeType;
}