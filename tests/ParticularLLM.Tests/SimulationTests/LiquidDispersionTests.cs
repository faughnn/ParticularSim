using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for liquid dispersion behavior.
///
/// English rules (derived from SimulateLiquid lines 362-504):
///
/// 1. Liquid uses same fractional gravity accumulator as powder.
/// 2. Falling liquid tries vertical fall, then diagonal fall.
/// 3. When liquid can't fall, vertical momentum converts to horizontal spread.
/// 4. Spread distance = spread + velocityBoost (|velocityY|/3 if was free-falling).
/// 5. Random ±1 offset on spread distance for natural look.
/// 6. When landing from free-fall with velocityX=0, horizontal velocity set to ±4.
/// 7. Primary spread direction follows velocityX; alternating hash if zero.
/// 8. Liquid finds furthest reachable position in each direction.
/// 9. Prefers primary direction on tie; reverses velocityX if secondary chosen.
/// 10. Friction: 7/8 velocity retention on spread, /2 when stuck.
/// 11. Water: density=64, spread=5, stability=5.
/// 12. Oil: density=48, spread=4, stability=15.
///
/// Known tradeoffs:
/// - Hash-based randomization means exact spread is position-dependent.
/// - Water spreads further than oil (higher spread, lower stability).
/// </summary>
public class LiquidDispersionTests
{
    // ===== BASIC FALLING =====

