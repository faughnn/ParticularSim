using System.Runtime.InteropServices;

namespace ParticularLLM;

[StructLayout(LayoutKind.Sequential)]
public struct ChunkState
{
    public ushort minX, minY;
    public ushort maxX, maxY;
    public byte flags;
    public byte activeLastFrame;
    public ushort structureMask;
}
