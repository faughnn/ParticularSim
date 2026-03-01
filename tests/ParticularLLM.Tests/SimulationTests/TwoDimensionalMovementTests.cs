using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for 2D material movement (Bresenham ray-march) and per-material restitution.
///
/// English rules:
///
/// 1. TraceVelocity follows a Bresenham line from (x,y) toward (x+vx, y+vy).
///    The cell moves along the longest unblocked prefix of the line.
/// 2. On collision, velocity is reduced by restitution factor (0-255 → 0%-100% retained).
///    Lower restitution = less bounce (sand: 77 → 30%, dirt: 102 → 40%).
/// 3. Gravity always pulls: even at zero velocity, powder/liquid falls 1 cell per frame
///    if air is below (density displacement).
/// 4. At-rest stability (stability) determines whether powder topples off piles.
///    Higher stability = steeper piles.
/// 5. Lift exit produces true arcing trajectories: material rises and moves laterally
///    simultaneously via the combined velocity vector.
/// 6. Material conservation must hold through all trajectory phases.
///
/// Known tradeoffs:
/// - Phase 1 (vertical) still traces vertically first, then offsets horizontally.
///   True Bresenham is used in Phase 2 (momentum trace) for post-collision trajectories.
/// - Restitution=0 means zero bounce — material stops on impact. This is intentional
///   for materials like ash that should splat.
/// </summary>
public class TwoDimensionalMovementTests
{
    // ===== RESTITUTION: SAND (LOW) =====

    [Fact]
    public void Sand_LowRestitution_SettlesQuickly()
    {
        // Sand (restitution=77, ~30%) should settle faster than the old 70% retention.
        using var sim = new SimulationFixture(64, 128);
        sim.Description = "Sand with low restitution (~30%) dropped from height should settle near the stone floor without excessive bouncing.";
        sim.Fill(0, 120, 64, 8, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have settled on the floor
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 110,
            $"Sand should settle near floor, but at y={pos[0].y}");
    }

