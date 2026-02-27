using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

/// <summary>
/// Tests for the furnace structure system.
///
/// English rules (derived from FurnaceManager and design doc):
///
/// 1. Placement: Furnace is a rectangle with 1-cell-thick walls (Furnace material)
///    and a hollow interior. Minimum size is 3x3. Walls must be placed on Air cells.
/// 2. Heating: When state=Heating, all non-Air interior cells gain heatOutput
///    temperature per frame, capped at maxTemp.
/// 3. Phase changes: Handled by existing material reactions system (CheckPhaseChange
///    in SimulateChunksLogic). Furnace just applies heat; reactions run during cell sim.
/// 4. Off/Cooling: No heat applied. Ambient cooling from heat transfer system applies.
/// 5. Walls: Static Furnace material that blocks movement and conducts heat.
/// 6. Removal: Clears all wall cells to Air.
/// 7. Conservation: Phase changes inside furnace obey 1:1 material transformation.
///
/// Known tradeoffs:
/// - Furnace heat is applied after cell simulation but before heat diffusion.
///   This means placed materials experience furnace heat starting the same frame.
/// - Phase changes from furnace heating happen on the NEXT frame's cell simulation,
///   not immediately when heat is applied.
/// - No fuel consumption — furnaces heat indefinitely while in Heating state.
/// </summary>
public class FurnaceTests
{
    // ===== PLACEMENT =====

    [Fact]
    public void PlaceFurnace_CreatesWallsAndHollowInterior()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        ushort id = manager.PlaceFurnace(4, 4, 6, 5);

        Assert.NotEqual((ushort)0, id);

        // Check perimeter is Furnace material
        // Top and bottom walls
        for (int x = 4; x < 10; x++)
        {
            Assert.Equal(Materials.Furnace, sim.Get(x, 4));
            Assert.Equal(Materials.Furnace, sim.Get(x, 8));
        }
        // Left and right walls
        for (int y = 4; y < 9; y++)
        {
            Assert.Equal(Materials.Furnace, sim.Get(4, y));
            Assert.Equal(Materials.Furnace, sim.Get(9, y));
        }

