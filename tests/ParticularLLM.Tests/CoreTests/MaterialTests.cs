using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

/// <summary>
/// Contract: Materials.CreateDefaults() returns a 256-element array of MaterialDef.
/// Each material has a defined behaviour type, density, and flags.
/// Helper methods (IsBelt, IsLift, IsPiston, IsStructureMaterial, IsSoftTerrain, IsDiggable)
/// correctly classify materials by their role.
///
/// Density ordering (heaviest to lightest): Stone=Iron > IronOre=MoltenIron > Dirt > Sand > Coal > Water > Oil > Ash > Steam > Smoke > Air
/// Behaviour types: Powder (Sand, Dirt, IronOre, Coal, Ash), Liquid (Water, Oil, MoltenIron),
///   Gas (Steam, Smoke), Static (Air, Stone, Iron, Ground, Wall, Belt*, Lift*, Piston*, Furnace)
/// </summary>
public class MaterialTests
{
    [Fact]
    public void CreateDefaults_Returns256Materials()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(256, mats.Length);
    }

    [Fact]
    public void Air_IsStatic_ZeroDensity()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Static, mats[Materials.Air].behaviour);
        Assert.Equal(0, mats[Materials.Air].density);
    }

    [Fact]
    public void Sand_IsPowder_Density128()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Powder, mats[Materials.Sand].behaviour);
        Assert.Equal(128, mats[Materials.Sand].density);
    }

    [Fact]
    public void Water_IsLiquid_Density64_Spread5()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Liquid, mats[Materials.Water].behaviour);
        Assert.Equal(64, mats[Materials.Water].density);
        Assert.Equal(5, mats[Materials.Water].spread);
    }

    [Fact]
    public void Steam_IsGas_LowDensity()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Gas, mats[Materials.Steam].behaviour);
        Assert.Equal(4, mats[Materials.Steam].density);
    }

    [Fact]
    public void Oil_IsLiquid_Flammable()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Liquid, mats[Materials.Oil].behaviour);
        Assert.True((mats[Materials.Oil].flags & MaterialFlags.Flammable) != 0);
    }

    [Fact]
    public void IronOre_IsPowder_MeltsToMoltenIron()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Powder, mats[Materials.IronOre].behaviour);
        Assert.Equal(200, mats[Materials.IronOre].meltTemp);
        Assert.Equal(Materials.MoltenIron, mats[Materials.IronOre].materialOnMelt);
    }

    [Fact]
    public void MoltenIron_IsLiquid_FreezesToIron()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Liquid, mats[Materials.MoltenIron].behaviour);
        Assert.Equal(150, mats[Materials.MoltenIron].freezeTemp);
        Assert.Equal(Materials.Iron, mats[Materials.MoltenIron].materialOnFreeze);
    }

    [Fact]
    public void Iron_IsStatic_MeltsToMoltenIron()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Static, mats[Materials.Iron].behaviour);
        Assert.Equal(200, mats[Materials.Iron].meltTemp);
        Assert.Equal(Materials.MoltenIron, mats[Materials.Iron].materialOnMelt);
    }

    [Fact]
    public void Coal_IsPowder_Flammable_BurnsToAsh()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Powder, mats[Materials.Coal].behaviour);
        Assert.True((mats[Materials.Coal].flags & MaterialFlags.Flammable) != 0);
        Assert.Equal(Materials.Ash, mats[Materials.Coal].materialOnBurn);
    }

    [Fact]
    public void Smoke_IsGas()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Gas, mats[Materials.Smoke].behaviour);
        Assert.Equal(2, mats[Materials.Smoke].density);
    }

    [Fact]
    public void Sand_DenserThanWater()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(mats[Materials.Sand].density > mats[Materials.Water].density);
    }

    [Fact]
    public void Dirt_DenserThanSand()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(mats[Materials.Dirt].density > mats[Materials.Sand].density);
    }

    [Fact]
    public void DensityOrdering_HeavySinksLightRises()
    {
        var mats = Materials.CreateDefaults();
        // Stone/Iron are max density statics
        Assert.Equal(255, mats[Materials.Stone].density);
        Assert.Equal(255, mats[Materials.Iron].density);
        // IronOre/MoltenIron > Dirt > Sand > Coal > Water > Oil > Ash > Steam > Smoke
        Assert.True(mats[Materials.IronOre].density > mats[Materials.Dirt].density);
        Assert.True(mats[Materials.Dirt].density > mats[Materials.Sand].density);
        Assert.True(mats[Materials.Sand].density > mats[Materials.Coal].density);
        Assert.True(mats[Materials.Coal].density > mats[Materials.Water].density);
        Assert.True(mats[Materials.Water].density > mats[Materials.Oil].density);
        Assert.True(mats[Materials.Oil].density > mats[Materials.Ash].density);
        Assert.True(mats[Materials.Ash].density > mats[Materials.Steam].density);
        Assert.True(mats[Materials.Steam].density > mats[Materials.Smoke].density);
    }

    [Fact]
    public void IsBelt_TrueForAllBeltMaterials()
    {
        Assert.True(Materials.IsBelt(Materials.Belt));
        Assert.True(Materials.IsBelt(Materials.BeltLeft));
        Assert.True(Materials.IsBelt(Materials.BeltRight));
        Assert.True(Materials.IsBelt(Materials.BeltLeftLight));
        Assert.True(Materials.IsBelt(Materials.BeltRightLight));
    }

    [Fact]
    public void IsBelt_FalseForNonBelt()
    {
        Assert.False(Materials.IsBelt(Materials.Air));
        Assert.False(Materials.IsBelt(Materials.Sand));
        Assert.False(Materials.IsBelt(Materials.Wall));
    }

    [Fact]
    public void IsLift_TrueForLiftMaterials()
    {
        Assert.True(Materials.IsLift(Materials.LiftUp));
        Assert.True(Materials.IsLift(Materials.LiftUpLight));
    }

    [Fact]
    public void IsPiston_TrueForPistonMaterials()
    {
        Assert.True(Materials.IsPiston(Materials.PistonBase));
        Assert.True(Materials.IsPiston(Materials.PistonArm));
        Assert.False(Materials.IsPiston(Materials.Sand));
    }

    [Fact]
    public void IsStructureMaterial_ClassifiesCorrectly()
    {
        Assert.True(Materials.IsStructureMaterial(Materials.Belt));
        Assert.True(Materials.IsStructureMaterial(Materials.BeltLeft));
        Assert.True(Materials.IsStructureMaterial(Materials.LiftUp));
        Assert.True(Materials.IsStructureMaterial(Materials.Wall));
        Assert.True(Materials.IsStructureMaterial(Materials.PistonBase));
        Assert.True(Materials.IsStructureMaterial(Materials.Furnace));
        Assert.False(Materials.IsStructureMaterial(Materials.Sand));
        Assert.False(Materials.IsStructureMaterial(Materials.Air));
    }

    [Fact]
    public void IsSoftTerrain_CorrectMaterials()
    {
        Assert.True(Materials.IsSoftTerrain(Materials.Ground));
        Assert.True(Materials.IsSoftTerrain(Materials.Dirt));
        Assert.True(Materials.IsSoftTerrain(Materials.Sand));
        Assert.True(Materials.IsSoftTerrain(Materials.Water));
        Assert.False(Materials.IsSoftTerrain(Materials.Stone));
        Assert.False(Materials.IsSoftTerrain(Materials.Air));
    }

    [Fact]
    public void LiftMaterials_ArePassable()
    {
        var mats = Materials.CreateDefaults();
        Assert.True((mats[Materials.LiftUp].flags & MaterialFlags.Passable) != 0);
        Assert.True((mats[Materials.LiftUpLight].flags & MaterialFlags.Passable) != 0);
    }

    [Fact]
    public void Ground_IsDiggable()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(Materials.IsDiggable(mats[Materials.Ground]));
        Assert.False(Materials.IsDiggable(mats[Materials.Stone]));
    }

    [Fact]
    public void StructureMaterials_AreStatic_MaxDensity()
    {
        var mats = Materials.CreateDefaults();
        byte[] structureMats = [Materials.Belt, Materials.Wall, Materials.PistonBase, Materials.PistonArm, Materials.Furnace];
        foreach (var mat in structureMats)
        {
            Assert.Equal(BehaviourType.Static, mats[mat].behaviour);
            Assert.Equal(255, mats[mat].density);
        }
    }
}