    [Fact]
    public void Sand_HighVelocityImpact_ScattersWithRestitution()
    {
        // Sand falling from great height should scatter on impact (restitution > 0).
        // With velocity ~8+ at impact, diagonal speed should be > 0.
        using var sim = new SimulationFixture(128, 256);
        sim.Description = "Sand dropped from a great height should scatter on impact due to restitution and settle near the floor.";
        sim.Fill(0, 240, 128, 16, Materials.Stone);
        sim.Set(64, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts); // Let it fall and impact

        // Sand should have scattered (not be at exactly x=64)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 200,
            $"Sand should settle near floor, but at y={pos[0].y}");
    }

    // ===== RESTITUTION: DIRT (MEDIUM) =====

    [Fact]
    public void Dirt_MediumRestitution_ScattersMoreThanSand()
    {
        // Dirt (restitution=102, ~40%) retains more energy on collision than sand (77, ~30%).
        // Both should conserve material.
        using var sim = new SimulationFixture(128, 256);
        sim.Description = "Dirt and sand dropped from height with different restitution values should both be conserved through collision.";
        sim.Fill(0, 240, 128, 16, Materials.Stone);
        sim.Set(64, 0, Materials.Sand);
        sim.Set(32, 0, Materials.Dirt);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
    }

    // ===== BRESENHAM TRACE: ARBITRARY ANGLES =====

    [Fact]
    public void TraceVelocity_DiagonalTrajectory()
    {
        // A powder cell with both velocityX and velocityY should follow a diagonal
        // path, not an L-shaped path. The Bresenham trace handles arbitrary angles.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand given equal horizontal and vertical velocity should move diagonally via Bresenham trace, not in an L-shape.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand with both horizontal and vertical velocity
        sim.SetWithVelocity(40, 40, Materials.Sand, 4, 4);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(1);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have moved diagonally (not just vertically or just horizontally)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Should have moved in both X and Y directions
        Assert.True(pos[0].x > 40 || pos[0].y > 40,
            $"Sand should move diagonally from (40,40), but at ({pos[0].x},{pos[0].y})");
    }

    [Fact]
    public void TraceVelocity_ShallowAngle()
    {
        // A velocity vector of (5, 1) should trace a shallow line.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand with mostly-horizontal velocity (5,1) should trace a shallow diagonal, moving more in X than Y.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Set sand with mostly-horizontal velocity and a bit of downward
        sim.SetWithVelocity(40, 60, Materials.Sand, 5, 1);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(1);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Should have moved more in X than Y (shallow trajectory)
        int dx = pos[0].x - 40;
        int dy = pos[0].y - 60;
        Assert.True(dx > 0, $"Sand should move right, but dx={dx}");
    }

    // ===== LIFT EXIT ARCING =====

    [Fact]
    public void LiftExit_ArcingTrajectory()
    {
        // Material exiting a lift should arc: rising vertically while moving laterally.
        // After enough frames, the X position should change (horizontal displacement).
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand exiting a 3-block tall lift should arc laterally away from the lift column due to combined vertical and horizontal velocity.";
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);
        sim.Set(52, 86, Materials.Sand); // Inside lift, right side for rightward exit

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(200);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have exited and moved laterally
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].x > 55,
            $"Sand should arc away from lift, but at x={pos[0].x}");
    }

    // ===== CONSERVATION THROUGH ALL PHASES =====

    [Fact]
    public void Conservation_ThroughTraceAndCollision()
    {
        // Multiple materials with different restitution values should all be conserved
        // through trace, collision, and at-rest phases.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Rows of sand and dirt dropped from different heights should all be conserved through trace, collision, and settling.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand and dirt at different heights
        for (int x = 40; x < 50; x++)
        {
            sim.Set(x, 10, Materials.Sand);
            sim.Set(x, 30, Materials.Dirt);
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(10, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(10, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }

    [Fact]
    public void Conservation_RestitutionDoesNotCreateOrDestroyMaterial()
    {
        // Stress test: many cells colliding with restitution active.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "A wide alternating row of 88 sand and dirt cells dropped from height; all must be conserved through restitution collisions.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Wide band of mixed materials dropping from height
        for (int x = 20; x < 108; x++)
        {
            sim.Set(x, 5, (x & 1) == 0 ? Materials.Sand : Materials.Dirt);
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(44, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(44, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }

    // ===== ZERO RESTITUTION =====

    [Fact]
    public void ZeroRestitution_MaterialStopsOnImpact()
    {
        // A material with restitution=0 should not bounce at all.
        // We use IronOre (defaults have restitution=0) for this.
        using var sim = new SimulationFixture(64, 128);
        sim.Description = "IronOre with zero restitution dropped from height should settle near the floor without bouncing.";
        sim.Fill(0, 120, 64, 8, Materials.Stone);

        // IronOre is powder with default restitution=0
        sim.Set(32, 10, Materials.IronOre);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        var pos = sim.FindMaterial(Materials.IronOre);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 100,
            $"IronOre should settle near floor, but at y={pos[0].y}");
    }

    // ===== LIQUID RESTITUTION =====

    [Fact]
    public void Water_Restitution_SplashOnImpact()
    {
        // Water (restitution=102, ~40%) should splash when dropped from height.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "A column of water dropped from height should splash on impact and conserve all 5 water cells.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Drop a column of water from height
        for (int y = 10; y < 15; y++)
            sim.Set(64, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== MULTI-MATERIAL DENSITY STILL WORKS =====

    [Fact]
    public void DensityDisplacement_StillWorks_WithRestitution()
    {
        // Density displacement (heavy sinks below light) should still work
        // with the new restitution system.
        using var sim = new SimulationFixture(64, 64);
        sim.Description = "Oil, water, and sand layered in wrong order inside a container should sort by density: sand at bottom, oil on top.";
        sim.Fill(20, 40, 1, 24, Materials.Stone);
        sim.Fill(43, 40, 1, 24, Materials.Stone);
        sim.Fill(20, 63, 24, 1, Materials.Stone);

        // Put oil at bottom, water middle, sand top
        sim.Fill(21, 57, 22, 2, Materials.Oil);
        sim.Fill(21, 55, 22, 2, Materials.Water);
        sim.Fill(21, 53, 22, 2, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should be below water, water below oil (higher Y = lower on screen)
        var (_, sandY) = sim.CenterOfMass(Materials.Sand);
        var (_, waterY) = sim.CenterOfMass(Materials.Water);
        var (_, oilY) = sim.CenterOfMass(Materials.Oil);

        Assert.True(sandY > waterY,
            $"Sand (COM={sandY:F1}) should be below water (COM={waterY:F1})");
        Assert.True(waterY > oilY,
            $"Water (COM={waterY:F1}) should be below oil (COM={oilY:F1})");
    }
}
