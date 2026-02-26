namespace ParticularLLM;

public struct BeltStructure
{
    public const int Width = 8;
    public const int Height = 8;
    public ushort id;
    public int tileY;
    public int minX;
    public int maxX;
    public sbyte direction;
    public byte speed;
    public byte frameOffset;
    public int SurfaceY => tileY - 1;
    public int Span => maxX - minX + Width;
}
