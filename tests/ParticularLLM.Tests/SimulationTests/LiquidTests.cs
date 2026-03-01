using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// English rules for liquid simulation (derived from SimulateChunksLogic.SimulateLiquid):
///
/// 1. Liquid falls downward due to gravity, same as powder — zero-velocity gravity pull
///    always attempts y+1 if open.
/// 2. On landing (vertical collision), vertical momentum converts to horizontal spread velocity.
///    Boost scales with mat.spread, so viscous liquids (high stability) spread less.
/// 3. At rest, liquid spreads horizontally up to mat.spread distance (water=5).
///    Direction alternates based on cell position + frame to avoid bias.
/// 4. Viscosity (stability) provides probabilistic resistance to spreading — higher stability
///    means the liquid settles and stops sooner (oil is more viscous than water).
/// 5. Liquid displaces lighter materials via density comparison (water density=64).
/// 6. Liquid fills containers: spreads to fill available horizontal space, pooling above floors.
/// 7. Material conservation: MoveCell swaps cells; no liquid is created or destroyed.
/// </summary>
public class LiquidTests
{
    [Fact]
    public void Water_FallsDown()
    {
        // Rule 1: liquid falls to bottom row with no floor
        using var sim = new SimulationFixture();
        sim.Description = "A single water cell should fall to the bottom row of the world when there are no obstacles.";
        sim.Set(32, 10, Materials.Water);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);
        int waterOnBottom = WorldAssert.CountMaterial(sim.World, 0, 63, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnBottom);
    }

    [Fact]
    public void Water_SpreadsHorizontally()
    {
        // Rule 3: single water cell lands on floor and doesn't stack vertically
        using var sim = new SimulationFixture();
        sim.Description = "A single water cell dropped onto a stone floor should land on the row above the floor and not stack vertically.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Water);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);
        int waterOnRow62 = WorldAssert.CountMaterial(sim.World, 0, 62, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnRow62);
    }

    [Fact]
    public void Water_FillsContainer()
    {
        // Rule 6: liquid fills available horizontal space within container walls
        using var sim = new SimulationFixture();
        sim.Description = "10 water cells placed inside a walled container should spread to fill the available horizontal space, with all water remaining inside the container.";
        sim.Fill(20, 50, 1, 14, Materials.Stone);   // Left wall
        sim.Fill(43, 50, 1, 14, Materials.Stone);   // Right wall
        sim.Fill(20, 63, 24, 1, Materials.Stone);   // Floor

        int waterPlaced = 0;
        for (int i = 0; i < 10; i++)
        {
            sim.Set(32, 51 + i, Materials.Water);
            waterPlaced++;
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(waterPlaced, waterCount);

        int waterInContainer = WorldAssert.CountMaterial(sim.World, 21, 50, 22, 13, Materials.Water);
        Assert.Equal(waterPlaced, waterInContainer);
    }

    [Fact]
    public void Water_MaterialConservation()
    {
        // Rule 7: per-frame conservation with 140 water cells
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "140 water cells falling onto a stone floor should all be conserved with no material lost or duplicated on any frame.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int placed = 0;
        for (int x = 50; x < 78; x++)
            for (int y = 0; y < 5; y++)
            {
                sim.Set(x, y, Materials.Water);
                placed++;
            }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Water_LeavesOriginalPosition()
    {
        // Rule 1: after enough frames, liquid has fallen away from start
        using var sim = new SimulationFixture();
        sim.Description = "A water cell should vacate its starting position after 30 frames of gravitational pull.";
        sim.Set(32, 10, Materials.Water);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(30, counts);
        WorldAssert.IsAir(sim.World, 32, 10);
    }

    [Fact]
    public void Water_StopsAboveStone()
    {
        // Rule 1: liquid stops above static blocking material
        using var sim = new SimulationFixture();
        sim.Description = "Water falling onto a horizontal stone floor should come to rest on the row directly above the stone.";
        sim.Fill(0, 40, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Water);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int waterOnRow39 = WorldAssert.CountMaterial(sim.World, 0, 39, 64, 1, Materials.Water);
        Assert.Equal(1, waterOnRow39);
    }
}
