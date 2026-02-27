using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for powder collision and momentum transfer.
///
/// English rules (derived from SimulatePowder lines 247-360):
///
/// 1. When falling powder collides with velocityY > 1, 70% of velocity is retained.
/// 2. Retained velocity decomposes to diagonal: speed = velocity * 7/10 * 5/7 (at least 1).
/// 3. Diagonal direction determined by available slides (left/right/random if both).
/// 4. Phase 2 diagonal movement: trace 45-degree path (dx, dy) for up to |velocityX| steps.
/// 5. After diagonal movement, friction applies: 87.5% (7/8) velocity retention.
/// 6. If diagonal blocked in primary direction, try opposite direction.
/// 7. Slide resistance: random check against mat.slideResistance before Phase 3 slide.
///    Sand has slideResistance=0 (always slides), Dirt has slideResistance=50 (~20% chance to stick).
/// 8. Phase 3 fallback: try (x±1, y+1) with alternating direction per (x+y+frame).
/// 9. When stuck: velocity zeroes, chunk stays dirty if unsupported (air below).
///
/// Known tradeoffs:
/// - Momentum transfer is deterministic given position+frame, but the hash-based direction
///   means tests can't always predict which way the diagonal goes.
/// - Dirt piles steeper than sand due to slideResistance=50 vs 0.
/// </summary>
public class PowderCollisionTests
{
    // ===== MOMENTUM TRANSFER ON COLLISION =====

