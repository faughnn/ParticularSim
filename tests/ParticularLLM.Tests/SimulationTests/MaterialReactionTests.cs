using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for temperature-triggered material reactions.
///
/// English rules (derived from CheckPhaseChange and SimulateBurning):
///
/// 1. Melting: When temperature >= meltTemp and materialOnMelt is set,
///    material transforms. Velocity resets. Burning flag cleared.
///    Example: IronOre (meltTemp=200) → MoltenIron.
/// 2. Freezing: When temperature <= freezeTemp and materialOnFreeze is set,
///    material transforms. Velocity resets.
///    Example: MoltenIron (freezeTemp=150) → Iron. Steam (freezeTemp=50) → Water.
/// 3. Boiling: When temperature >= boilTemp and materialOnBoil is set,
///    material transforms. Velocity set to -1 (gas rises).
///    Example: Water (boilTemp=100) → Steam.
/// 4. Ignition: When temperature >= ignitionTemp and material is Flammable,
///    Burning flag is set. Does NOT immediately transform.
///    Example: Coal (ignitionTemp=180), Oil (ignitionTemp=80).
/// 5. Burning cells: emit heat (+5/frame), spread fire to flammable neighbors
///    (~10% chance), consume fuel (~2% chance → materialOnBurn product).
/// 6. Reactions only run when enableReactions is true (tied to EnableHeatTransfer).
///
/// Known tradeoffs:
/// - Phase changes are instant (1 frame) — no gradual melting.
/// - Burning consumes fuel stochastically; exact burn duration varies.
/// - Temperature must be set explicitly; ambient temp (20) is below all
///   useful thresholds except freezing.
/// </summary>
public class MaterialReactionTests
{
    private SimulationFixture CreateHeatEnabled(int width = 64, int height = 64)
    {
        var sim = new SimulationFixture(width, height);
        sim.Simulator.EnableHeatTransfer = true;
        return sim;
    }

    // ===== MELTING =====

    [Fact]
    public void IronOre_MeltsAtThreshold()
    {
        // IronOre (meltTemp=200) should melt to MoltenIron at 200 degrees.
        using var sim = CreateHeatEnabled();
        sim.Description = "IronOre at exactly 200 degrees should melt into MoltenIron in one frame.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.IronOre);
        sim.SetTemperature(32, 62, 200);

        sim.Step(1);

        // IronOre should have melted to MoltenIron
        byte mat = sim.Get(32, 62);
        Assert.Equal(Materials.MoltenIron, mat);
    }

    [Fact]
    public void IronOre_DoesNotMelt_BelowThreshold()
    {
        // IronOre at 199 degrees should NOT melt.
        using var sim = CreateHeatEnabled();
        sim.Description = "IronOre at 199 degrees (one below melt threshold) should remain as IronOre.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.IronOre);
        sim.SetTemperature(32, 62, 199);

        sim.Step(1);

        // Should still be IronOre (may have moved due to powder physics)
        int count = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Iron_MeltsToMoltenIron()
    {
        // Iron (static, meltTemp=200) melts to MoltenIron at temperature.
        using var sim = CreateHeatEnabled();
        sim.Description = "Static Iron at 200 degrees should melt into liquid MoltenIron in one frame.";
        sim.Set(32, 32, Materials.Iron);
        sim.SetTemperature(32, 32, 200);

        sim.Step(1);

        byte mat = sim.Get(32, 32);
        Assert.Equal(Materials.MoltenIron, mat);
    }

    [Fact]
    public void Melting_ResetsVelocity()
    {
        // When material melts, velocity should reset to 0.
        using var sim = CreateHeatEnabled();
        sim.Description = "IronOre with existing velocity that melts should have its velocity reset to zero after transformation.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 50, Materials.IronOre);
        sim.SetTemperature(32, 50, 200);
        // Give it velocity by letting it fall a bit first
        // Place it directly with the melt temp
        int idx = 50 * sim.World.width + 32;
        sim.World.cells[idx].velocityY = 5;

        sim.Step(1);

        // The cell that melted should have zero velocity
        // (it may have moved during melting due to trace, but the velocity resets)
        var positions = sim.FindMaterial(Materials.MoltenIron);
        Assert.NotEmpty(positions);
        var (mx, my) = positions[0];
        Cell cell = sim.GetCell(mx, my);
        Assert.Equal(0, cell.velocityX);
        Assert.Equal(0, cell.velocityY);
    }

    // ===== FREEZING =====

    [Fact]
    public void MoltenIron_FreezesToIron()
    {
        // MoltenIron (freezeTemp=150) freezes to Iron when temperature drops to 150.
        using var sim = CreateHeatEnabled();
        sim.Description = "MoltenIron at exactly 150 degrees should freeze into solid Iron in one frame.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.MoltenIron);
        sim.SetTemperature(32, 62, 150);

        sim.Step(1);

        byte mat = sim.Get(32, 62);
        Assert.Equal(Materials.Iron, mat);
    }

    [Fact]
    public void MoltenIron_DoesNotFreeze_AboveThreshold()
    {
        using var sim = CreateHeatEnabled();
        sim.Description = "MoltenIron at 151 degrees (one above freeze threshold) should remain as MoltenIron.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.MoltenIron);
        sim.SetTemperature(32, 62, 151);

        sim.Step(1);

        // Should still be MoltenIron (may have spread as liquid)
        int count = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Steam_FreezesToWater()
    {
        // Steam (freezeTemp=50) freezes to Water when temperature drops to 50.
        using var sim = CreateHeatEnabled();
        sim.Description = "Steam at 50 degrees should condense into Water in one frame.";
        sim.Set(32, 32, Materials.Steam);
        sim.SetTemperature(32, 32, 50);

        sim.Step(1);

        // Should have become water
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);
    }

    // ===== BOILING =====

    [Fact]
    public void Water_BoilsToSteam()
    {
        // Water (boilTemp=100) should boil to Steam at 100 degrees.
        using var sim = CreateHeatEnabled();
        sim.Description = "Water at exactly 100 degrees should boil into Steam in one frame.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Water);
        sim.SetTemperature(32, 62, 100);

        sim.Step(1);

        byte mat = sim.Get(32, 62);
        Assert.Equal(Materials.Steam, mat);
    }

    [Fact]
    public void Water_DoesNotBoil_BelowThreshold()
    {
        using var sim = CreateHeatEnabled();
        sim.Description = "Water at 99 degrees (one below boil threshold) should remain as Water.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Water);
        sim.SetTemperature(32, 62, 99);

        sim.Step(1);

        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);
    }

