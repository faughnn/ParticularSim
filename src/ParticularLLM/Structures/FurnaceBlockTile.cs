namespace ParticularLLM;

public enum FurnaceDirection : byte
{
    Up = 0,
    Right = 1,
    Down = 2,
    Left = 3,
}

public struct FurnaceBlockTile
{
    public bool exists;
    public bool isGhost;
    public FurnaceDirection direction;
}
