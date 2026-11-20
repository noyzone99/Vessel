using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeOptions
{
    public int EnableMetadataCompression;
    public delegate* unmanaged[Cdecl]<void*, int, byte*, void> Log;
    public void* LogContext;
}
