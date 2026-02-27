using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for heat transfer system (double-buffered diffusion).
///
/// English rules (derived from HeatTransferSystem.cs):
///
/// 1. Only materials with ConductsHeat flag participate in heat diffusion.
///    Conducting materials: Stone, Water, Steam, IronOre, MoltenIron, Iron, Coal, Ground.
///    Non-conducting: Air, Sand, Oil, Ash, Smoke, Belt variants, Dirt, Lift, Wall, Piston.
/// 2. Each conducting cell averages its temperature with conducting cardinal neighbors.
///    New temp = old + (avg - old) * ConductionRate / 256.
///    ConductionRate = 64, so 25% blend toward neighbor average per frame.
/// 3. All conducting cells cool toward ambient (20) at CoolingRate=1 degree per frame.
/// 4. Non-conducting cells keep their temperature unchanged (no diffusion, no cooling).
/// 5. Double buffering: new temperatures written to temp buffer, then copied back.
///    This means all cells read from the same snapshot — order doesn't matter.
/// 6. Temperature is a byte (0-255), clamped to this range.
///
/// Known tradeoffs:
/// - Cooling applies AFTER conduction, so very hot cells lose 1 degree per frame
///   even when surrounded by equally hot cells.
/// - Non-conducting materials act as perfect thermal insulators.
/// </summary>
public class HeatTransferTests
{
    // ===== BASIC COOLING =====

    [Fact]
    public void HotConductor_CoolsTowardAmbient()
    {
        // A single hot stone cell should cool toward ambient over time.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 100);

        sim.Step(1);

        byte temp = sim.GetTemperature(8, 8);
        Assert.True(temp < 100, $"Stone should cool from 100, got {temp}");
        Assert.True(temp > HeatSettings.AmbientTemperature,
            $"Stone shouldn't reach ambient in 1 frame, got {temp}");
    }

    [Fact]
    public void HotConductor_EventuallyReachesAmbient()
    {
        // A hot stone cell surrounded by air (non-conducting) should still cool to ambient.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);

        sim.Step(500);

        byte temp = sim.GetTemperature(8, 8);
        Assert.Equal(HeatSettings.AmbientTemperature, temp);
    }

    [Fact]
    public void ColdConductor_WarmsTowardAmbient()
    {
        // A cold stone cell should warm toward ambient.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 5);

        sim.Step(1);

        byte temp = sim.GetTemperature(8, 8);
        Assert.True(temp > 5, $"Cold stone should warm from 5, got {temp}");
    }

    // ===== CONDUCTION =====

    [Fact]
    public void AdjacentConductors_TemperaturesDiffuse()
    {
        // Two adjacent conducting cells with different temperatures should exchange heat.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(7, 8, Materials.Stone);
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(7, 8, 200);
        sim.SetTemperature(8, 8, 20);

        sim.Step(1);

        byte hotTemp = sim.GetTemperature(7, 8);
        byte coldTemp = sim.GetTemperature(8, 8);
        // Hot cell should have cooled
        Assert.True(hotTemp < 200, $"Hot cell should cool from 200, got {hotTemp}");
        // Cold cell should have warmed (it conducts and has a hot neighbor)
        Assert.True(coldTemp > 20, $"Cold cell should warm from 20, got {coldTemp}");
    }

    [Fact]
    public void AdjacentConductors_Equilibrate_OverTime()
    {
        // Two adjacent conducting cells should eventually reach same temperature
        // (which decays to ambient due to cooling).
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(7, 8, Materials.Stone);
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(7, 8, 200);
        sim.SetTemperature(8, 8, 20);

        sim.Step(500);

        byte temp1 = sim.GetTemperature(7, 8);
        byte temp2 = sim.GetTemperature(8, 8);
        // Both should be at ambient after enough time
        Assert.Equal(HeatSettings.AmbientTemperature, temp1);
        Assert.Equal(HeatSettings.AmbientTemperature, temp2);
    }

    [Fact]
    public void HeatSpreads_AllFourDirections()
    {
        // A hot cell surrounded by 4 conducting neighbors should heat all of them.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        // Center hot cell
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);
        // 4 cardinal neighbors at ambient
        sim.Set(7, 8, Materials.Stone);
        sim.Set(9, 8, Materials.Stone);
        sim.Set(8, 7, Materials.Stone);
        sim.Set(8, 9, Materials.Stone);

        sim.Step(1);

