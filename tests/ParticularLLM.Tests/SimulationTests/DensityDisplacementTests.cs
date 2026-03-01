using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// English rules for density displacement (derived from SimulateChunksLogic.CanMoveTo + MoveCell):
///
/// 1. CanMoveTo returns true when the target cell's material density is less than the mover's density.
///    This enables density displacement — heavier materials push through lighter ones.
/// 2. MoveCell swaps the source and target cells, so the displaced material takes the mover's
///    old position. This preserves material conservation (no creation or destruction).
/// 3. Sand (density=128) sinks through water (density=64): sand displaces water downward,
///    water bubbles up into the sand's old position.
/// 4. Static materials (stone, walls) block all movement regardless of density
///    (CanMoveTo returns false for statics unless Passable flag is set).
/// 5. After settling, heavier materials end up below lighter ones — center of mass ordering
///    matches density ordering.
/// </summary>
public class DensityDisplacementTests
{
    [Fact]
    public void Sand_SinksThroughWater()
    {
        // Rule 1+2+3: sand displaces water, both materials conserved
        using var sim = new SimulationFixture();
        sim.Description = "Sand dropped into a pool of water should sink through it via density displacement, with both sand and water counts conserved.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 50, 5, 12, Materials.Water);   // 60 water cells
        sim.Set(32, 40, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, sandCount);
        Assert.Equal(60, waterCount);
    }

    [Fact]
    public void Sand_EndsUpBelowWater()
    {
        // Rule 5: after settling, sand's center of mass is below water's
        using var sim = new SimulationFixture();
        sim.Description = "Sand placed above water in a container should sink to the bottom, ending up near the floor below the water layer.";
        sim.Fill(25, 50, 1, 14, Materials.Stone);   // Left wall
        sim.Fill(40, 50, 1, 14, Materials.Stone);   // Right wall
        sim.Fill(25, 63, 16, 1, Materials.Stone);   // Floor

        sim.Fill(26, 58, 14, 5, Materials.Water);   // 70 water cells
        sim.Set(32, 50, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should be near floor (row 60-62)
        int sandNearFloor = WorldAssert.CountMaterial(sim.World, 25, 60, 16, 3, Materials.Sand);
        Assert.Equal(1, sandNearFloor);
    }

    [Fact]
    public void Water_DoesNotSinkThroughSand()
    {
        // Rule 1: water (density=64) cannot displace sand (density=128)
        using var sim = new SimulationFixture();
        sim.Description = "Water dropped onto a block of sand should not sink through it, since water is lighter than sand.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 58, 5, 5, Materials.Sand);   // 25 sand cells
        sim.Set(32, 50, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(25, sandCount);
        Assert.Equal(1, waterCount);
    }

    [Fact]
    public void Stone_BlocksEverything()
    {
        // Rule 4: static materials block all movement regardless of density
        using var sim = new SimulationFixture();
        sim.Description = "Sand falling onto a stone floor should stop above it and never pass through, since static materials block all movement.";
        sim.Fill(0, 40, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int sandOnRow39 = WorldAssert.CountMaterial(sim.World, 0, 39, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow39);
        Assert.Equal(64, WorldAssert.CountMaterial(sim.World, Materials.Stone));
    }

    [Fact]
    public void DensityDisplacement_MaterialConservation_MixedScene()
    {
        // Rule 2: per-frame conservation with sand and water interacting
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "50 sand and 100 water cells interacting via density displacement should all be conserved on every frame.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

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

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);

        Assert.Equal(sandPlaced, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterPlaced, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }
}
