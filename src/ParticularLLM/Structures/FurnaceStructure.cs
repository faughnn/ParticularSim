namespace ParticularLLM;

public enum FurnaceState : byte
{
    Off = 0,
    Heating = 1,
    Cooling = 2,
}

public struct FurnaceStructure
{
    public ushort id;
    public int x, y;           // Bottom-left corner (cell coordinates)
    public int width, height;  // Total size including walls
    public byte heatOutput;    // Temperature increase per frame for interior cells
    public byte maxTemp;       // Maximum interior temperature (caps heating)
    public FurnaceState state;
}