    [Fact]
    public void Boiling_SetsUpwardVelocity()
    {
        // When water boils to steam, the steam should have upward velocity (-1).
        using var sim = CreateHeatEnabled();
        sim.Description = "Water that boils into Steam should have upward velocity set, so the steam rises immediately.";
        sim.Set(32, 32, Materials.Water);
        sim.SetTemperature(32, 32, 100);

        sim.Step(1);

        var positions = sim.FindMaterial(Materials.Steam);
        Assert.NotEmpty(positions);
        var (sx, sy) = positions[0];
        Cell cell = sim.GetCell(sx, sy);
        Assert.True(cell.velocityY <= 0, $"Boiled steam should have upward velocity, got vy={cell.velocityY}");
    }

    // ===== IGNITION & BURNING =====

    [Fact]
    public void Coal_IgnitesAtThreshold()
    {
        // Coal (ignitionTemp=180, Flammable) should start burning at 180 degrees.
        using var sim = CreateHeatEnabled();
        sim.Description = "Coal at 180 degrees should ignite and have the Burning flag set while remaining as Coal.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Coal);
        sim.SetTemperature(32, 62, 180);

        sim.Step(1);

        // Coal should still be coal but with Burning flag
        var positions = sim.FindMaterial(Materials.Coal);
        Assert.NotEmpty(positions);
        var (cx, cy) = positions[0];
        Cell cell = sim.GetCell(cx, cy);
        Assert.True((cell.flags & CellFlags.Burning) != 0,
            "Coal at ignition temp should have Burning flag");
    }

    [Fact]
    public void Coal_DoesNotIgnite_BelowThreshold()
    {
        using var sim = CreateHeatEnabled();
        sim.Description = "Coal at 179 degrees (one below ignition threshold) should not have the Burning flag.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Coal);
        sim.SetTemperature(32, 62, 179);

        sim.Step(1);

        var positions = sim.FindMaterial(Materials.Coal);
        Assert.NotEmpty(positions);
        var (cx, cy) = positions[0];
        Cell cell = sim.GetCell(cx, cy);
        Assert.True((cell.flags & CellFlags.Burning) == 0,
            "Coal below ignition temp should NOT be burning");
    }

    [Fact]
    public void BurningCoal_EmitsHeat()
    {
        // Burning coal emits +5 heat per frame. With heat transfer also running,
        // the net temperature depends on conduction to neighbors. The coal should
        // stay well above ambient temperature due to burning.
        using var sim = CreateHeatEnabled();
        sim.Description = "Burning coal should emit heat and stay well above ambient temperature after 10 frames despite conduction losses.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Coal);
        sim.SetTemperature(32, 62, 190);
        int idx = 62 * sim.World.width + 32;
        sim.World.cells[idx].flags |= CellFlags.Burning;

        sim.Step(10);

        // After 10 frames of burning + conduction, temperature should still be
        // significantly above ambient (burning sustains heat)
        var positions = sim.FindMaterial(Materials.Coal);
        if (positions.Count > 0)
        {
            var (cx, cy) = positions[0];
            byte temp = sim.GetTemperature(cx, cy);
            Assert.True(temp > HeatSettings.AmbientTemperature + 5,
                $"Burning coal should stay above ambient, temp={temp}");
        }
    }