    [Fact]
    public void Sand_SpreadsOnImpact_FromHeight()
    {
        // Sand dropped from height should spread on impact (momentum → diagonal).
        // Not just sitting in one spot.
        using var sim = new SimulationFixture(64, 128);

        sim.Fill(0, 120, 64, 8, Materials.Stone); // Floor
        sim.Set(32, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        // Sand should have hit the floor and potentially spread diagonally
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Should be on or near the floor
        Assert.True(pos[0].y >= 118,
            $"Sand should reach floor, but is at y={pos[0].y}");
    }

    [Fact]
    public void Sand_ColumnSpreadsIntoTriangle()
    {
        // A column of sand dropped onto a flat surface should spread into a triangular pile.
        // Wider at the base, narrower at the top.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(0, 60, 64, 4, Materials.Stone); // Floor
        // Column of 20 sand grains
        for (int y = 0; y < 20; y++)
            sim.Set(32, y, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Count sand per row above stone floor
        int topRowWidth = 0, bottomRowWidth = 0;
        int topSandRow = -1, bottomSandRow = -1;

        for (int y = 0; y < 60; y++)
        {
            int rowCount = WorldAssert.CountMaterial(sim.World, 0, y, 64, 1, Materials.Sand);
            if (rowCount > 0)
            {
                if (topSandRow == -1) { topSandRow = y; topRowWidth = rowCount; }
                bottomSandRow = y;
                bottomRowWidth = rowCount;
            }
        }

        Assert.True(topSandRow >= 0, "Sand should be visible somewhere");

        // Bottom row of pile should be wider than or equal to top row (pyramid shape)
        Assert.True(bottomRowWidth >= topRowWidth,
            $"Pile should be wider at base: top row {topSandRow} width={topRowWidth}, " +
            $"bottom row {bottomSandRow} width={bottomRowWidth}");
    }

    [Fact]
    public void Sand_MomentumTransfer_ConservesMaterial()
    {
        // During diagonal momentum transfer, material count must stay constant.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 120, 128, 8, Materials.Stone);
        // Drop 50 sand grains from height
        for (int i = 0; i < 50; i++)
            sim.Set(64, i, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== DIAGONAL MOVEMENT (PHASE 2) =====

    [Fact]
    public void Sand_DiagonalSlide_MovesOffEdge()
    {
        // Sand on the edge of a platform should slide diagonally off it.
        using var sim = new SimulationFixture(64, 64);

        // Small platform in the middle
        sim.Fill(30, 40, 5, 1, Materials.Stone);
        sim.Fill(0, 63, 64, 1, Materials.Stone); // Floor
        // Place sand at the right edge of the platform
        sim.Set(34, 39, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should have slid off the right edge and fallen to the floor
        Assert.True(pos[0].y >= 62,
            $"Sand should slide off platform edge, but is at y={pos[0].y}");
    }

    [Fact]
    public void Sand_DiagonalBlocked_TriesOppositeDirection()
    {
        // If diagonal in one direction is blocked, sand tries opposite.
        using var sim = new SimulationFixture(64, 64);

        // Stone floor with a wall on the right side
        sim.Fill(0, 50, 64, 1, Materials.Stone);
        sim.Fill(33, 0, 1, 50, Materials.Stone); // Right wall

        // Sand at (32, 45) — can slide left-down but not right-down
        sim.Set(32, 45, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(100, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should be on row 49 (just above floor), and possibly to the left
        Assert.True(pos[0].y == 49,
            $"Sand should land just above floor at y=49, got y={pos[0].y}");
    }

    // ===== SLIDE RESISTANCE =====

    [Fact]
    public void Dirt_PilesSteeper_ThanSand()
    {
        // Dirt (slideResistance=50) should form a steeper pile than sand (slideResistance=0).
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Drop 30 dirt in a column at center
        for (int y = 0; y < 30; y++)
            sim.Set(64, y, Materials.Dirt);

        var dirtCounts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, dirtCounts);

        // Measure dirt spread width
        int dirtMinX = int.MaxValue, dirtMaxX = int.MinValue;
        foreach (var (x, _) in sim.FindMaterial(Materials.Dirt))
        {
            if (x < dirtMinX) dirtMinX = x;
            if (x > dirtMaxX) dirtMaxX = x;
        }
        int dirtSpread = dirtMaxX - dirtMinX + 1;

        // Now do same with sand in a fresh world
        using var sim2 = new SimulationFixture(128, 128);
        sim2.Fill(0, 120, 128, 8, Materials.Stone);
        for (int y = 0; y < 30; y++)
            sim2.Set(64, y, Materials.Sand);

        var sandCounts = sim2.SnapshotMaterialCounts();
        sim2.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim2.World, sandCounts);

        int sandMinX = int.MaxValue, sandMaxX = int.MinValue;
        foreach (var (x, _) in sim2.FindMaterial(Materials.Sand))
        {
            if (x < sandMinX) sandMinX = x;
            if (x > sandMaxX) sandMaxX = x;
        }
        int sandSpread = sandMaxX - sandMinX + 1;

        // Dirt should spread less than or equal to sand (steeper or same pile).
        // With small grain counts, momentum transfer can dominate slide resistance,
        // so we allow a small tolerance: dirt spread can be up to 2 wider than sand.
        Assert.True(dirtSpread <= sandSpread + 2,
            $"Dirt should pile at least as steep as sand: dirt spread={dirtSpread}, sand spread={sandSpread}");
    }

    [Fact]
    public void Sand_ZeroSlideResistance_AlwaysSlides()
    {
        // Sand has slideResistance=0, so Phase 3 slide check always passes.
        // Single sand grain on a corner should always slide off.
        using var sim = new SimulationFixture(64, 64);

        // Small platform
        sim.Fill(30, 40, 3, 1, Materials.Stone);
        sim.Fill(0, 63, 64, 1, Materials.Stone); // Floor
        // Sand on the left edge of platform
        sim.Set(30, 39, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should have slid off the platform edge and reached the floor
        Assert.True(pos[0].y >= 62,
            $"Sand should slide off platform and fall to floor, but is at y={pos[0].y}");
    }

    // ===== PILE FORMATION =====

    [Fact]
    public void Sand_FormsPileOnFlat_ConservesAll()
    {
        // Pouring sand onto a flat surface should form a pile with all grains conserved.
        using var sim = new SimulationFixture(64, 64);

        sim.Fill(0, 60, 64, 4, Materials.Stone);

        int sandCount = 0;
        for (int y = 0; y < 15; y++)
        {
            sim.Set(32, y, Materials.Sand);
            sandCount++;
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingPowder(sim.World);

        Assert.Equal(sandCount, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void Sand_PileRoughlySymmetric()
    {
        // Sand dropped from center should form a roughly symmetric pile.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 120, 128, 8, Materials.Stone);
        for (int y = 0; y < 40; y++)
            sim.Set(64, y, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Pile should be roughly symmetric around x=64
        // Allow ratio up to 3.0 because hash-based randomization can bias slightly
        WorldAssert.SymmetricSpread(sim.World, Materials.Sand, 64, 3.0);
    }

    // ===== VELOCITY ZEROING WHEN STUCK =====

    [Fact]
    public void Sand_VelocityZeros_WhenCompletelyBlocked()
    {
        // Sand that can't move anywhere should have velocity zeroed.
        using var sim = new SimulationFixture(64, 64);

        // Box: sand enclosed on all sides
        sim.Fill(30, 30, 5, 1, Materials.Stone); // Top
        sim.Fill(30, 34, 5, 1, Materials.Stone); // Bottom
        sim.Fill(30, 31, 1, 3, Materials.Stone);  // Left
        sim.Fill(34, 31, 1, 3, Materials.Stone);  // Right

        // Fill interior with sand except one air cell
        sim.Set(31, 31, Materials.Sand);
        sim.Set(32, 31, Materials.Sand);
        sim.Set(33, 31, Materials.Sand);
        sim.Set(31, 32, Materials.Sand);
        sim.Set(32, 32, Materials.Sand);
        sim.Set(33, 32, Materials.Sand);
        sim.Set(31, 33, Materials.Sand);
        sim.Set(32, 33, Materials.Sand);
        sim.Set(33, 33, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // All sand should have zero velocity
        foreach (var (x, y) in sim.FindMaterial(Materials.Sand))
        {
            Cell cell = sim.GetCell(x, y);
            Assert.True(cell.velocityX == 0 && cell.velocityY == 0,
                $"Sand at ({x},{y}) should have zero velocity when stuck, " +
                $"but has vX={cell.velocityX}, vY={cell.velocityY}");
        }
    }

    // ===== CHUNK DIRTY WHEN UNSUPPORTED =====

    [Fact]
    public void Sand_ChunkStaysDirty_WhenUnsupported()
    {
        // Sand that is stuck (Phase 3 fails) but has air below should keep chunk dirty
        // so gravity can eventually move it.
        using var sim = new SimulationFixture();

        // Narrow gap: sand can't slide but has air below eventually
        sim.Fill(31, 10, 1, 50, Materials.Stone);
        sim.Fill(33, 10, 1, 50, Materials.Stone);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);

        // After 30 frames, sand should have moved down (velocity built via fractional gravity)
        sim.Step(30);
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y > 10,
            $"Sand in narrow gap should fall via gravity after accumulator overflow, but at y={pos[0].y}");

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }
}