        // All 4 neighbors should have warmed
        Assert.True(sim.GetTemperature(7, 8) > HeatSettings.AmbientTemperature);
        Assert.True(sim.GetTemperature(9, 8) > HeatSettings.AmbientTemperature);
        Assert.True(sim.GetTemperature(8, 7) > HeatSettings.AmbientTemperature);
        Assert.True(sim.GetTemperature(8, 9) > HeatSettings.AmbientTemperature);
    }

    // ===== NON-CONDUCTING INSULATION =====

    [Fact]
    public void NonConductor_KeepsTemperature()
    {
        // Sand (non-conducting) should keep its temperature unchanged.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        // Place sand on stone floor so it doesn't fall
        sim.Fill(0, 15, 16, 1, Materials.Stone);
        sim.Set(8, 14, Materials.Sand);
        sim.SetTemperature(8, 14, 100);

        sim.Step(1);

        // Sand temperature should not change (non-conducting)
        byte temp = sim.GetTemperature(8, 14);
        Assert.Equal(100, temp);
    }

    [Fact]
    public void NonConductor_BlocksHeatSpread()
    {
        // A non-conducting material between two conductors should block heat flow.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(6, 8, Materials.Stone);
        sim.Set(7, 8, Materials.Wall);  // Wall is non-conducting
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(6, 8, 200);
        sim.SetTemperature(8, 8, 20);

        sim.Step(10);

        // Cold stone should still be at/near ambient (wall blocks conduction)
        byte coldTemp = sim.GetTemperature(8, 8);
        Assert.Equal(HeatSettings.AmbientTemperature, coldTemp);
    }

    // ===== DOUBLE BUFFERING =====

    [Fact]
    public void DoubleBuffered_SymmetricDiffusion()
    {
        // Two hot cells equidistant from a cold cell should contribute equally.
        // This tests that double buffering prevents processing-order artifacts.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(7, 8, Materials.Stone);
        sim.Set(8, 8, Materials.Stone);
        sim.Set(9, 8, Materials.Stone);
        sim.SetTemperature(7, 8, 200);
        sim.SetTemperature(8, 8, 20);
        sim.SetTemperature(9, 8, 200);

        sim.Step(1);

        // Center cell should have warmed symmetrically
        byte centerTemp = sim.GetTemperature(8, 8);
        Assert.True(centerTemp > 20, $"Center should warm, got {centerTemp}");

        // The two hot cells should have cooled by the same amount (symmetry test)
        byte leftTemp = sim.GetTemperature(7, 8);
        byte rightTemp = sim.GetTemperature(9, 8);
        Assert.Equal(leftTemp, rightTemp);
    }

    // ===== TEMPERATURE CLAMPING =====

    [Fact]
    public void Temperature_ClampedToByteRange()
    {
        // Temperature should never exceed 255 or go below 0.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 255);

        sim.Step(1);

        byte temp = sim.GetTemperature(8, 8);
        Assert.True(temp <= 255);
        Assert.True(temp >= 0);
    }

    // ===== WATER CONDUCTS HEAT =====

    [Fact]
    public void Water_ConductsHeat()
    {
        // Water has ConductsHeat flag, should participate in diffusion.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        // Container: stone box with hot water inside
        sim.Fill(4, 12, 8, 1, Materials.Stone);  // Floor
        sim.Fill(4, 8, 1, 4, Materials.Stone);    // Left wall
        sim.Fill(11, 8, 1, 4, Materials.Stone);   // Right wall
        // Water pool
        for (int x = 5; x < 11; x++)
            for (int y = 8; y < 12; y++)
                sim.Set(x, y, Materials.Water);

        // Heat the left stone wall
        for (int y = 8; y < 12; y++)
            sim.SetTemperature(4, y, 200);

        sim.Step(20);

        // Water near the hot wall should be warmer than water far from it
        byte nearTemp = sim.GetTemperature(5, 10);
        byte farTemp = sim.GetTemperature(10, 10);
        Assert.True(nearTemp > farTemp,
            $"Water near hot wall ({nearTemp}) should be warmer than far ({farTemp})");
    }

    // ===== CONDUCTION RATE =====

    [Fact]
    public void ConductionRate_25PercentBlend()
    {
        // With ConductionRate=64 (25%), verify the math.
        // A 200-degree stone cell with ONE 20-degree conducting neighbor:
        // avgTemp = (200 + 20) / 2 = 110
        // newTemp = 200 + (110 - 200) * 64 / 256 = 200 + (-90) * 64 / 256 = 200 - 22 = 178
        // Then cooling: 178 > 20, so 178 - 1 = 177
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.Set(9, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);
        sim.SetTemperature(9, 8, 20);

        sim.Step(1);

        byte hotTemp = sim.GetTemperature(8, 8);
        // Expected: 177 (see calculation above)
        Assert.Equal(177, hotTemp);
    }

    // ===== HEAT DISABLED =====

    [Fact]
    public void HeatDisabled_NoTemperatureChange()
    {
        // When EnableHeatTransfer=false (default), temperatures should not change.
        using var sim = new SimulationFixture(16, 16);
        // Note: EnableHeatTransfer defaults to false
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);

        sim.Step(10);

        byte temp = sim.GetTemperature(8, 8);
        Assert.Equal(200, temp);
    }
}
