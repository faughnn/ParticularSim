namespace ParticularLLM;

public struct LiftStructure
{
    public const int Width = 8;
    public const int Height = 8;
    public ushort id;
    public int tileX;
    public int minY;
    public int maxY;
    public byte liftForce;
    public int Span => maxY - minY + Height;
}
