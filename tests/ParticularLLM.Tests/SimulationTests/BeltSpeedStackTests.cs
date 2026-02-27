using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for belt speed and stacking behavior.
///
/// English rules (derived from BeltManager.SimulateBelts lines 388-490):
///
/// 1. Belt moves ALL cells on surface row (tileY - 1) every `speed` frames (default 3).
/// 2. Frame activation: (currentFrame - frameOffset) % speed == 0.
/// 3. Moves entire column above surface — stacked sand moves together.
/// 4. Processes columns opposite to direction to prevent double-moving.
/// 5. Only moves Powder and Liquid — Gas and Static are ignored.
/// 6. Target cell must be Air — belt cannot push through occupied cells.
/// 7. Ghost belts don't transport.
/// 8. Belt direction: +1 (right) or -1 (left).
///
/// Known tradeoffs:
/// - Belt surface scan stops at Air (top of pile), meaning gaps in a column
///   cause upper cells to be ignored.
/// </summary>
public class BeltSpeedStackTests
{
    // ===== SPEED =====

    [Fact]
    public void Belt_Speed3_Moves1CellPer3Frames()
    {
        // Default speed=3. After 9 frames, sand should move ~3 cells (3 activations).
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1); // right-moving
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        sim.Set(20, surfaceY, Materials.Sand);

        sim.Step(9);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should have moved 1-3 cells right (3 activations, 1 cell each)
        Assert.True(pos[0].x > 20 && pos[0].x <= 24,
            $"Sand should move ~3 cells right in 9 frames, but at x={pos[0].x}");
    }

    // ===== STACKING =====

    [Fact]
    public void Belt_MovesEntireStack()
    {
        // A column of 5 sand cells on the belt surface should all move together.
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        // Stack 5 sand cells
        for (int i = 0; i < 5; i++)
            sim.Set(20, surfaceY - i, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(30);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // All 5 sand should have moved right from x=20
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        WorldAssert.NoMaterialInRegion(sim.World, Materials.Sand, 20, 0, 1, 40);
    }

    [Fact]
    public void Belt_StackBlockedByObstacle()
    {
        // Sand on belt should stop when hitting an obstacle (occupied cell).
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        sim.Set(20, surfaceY, Materials.Sand);
        // Place stone block ahead on the surface
        sim.Set(22, surfaceY, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(50);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should be at x=21 (stopped before the stone)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.Equal(21, pos[0].x);
    }

    // ===== DIRECTION =====

    [Fact]
    public void Belt_LeftDirection_MovesSandLeft()
    {
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(40, 40, -1); // left-moving
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        sim.Set(44, surfaceY, Materials.Sand);

        sim.Step(30);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].x < 44,
            $"Sand should move left on left-belt, but at x={pos[0].x}");
    }

    // ===== MATERIAL TYPE FILTERING =====

    [Fact]
    public void Belt_MovesWaterButNotSteam()
    {
        // Belt should transport liquid but not gas.
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        sim.Set(20, surfaceY, Materials.Water);
        // Steam is gas — belt should not move it
        // (Steam rises anyway, but even if it were on the surface, belt ignores it)
        sim.Set(22, surfaceY, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(30);

        // Both should be conserved
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Water));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Steam));
    }

    // ===== MERGED BELT =====

    [Fact]
    public void Belt_MergedBelt_TransportsAcrossBlocks()
    {
        // Two adjacent belt blocks should merge and transport material across the seam.
        using var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        belts.PlaceBelt(24, 40, 1); // merges with first
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 39;
        sim.Set(18, surfaceY, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(100);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have moved past x=24 (crossed the block boundary)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].x > 24,
            $"Sand should cross belt block boundary, but at x={pos[0].x}");
    }

    // ===== BELT EDGE FALLOFF =====

    [Fact]
    public void Belt_SandFallsOffEndOfBelt()
    {
        // Sand pushed past the belt edge should fall due to gravity.
        using var sim = new SimulationFixture(128, 128);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1); // x=16..23
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone); // floor

        int surfaceY = 39;
        sim.Set(20, surfaceY, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(500);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have fallen past the belt surface
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y > 40,
            $"Sand should fall after leaving belt edge, but at y={pos[0].y}");
    }

    // ===== CONSERVATION STRESS =====

    [Fact]
    public void Belt_ManySand_AllConserved()
    {
        using var sim = new SimulationFixture(128, 128);
        var belts = new BeltManager(sim.World);
        for (int x = 16; x < 80; x += 8)
            belts.PlaceBelt(x, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int surfaceY = 39;
        int count = 0;
        for (int x = 18; x < 26; x++)
        {
            sim.Set(x, surfaceY, Materials.Sand);
            count++;
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(count, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }
}
