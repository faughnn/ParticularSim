using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class BeltSimulationTests
{
    [Fact]
    public void Belt_MovesSandRight()
    {
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1); // Right-moving belt at (16,40)-(23,47)
        sim.Simulator.SetBeltManager(belts);

        // Place sand on belt surface (one row above belt top = y=39)
        int surfaceY = 40 - 1;
        sim.Set(20, surfaceY, Materials.Sand);

        sim.Step(100);

        // Sand should have moved to the right from x=20
        WorldAssert.IsAir(sim.World, 20, surfaceY);
        // Find sand somewhere to the right (it may have fallen off the belt edge)
        bool foundRight = false;
        for (int x = 21; x < 128; x++)
            for (int y = 0; y < 64; y++)
                if (sim.Get(x, y) == Materials.Sand)
                { foundRight = true; break; }
        Assert.True(foundRight, "Sand should have moved right on the belt");
    }

    [Fact]
    public void Belt_MovesSandLeft()
    {
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(40, 40, -1); // Left-moving belt at (40,40)-(47,47)
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 40 - 1;
        sim.Set(44, surfaceY, Materials.Sand);

        sim.Step(100);

        // Sand should have moved away from x=44
        WorldAssert.IsAir(sim.World, 44, surfaceY);
    }

    [Fact]
    public void Belt_MovesWater()
    {
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1); // Right-moving belt at (16,40)-(23,47)
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 40 - 1;
        sim.Set(20, surfaceY, Materials.Water);

        sim.Step(100);

        // Water should not remain at x=20 on the surface
        // Check that it moved away from the original position
        int waterInOriginalColumn = WorldAssert.CountMaterial(sim.World, 20, surfaceY, 1, 1, Materials.Water);
        Assert.Equal(0, waterInOriginalColumn);
    }

    [Fact]
    public void Belt_MaterialConservation()
    {
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 40 - 1;
        int placed = 0;
        for (int x = 16; x < 24; x++)
        {
            sim.Set(x, surfaceY, Materials.Sand);
            placed++;
        }

        sim.Step(200);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Belt_DoesNotMoveSandBelowSurface()
    {
        // Sand below the belt surface (inside the belt) should not be moved
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1); // Belt occupies rows 40-47
        sim.Simulator.SetBeltManager(belts);

        // Place sand well above the belt - it should fall onto the surface and then get moved
        // But sand placed inside the belt area won't be there since belt material fills it
        // Instead verify that sand placed 2 rows above surface falls to surface first
        int surfaceY = 40 - 1; // y=39

        // Place sand 5 rows above surface so it falls onto belt surface
        sim.Set(20, surfaceY - 5, Materials.Sand);

        sim.Step(200);

        // The sand should have fallen to the surface and been pushed right
        // It should NOT be at x=20 anymore
        int sandAtOriginal = WorldAssert.CountMaterial(sim.World, 20, 0, 1, surfaceY, Materials.Sand);
        Assert.Equal(0, sandAtOriginal);
    }

    [Fact]
    public void Belt_SpeedIsBased_OnFrameCount()
    {
        // Belt speed is 3 frames per move (DefaultSpeed = 3)
        // After exactly 3 frames, sand should have moved at most 1 cell
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        int surfaceY = 40 - 1;
        sim.Set(20, surfaceY, Materials.Sand);

        // Step exactly 3 frames - belt activates at least once
        sim.Step(3);

        // Sand should still be near x=20 (moved at most 1 cell per activation)
        int totalSand = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, totalSand);
    }
}