        // Check interior is Air
        for (int y = 5; y <= 7; y++)
            for (int x = 5; x <= 8; x++)
                Assert.Equal(Materials.Air, sim.Get(x, y));
    }

    [Fact]
    public void PlaceFurnace_MinimumSize3x3()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        // 3x3 is minimum
        ushort id = manager.PlaceFurnace(10, 10, 3, 3);
        Assert.NotEqual((ushort)0, id);

        // 1x1 interior
        Assert.Equal(Materials.Air, sim.Get(11, 11));
        Assert.Equal(Materials.Furnace, sim.Get(10, 10));
        Assert.Equal(Materials.Furnace, sim.Get(12, 12));
    }

    [Fact]
    public void PlaceFurnace_RejectsTooSmall()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        Assert.Equal((ushort)0, manager.PlaceFurnace(10, 10, 2, 3));
        Assert.Equal((ushort)0, manager.PlaceFurnace(10, 10, 3, 2));
        Assert.Equal((ushort)0, manager.PlaceFurnace(10, 10, 1, 1));
    }

    [Fact]
    public void PlaceFurnace_RejectsOutOfBounds()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        Assert.Equal((ushort)0, manager.PlaceFurnace(30, 10, 5, 5));
        Assert.Equal((ushort)0, manager.PlaceFurnace(10, 30, 5, 5));
        Assert.Equal((ushort)0, manager.PlaceFurnace(-1, 10, 5, 5));
    }

    [Fact]
    public void PlaceFurnace_RejectsNonAirPerimeter()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        // Put stone where a wall would go (left wall at x=4)
        sim.Set(4, 5, Materials.Stone);

        Assert.Equal((ushort)0, manager.PlaceFurnace(4, 4, 5, 5));
    }

    [Fact]
    public void PlaceFurnace_AllowsNonAirInterior()
    {
        // Interior cells don't need to be Air — the furnace heats whatever is inside
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        // Put IronOre in the interior area before placing furnace
        sim.Set(6, 6, Materials.IronOre);

        ushort id = manager.PlaceFurnace(4, 4, 5, 5);
        Assert.NotEqual((ushort)0, id);
        Assert.Equal(Materials.IronOre, sim.Get(6, 6));
    }

    // ===== REMOVAL =====

    [Fact]
    public void RemoveFurnace_ClearsWalls()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        ushort id = manager.PlaceFurnace(4, 4, 5, 5);
        Assert.True(manager.RemoveFurnace(id));

        // All perimeter cells should be Air
        for (int y = 4; y < 9; y++)
            for (int x = 4; x < 9; x++)
                Assert.Equal(Materials.Air, sim.Get(x, y));
    }

    [Fact]
    public void RemoveFurnace_PreservesInteriorMaterials()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        ushort id = manager.PlaceFurnace(4, 4, 5, 5);

        // Put material inside
        sim.Set(6, 6, Materials.Sand);

        manager.RemoveFurnace(id);

        // Interior material should still be there
        Assert.Equal(Materials.Sand, sim.Get(6, 6));
    }

    [Fact]
    public void RemoveFurnace_InvalidId_ReturnsFalse()
    {
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceManager(sim.World);

        Assert.False(manager.RemoveFurnace(999));
    }

    // ===== HEATING =====

    [Fact]
    public void Heating_IncreasesInteriorTemperature()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 255);

        // Place Stone inside (static, won't move or phase change)
        sim.Set(12, 12, Materials.Stone);
        byte initialTemp = sim.GetTemperature(12, 12);

        sim.Step(1);

        byte newTemp = sim.GetTemperature(12, 12);
        Assert.True(newTemp > initialTemp,
            $"Interior temp should increase from {initialTemp}, got {newTemp}");
    }

    [Fact]
    public void Heating_CappedAtMaxTemp()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 50);

        // Place stone inside (static, conducts heat, won't move or phase change)
        sim.Set(12, 12, Materials.Stone);
        sim.SetTemperature(12, 12, 45);

        sim.Step(1);

        byte temp = sim.GetTemperature(12, 12);
        // 45 + 10 = 55, but capped at 50. Heat diffusion may reduce further.
        Assert.True(temp <= 50, $"Temperature should be capped at 50, got {temp}");
    }

    [Fact]
    public void Heating_SkipsAirCells()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 255);

        // Interior is all Air by default
        byte tempBefore = sim.GetTemperature(12, 12);

        sim.Step(5);

        // Air is not a conducting material and furnace skips Air cells
        byte tempAfter = sim.GetTemperature(12, 12);
        Assert.Equal(tempBefore, tempAfter);
    }

    [Fact]
    public void Heating_AccumulatesOverFrames()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Use high maxTemp and moderate heatOutput
        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 5, maxTemp: 255);

        // Place stone inside (static, conducts heat but won't phase change)
        sim.Set(12, 12, Materials.Stone);
        sim.SetTemperature(12, 12, 20);

        sim.Step(10);

        byte temp = sim.GetTemperature(12, 12);
        // After 10 frames of +5/frame = +50 gross, minus cooling/conduction losses
        // Should be well above initial 20
        Assert.True(temp > 40, $"After 10 frames of heating, temp should be >40, got {temp}");
    }

    // ===== OFF STATE =====

    [Fact]
    public void Off_DoesNotHeatInterior()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 255);
        manager.SetState(id, FurnaceState.Off);

        sim.Set(12, 12, Materials.Stone);
        sim.SetTemperature(12, 12, 100);

        sim.Step(10);

        byte temp = sim.GetTemperature(12, 12);
        // With furnace off, stone should cool toward ambient (20)
        Assert.True(temp < 100, $"Stone should cool when furnace is off, got {temp}");
    }

    [Fact]
    public void SetState_SwitchesHeatingToOff()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 255);

        // Verify initially Heating
        var furnace = manager.GetFurnace(id);
        Assert.NotNull(furnace);
        Assert.Equal(FurnaceState.Heating, furnace.Value.state);

        // Switch to Off
        Assert.True(manager.SetState(id, FurnaceState.Off));
        furnace = manager.GetFurnace(id);
        Assert.NotNull(furnace);
        Assert.Equal(FurnaceState.Off, furnace.Value.state);
    }

    // ===== PHASE CHANGES (INTEGRATION WITH REACTIONS) =====

    [Fact]
    public void Furnace_MeltsIronOre()
    {
        // IronOre (meltTemp=200) should melt to MoltenIron when furnace heats it enough.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // 7x7 furnace at (10,10). Interior: x=11-15, y=11-15. Bottom wall: y=16.
        // heatOutput=50 to overcome conduction losses through conducting walls.
        ushort id = manager.PlaceFurnace(10, 10, 7, 7, heatOutput: 50, maxTemp: 255);

        // Place IronOre on the furnace floor (y=15, just above bottom wall y=16)
        sim.Set(13, 15, Materials.IronOre);

        // Run enough frames for temperature to reach meltTemp=200
        sim.Step(30);

        // IronOre should have melted to MoltenIron
        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        int ironCount = WorldAssert.CountMaterial(sim.World, Materials.Iron);
        Assert.True(moltenCount > 0 || ironCount > 0,
            $"IronOre should have melted. MoltenIron={moltenCount}, Iron={ironCount}");
    }

    [Fact]
    public void Furnace_BoilsWater()
    {
        // Water (boilTemp=100) should boil to Steam in a furnace.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // 7x7 furnace. Interior: x=11-15, y=11-15. Bottom wall: y=16.
        // heatOutput=30 to overcome conduction losses for boilTemp=100.
        ushort id = manager.PlaceFurnace(10, 10, 7, 7, heatOutput: 30, maxTemp: 200);

        // Place water on the furnace floor (y=15)
        sim.Set(13, 15, Materials.Water);

        // boilTemp=100. Run enough frames for temperature to reach threshold.
        sim.Step(20);

        int steamCount = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.True(steamCount > 0,
            $"Water should have boiled to Steam after furnace heating");
    }

    [Fact]
    public void Furnace_IgnitesCoal()
    {
        // Coal (ignitionTemp=180, Flammable) should ignite in a furnace.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // 7x7 furnace. Interior: x=11-15, y=11-15. Bottom wall: y=16.
        // heatOutput=50 to overcome conduction losses for ignitionTemp=180.
        ushort id = manager.PlaceFurnace(10, 10, 7, 7, heatOutput: 50, maxTemp: 255);

        // Place coal on the furnace floor (y=15, just above bottom wall y=16)
        sim.Set(13, 15, Materials.Coal);

        // ignitionTemp=180. Run enough frames for temperature to reach threshold.
        sim.Step(25);

        // Coal should be burning (if not already consumed to ash)
        var coalPositions = sim.FindMaterial(Materials.Coal);
        int ashCount = WorldAssert.CountMaterial(sim.World, Materials.Ash);

        // Either coal is burning or has already turned to ash
        bool coalBurning = false;
        foreach (var (cx, cy) in coalPositions)
        {
            Cell cell = sim.GetCell(cx, cy);
            if ((cell.flags & CellFlags.Burning) != 0)
                coalBurning = true;
        }

        Assert.True(coalBurning || ashCount > 0,
            $"Coal should be burning or converted to ash. Coal={coalPositions.Count}, Ash={ashCount}");
    }

    [Fact]
    public void Furnace_MaxTemp_PreventsPhaseChange()
    {
        // With maxTemp=150, IronOre (meltTemp=200) should NOT melt.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 15, maxTemp: 150);

        sim.Set(12, 12, Materials.IronOre);

        sim.Step(50);

        // IronOre should still exist (temp capped at 150, meltTemp=200)
        int oreCount = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        Assert.True(oreCount > 0,
            $"IronOre should NOT melt when furnace maxTemp=150 < meltTemp=200");
    }

    // ===== WALL BEHAVIOR =====

    [Fact]
    public void FurnaceWalls_BlockMaterial()
    {
        // Sand placed above a furnace should not pass through the walls.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Place furnace near bottom
        ushort id = manager.PlaceFurnace(10, 50, 10, 10, heatOutput: 0, maxTemp: 0);
        manager.SetState(id, FurnaceState.Off);

        // Place sand above the furnace
        for (int x = 12; x < 18; x++)
            sim.Set(x, 49, Materials.Sand);

        sim.Step(50);

        // Sand should rest on top of the furnace wall, not inside
        // The top wall is at y=50, so sand should settle at y=49 or slide off
        int sandInsideCount = 0;
        for (int y = 51; y <= 58; y++)
            for (int x = 11; x <= 18; x++)
                if (sim.Get(x, y) == Materials.Sand)
                    sandInsideCount++;

        Assert.Equal(0, sandInsideCount);
    }

    [Fact]
    public void FurnaceWalls_AreStatic()
    {
        // Furnace walls should not move under gravity or any forces.
        using var sim = new SimulationFixture(64, 64);
        var manager = new FurnaceManager(sim.World);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5);

        sim.Step(100);

        // Verify walls are still in place
        for (int x = 10; x < 15; x++)
        {
            Assert.Equal(Materials.Furnace, sim.Get(x, 10));
            Assert.Equal(Materials.Furnace, sim.Get(x, 14));
        }
        for (int y = 10; y < 15; y++)
        {
            Assert.Equal(Materials.Furnace, sim.Get(10, y));
            Assert.Equal(Materials.Furnace, sim.Get(14, y));
        }
    }

    [Fact]
    public void FurnaceWalls_ConductHeat()
    {
        // Furnace material has ConductsHeat flag, so heat should diffuse through walls.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // 5x5 furnace at (10,10). Interior: x=11-13, y=11-13. Left wall: x=10.
        // heatOutput=50 so interior gets very hot and conducts through walls.
        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 50, maxTemp: 255);

        // Place stone inside adjacent to the left wall (so heat conducts wall → exterior)
        sim.Set(11, 12, Materials.Stone);

        // Place stone outside adjacent to furnace wall
        sim.Set(9, 12, Materials.Stone);

        sim.Step(50);

        // The exterior stone should warm up via conduction through furnace walls
        byte exteriorTemp = sim.GetTemperature(9, 12);
        Assert.True(exteriorTemp > HeatSettings.AmbientTemperature,
            $"Stone outside furnace should warm via wall conduction, got {exteriorTemp}");
    }

    // ===== CONSERVATION =====

    [Fact]
    public void Furnace_MeltingConservesMaterials()
    {
        // Melting IronOre → MoltenIron should be 1:1 conservation.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 8, 8, heatOutput: 15, maxTemp: 255);

        // Place 5 IronOre inside
        int placed = 0;
        for (int x = 12; x <= 16; x++)
        {
            sim.Set(x, 16, Materials.IronOre);
            placed++;
        }

        sim.Step(50);

        int oreCount = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        int ironCount = WorldAssert.CountMaterial(sim.World, Materials.Iron);

        // All iron-related materials should sum to what we placed
        int total = oreCount + moltenCount + ironCount;
        Assert.Equal(placed, total);
    }

    // ===== MULTIPLE FURNACES =====

    [Fact]
    public void MultipleFurnaces_IndependentHeating()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Furnace 1: high heat
        ushort id1 = manager.PlaceFurnace(5, 5, 5, 5, heatOutput: 15, maxTemp: 255);
        // Furnace 2: low heat
        ushort id2 = manager.PlaceFurnace(15, 5, 5, 5, heatOutput: 2, maxTemp: 100);

        // Place stone in each
        sim.Set(7, 7, Materials.Stone);
        sim.Set(17, 7, Materials.Stone);

        sim.Step(10);

        byte temp1 = sim.GetTemperature(7, 7);
        byte temp2 = sim.GetTemperature(17, 7);

        // Furnace 1 should be hotter
        Assert.True(temp1 > temp2,
            $"High-heat furnace ({temp1}) should be hotter than low-heat ({temp2})");
    }

    [Fact]
    public void RemoveOneFurnace_OtherContinues()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id1 = manager.PlaceFurnace(5, 5, 5, 5, heatOutput: 10, maxTemp: 255);
        ushort id2 = manager.PlaceFurnace(15, 5, 5, 5, heatOutput: 10, maxTemp: 255);

        sim.Set(17, 7, Materials.Stone);
        sim.SetTemperature(17, 7, 20);

        // Remove furnace 1
        manager.RemoveFurnace(id1);

        sim.Step(10);

        // Furnace 2 should still be heating
        byte temp2 = sim.GetTemperature(17, 7);
        Assert.True(temp2 > 40,
            $"Remaining furnace should still heat, got {temp2}");
    }

    // ===== EXTERIOR CELLS NOT HEATED =====

    [Fact]
    public void Heating_OnlyAffectsInterior()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        ushort id = manager.PlaceFurnace(10, 10, 5, 5, heatOutput: 10, maxTemp: 255);

        // Place stone outside, adjacent to furnace wall
        // Disable heat transfer to isolate furnace heating from conduction
        sim.Simulator.EnableHeatTransfer = false;
        sim.Set(9, 12, Materials.Stone);
        sim.SetTemperature(9, 12, 20);

        sim.Step(10);

        // Exterior stone should not be directly heated by furnace
        byte exteriorTemp = sim.GetTemperature(9, 12);
        Assert.Equal(20, exteriorTemp);
    }

    // ===== SMELTING SCENARIO =====

    [Fact]
    public void Furnace_SmeltingScenario_IronOreToMoltenToIron()
    {
        // Full smelting cycle: IronOre → MoltenIron (at 200) → cools → Iron (at 150).
        // Use a furnace to heat, then turn it off and let it cool.
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // 9x9 furnace. Interior: x=11-17, y=11-17. Bottom wall: y=18.
        // heatOutput=50 to overcome conduction losses for meltTemp=200.
        ushort id = manager.PlaceFurnace(10, 10, 9, 9, heatOutput: 50, maxTemp: 220);

        // Place IronOre on the furnace floor (y=17, just above bottom wall y=18)
        sim.Set(14, 17, Materials.IronOre);

        // Heat until melted (from 20, net +14/frame, ~13 frames to reach 200)
        sim.Step(30);

        // Should have melted
        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        Assert.True(moltenCount > 0, "IronOre should have melted to MoltenIron");

        // Turn off furnace and let it cool
        manager.SetState(id, FurnaceState.Off);

        // Cool for many frames until temp drops below freezeTemp=150
        sim.Step(500);

        // MoltenIron should have frozen to Iron
        int ironCount = WorldAssert.CountMaterial(sim.World, Materials.Iron);
        Assert.True(ironCount > 0,
            $"MoltenIron should freeze to Iron after cooling. Iron={ironCount}, " +
            $"MoltenIron={WorldAssert.CountMaterial(sim.World, Materials.MoltenIron)}");
    }
}
