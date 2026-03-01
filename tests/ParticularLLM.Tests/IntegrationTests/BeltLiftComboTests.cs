using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests for structure interactions with cell simulation.
///
/// English rules:
/// 1. Sand on a right-moving belt is carried rightward until it reaches the belt's end,
///    then falls off under gravity. After settling, the sand must be below the belt level.
/// 2. A wall is an impenetrable static barrier. Sand falling onto a wall rests on its top
///    surface and cannot pass through.
/// 3. A full pipeline (belt chain + wall container + gravity) must conserve all placed
///    material every frame, not just at the end.
/// </summary>
public class BeltLiftComboTests
{
    [Fact]
    public void Sand_OnBelt_FallsOffEnd()
    {
        // Rule 1: sand on belt gets carried to edge, falls off, settles below belt level
        var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand dropped above a two-segment right-moving belt should be carried to the belt end, fall off, and settle below belt level.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);  // Floor

        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(24, 80, 1);   // Right-moving belt
        belts.PlaceBelt(32, 80, 1);   // Extended belt (same direction, merges)
        sim.Simulator.SetBeltManager(belts);

        // Drop sand above belt - it will fall to the surface then be carried right
        int surfaceY = 80 - 1;
        sim.Set(26, surfaceY - 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should be below the belt level (fell off the right end)
        int sandBelowBelt = WorldAssert.CountMaterial(sim.World, 0, 80, 128, 48, Materials.Sand);
        Assert.Equal(1, sandBelowBelt);
    }

    [Fact]
    public void WallBlocksSandFalling()
    {
        // Rule 2: wall is impenetrable — sand rests on wall top surface
        var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand falling onto a wall should rest on the wall's top surface and not pass through it.";
        var walls = new WallManager(sim.World);
        walls.PlaceWall(32, 80);  // Wall block at (32,80)-(39,87)
        sim.Simulator.SetWallManager(walls);

        sim.Set(35, 50, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

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
        // Rule 3: full pipeline conserves every frame
        var sim = new SimulationFixture(256, 256);
        sim.Description = "A belt chain feeding sand into a walled container should conserve all placed sand every frame across 2000 steps.";
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

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(2000, counts);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }
}
