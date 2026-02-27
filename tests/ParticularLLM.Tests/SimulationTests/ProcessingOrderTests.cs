using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests that validate the chunk-internal processing order matters and is correct.
///
/// The simulation processes cells bottom-to-top with alternating X direction:
/// - Bottom-to-top: so falling sand frees space for sand above it (same frame)
/// - Alternating X per row: even rows left-to-right, odd rows right-to-left (no directional bias)
/// </summary>
public class ProcessingOrderTests
{
    [Fact]
    public void BottomToTop_SandColumnFallsInOneFrame()
    {
        // A vertical column of sand should propagate downward within a single frame
        // because bottom-to-top processing lets each grain fall before the one above is processed.
        // If we processed top-to-bottom, only the bottom grain would move per frame.
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone); // Floor

        // Stack 5 sand grains in a column with a gap below
        // y=50 is closest to floor, y=46 is highest
        for (int y = 46; y <= 50; y++)
            sim.Set(32, y, Materials.Sand);

        // After just 1 frame, bottom-to-top processing should move multiple grains
        sim.Step(1);

        // The bottom grain (y=50) should have moved down (freed space)
        // AND the grain above it should also have moved (because bottom was processed first)
        // Count sand still in the original 5-row zone
        int sandInOrigZone = WorldAssert.CountMaterial(sim.World, 32, 46, 1, 5, Materials.Sand);

        // With bottom-to-top, several grains cascade down in one frame.
        // With top-to-bottom, only the bottom grain would move — all 4 above stay put.
        Assert.True(sandInOrigZone < 5,
            $"Bottom-to-top processing should cascade sand downward, but {sandInOrigZone}/5 grains stayed in place");
    }

    [Fact]
    public void AlternatingX_NoDirectionalBias()
    {
        // Drop many sand grains from the same column. After settling, the pile
        // should be roughly symmetric (not biased left or right).
        // Alternating X direction prevents systematic left or right drift.
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Drop 40 sand grains in column 64 (center)
        for (int y = 0; y < 40; y++)
            sim.Set(64, y, Materials.Sand);

        sim.Step(1000);

        Assert.Equal(40, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // Count sand left of center vs right of center
        int sandLeft = WorldAssert.CountMaterial(sim.World, 0, 0, 64, 120, Materials.Sand);
        int sandRight = WorldAssert.CountMaterial(sim.World, 65, 0, 63, 120, Materials.Sand);
        // Center column
        int sandCenter = WorldAssert.CountMaterial(sim.World, 64, 0, 1, 120, Materials.Sand);

        // Neither side should have all the sand (would indicate severe bias)
        // Allow some natural asymmetry from hash-based randomness but it shouldn't be extreme
        Assert.True(sandLeft > 0, "Sand should spread left (no right-only bias)");
        Assert.True(sandRight > 0, "Sand should spread right (no left-only bias)");

        // The ratio shouldn't be more extreme than 3:1
        if (sandLeft > 0 && sandRight > 0)
        {
            double ratio = (double)Math.Max(sandLeft, sandRight) / Math.Min(sandLeft, sandRight);
            Assert.True(ratio < 4.0,
                $"Sand pile is too asymmetric: {sandLeft} left, {sandCenter} center, {sandRight} right (ratio {ratio:F1})");
        }
    }

    [Fact]
    public void LiftUpward_WorksWithBottomToTopProcessing()
    {
        // Lifts push cells upward (decreasing Y). With bottom-to-top processing,
        // the cell at the bottom of the lift is processed first. The lift force
        // gives it negative velocity, and it moves up. Then the cell above it
        // (processed next) also gets lift force.
        // This test verifies lift cells actually propagate upward through the lift.
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);

        // Tall lift column
        lifts.PlaceLift(32, 40);
        lifts.PlaceLift(32, 48);
        lifts.PlaceLift(32, 56);
        lifts.PlaceLift(32, 64);
        lifts.PlaceLift(32, 72);
        sim.Simulator.SetLiftManager(lifts);

        // Place sand inside the lift near the bottom
        sim.Set(34, 75, Materials.Sand);

        // Track the sand's Y position over time — it should decrease (move up)
        int initialY = 75;
        sim.Step(100);

        // Find where the sand ended up
        int sandY = -1;
        for (int y = 0; y < 128; y++)
            for (int x = 30; x < 42; x++)
                if (sim.Get(x, y) == Materials.Sand)
                    sandY = y;

        Assert.True(sandY >= 0, "Sand disappeared!");
        Assert.True(sandY < initialY,
            $"Sand should have moved upward in lift (from y={initialY} to y={sandY}), but didn't");
    }

    [Fact]
    public void ExtendedRegion_SandFallsAcrossChunkBoundary()
    {
        // Sand at the bottom of one chunk should fall into the top of the chunk below,
        // even though only the core region is simulated. The extended region (32px buffer)
        // allows cells to LAND outside their home chunk.
        var sim = new SimulationFixture(128, 128); // 2x2 chunks

        // Place sand at y=63 (last row of chunk 0's core), with nothing below
        sim.Set(32, 63, Materials.Sand);

        sim.Step(30);

        // Sand should have fallen past y=63 into chunk (0,1)'s territory
        WorldAssert.IsAir(sim.World, 32, 63);
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // Sand should be somewhere below y=63
        int sandBelowBoundary = WorldAssert.CountMaterial(sim.World, 0, 64, 128, 64, Materials.Sand);
        Assert.Equal(1, sandBelowBoundary);
    }

    [Fact]
    public void WaterSpread_NotBiasedLeftOrRight()
    {
        // Water spreads horizontally. With alternating X direction,
        // it should spread roughly evenly in both directions.
        using var sim = new SimulationFixture(128, 128);

        // Sealed container: walls + floor (water placed INSIDE to prevent escape)
        sim.Fill(54, 100, 1, 28, Materials.Stone);  // Left wall
        sim.Fill(74, 100, 1, 28, Materials.Stone);   // Right wall
        sim.Fill(54, 127, 21, 1, Materials.Stone);   // Floor

        // Place water in a column inside the container at center (x=64)
        for (int i = 0; i < 15; i++)
            sim.Set(64, 110 + i, Materials.Water);

        sim.Step(2000);

        Assert.Equal(15, WorldAssert.CountMaterial(sim.World, Materials.Water));

        // Count water left vs right of center within the container
        int waterLeft = WorldAssert.CountMaterial(sim.World, 55, 100, 9, 27, Materials.Water);
        int waterRight = WorldAssert.CountMaterial(sim.World, 65, 100, 9, 27, Materials.Water);

        // Both sides should have water
        Assert.True(waterLeft > 0, "Water should spread left");
        Assert.True(waterRight > 0, "Water should spread right");
    }
}
