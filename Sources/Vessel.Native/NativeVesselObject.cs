using System.Runtime.InteropServices;

namespace Vessel.Native;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeVesselObject
{
    public byte* Data;         // Puntatore al buffer dei dati
    public long Length;        // Dimensione dei dati
    public byte* Checksum;
    public byte* FileName;
    public byte* Extension;
    public byte* MimeType;
}