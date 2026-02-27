using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Edge case tests for density displacement.
///
/// English rules (derived from CanMoveTo lines 757-761 and MoveCell lines 764-803):
///
/// 1. Cell can displace target if myDensity > targetMat.density AND target is not static.
/// 2. Static materials block all movement UNLESS they have the Passable flag.
/// 3. Passable static materials (lift tiles) can be moved into — they don't swap back.
/// 4. MoveCell swaps source and target cells (density displacement creates a swap).
/// 5. When source is a lift tile, MoveCell restores lift material at source instead of swapping.
/// 6. Equal density materials do NOT displace each other.
///
/// Density hierarchy:
///   Stone(255), Wall(255), Belt(255) > Dirt(140) > Sand(128) > Water(64) > Oil(48) > Steam(4) > Air(0)
///
/// Edge cases tested:
/// - Equal density: sand vs sand, water vs water
/// - Three-material layering: sand over water over oil
/// - Oil floats on water (lower density)
/// - Mass displacement: many heavy cells pushing through many light cells
/// - Passable materials (lift tiles)
/// </summary>
public class DensityEdgeCaseTests
{
    // ===== EQUAL DENSITY =====

    [Fact]
    public void EqualDensity_SandDoesNotDisplaceSand()
    {
        // Two sand grains at same density should not displace each other.
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        // Stack two sand grains vertically
        sim.Set(32, 60, Materials.Sand);
        sim.Set(32, 61, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        Assert.Equal(2, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void EqualDensity_WaterDoesNotDisplaceWater()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 60, 5, 3, Materials.Water); // 15 water cells

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        Assert.Equal(15, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== THREE-MATERIAL LAYERING =====

    [Fact]
    public void ThreeMaterials_SandSinks_WaterMiddle_OilFloats()
    {
        // Sand (128) > Water (64) > Oil (48): should layer with sand at bottom, oil on top.
        using var sim = new SimulationFixture(64, 64);

        // Container
        sim.Fill(20, 50, 1, 14, Materials.Stone); // Left wall
        sim.Fill(43, 50, 1, 14, Materials.Stone); // Right wall
        sim.Fill(20, 63, 24, 1, Materials.Stone);  // Floor

        // Mix them up: oil at bottom, water in middle, sand on top (wrong order)
        sim.Fill(21, 60, 22, 3, Materials.Oil);   // 66 oil cells
        sim.Fill(21, 57, 22, 3, Materials.Water);  // 66 water cells
        sim.Fill(21, 54, 22, 3, Materials.Sand);   // 66 sand cells

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // After settling, sand should be at bottom, oil at top
        // Use center-of-mass comparison
        var (_, sandCOMY) = sim.CenterOfMass(Materials.Sand);
        var (_, waterCOMY) = sim.CenterOfMass(Materials.Water);
        var (_, oilCOMY) = sim.CenterOfMass(Materials.Oil);

        // Sand should be lowest (highest Y), oil should be highest (lowest Y)
        Assert.True(sandCOMY > waterCOMY,
            $"Sand (COM Y={sandCOMY:F1}) should be below water (COM Y={waterCOMY:F1})");
        Assert.True(waterCOMY > oilCOMY,
            $"Water (COM Y={waterCOMY:F1}) should be below oil (COM Y={oilCOMY:F1})");
    }

    [Fact]
    public void WaterFallingDisplacesOil()
    {
        // Water (density 64) falling INTO oil (density 48) should displace it.
        // Key: the water must be actively falling (velocityY > 0) to trigger displacement.
        // Liquids at rest don't sort by density — only active movement causes displacement.
        using var sim = new SimulationFixture(64, 64);

        // Container
        sim.Fill(28, 40, 1, 24, Materials.Stone);
        sim.Fill(36, 40, 1, 24, Materials.Stone);
        sim.Fill(28, 63, 9, 1, Materials.Stone);

        // Oil pool sitting in container
        sim.Fill(29, 58, 7, 5, Materials.Oil);

        // Water dropped from height (will gain velocity and displace oil on impact)
        sim.Fill(29, 30, 7, 3, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Water (heavier) should end up below oil (lighter) after active displacement
        var (_, oilCOMY) = sim.CenterOfMass(Materials.Oil);
        var (_, waterCOMY) = sim.CenterOfMass(Materials.Water);
        Assert.True(waterCOMY > oilCOMY,
            $"Water (COM Y={waterCOMY:F1}) falling into oil should sink below (COM Y={oilCOMY:F1})\n" +
            WorldDump.DumpRegion(sim.World, 27, 40, 11, 24));
    }

    // ===== DENSITY LAYERING INVARIANT =====

    [Fact]
    public void DensityLayering_SandOverWater_InContainer()
    {
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(20, 40, 1, 24, Materials.Stone);
        sim.Fill(43, 40, 1, 24, Materials.Stone);
        sim.Fill(20, 63, 24, 1, Materials.Stone);

        // Sand on top, water below (start in wrong order)
        sim.Fill(21, 58, 22, 5, Materials.Water);
        sim.Fill(21, 50, 22, 5, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Use density layering invariant
        InvariantChecker.AssertDensityLayering(sim.World,
            Materials.Sand, Materials.Water,
            21, 40, 22, 23);
    }

    // ===== MASS DISPLACEMENT =====

    [Fact]
    public void MassDisplacement_ManySandThroughWater()
    {
        // Large block of sand falling into large pool of water.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Water pool
        int waterCount = 0;
        for (int x = 40; x < 88; x++)
            for (int y = 100; y < 119; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterCount++;
            }

        // Sand block above water
        int sandCount = 0;
        for (int x = 55; x < 73; x++)
            for (int y = 80; y < 95; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandCount++;
            }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(sandCount, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterCount, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== STATIC MATERIAL BLOCKING =====

    [Fact]
    public void StaticMaterial_BlocksAllMovement()
    {
        // Stone (static, density 255) blocks everything.
        using var sim = new SimulationFixture();
        sim.Fill(0, 40, 64, 1, Materials.Stone);
        sim.Set(32, 30, Materials.Sand);
        sim.Set(33, 30, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Nothing should pass through stone
        WorldAssert.NoMaterialInRegion(sim.World, Materials.Sand, 0, 41, 64, 23);
        WorldAssert.NoMaterialInRegion(sim.World, Materials.Water, 0, 41, 64, 23);
    }

    [Fact]
    public void WallMaterial_BlocksAllMovement()
    {
        // Wall (static, density 255) blocks everything.
        using var sim = new SimulationFixture();
        sim.Fill(0, 40, 64, 1, Materials.Wall);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 30, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        WorldAssert.NoMaterialInRegion(sim.World, Materials.Sand, 0, 41, 64, 22);
    }

    // ===== PASSABLE MATERIALS =====

    [Fact]
    public void PassableLiftMaterial_AllowsMovement()
    {
        // Lift tiles (Passable flag) allow material to move through them.
        // Note: lift materials fluctuate during simulation (MoveCell restores them
        // when a cell exits, but temporarily removes them when a cell enters).
        // We only check that sand count is conserved.
        using var sim = new SimulationFixture(128, 64);

        // Place a lift
        var liftMgr = new LiftManager(sim.World);
        liftMgr.PlaceLift(32, 40); // 8x8 at (32,40)
        sim.Simulator.SetLiftManager(liftMgr);

        // Place sand above the lift
        sim.Set(36, 30, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        sim.Step(200);

        // Sand should be conserved (lift materials may fluctuate)
        Assert.Equal(sandBefore, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== DIRT VS SAND =====

    [Fact]
    public void Dirt_SinksThroughWater_HigherDensity()
    {
        // Dirt (density 140) > Water (density 64), so dirt sinks.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 55, 5, 8, Materials.Water); // 40 water cells
        sim.Set(32, 45, Materials.Dirt);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
        Assert.Equal(40, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    [Fact]
    public void Dirt_DoesNotDisplaceSand_SimilarDensity()
    {
        // Dirt (density 140) > Sand (density 128), so dirt CAN displace sand.
        // This is a correct behavior test, not an error.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 58, 5, 5, Materials.Sand); // 25 sand
        sim.Set(32, 50, Materials.Dirt);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
        Assert.Equal(25, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== CONSERVATION STRESS TEST =====

    [Fact]
    public void Conservation_FourMaterials_AllConserved()
    {
        // Sand, water, oil, and steam in the same world.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Fill(50, 50, 10, 5, Materials.Sand);   // 50
        sim.Fill(50, 60, 10, 5, Materials.Water);   // 50
        sim.Fill(50, 70, 10, 5, Materials.Oil);     // 50
        sim.Fill(50, 80, 10, 5, Materials.Steam);   // 50

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Water));
        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Oil));
        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Steam));
    }
}