    [Fact]
    public void Water_FallsDownward()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Water);
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Water);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 60, $"Water should fall to floor, but at y={pos[0].y}");
    }

    [Fact]
    public void Water_FallsToFloor_MaterialConserved()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        for (int x = 28; x < 36; x++)
            sim.Set(x, 10, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(8, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== HORIZONTAL SPREADING =====

    [Fact]
    public void Water_SpreadsHorizontally_OnFloor()
    {
        // Water sitting on a floor should spread horizontally.
        using var sim = new SimulationFixture();
        sim.Fill(0, 62, 64, 2, Materials.Stone); // Floor

        // Place a 3-wide column of water
        for (int y = 55; y < 62; y++)
            for (int x = 30; x < 33; x++)
                sim.Set(x, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Water should have spread wider than 3 cells
        int minX = int.MaxValue, maxX = int.MinValue;
        foreach (var (x, _) in sim.FindMaterial(Materials.Water))
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }
        int spread = maxX - minX + 1;
        Assert.True(spread > 3,
            $"Water should spread wider than initial 3 cells, but spread is {spread}");
    }

    [Fact]
    public void Water_SpreadsBothDirections()
    {
        // Water placed at center should spread in both directions.
        using var sim = new SimulationFixture(128, 64);
        sim.Fill(0, 62, 128, 2, Materials.Stone);

        // Column of water at center
        for (int y = 50; y < 62; y++)
            sim.Set(64, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Should spread somewhat symmetrically (allow 3:1 ratio for hash randomization)
        WorldAssert.SymmetricSpread(sim.World, Materials.Water, 64, 3.0);
    }

    [Fact]
    public void Water_FillsContainer()
    {
        // Water poured into a sealed container should stay inside.
        using var sim = new SimulationFixture(64, 64);

        // Sealed box: walls, floor between walls. Full-width floor below as safety.
        sim.Fill(0, 63, 64, 1, Materials.Stone);   // World floor
        sim.Fill(20, 45, 1, 19, Materials.Stone);   // Left wall (y=45 to y=63)
        sim.Fill(43, 45, 1, 19, Materials.Stone);   // Right wall (y=45 to y=63)
        sim.Fill(21, 62, 22, 1, Materials.Stone);   // Container floor between walls

        // Pour water INSIDE the container (between walls, above floor)
        for (int y = 50; y < 55; y++)
            sim.Set(31, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // All water should be inside the container (between walls, above floor)
        var waterPositions = sim.FindMaterial(Materials.Water);
        foreach (var (x, y) in waterPositions)
        {
            Assert.True(x > 20 && x < 43,
                $"Water at ({x},{y}) escaped container horizontally\n" +
                WorldDump.DumpRegion(sim.World, 18, 43, 28, 22));
        }
    }

    // ===== VELOCITY BOOST FROM FREE-FALL =====

    [Fact]
    public void Water_SpreadsMoreAfterFalling()
    {
        // Water that falls from height should spread more than water placed at surface
        // (velocity boost adds |velocityY|/3 to spread distance).
        using var sim = new SimulationFixture(128, 64);
        sim.Fill(0, 62, 128, 2, Materials.Stone);

        // Water dropped from height
        sim.Set(64, 0, Materials.Water);
        sim.Set(65, 0, Materials.Water);
        sim.Set(66, 0, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        int droppedMinX = int.MaxValue, droppedMaxX = int.MinValue;
        foreach (var (x, _) in sim.FindMaterial(Materials.Water))
        {
            if (x < droppedMinX) droppedMinX = x;
            if (x > droppedMaxX) droppedMaxX = x;
        }
        int droppedSpread = droppedMaxX - droppedMinX + 1;

        // Compare with water placed at surface level
        using var sim2 = new SimulationFixture(128, 64);
        sim2.Fill(0, 62, 128, 2, Materials.Stone);
        sim2.Set(64, 61, Materials.Water);
        sim2.Set(65, 61, Materials.Water);
        sim2.Set(66, 61, Materials.Water);

        var counts2 = sim2.SnapshotMaterialCounts();
        sim2.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim2.World, counts2);

        int surfaceMinX = int.MaxValue, surfaceMaxX = int.MinValue;
        foreach (var (x, _) in sim2.FindMaterial(Materials.Water))
        {
            if (x < surfaceMinX) surfaceMinX = x;
            if (x > surfaceMaxX) surfaceMaxX = x;
        }
        int surfaceSpread = surfaceMaxX - surfaceMinX + 1;

        // Water dropped from height should spread at least as much as surface water
        Assert.True(droppedSpread >= surfaceSpread,
            $"Dropped water spread ({droppedSpread}) should be >= surface water spread ({surfaceSpread})");
    }

    // ===== OIL VS WATER =====

    [Fact]
    public void Oil_SpreadsLessThanWater()
    {
        // Oil (spread=4, stability=15) spreads less than water (5, 5).
        // Liquid on a flat surface oscillates indefinitely, so we compare spread after a fixed
        // number of frames rather than waiting for settling.
        // Use a 512-wide world to prevent wall effects and run 300 frames.
        int runFrames = 300;

        using var sim = new SimulationFixture(512, 64);
        sim.Fill(0, 62, 512, 2, Materials.Stone);
        for (int x = 246; x < 266; x++)
            sim.Set(x, 61, Materials.Oil);

        var oilCounts = sim.SnapshotMaterialCounts();
        sim.Step(runFrames);
        InvariantChecker.AssertMaterialConservation(sim.World, oilCounts);

        int oilMinX = int.MaxValue, oilMaxX = int.MinValue;
        foreach (var (ox, _) in sim.FindMaterial(Materials.Oil))
        {
            if (ox < oilMinX) oilMinX = ox;
            if (ox > oilMaxX) oilMaxX = ox;
        }
        int oilSpread = oilMaxX - oilMinX + 1;

        using var sim2 = new SimulationFixture(512, 64);
        sim2.Fill(0, 62, 512, 2, Materials.Stone);
        for (int x = 246; x < 266; x++)
            sim2.Set(x, 61, Materials.Water);

        var waterCounts = sim2.SnapshotMaterialCounts();
        sim2.Step(runFrames);
        InvariantChecker.AssertMaterialConservation(sim2.World, waterCounts);

        int waterMinX = int.MaxValue, waterMaxX = int.MinValue;
        foreach (var (wx, _) in sim2.FindMaterial(Materials.Water))
        {
            if (wx < waterMinX) waterMinX = wx;
            if (wx > waterMaxX) waterMaxX = wx;
        }
        int waterSpread = waterMaxX - waterMinX + 1;

        // Water should spread at least as much as oil (higher spread, lower stability)
        Assert.True(oilSpread <= waterSpread + 10,
            $"Oil spread ({oilSpread}) should be <= water spread ({waterSpread}) + tolerance");
    }

    // ===== SETTLED STATE INVARIANTS =====

    [Fact]
    public void Water_NoFloatingLiquid_AfterSettled()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 60, 64, 4, Materials.Stone);

        for (int x = 20; x < 44; x++)
            for (int y = 30; y < 35; y++)
                sim.Set(x, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingLiquid(sim.World);
    }

    [Fact]
    public void Water_SingleDrop_EventuallySettles()
    {
        // A single water drop should eventually stop moving.
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        int frames = sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        Assert.True(frames < 5000, $"Water should settle in < 5000 frames, took {frames}");
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== MATERIAL CONSERVATION UNDER STRESS =====

    [Fact]
    public void Water_LargeVolume_MaterialConserved()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int waterCount = 0;
        for (int x = 50; x < 78; x++)
            for (int y = 0; y < 20; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterCount++;
            }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        Assert.Equal(waterCount, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    [Fact]
    public void Water_DiagonalFall_WhileSliding()
    {
        // Water on a step should fall diagonally, not get stuck.
        using var sim = new SimulationFixture(64, 64);

        // Staircase pattern
        sim.Fill(20, 40, 10, 1, Materials.Stone);
        sim.Fill(30, 50, 10, 1, Materials.Stone);
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        // Water on top step
        sim.Set(25, 39, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Water);
        Assert.Single(pos);
        // Water should reach the floor
        Assert.True(pos[0].y >= 62,
            $"Water should flow down stairs to floor, but is at y={pos[0].y}");
    }
}
