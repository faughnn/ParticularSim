using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class BeltPlacementTests
{
    [Fact]
    public void PlaceBelt_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(10, 10, 1));
        Assert.True(belts.HasBeltAt(8, 8));
    }

    [Fact]
    public void PlaceBelt_SnapsToGrid()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(10, 10, 1);
        Assert.True(belts.HasBeltAt(8, 8));
        Assert.True(belts.HasBeltAt(15, 15));
        Assert.False(belts.HasBeltAt(7, 8));
        Assert.False(belts.HasBeltAt(16, 8));
    }

    [Fact]
    public void PlaceBelt_OnStone_Fails()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Stone);
        var belts = new BeltManager(world);
        Assert.False(belts.PlaceBelt(10, 10, 1));
    }

    [Fact]
    public void PlaceBelt_OnSoftTerrain_CreatesGhost()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(8, 8, Materials.Ground);
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(8, 8, 1));
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);
    }

    [Fact]
    public void PlaceBelt_WritesBeltMaterialToCells()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.True(Materials.IsBelt(world.GetCell(8 + dx, 8 + dy)),
                    $"Cell ({8+dx},{8+dy}) should be belt material");
    }

    [Fact]
    public void PlaceBelt_Overlap_Fails()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(8, 8, 1));
        Assert.False(belts.PlaceBelt(8, 8, -1));
    }

    [Fact]
    public void RemoveBelt_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);
        Assert.True(belts.RemoveBelt(8, 8));
        Assert.False(belts.HasBeltAt(8, 8));
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Air, world.GetCell(8 + dx, 8 + dy));
    }

    [Fact]
    public void AdjacentBelts_SameDirection_Merge()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);
        belts.PlaceBelt(16, 8, 1);
        Assert.Equal(1, belts.BeltCount);
    }

    [Fact]
    public void AdjacentBelts_DifferentDirection_DontMerge()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);
        belts.PlaceBelt(16, 8, -1);
        Assert.Equal(2, belts.BeltCount);
    }
}
