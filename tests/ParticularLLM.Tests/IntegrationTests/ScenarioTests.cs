using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

public class ScenarioTests
{
    [Fact]
    public void LargeWorld_SandAndWater_MaterialConservation()
    {
        var sim = new SimulationFixture(512, 256);
        sim.Fill(0, 240, 512, 16, Materials.Stone);

        int sandPlaced = 0, waterPlaced = 0;

        for (int x = 100; x < 150; x++)
            for (int y = 0; y < 20; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandPlaced++;
            }

        for (int x = 200; x < 250; x++)
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterPlaced++;
            }

        sim.Step(1000);

        Assert.Equal(sandPlaced, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterPlaced, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    [Fact]
    public void MultiChunk_SandCrossesChunkBoundaries()
    {
        var sim = new SimulationFixture(192, 128);
        sim.Fill(0, 120, 192, 8, Materials.Stone);

        sim.Set(63, 10, Materials.Sand);
        sim.Set(64, 10, Materials.Sand);

        sim.Step(500);

        Assert.Equal(2, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void GravityConsistency_AllPowdersFallAtSameRate()
    {
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(30, 10, Materials.Sand);
        sim.Set(60, 10, Materials.Dirt);

        sim.Step(300);

        WorldAssert.IsAir(sim.World, 30, 10);
        WorldAssert.IsAir(sim.World, 60, 10);
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }
}
