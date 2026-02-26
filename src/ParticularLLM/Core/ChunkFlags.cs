namespace ParticularLLM;

public static class ChunkFlags
{
    public const byte None         = 0;
    public const byte IsDirty      = 1 << 0;
    public const byte HasStructure = 1 << 1;
}
