using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct NativeObjectInfo
{
    public byte* FileName;
    public byte* FileExtension;
    public byte* MimeType;
}
