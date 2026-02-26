using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

public class BeltLiftComboTests
{
    [Fact]
    public void Sand_OnBelt_FallsOffEnd()
    {
        // Sand on a belt gets carried to the edge, then falls off
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);  // Floor

        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(24, 80, 1);   // Right-moving belt
        belts.PlaceBelt(32, 80, 1);   // Extended belt (same direction, merges)
        sim.Simulator.SetBeltManager(belts);

        // Drop sand above belt - it will fall to the surface then be carried right
        int surfaceY = 80 - 1;
        sim.Set(26, surfaceY - 10, Materials.Sand);

        sim.Step(500);

        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should be below the belt level (fell off the right end)
        int sandBelowBelt = WorldAssert.CountMaterial(sim.World, 0, 80, 128, 48, Materials.Sand);
        Assert.Equal(1, sandBelowBelt);
    }

    [Fact]
    public void WallBlocksSandFalling()
    {
        var sim = new SimulationFixture(128, 128);
        var walls = new WallManager(sim.World);
        walls.PlaceWall(32, 80);  // Wall block at (32,80)-(39,87)
        sim.Simulator.SetWallManager(walls);

        sim.Set(35, 50, Materials.Sand);

        sim.Step(500);

        // Sand should be conserved
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // Sand should rest on top of the wall (y = 79) somewhere within the wall's x range.
        // It may slide diagonally on landing, so check the full wall surface row.
        int sandOnWallSurface = WorldAssert.CountMaterial(sim.World, 32, 79, 8, 1, Materials.Sand);
        Assert.Equal(1, sandOnWallSurface);
    }

    [Fact]
    public void FullPipeline_MaterialConservation()
    {
        var sim = new SimulationFixture(256, 256);
        sim.Fill(0, 240, 256, 16, Materials.Stone);

        var belts = new BeltManager(sim.World);
        var walls = new WallManager(sim.World);

        // Right-moving belt chain
        for (int x = 40; x < 120; x += 8)
            belts.PlaceBelt(x, 160, 1);

        // Container walls at belt end
        walls.PlaceWall(120, 160);
        walls.PlaceWall(120, 168);
        walls.PlaceWall(120, 176);

        sim.Simulator.SetBeltManager(belts);
        sim.Simulator.SetWallManager(walls);

        // Drop sand above belt start
        int placed = 0;
        for (int y = 100; y < 110; y++)
            for (int x = 42; x < 48; x++)
            {
                sim.Set(x, y, Materials.Sand);
                placed++;
            }

        sim.Step(2000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }
}
