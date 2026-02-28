using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// English rules for liquid drag on powder:
///
/// 1. When powder displaces a liquid (density check), its velocity is damped by a factor
///    proportional to the liquid's density: velocity *= (256 - liquidDensity) / 256.
/// 2. Water (density 64) applies 25% drag per frame → velocity *= 0.75.
/// 3. Oil (density 48) applies ~19% drag per frame → velocity *= 0.81 (less drag than water).
/// 4. Drag slows powder but doesn't stop it — sand still sinks to the bottom through liquid.
/// 5. Material conservation: drag never creates or destroys materials.
/// 6. MoltenIron (density 200) is denser than all powders, so powder sits on top — drag never applies.
/// </summary>
public class LiquidDragTests
{
    [Fact]
    public void Sand_FallsSlowerThroughWater_ThanAir()
    {
        // Drop sand from the same height in two worlds: one with air, one with a water pool.
        // Sand through water should take more frames to settle.

        // Air world
        using var airSim = new SimulationFixture(64, 64);
        airSim.Fill(0, 63, 64, 1, Materials.Stone);
        airSim.Set(32, 10, Materials.Sand);
        var airCounts = airSim.SnapshotMaterialCounts();
        int airFrames = airSim.StepUntilSettledWithInvariants(airCounts, 500);

        // Water world: fill rows 20-62 with water, stone floor at 63
        using var waterSim = new SimulationFixture(64, 64);
        waterSim.Fill(0, 63, 64, 1, Materials.Stone);
        waterSim.Fill(0, 20, 64, 43, Materials.Water);
        waterSim.Set(32, 10, Materials.Sand);
        var waterCounts = waterSim.SnapshotMaterialCounts();
        int waterFrames = waterSim.StepUntilSettledWithInvariants(waterCounts, 2000);

        Assert.True(waterFrames > airFrames,
            $"Sand should fall slower through water ({waterFrames} frames) than air ({airFrames} frames)");
    }

    [Fact]
    public void Oil_ProvidesLessDrag_ThanWater()
    {
        // Sand through oil should settle faster than sand through water (oil is less dense).

        // Water world
        using var waterSim = new SimulationFixture(64, 64);
        waterSim.Fill(0, 63, 64, 1, Materials.Stone);
        waterSim.Fill(0, 20, 64, 43, Materials.Water);
        waterSim.Set(32, 10, Materials.Sand);
        var waterCounts = waterSim.SnapshotMaterialCounts();
        int waterFrames = waterSim.StepUntilSettledWithInvariants(waterCounts, 2000);

        // Oil world
        using var oilSim = new SimulationFixture(64, 64);
        oilSim.Fill(0, 63, 64, 1, Materials.Stone);
        oilSim.Fill(0, 20, 64, 43, Materials.Oil);
        oilSim.Set(32, 10, Materials.Sand);
        var oilCounts = oilSim.SnapshotMaterialCounts();
        int oilFrames = oilSim.StepUntilSettledWithInvariants(oilCounts, 2000);

        Assert.True(oilFrames < waterFrames,
            $"Sand through oil ({oilFrames} frames) should be faster than through water ({waterFrames} frames)");
    }

    [Fact]
    public void Sand_StillSinksToBottom_ThroughWater()
    {
        // Drag slows but doesn't stop: sand must reach the floor below a water pool.
        using var sim = new SimulationFixture(64, 64);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(0, 20, 64, 43, Materials.Water);
        sim.Set(32, 10, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettledWithInvariants(counts, 2000);

        // Sand should be on row 62 (just above the stone floor)
        int sandOnRow62 = WorldAssert.CountMaterial(sim.World, 0, 62, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow62);
    }

    [Fact]
    public void MaterialConservation_WithDrag()
    {
        // Bulk test: many sand cells falling through a deep water pool.
        using var sim = new SimulationFixture(64, 64);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(0, 30, 64, 33, Materials.Water);

        int sandCount = 0;
        for (int x = 20; x < 44; x++)
        {
            sim.Set(x, 5, Materials.Sand);
            sandCount++;
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(sandCount, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void MultipleSand_SinkThroughWater_WithConservation()
    {
        // Larger stress test: 10x5 block of sand through water
        using var sim = new SimulationFixture(64, 64);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(0, 25, 64, 38, Materials.Water);

        int sandCount = 0;
        for (int x = 27; x < 37; x++)
            for (int y = 5; y < 10; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandCount++;
            }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);

        Assert.Equal(sandCount, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        int waterCount = counts[Materials.Water];
        Assert.Equal(waterCount, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }
}
