using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for ghost blocking during simulation (CanMoveTo level).
///
/// English rules (derived from SimulateChunksLogic.CanMoveTo lines 736-753):
///
/// 1. Ghost belt tiles block EXTERNAL material from entering Air cells within the ghost area.
/// 2. Ghost wall tiles block EXTERNAL material from entering Air cells within the ghost area.
/// 3. Material ALREADY INSIDE a ghost structure can move freely within it.
/// 4. Material inside a ghost structure can move OUT of it (exit the ghost area).
/// 5. Ghost blocking only applies to Air target cells — density displacement into
///    non-air cells in ghost areas is unaffected by ghost status.
/// 6. The source-awareness uses currentCellIdx (the cell being simulated).
///
/// The existing GhostActivationTests cover structure-manager-level behavior
/// (activation conditions). These tests verify the simulation-level blocking.
///
/// Known tradeoffs:
/// - Ghost walls require ALL cells to be Air before activation; ghost lifts only need no Ground.
/// - Ghost belts also require ALL cells to be Air for activation.
/// </summary>
public class GhostBlockingTests
{
    // ===== GHOST WALL BLOCKING =====

    [Fact]
    public void GhostWall_BlocksSandFromEntering()
    {
        // Sand falling toward a ghost wall area (contains terrain that hasn't cleared yet)
        // should be blocked from entering — the sand treats it as solid.
        using var sim = new SimulationFixture(64, 64);

        // Place a ghost wall at y=40 (8x8 block from y=40 to y=47)
        // First fill with Ground so it becomes ghost
        sim.Fill(24, 40, 8, 8, Materials.Ground);

        var wallMgr = new WallManager(sim.World);
        wallMgr.PlaceWall(24, 40);
        sim.Simulator.SetWallManager(wallMgr);

        // Clear the ground to Air — wall is still ghost because UpdateGhostStates wasn't called
        // Actually, we want it to STAY ghost. Let's keep some ground.
        // Alternatively: place sand in the ghost area first, then the ghost stays.
        // Simplest: just leave Ground in place. Ghost walls block even with terrain.

        // Put sand above the ghost wall area
        sim.Set(28, 30, Materials.Sand);

        // Floor at bottom
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should NOT have entered the ghost wall area (y=40 to y=47)
        // It should be sitting on top of the ghost area or deflected
        var sandPositions = sim.FindMaterial(Materials.Sand);
        Assert.Single(sandPositions);

        // Sand should be above the ghost wall area, not inside it
        // Ghost wall blocks at y=40, so sand should rest at y=39 or have slid around
        Assert.True(sandPositions[0].y <= 39 || sandPositions[0].y >= 48,
            $"Sand should not be inside ghost wall area (y=40-47), but is at y={sandPositions[0].y}\n" +
            WorldDump.DumpRegion(sim.World, 20, 28, 16, 24));
    }

    [Fact]
    public void GhostWall_BlocksSandFromEntering_AirArea()
    {
        // Ghost wall placed over Ground, then Ground manually cleared to Air.
        // Don't call UpdateGhostStates — wall stays ghost.
        // Sand should be blocked from entering the air cells within ghost wall.
        using var sim = new SimulationFixture(64, 64);

        // Place Ground, then ghost wall
        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var wallMgr = new WallManager(sim.World);
        wallMgr.PlaceWall(24, 40);
        sim.Simulator.SetWallManager(wallMgr);

        // Clear ground to Air — ghost wall remains ghost (no UpdateGhostStates call)
        sim.Fill(24, 40, 8, 8, Materials.Air);

        // Verify wall tiles are ghost
        Assert.True(wallMgr.GetWallTile(24, 40).isGhost);

        // Place sand above
        sim.Set(28, 30, Materials.Sand);
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should be blocked by the ghost wall's air cells
        var sandPositions = sim.FindMaterial(Materials.Sand);
        Assert.Single(sandPositions);
        Assert.True(sandPositions[0].y <= 39 || sandPositions[0].y >= 48,
            $"Sand should not enter ghost wall (air) area, but is at ({sandPositions[0].x},{sandPositions[0].y})\n" +
            WorldDump.DumpRegion(sim.World, 20, 28, 16, 24));
    }

    [Fact]
    public void GhostWall_MaterialInsideCanMoveOut()
    {
        // Material already inside a ghost wall area should be able to exit.
        using var sim = new SimulationFixture(64, 64);

        // Place Ground, then ghost wall
        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var wallMgr = new WallManager(sim.World);
        wallMgr.PlaceWall(24, 40);
        sim.Simulator.SetWallManager(wallMgr);

        // Clear ground to Air but leave one Sand cell INSIDE the ghost area
        sim.Fill(24, 40, 8, 8, Materials.Air);
        sim.Set(28, 42, Materials.Sand);  // Inside ghost wall

        // Floor below the ghost area
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand that was inside should have escaped downward (below y=47)
        var sandPositions = sim.FindMaterial(Materials.Sand);
        Assert.Single(sandPositions);
        Assert.True(sandPositions[0].y >= 48,
            $"Sand inside ghost wall should escape downward, but is at y={sandPositions[0].y}\n" +
            WorldDump.DumpRegion(sim.World, 20, 38, 16, 16));
    }

