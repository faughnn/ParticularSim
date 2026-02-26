using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class DensityDisplacementTests
{
    [Fact]
    public void Sand_SinksThroughWater()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);   // Floor
        sim.Fill(30, 50, 5, 12, Materials.Water);   // Water pool (5x12 = 60 cells)
        sim.Set(32, 40, Materials.Sand);             // Sand above water

        sim.Step(500);

        // Material conservation: all sand and water must still exist
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, sandCount);
        Assert.Equal(60, waterCount);
    }

    [Fact]
    public void Sand_EndsUpBelowWater()
    {
        using var sim = new SimulationFixture();
        // Container with walls to keep things contained
        sim.Fill(25, 50, 1, 14, Materials.Stone);   // Left wall
        sim.Fill(40, 50, 1, 14, Materials.Stone);   // Right wall
        sim.Fill(25, 63, 16, 1, Materials.Stone);   // Floor

        // Fill with water
        sim.Fill(26, 58, 14, 5, Materials.Water);   // 14x5 = 70 water cells
        // Place sand above
        sim.Set(32, 50, Materials.Sand);

        sim.Step(500);

        // Sand (density 128) should end up below water (density 64)
        // Find where the sand is
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should be at row 62 (just above floor at 63)
        int sandNearFloor = WorldAssert.CountMaterial(sim.World, 25, 60, 16, 3, Materials.Sand);
        Assert.Equal(1, sandNearFloor);
    }

    [Fact]
    public void Water_DoesNotSinkThroughSand()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 58, 5, 5, Materials.Sand);   // Sand pile (25 cells)
        sim.Set(32, 50, Materials.Water);           // Water above sand

        sim.Step(500);

        // Material conservation
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(25, sandCount);
        Assert.Equal(1, waterCount);
    }

    [Fact]
    public void Stone_BlocksEverything()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 40, 64, 1, Materials.Stone);  // Stone floor
        sim.Set(32, 10, Materials.Sand);

        sim.Step(500);

        // Sand should be on row 39 (just above stone), possibly shifted laterally due to diagonal slide
        int sandOnRow39 = WorldAssert.CountMaterial(sim.World, 0, 39, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow39);
        Assert.Equal(64, WorldAssert.CountMaterial(sim.World, Materials.Stone));
    }

    [Fact]
    public void DensityDisplacement_MaterialConservation_MixedScene()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);  // Floor

        // Place sand block above water block
        int sandPlaced = 0;
        for (int x = 50; x < 60; x++)
            for (int y = 0; y < 5; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandPlaced++;
            }

        int waterPlaced = 0;
        for (int x = 50; x < 60; x++)
            for (int y = 50; y < 60; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterPlaced++;
            }

        sim.Step(1000);

        Assert.Equal(sandPlaced, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterPlaced, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }
}
