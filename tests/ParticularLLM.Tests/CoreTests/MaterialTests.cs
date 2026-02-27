using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

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
}
