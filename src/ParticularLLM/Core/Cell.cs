using System.Runtime.InteropServices;

namespace ParticularLLM;

[StructLayout(LayoutKind.Sequential)]
public struct Cell
{
    public byte materialId;
    public byte flags;
    public sbyte velocityX;
    public sbyte velocityY;
    public byte temperature;
    public byte structureId;
    public ushort ownerId;
    public byte velocityFracX;
    public byte velocityFracY;
    public byte frameUpdated;
}
