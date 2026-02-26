using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class WallPlacementTests
{
    [Fact]
    public void PlaceWall_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        Assert.True(walls.PlaceWall(10, 10));
        Assert.True(walls.HasWallAt(8, 8));
    }

    [Fact]
    public void PlaceWall_WritesWallMaterial()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Wall, world.GetCell(8 + dx, 8 + dy));
    }

    [Fact]
    public void PlaceWall_OnStone_Fails()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Stone);
        var walls = new WallManager(world);
        Assert.False(walls.PlaceWall(10, 10));
    }

    [Fact]
    public void RemoveWall_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);
        Assert.True(walls.RemoveWall(8, 8));
        Assert.False(walls.HasWallAt(8, 8));
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Air, world.GetCell(8 + dx, 8 + dy));
    }
}
