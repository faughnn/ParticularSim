using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class GasTests
{
    [Fact]
    public void Steam_RisesUp()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 50, Materials.Steam);
        sim.Step(200);
        // Steam should have risen away from its starting position
        WorldAssert.IsAir(sim.World, 32, 50);
    }

    [Fact]
    public void Steam_RisesToTop()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 60, Materials.Steam);
        sim.Step(500);
        // Steam should reach row 0 (the top), possibly shifted laterally
        int steamOnTopRow = WorldAssert.CountMaterial(sim.World, 0, 0, 64, 1, Materials.Steam);
        Assert.Equal(1, steamOnTopRow);
    }

    [Fact]
    public void Steam_MaterialConservation()
    {
        using var sim = new SimulationFixture();
        int placed = 5;
        for (int i = 0; i < placed; i++)
            sim.Set(30 + i, 50, Materials.Steam);

        sim.Step(200);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Steam_LeavesOriginalPosition()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 32, Materials.Steam);
        sim.Step(30);
        // After enough frames, steam should have moved away from starting position
        WorldAssert.IsAir(sim.World, 32, 32);
    }

    [Fact]
    public void Steam_StopsBelowCeiling()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 10, 64, 1, Materials.Stone);  // Stone ceiling at row 10
        sim.Set(32, 50, Materials.Steam);
        sim.Step(500);

        // Steam should be on row 11 (just below ceiling), possibly shifted laterally
        int steamOnRow11 = WorldAssert.CountMaterial(sim.World, 0, 11, 64, 1, Materials.Steam);
        Assert.Equal(1, steamOnRow11);
    }
}
