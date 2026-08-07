using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeVesselObject
{
    public byte* Data;
    public long Length;
    public byte* Checksum;
    public byte* FileName;
    public byte* Extension;
    public byte* MimeType;
}