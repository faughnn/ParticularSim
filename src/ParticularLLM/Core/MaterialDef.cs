using System.Runtime.InteropServices;

namespace ParticularLLM;

public enum BehaviourType : byte
{
    Static = 0,
    Powder = 1,
    Liquid = 2,
    Gas = 3,
}

public static class MaterialFlags
{
    public const byte None         = 0;
    public const byte ConductsHeat = 1 << 0;
    public const byte Flammable    = 1 << 1;
    public const byte Conductive   = 1 << 2;
    public const byte Corrodes     = 1 << 3;
    public const byte Diggable     = 1 << 4;
    public const byte Passable     = 1 << 5;
}

[StructLayout(LayoutKind.Sequential)]
public struct MaterialDef
{
    public byte density;
    public byte slideResistance;
    public BehaviourType behaviour;
    public byte flags;
    public byte ignitionTemp;
    public byte meltTemp;
    public byte freezeTemp;
    public byte boilTemp;
    public byte materialOnMelt;
    public byte materialOnFreeze;
    public byte materialOnBurn;
    public byte materialOnBoil;
    public Color32 baseColour;
    public byte colourVariation;
    public byte dispersionRate;
    public byte emission;
    public byte padding3;
}
