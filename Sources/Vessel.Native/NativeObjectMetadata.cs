using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeObjectMetadata
{
    public long OriginalSize;
    public long CompressedSize;
    public int CompressionType;
    public long CreationDateUnix;
    public byte* Key;
    public byte* Hash;
    public byte* ETag;
    public byte* FileName;
    public byte* Extension;
    public byte* MimeType;
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeMetadataResult
{
    public NativeObjectMetadata Metadata;
    public long CreationDateUnixMs;
}
