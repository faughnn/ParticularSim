using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

/// <summary>
/// Contract: Ghost structure activation.
///
/// Structures placed on soft terrain start as "ghosts" — they reserve the space but don't
/// write their material until the terrain is cleared. Ghost activation rules:
///
/// - Belt ghosts: activate when ALL 64 cells in the 8x8 area are Air (no material of any kind).
///   Even Sand or Water blocks activation.
/// - Wall ghosts: same as belts — activate only when all cells are Air.
/// - Lift ghosts: activate when no Ground cells remain. Sand, Water, and other non-Ground
///   materials are allowed (lifts are more permissive than belts/walls).
/// - On activation, the structure writes its material to Air cells only.
///   Non-Air cells (like Sand inside an activated lift) are preserved.
/// - Ghost structures still block non-ghost material from entering their zone
///   via the CanMoveTo ghost-blocking check.
/// </summary>
public class GhostActivationTests
{
    [Fact]
    public void GhostBelt_ActivatesWhenTerrainCleared()
    {
        var world = new CellWorld(128, 64);
        // Fill 8x8 area with Ground so belt becomes ghost
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);

        // Verify it was placed as ghost
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);

        // Ghost belt should NOT have written belt material to cells
        Assert.Equal(Materials.Ground, world.GetCell(8, 8));

        // Clear terrain to Air
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);

        belts.UpdateGhostStates();

        // Belt should now be activated (not ghost)
        Assert.True(belts.TryGetBeltTile(8, 8, out var activatedTile));
        Assert.False(activatedTile.isGhost);

        // Belt material should now be written to cells
        Assert.True(Materials.IsBelt(world.GetCell(8, 8)),
            $"Expected belt material at (8,8), got {world.GetCell(8, 8)}");
    }

    [Fact]
    public void GhostBelt_DoesNotActivate_WithRemainingTerrain()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);

        // Clear most but leave one cell as Ground
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);
        world.SetCell(10, 10, Materials.Ground);

        belts.UpdateGhostStates();

        // Should still be ghost because one Ground cell remains
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);
    }

    [Fact]
    public void GhostBelt_DoesNotActivate_WithSandRemaining()
    {
        // Belts require ALL cells to be Air (even powder blocks activation)
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);

        // Replace all ground with Air except one cell which is Sand
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);
        world.SetCell(10, 10, Materials.Sand);

        belts.UpdateGhostStates();

        // Should still be ghost - belts need ALL Air
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);
    }

    [Fact]
    public void GhostWall_ActivatesWhenTerrainCleared()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);

        // Verify placed as ghost
        Assert.True(walls.GetWallTile(8, 8).isGhost);

        // Ghost wall should NOT have written wall material to cells
        Assert.Equal(Materials.Ground, world.GetCell(8, 8));

        // Clear terrain
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);

        walls.UpdateGhostStates();

        // Wall should now be activated
        Assert.False(walls.GetWallTile(8, 8).isGhost);
        Assert.Equal(Materials.Wall, world.GetCell(8, 8));
    }

    [Fact]
    public void GhostWall_DoesNotActivate_WithRemainingTerrain()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);

        // Clear most but leave one cell as Sand
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);
        world.SetCell(12, 12, Materials.Sand);

        walls.UpdateGhostStates();

        // Should still be ghost
        Assert.True(walls.GetWallTile(8, 8).isGhost);
    }

    [Fact]
    public void GhostLift_ActivatesWhenGroundCleared_AllowsPowder()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        // Verify placed as ghost
        Assert.True(lifts.GetLiftTile(8, 8).isGhost);

        // Replace all Ground with Sand (not Air)
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Sand);

        lifts.UpdateGhostStates();

        // Should activate because no Ground remains (Sand is OK for lifts)
        Assert.False(lifts.GetLiftTile(8, 8).isGhost);
    }

    [Fact]
    public void GhostLift_DoesNotActivate_WithGroundRemaining()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        // Clear most to Sand but leave one cell as Ground
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Sand);
        world.SetCell(10, 10, Materials.Ground);

        lifts.UpdateGhostStates();

        // Should still be ghost because Ground remains
        Assert.True(lifts.GetLiftTile(8, 8).isGhost);
    }

    [Fact]
    public void GhostLift_ActivatesWithWaterPresent()
    {
        // Lifts should activate even with Water present (only Ground blocks activation)
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        // Replace all Ground with Water
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Water);

        lifts.UpdateGhostStates();

        // Should activate because no Ground remains
        Assert.False(lifts.GetLiftTile(8, 8).isGhost);
    }

    [Fact]
    public void GhostLift_WritesLiftMaterial_OnlyToAirCells()
    {
        // When a ghost lift activates with Sand present, lift material
        // should only be written to Air cells, not over the Sand
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        // Replace ground: mostly Air but a few Sand cells
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);
        world.SetCell(10, 10, Materials.Sand);
        world.SetCell(11, 11, Materials.Sand);

        lifts.UpdateGhostStates();

        // Should activate (no Ground)
        Assert.False(lifts.GetLiftTile(8, 8).isGhost);

        // Air cells should have lift material
        Assert.True(Materials.IsLift(world.GetCell(8, 8)),
            $"Expected lift material at (8,8), got {world.GetCell(8, 8)}");

        // Sand cells should still be Sand (lift doesn't overwrite them)
        Assert.Equal(Materials.Sand, world.GetCell(10, 10));
        Assert.Equal(Materials.Sand, world.GetCell(11, 11));
    }
}