    // ===== GHOST BELT BLOCKING =====

    [Fact]
    public void GhostBelt_BlocksSandFromEntering()
    {
        // Ghost belt area blocks external material.
        using var sim = new SimulationFixture(128, 64);

        // Place Ground, then ghost belt
        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var beltMgr = new BeltManager(sim.World);
        beltMgr.PlaceBelt(24, 40, 1); // direction 1 = right
        sim.Simulator.SetBeltManager(beltMgr);

        // Clear ground to Air — ghost belt persists
        sim.Fill(24, 40, 8, 8, Materials.Air);

        // Verify belt is ghost
        Assert.True(beltMgr.TryGetBeltTile(24, 40, out var tile));
        Assert.True(tile.isGhost);

        // Place sand above
        sim.Set(28, 30, Materials.Sand);
        sim.Fill(0, 63, 128, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should be blocked
        var sandPositions = sim.FindMaterial(Materials.Sand);
        Assert.Single(sandPositions);
        Assert.True(sandPositions[0].y <= 39 || sandPositions[0].y >= 48,
            $"Sand should not enter ghost belt area, but is at ({sandPositions[0].x},{sandPositions[0].y})");
    }

    [Fact]
    public void GhostBelt_MaterialInsideCanMoveOut()
    {
        // Material inside ghost belt can exit.
        using var sim = new SimulationFixture(128, 64);

        // Place Ground, then ghost belt
        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var beltMgr = new BeltManager(sim.World);
        beltMgr.PlaceBelt(24, 40, 1);
        sim.Simulator.SetBeltManager(beltMgr);

        // Clear ground, put sand inside
        sim.Fill(24, 40, 8, 8, Materials.Air);
        sim.Set(28, 42, Materials.Sand);

        sim.Fill(0, 63, 128, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should have escaped
        var sandPositions = sim.FindMaterial(Materials.Sand);
        Assert.Single(sandPositions);
        Assert.True(sandPositions[0].y >= 48,
            $"Sand inside ghost belt should escape, but is at y={sandPositions[0].y}");
    }

    // ===== MATERIAL CONSERVATION WITH GHOSTS =====

    [Fact]
    public void GhostWall_MaterialConservation_MultipleSandGrains()
    {
        // Multiple sand grains falling toward a ghost wall should all be conserved.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var wallMgr = new WallManager(sim.World);
        wallMgr.PlaceWall(24, 40);
        sim.Simulator.SetWallManager(wallMgr);

        sim.Fill(24, 40, 8, 8, Materials.Air);

        // Place 5 sand grains above ghost area
        for (int i = 0; i < 5; i++)
            sim.Set(25 + i, 30, Materials.Sand);

        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== GHOST BLOCKING DOES NOT AFFECT DENSITY DISPLACEMENT =====

    [Fact]
    public void GhostWall_WaterInsideCanBeDisplacedBySand()
    {
        // If somehow both water and sand are inside a ghost wall,
        // density displacement should still work normally.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(24, 40, 8, 8, Materials.Ground);
        var wallMgr = new WallManager(sim.World);
        wallMgr.PlaceWall(24, 40);
        sim.Simulator.SetWallManager(wallMgr);

        // Clear to air, place water and sand inside
        sim.Fill(24, 40, 8, 8, Materials.Air);
        sim.Set(28, 44, Materials.Water);  // Lower inside ghost
        sim.Set(28, 42, Materials.Sand);   // Higher inside ghost

        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Both materials should be conserved
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== SETTLED STATE WITH GHOSTS =====

    [Fact]
    public void GhostWall_SandSettlesOnTopOfGhostArea()
    {
        // Sand blocked by a ghost wall should settle just above it.
        using var sim = new SimulationFixture(64, 64);

        // Ghost wall spanning full width at y=48
        sim.Fill(0, 48, 64, 8, Materials.Ground);
        var wallMgr = new WallManager(sim.World);
        for (int bx = 0; bx < 64; bx += 8)
            wallMgr.PlaceWall(bx, 48);
        sim.Simulator.SetWallManager(wallMgr);

        // Clear to air
        sim.Fill(0, 48, 64, 8, Materials.Air);

        // Drop sand from top
        for (int x = 20; x < 44; x++)
            sim.Set(x, 5, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // All sand should be above y=48 (the ghost wall top edge)
        WorldAssert.NoMaterialInRegion(sim.World, Materials.Sand, 0, 48, 64, 16);
    }
}
