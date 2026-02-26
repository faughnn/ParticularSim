using System.Runtime.InteropServices;

namespace ParticularLLM;

[StructLayout(LayoutKind.Sequential)]
public struct ClusterPixel
{
    public short localX;
    public short localY;
    public byte materialId;

    public ClusterPixel(short localX, short localY, byte materialId)
    {
        this.localX = localX;
        this.localY = localY;
        this.materialId = materialId;
    }
}
