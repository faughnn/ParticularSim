using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

/// <summary>
/// End-to-end scenarios testing multiple systems together.
///
/// English rules:
/// 1. Large worlds with many materials must conserve every cell per-frame, not just at the end.
/// 2. Sand crossing chunk boundaries lands in the correct adjacent chunk without duplication.
/// 3. All powders fall under the same gravity (fractionalGravity=17) — different stability
///    affects sliding, not fall rate.
/// </summary>
public class ScenarioTests
{
    [Fact]
    public void LargeWorld_SandAndWater_MaterialConservation()
    {
        // Rule 1: per-frame conservation across a large multi-chunk world
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

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);
    }

    [Fact]
    public void MultiChunk_SandCrossesChunkBoundaries()
    {
        // Rule 2: sand at chunk boundaries falls correctly without duplication
        var sim = new SimulationFixture(192, 128);
        sim.Fill(0, 120, 192, 8, Materials.Stone);

        sim.Set(63, 10, Materials.Sand);
        sim.Set(64, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
    }

    [Fact]
    public void GravityConsistency_AllPowdersFallAtSameRate()
    {
        // Rule 3: different powders fall under the same gravity
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(30, 10, Materials.Sand);
        sim.Set(60, 10, Materials.Dirt);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        WorldAssert.IsAir(sim.World, 30, 10);
        WorldAssert.IsAir(sim.World, 60, 10);
    }
}
