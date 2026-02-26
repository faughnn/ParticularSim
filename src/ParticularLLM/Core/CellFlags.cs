namespace ParticularLLM;

public static class CellFlags
{
    public const byte None    = 0;
    public const byte OnBelt  = 1 << 0;
    public const byte OnLift  = 1 << 1;
    public const byte Burning = 1 << 2;
    public const byte Wet     = 1 << 3;
    public const byte Settled = 1 << 4;
}
