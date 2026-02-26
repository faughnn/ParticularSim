using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class LiquidTests
{
    [Fact]
    public void Water_FallsDown()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Water);
        sim.Step(200);
        // Water should have fallen to the bottom row (63) since there's no floor
        // It may have spread laterally so check any x on bottom row
        int waterOnBottom = WorldAssert.CountMaterial(sim.World, 0, 63, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnBottom);
    }

    [Fact]
    public void Water_SpreadsHorizontally()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);  // Floor
        sim.Set(32, 10, Materials.Water);
        sim.Step(500);

        // Material conservation: still exactly 1 water cell
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);

        // Water should be on row 62 (just above stone floor), possibly spread laterally
        int waterOnRow62 = WorldAssert.CountMaterial(sim.World, 0, 62, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnRow62);
    }

    [Fact]
    public void Water_FillsContainer()
    {
        using var sim = new SimulationFixture();
        // Build a container: left wall, right wall, floor
        sim.Fill(20, 50, 1, 14, Materials.Stone);   // Left wall
        sim.Fill(43, 50, 1, 14, Materials.Stone);   // Right wall
        sim.Fill(20, 63, 24, 1, Materials.Stone);   // Floor

        // Place water cells directly inside the container (stacked near the top)
        // so they can fall and settle without escaping
        int waterPlaced = 0;
        for (int i = 0; i < 10; i++)
        {
            sim.Set(32, 51 + i, Materials.Water);
            waterPlaced++;
        }

        sim.Step(500);

        // Material conservation: all water cells should still exist
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(waterPlaced, waterCount);

        // All water should be inside the container (between walls, above floor)
        int waterInContainer = WorldAssert.CountMaterial(sim.World, 21, 50, 22, 13, Materials.Water);
        Assert.Equal(waterPlaced, waterInContainer);
    }

    [Fact]
    public void Water_MaterialConservation()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int placed = 0;
        for (int x = 50; x < 78; x++)
            for (int y = 0; y < 5; y++)
            {
                sim.Set(x, y, Materials.Water);
                placed++;
            }

        sim.Step(1000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Water_LeavesOriginalPosition()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Water);
        sim.Step(30);
        // After enough frames, water should have moved away from starting position
        WorldAssert.IsAir(sim.World, 32, 10);
    }

    [Fact]
    public void Water_StopsAboveStone()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 40, 64, 1, Materials.Stone);  // Stone floor at row 40
        sim.Set(32, 10, Materials.Water);
        sim.Step(500);

        // Water should be on row 39 (just above stone)
        int waterOnRow39 = WorldAssert.CountMaterial(sim.World, 0, 39, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnRow39);
    }
}