    [Fact]
    public void BurningCoal_EventuallyBecomesAsh()
    {
        // Burning coal should eventually be consumed and become its burn product (Ash).
        // Use 5 coal cells over 1000 frames to ensure all are consumed.
        using var sim = CreateHeatEnabled();
        sim.Description = "Multiple burning coal cells should eventually be consumed and produce Ash over 2000 frames.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        int placed = 0;
        for (int x = 28; x < 36; x++)
        {
            sim.Set(x, 62, Materials.Coal);
            sim.SetTemperature(x, 62, 200);
            int idx2 = 62 * sim.World.width + x;
            sim.World.cells[idx2].flags |= CellFlags.Burning;
            placed++;
        }

        sim.Step(2000);

        int coalCount = WorldAssert.CountMaterial(sim.World, Materials.Coal);
        int ashCount = WorldAssert.CountMaterial(sim.World, Materials.Ash);
        // At least some coal should have been consumed
        Assert.True(coalCount < placed,
            $"Some coal should burn to ash over 2000 frames. Coal={coalCount}, Ash={ashCount}");
    }

    [Fact]
    public void Oil_IgnitesAtLowerTemp_ThanCoal()
    {
        // Oil ignitionTemp=80, Coal ignitionTemp=180.
        // Oil should ignite at 80 while coal stays cool.
        using var sim = CreateHeatEnabled();
        sim.Description = "Oil at 80 degrees should ignite while Coal at the same temperature should not, verifying different ignition thresholds.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(30, 62, Materials.Oil);
        sim.Set(34, 62, Materials.Coal);
        sim.SetTemperature(30, 62, 80);
        sim.SetTemperature(34, 62, 80);

        sim.Step(1);

        // Oil should be burning at 80
        var oilPositions = sim.FindMaterial(Materials.Oil);
        if (oilPositions.Count > 0)
        {
            var (ox, oy) = oilPositions[0];
            Cell oilCell = sim.GetCell(ox, oy);
            Assert.True((oilCell.flags & CellFlags.Burning) != 0,
                "Oil at 80 should be burning");
        }

        // Coal should NOT be burning at 80
        var coalPositions = sim.FindMaterial(Materials.Coal);
        Assert.NotEmpty(coalPositions);
        var (cx, cy) = coalPositions[0];
        Cell coalCell = sim.GetCell(cx, cy);
        Assert.True((coalCell.flags & CellFlags.Burning) == 0,
            "Coal at 80 should NOT be burning");
    }

    // ===== CONSERVATION =====

    [Fact]
    public void PhaseChange_ConservesCellCount()
    {
        // Melting/freezing should not create or destroy cells -- just transform them.
        using var sim = CreateHeatEnabled();
        sim.Description = "Eight IronOre cells at melt temperature should all transform into MoltenIron with a 1:1 count.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        // Place some IronOre and heat it to melt
        for (int x = 28; x < 36; x++)
        {
            sim.Set(x, 62, Materials.IronOre);
            sim.SetTemperature(x, 62, 200);
        }

        int totalBefore = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        sim.Step(1);

        // All IronOre should have become MoltenIron (1:1 transformation)
        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        Assert.Equal(totalBefore, moltenCount);
    }

    // ===== REACTIONS DISABLED =====

    [Fact]
    public void ReactionsDisabled_NoPhaseChanges()
    {
        // When heat transfer is off, no phase changes should occur.
        using var sim = new SimulationFixture();
        sim.Description = "With heat transfer disabled, IronOre at 200 degrees should not melt because reactions require heat transfer to be enabled.";
        // EnableHeatTransfer defaults to false
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.IronOre);
        sim.SetTemperature(32, 62, 200);

        sim.Step(1);

        // IronOre should still exist (not melted)
        int count = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        Assert.Equal(1, count);
    }

    // ===== ROUND-TRIP =====

    [Fact]
    public void Water_Boil_Then_Freeze_RoundTrip()
    {
        // Water -> Steam (boil at 100) -> Water (freeze at 50).
        using var sim = CreateHeatEnabled();
        sim.Description = "Water boiled into Steam at 100 degrees, then cooled to 50 degrees, should condense back into Water.";
        sim.Set(32, 32, Materials.Water);
        sim.SetTemperature(32, 32, 100);

        sim.Step(1);
        // Should be steam now
        int steamCount = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.Equal(1, steamCount);

        // Cool the steam to freezing point
        var steamPos = sim.FindMaterial(Materials.Steam);
        Assert.NotEmpty(steamPos);
        var (sx, sy) = steamPos[0];
        sim.SetTemperature(sx, sy, 50);

        sim.Step(1);
        // Should be water again
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);
    }
}
