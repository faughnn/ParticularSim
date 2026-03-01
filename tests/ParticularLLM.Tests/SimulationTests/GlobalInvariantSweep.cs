using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Global invariant sweep — runs all Layer 1 invariants against a variety of scenarios.
///
/// Per-step invariants (checked every frame):
/// - Material conservation (total count unchanged)
///
/// Settled-state invariants (checked after simulation settles):
/// - No floating powder (powder with air below, no upward velocity, not in lift)
/// - No floating liquid (liquid with air below and air on both sides)
/// - Density layering where applicable (heavy below light)
/// </summary>
public class GlobalInvariantSweep
{
    // ===== SCENARIO: SAND PILE =====

    [Fact]
    public void Invariants_SandPile()
    {
        using var sim = new SimulationFixture();
        sim.Description = "A row of sand dropped onto a stone floor should settle with all material conserved and no floating powder.";
        sim.Fill(0, 60, 64, 4, Materials.Stone);
        for (int x = 20; x < 44; x++)
            sim.Set(x, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingPowder(sim.World);
    }

    // ===== SCENARIO: WATER POOL =====

    [Fact]
    public void Invariants_WaterPool()
    {
        using var sim = new SimulationFixture();
        sim.Description = "Water poured into a walled container should settle with all material conserved and no floating liquid.";
        sim.Fill(20, 40, 1, 24, Materials.Stone); // left wall
        sim.Fill(43, 40, 1, 24, Materials.Stone); // right wall
        sim.Fill(20, 63, 24, 1, Materials.Stone);  // floor

        for (int x = 25; x < 38; x++)
            for (int y = 50; y < 55; y++)
                sim.Set(x, y, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingLiquid(sim.World);
    }

    // ===== SCENARIO: MIXED MATERIALS =====

    [Fact]
    public void Invariants_MixedMaterials()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand, water, and oil dropped in separate blocks should all be fully conserved after 500 frames of mixed-material interaction.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Fill(50, 50, 10, 5, Materials.Sand);
        sim.Fill(50, 60, 10, 5, Materials.Water);
        sim.Fill(50, 70, 10, 5, Materials.Oil);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Water));
        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Oil));
    }

    // ===== SCENARIO: DENSITY LAYERING =====

    [Fact]
    public void Invariants_DensityLayering_Settled()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Description = "Oil, water, and sand placed in reverse density order should self-sort so sand sinks below water, and water sinks below oil.";
        sim.Fill(20, 40, 1, 24, Materials.Stone);
        sim.Fill(43, 40, 1, 24, Materials.Stone);
        sim.Fill(20, 63, 24, 1, Materials.Stone);

        // Wrong order: oil at bottom, water middle, sand top
        sim.Fill(21, 57, 22, 2, Materials.Oil);
        sim.Fill(21, 55, 22, 2, Materials.Water);
        sim.Fill(21, 53, 22, 2, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // After settling, sand should be below water, water below oil
        var (_, sandY) = sim.CenterOfMass(Materials.Sand);
        var (_, waterY) = sim.CenterOfMass(Materials.Water);
        var (_, oilY) = sim.CenterOfMass(Materials.Oil);

        Assert.True(sandY > waterY,
            $"Sand (COM={sandY:F1}) should be below water (COM={waterY:F1})");
        Assert.True(waterY > oilY,
            $"Water (COM={waterY:F1}) should be below oil (COM={oilY:F1})");
    }

    // ===== SCENARIO: STEAM IN SEALED BOX =====

    [Fact]
    public void Invariants_SteamEnclosure()
    {
        using var sim = new SimulationFixture();
        sim.Description = "Steam enclosed in a sealed stone box should remain fully conserved after 500 frames, with no material escaping the enclosure.";
        sim.Fill(20, 20, 24, 1, Materials.Stone);
        sim.Fill(20, 43, 24, 1, Materials.Stone);
        sim.Fill(20, 20, 1, 24, Materials.Stone);
        sim.Fill(43, 20, 1, 24, Materials.Stone);

        for (int x = 25; x < 35; x++)
            for (int y = 30; y < 35; y++)
                sim.Set(x, y, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Steam));
    }

    // ===== SCENARIO: SAND ON BELT =====

    [Fact]
    public void Invariants_SandOnBelt()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand falling onto a long belt chain should be transported and conserved, with all 8 grains accounted for after 500 frames.";
        var belts = new BeltManager(sim.World);
        for (int x = 16; x < 80; x += 8)
            belts.PlaceBelt(x, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        for (int x = 18; x < 26; x++)
            sim.Set(x, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(8, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== SCENARIO: SAND THROUGH LIFT =====

    [Fact]
    public void Invariants_SandThroughLift()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "A single sand grain entering a lift column should be pushed upward and conserved through the entire lift-and-settle cycle.";
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);
        sim.Set(52, 96, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        sim.Step(1000);

        Assert.Equal(sandBefore, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== SCENARIO: LARGE SCALE STRESS =====

    [Fact]
    public void Invariants_LargeScale_AllMaterials()
    {
        using var sim = new SimulationFixture(256, 256);
        sim.Description = "Large quantities of sand, water, and dirt scattered across a 256x256 world should all be fully conserved after 300 frames.";
        sim.Fill(0, 240, 256, 16, Materials.Stone);

        // Scatter various materials
        int sandCount = 0, waterCount = 0, dirtCount = 0;
        for (int x = 80; x < 110; x++)
        {
            for (int y = 10; y < 20; y++) { sim.Set(x, y, Materials.Sand); sandCount++; }
            for (int y = 25; y < 35; y++) { sim.Set(x, y, Materials.Water); waterCount++; }
            for (int y = 40; y < 50; y++) { sim.Set(x, y, Materials.Dirt); dirtCount++; }
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        Assert.Equal(sandCount, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterCount, WorldAssert.CountMaterial(sim.World, Materials.Water));
        Assert.Equal(dirtCount, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }

    // ===== SCENARIO: 4-PASS MODE =====

    [Fact]
    public void Invariants_FourPassMode_Conservation()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand and water should be fully conserved in 4-pass checkerboard mode after 300 frames of simulation.";
        sim.Simulator.UseFourPassGrouping = true;

        sim.Fill(0, 120, 128, 8, Materials.Stone);
        sim.Fill(50, 50, 15, 10, Materials.Sand);
        sim.Fill(50, 70, 15, 5, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        Assert.Equal(150, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(75, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== SCENARIO: SETTLED SAND WITH FLOOR =====

    [Fact]
    public void Invariants_SettledSand_NoFloating()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "A wide band of sand should settle with all material conserved and no powder floating above air.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Wide band of sand
        for (int x = 30; x < 98; x++)
            for (int y = 50; y < 60; y++)
                sim.Set(x, y, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingPowder(sim.World);
    }
}
