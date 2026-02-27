using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

/// <summary>
/// Contract: WallManager placement operations.
///
/// - PlaceWall snaps to 8x8 grid, writes Wall material to all 64 cells.
/// - Placement fails if any cell has non-soft-terrain (stone, other structures).
/// - Placement on soft terrain creates a ghost wall (doesn't write material until cleared).
/// - RemoveWall clears all 64 cells to Air.
/// - Wall material is static with max density, blocking all material movement.
/// </summary>
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
