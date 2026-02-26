using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class PowderTests
{
    [Fact]
    public void Sand_MovesDownward_FirstFrame()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);
        sim.Step(1);
        // After 1 frame: fractional gravity 17 added, no overflow yet so velocityY stays 0.
        // But Phase 3 "simple slide" still tries diagonal fall (y+1).
        // Sand should have moved down one row (diagonally).
        WorldAssert.IsAir(sim.World, 32, 10);
        // Sand should be on row 11 somewhere (diagonal slide)
        int sandOnRow11 = WorldAssert.CountMaterial(sim.World, 0, 11, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow11);
    }

    [Fact]
    public void Sand_EventuallyFalls_After20Frames()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);
        sim.Step(20);
        WorldAssert.IsAir(sim.World, 32, 10);
    }

    [Fact]
    public void Sand_FallsToBottomRow()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 0, Materials.Sand);
        sim.Step(500);
        // Sand should reach the bottom row (63) - it may slide diagonally so check any x on row 63
        int sandOnBottom = WorldAssert.CountMaterial(sim.World, 0, 63, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnBottom);
    }

    [Fact]
    public void Sand_StopsAboveStone()
    {
        using var sim = new SimulationFixture();
        // Fill an entire row with stone
        sim.Fill(0, 50, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);
        sim.Step(500);
        // Sand must be on row 49 (just above stone), possibly at a different x due to diagonal slide
        int sandOnRow49 = WorldAssert.CountMaterial(sim.World, 0, 49, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow49);
    }

    [Fact]
    public void Sand_PilesDiagonally()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 60, 64, 4, Materials.Stone);
        for (int i = 0; i < 5; i++)
            sim.Set(32, i, Materials.Sand);
        sim.Step(500);
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        int sandInCol32 = WorldAssert.CountMaterial(sim.World, 32, 0, 1, 60, Materials.Sand);
        Assert.True(sandInCol32 < 5, "Sand should spread diagonally, not stack in one column");
    }

    [Fact]
    public void Sand_MaterialConservation_LargeAmount()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 60, 64, 4, Materials.Stone);
        int placed = 0;
        for (int x = 20; x < 44; x++)
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Sand);
                placed++;
            }
        sim.Step(500);
        Assert.Equal(placed, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void Sand_DoesNotMoveWhenSupported()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Sand);
        sim.Step(50);
        WorldAssert.CellIs(sim.World, 32, 62, Materials.Sand);
    }

    [Fact]
    public void Dirt_PilesSteeper_HigherSlideResistance()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);
        for (int y = 0; y < 30; y++)
            sim.Set(64, y, Materials.Dirt);
        sim.Step(1000);
        Assert.Equal(30, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
        int nearCenter = WorldAssert.CountMaterial(sim.World, 60, 0, 9, 120, Materials.Dirt);
        Assert.True(nearCenter > 15, $"Dirt should pile steeply near center, but only {nearCenter}/30 in 9-wide zone");
    }
}
