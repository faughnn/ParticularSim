using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for heat transfer system (double-buffered diffusion).
///
/// English rules (derived from HeatTransferSystem.cs):
///
/// 1. Only materials with ConductsHeat flag participate in heat diffusion.
///    Conducting materials: Air, Stone, Water, Steam, IronOre, MoltenIron, Iron, Coal, Ground, Furnace.
///    Non-conducting: Sand, Oil, Ash, Smoke, Belt variants, Dirt, Lift, Wall, Piston.
/// 2. Each conducting cell averages its temperature with conducting cardinal neighbors.
///    New temp = old + (avg - old) * mat.conductionRate / 256.
///    Each material has its own conduction rate: Stone/Iron/MoltenIron/Furnace=64 (25%),
///    Water/IronOre/Ground=48/32 (19%/12.5%), Air=8 (3%), Steam/Coal=32 (12.5%).
/// 3. All conducting cells cool proportionally toward ambient (20) using Newton's law:
///    accumulator += (temp - ambient) * CoolingFactor. When accumulator >= 2048, temp drops 1 degree.
///    Hotter cells cool faster; near-ambient cells cool very slowly.
/// 4. Non-conducting cells keep their temperature unchanged (no diffusion, no cooling).
/// 5. Double buffering: new temperatures written to temp buffer, then copied back.
///    This means all cells read from the same snapshot — order doesn't matter.
/// 6. Temperature is a byte (0-255), clamped to this range.
///
/// Known tradeoffs:
/// - Cooling applies AFTER conduction, so very hot cells cool proportionally.
/// - Non-conducting materials act as perfect thermal insulators.
/// - Near-ambient temperatures take many frames to reach ambient exactly (proportional slowdown).
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
        // A hot stone cell surrounded by air (conducting slowly) should still cool to ambient.
        // With AccumulatorThreshold=2048 and CoolingFactor=1, the last degree (21->20) takes
        // 2048 frames. 16000 frames is more than enough for full cooldown from 200°.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(16000, counts);

        byte temp = sim.GetTemperature(8, 8);
        Assert.Equal(HeatSettings.AmbientTemperature, temp);
    }

    [Fact]
    public void ColdConductor_WarmsTowardAmbient()
    {
        // A cold stone cell should warm toward ambient.
        // With proportional cooling below ambient, accumulator gains (20-5)*1=15/frame.
        // After 137 frames: 15*137=2055 >= 2048, so 1 degree gained. Temp goes from 5 to 6.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 5);

        sim.Step(200);

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
        // With AccumulatorThreshold=2048 and CoolingFactor=1, last degree takes 2048 frames.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(7, 8, Materials.Stone);
        sim.Set(8, 8, Materials.Stone);
        sim.SetTemperature(7, 8, 200);
        sim.SetTemperature(8, 8, 20);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(16000, counts);

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

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(20, counts);

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
        // With per-material conduction (stone conductionRate=64 = 25%), verify the math.
        // Hot stone at (8,8)=200, cold stone at (9,8)=20.
        // Air now conducts (rate=8), so (8,8) has 5 conducting neighbors (including self):
        //   (9,8)=20 stone, (7,8)=20 air, (8,7)=20 air, (8,9)=20 air, plus self=200
        // avgTemp = (200 + 20 + 20 + 20 + 20) / 5 = 56
        // newTemp = 200 + (56 - 200) * 64 / 256 = 200 + (-144) * 64 / 256 = 200 - 36 = 164
        // Then proportional cooling: diff = 164 - 20 = 144, accum += 144*1 = 144
        // degrees = 144 / 2048 = 0, no cooling this frame. Final = 164.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        sim.Set(8, 8, Materials.Stone);
        sim.Set(9, 8, Materials.Stone);
        sim.SetTemperature(8, 8, 200);
        sim.SetTemperature(9, 8, 20);

        sim.Step(1);

        byte hotTemp = sim.GetTemperature(8, 8);
        // Expected: 164 (see calculation above)
        Assert.Equal(164, hotTemp);
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

    // ===== PER-MATERIAL CONDUCTION =====

    [Fact]
    public void Air_ConductsHeatSlowly()
    {
        // Air has ConductsHeat flag with conductionRate=8 (3%).
        // Surround one air cell with 4 hot stone cells at 255 to overwhelm the cooling rate.
        // Air at center, temp=20, with 4 stone neighbors at 255:
        //   totalTemp = 20 + 255*4 = 1040, conductingNeighbors = 5
        //   avgTemp = 208, newTemp = 20 + (208-20)*8/256 = 20 + 5 = 25
        //   After proportional cooling: diff = 25 - 20 = 5, accum += 5*1 = 5
        //   degrees = 5/2048 = 0, no cooling this frame. Final = 25.
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;
        // 4 hot stone cells surrounding the air cell at (8,8)
        sim.Set(7, 8, Materials.Stone);
        sim.Set(9, 8, Materials.Stone);
        sim.Set(8, 7, Materials.Stone);
        sim.Set(8, 9, Materials.Stone);
        sim.SetTemperature(7, 8, 255);
        sim.SetTemperature(9, 8, 255);
        sim.SetTemperature(8, 7, 255);
        sim.SetTemperature(8, 9, 255);
        // (8, 8) is air by default at ambient temperature

        sim.Step(1);

        byte airTemp = sim.GetTemperature(8, 8);
        Assert.True(airTemp > HeatSettings.AmbientTemperature,
            $"Air surrounded by hot stones should warm above ambient ({HeatSettings.AmbientTemperature}), got {airTemp}");
    }

    [Fact]
    public void PerMaterialConduction_AirConductsSlowerThanStone()
    {
        // Compare heat propagation: stone chain vs stone-air-stone chain.
        // Stone has conductionRate=64, Air has conductionRate=8.
        // Surround the chains with non-conducting walls to prevent heat leaking sideways.
        // Continuously re-heat the source each frame to maintain a temperature gradient.

        // Setup 1: Stone chain insulated by walls
        // Layout (y=7,8,9): wall row, stone chain, wall row
        using var sim1 = new SimulationFixture(16, 16);
        sim1.Simulator.EnableHeatTransfer = true;
        for (int x = 4; x <= 10; x++)
        {
            sim1.Set(x, 7, Materials.Wall);
            sim1.Set(x, 8, Materials.Stone);
            sim1.Set(x, 9, Materials.Wall);
        }
        // Also wall-cap the ends
        sim1.Set(4, 8, Materials.Wall);
        sim1.Set(10, 8, Materials.Wall);
        // Heat the source stone
        sim1.SetTemperature(5, 8, 255);

        // Setup 2: Stone-Air-Stone chain insulated by walls
        using var sim2 = new SimulationFixture(16, 16);
        sim2.Simulator.EnableHeatTransfer = true;
        for (int x = 4; x <= 10; x++)
        {
            sim2.Set(x, 7, Materials.Wall);
            sim2.Set(x, 9, Materials.Wall);
        }
        sim2.Set(4, 8, Materials.Wall);
        sim2.Set(5, 8, Materials.Stone);
        // (6,8), (7,8), (8,8) remain air
        sim2.Set(9, 8, Materials.Stone);
        sim2.Set(10, 8, Materials.Wall);
        sim2.SetTemperature(5, 8, 255);

        // Run with source re-heating each frame
        for (int i = 0; i < 30; i++)
        {
            sim1.SetTemperature(5, 8, 255);
            sim2.SetTemperature(5, 8, 255);
            sim1.Step(1);
            sim2.Step(1);
        }

        byte stoneFarEnd = sim1.GetTemperature(9, 8);
        byte airFarEnd = sim2.GetTemperature(9, 8);

        Assert.True(stoneFarEnd > airFarEnd,
            $"Stone chain far end ({stoneFarEnd}) should be warmer than air chain far end ({airFarEnd})");
    }

    // ===== PROPORTIONAL COOLING =====

    [Fact]
    public void ProportionalCooling_HotterCoolsFaster()
    {
        // A cell at 200 degrees should cool more per step than a cell at 50 degrees.
        // With CoolingFactor=1, AccumulatorThreshold=2048:
        //   At 200: accumulator += (200-20)*1 = 180/frame, needs ~11 frames per degree
        //   At 50:  accumulator += (50-20)*1 = 30/frame, needs ~68 frames per degree
        using var sim = new SimulationFixture(16, 16);
        sim.Simulator.EnableHeatTransfer = true;

        sim.Set(4, 8, Materials.Stone);
        sim.Set(12, 8, Materials.Stone);
        sim.SetTemperature(4, 8, 200);
        sim.SetTemperature(12, 8, 50);

        sim.Step(100);

        byte hotDrop = (byte)(200 - sim.GetTemperature(4, 8));
        byte coldDrop = (byte)(50 - sim.GetTemperature(12, 8));

        Assert.True(hotDrop > coldDrop,
            $"Hot cell should cool more ({hotDrop} degrees) than cold cell ({coldDrop} degrees) over 5 frames");
    }
}
