namespace ParticularLLM;

public static class Materials
{
    public const byte Air = 0;
    public const byte Stone = 1;
    public const byte Sand = 2;
    public const byte Water = 3;
    public const byte Oil = 4;
    public const byte Steam = 5;
    public const byte IronOre = 6;
    public const byte MoltenIron = 7;
    public const byte Iron = 8;
    public const byte Coal = 9;
    public const byte Ash = 10;
    public const byte Smoke = 11;
    public const byte Belt = 12;
    public const byte BeltLeft = 13;
    public const byte BeltRight = 14;
    public const byte BeltLeftLight = 15;
    public const byte BeltRightLight = 16;
    public const byte Dirt = 17;
    public const byte Ground = 18;
    public const byte LiftUp = 19;
    public const byte LiftUpLight = 20;
    public const byte Wall = 21;
    public const byte PistonBase = 22;
    public const byte PistonArm = 23;

    public const int Count = 256;

    public static bool IsBelt(byte materialId)
    {
        return materialId == Belt ||
               materialId == BeltLeft ||
               materialId == BeltRight ||
               materialId == BeltLeftLight ||
               materialId == BeltRightLight;
    }

    public static bool IsLift(byte materialId)
    {
        return materialId == LiftUp || materialId == LiftUpLight;
    }

    public static bool IsDiggable(MaterialDef mat)
    {
        return (mat.flags & MaterialFlags.Diggable) != 0;
    }

    public static bool IsPiston(byte materialId)
    {
        return materialId == PistonBase || materialId == PistonArm;
    }

    public static bool IsStructureMaterial(byte materialId)
    {
        return IsBelt(materialId) || IsLift(materialId) || IsPiston(materialId) ||
               materialId == Wall;
    }

    public static bool IsSoftTerrain(byte materialId)
    {
        return materialId == Ground ||
               materialId == Dirt ||
               materialId == Sand ||
               materialId == Water;
    }

    public static MaterialDef[] CreateDefaults()
    {
        var defs = new MaterialDef[Count];

        defs[Air] = new MaterialDef
        {
            density = 0, slideResistance = 0,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(20, 20, 30, 255), colourVariation = 0,
        };
        defs[Stone] = new MaterialDef
        {
            density = 255, slideResistance = 0,
            behaviour = BehaviourType.Static, flags = MaterialFlags.ConductsHeat,
            baseColour = new Color32(100, 100, 105, 255), colourVariation = 10,
        };
        defs[Sand] = new MaterialDef
        {
            density = 128, slideResistance = 0,
            behaviour = BehaviourType.Powder, flags = MaterialFlags.None,
            baseColour = new Color32(194, 178, 128, 255), colourVariation = 15,
        };
        defs[Water] = new MaterialDef
        {
            density = 64, slideResistance = 5,
            behaviour = BehaviourType.Liquid, flags = MaterialFlags.ConductsHeat,
            boilTemp = 100, materialOnBoil = Steam,
            baseColour = new Color32(32, 64, 192, 255), colourVariation = 10,
            dispersionRate = 5,
        };
        defs[Oil] = new MaterialDef
        {
            density = 48, slideResistance = 15,
            behaviour = BehaviourType.Liquid, flags = MaterialFlags.Flammable,
            ignitionTemp = 80, materialOnBurn = Smoke,
            baseColour = new Color32(80, 60, 20, 255), colourVariation = 5,
            dispersionRate = 4,
        };
        defs[Steam] = new MaterialDef
        {
            density = 4, slideResistance = 2,
            behaviour = BehaviourType.Gas, flags = MaterialFlags.ConductsHeat,
            freezeTemp = 50, materialOnFreeze = Water,
            baseColour = new Color32(200, 200, 220, 255), colourVariation = 20,
        };
        defs[Belt] = new MaterialDef
        {
            density = 255, slideResistance = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(60, 60, 70, 255), colourVariation = 0,
        };
        defs[BeltLeft] = new MaterialDef
        {
            density = 255, slideResistance = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(50, 50, 60, 255), colourVariation = 0,
        };
        defs[BeltRight] = new MaterialDef
        {
            density = 255, slideResistance = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(50, 50, 60, 255), colourVariation = 0,
        };
        defs[BeltLeftLight] = new MaterialDef
        {
            density = 255, slideResistance = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(80, 80, 95, 255), colourVariation = 0,
        };
        defs[BeltRightLight] = new MaterialDef
        {
            density = 255, slideResistance = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(80, 80, 95, 255), colourVariation = 0,
        };
        defs[Dirt] = new MaterialDef
        {
            density = 140, slideResistance = 50,
            behaviour = BehaviourType.Powder, flags = MaterialFlags.None,
            baseColour = new Color32(139, 90, 43, 255), colourVariation = 12,
        };
        defs[Ground] = new MaterialDef
        {
            density = 255,
            behaviour = BehaviourType.Static,
            flags = MaterialFlags.ConductsHeat | MaterialFlags.Diggable,
            baseColour = new Color32(92, 64, 51, 255), colourVariation = 8,
        };
        defs[LiftUp] = new MaterialDef
        {
            density = 0,
            behaviour = BehaviourType.Static, flags = MaterialFlags.Passable,
            baseColour = new Color32(70, 90, 70, 255), colourVariation = 0,
        };
        defs[LiftUpLight] = new MaterialDef
        {
            density = 0,
            behaviour = BehaviourType.Static, flags = MaterialFlags.Passable,
            baseColour = new Color32(100, 130, 100, 255), colourVariation = 0,
        };
        defs[Wall] = new MaterialDef
        {
            density = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(70, 70, 80, 255), colourVariation = 5,
        };
        defs[PistonBase] = new MaterialDef
        {
            density = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(55, 55, 65, 255), colourVariation = 3,
        };
        defs[PistonArm] = new MaterialDef
        {
            density = 255,
            behaviour = BehaviourType.Static, flags = MaterialFlags.None,
            baseColour = new Color32(120, 120, 140, 255), colourVariation = 5,
        };

        return defs;
    }
}
