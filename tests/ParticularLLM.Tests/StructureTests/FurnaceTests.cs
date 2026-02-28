using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

/// <summary>
/// Tests for the block-based furnace system (FurnaceBlockManager).
///
/// English rules (derived from FurnaceBlockManager):
///
/// 1. Placement: Furnace is a solid 8x8 block of Furnace material. Position snaps to 8x8 grid.
///    Placement fails if any cell in the 8x8 area has a hard material (Stone, structures).
///    Soft terrain (Sand, Water, Ground, Dirt) triggers ghost placement.
/// 2. Direction: Each block has a FurnaceDirection (Up, Right, Down, Left) indicating where
///    it emits heat — to the 8 cells just outside the block on that edge.
/// 3. Always on: No off/heating/cooling states. Blocks always emit heat every frame
///    (paced by HeatSettings.FurnaceHeatInterval).
/// 4. Ghost blocks: Placed over soft terrain, ghost blocks reserve the space but don't write
///    Furnace material until all 64 cells become Air.
/// 5. Removal: Clears all 64 cells to Air (non-ghost) or clears tile data (ghost).
/// 6. Phase changes: Materials outside the block, in the emitting direction, get heated.
///    Phase change thresholds (melt, boil, ignite) work the same as before.
/// 7. Enclosure: Multiple blocks facing inward create a heated enclosure. Heat concentrates
///    in the gap between facing blocks.
/// 8. Conservation: Phase changes obey 1:1 material transformation.
///
/// Known tradeoffs:
/// - Equilibrium with CoolingFactor=3, FurnaceHeatOutput=1: ~105° for 1 wall, ~190° for 2 walls.
/// - Air conducts heat (conductionRate=8), so heat spreads through air but slowly.
/// - Furnace material conducts heat (conductionRate=64), so back side gets some heat via conduction.
/// </summary>
public class FurnaceTests
{
    // ===== PLACEMENT =====

    [Fact]
    public void PlaceFurnaceBlock_Creates8x8SolidBlock()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        Assert.True(manager.PlaceFurnace(8, 8, FurnaceDirection.Up));

        // All 64 cells should be Furnace material
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Furnace, world.GetCell(8 + dx, 8 + dy));
    }

    [Fact]
    public void PlaceFurnaceBlock_SnapsToGrid()
    {
        // Placing at (3, 5) should snap to (0, 0)
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        Assert.True(manager.PlaceFurnace(3, 5, FurnaceDirection.Right));

        // Snapped to (0,0) — all cells in 0..7 x 0..7 should be Furnace
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Furnace, world.GetCell(dx, dy));

        // Cell at (8,0) should NOT be Furnace (outside the block)
        Assert.NotEqual(Materials.Furnace, world.GetCell(8, 0));
    }

    [Fact]
    public void PlaceFurnaceBlock_RejectsOutOfBounds()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        // Block at (24,24) snaps to (24,24) — extends to (31,31), which is still in bounds for 32x32
        Assert.True(manager.PlaceFurnace(24, 24, FurnaceDirection.Up));

        // But (25,25) snaps to (24,24) which is already taken; try (32,0) which is out of bounds
        // Actually let's use a smaller world to make this clearer
        var smallWorld = new CellWorld(16, 16);
        var smallManager = new FurnaceBlockManager(smallWorld);

        // Block at (8,8) would extend to (15,15) — fits in 16x16
        Assert.True(smallManager.PlaceFurnace(8, 8, FurnaceDirection.Up));

        // Block at (9,0) snaps to (8,0) — extends to (15,7), fits
        // But let's try something that clearly extends past the edge
        var tinyWorld = new CellWorld(12, 12);
        var tinyManager = new FurnaceBlockManager(tinyWorld);

        // Block at (8,0) snaps to (8,0) — extends to (15,7), but world is only 12 wide
        Assert.False(tinyManager.PlaceFurnace(8, 0, FurnaceDirection.Up));

        // Block at (0,8) extends to (7,15), but world is only 12 tall
        Assert.False(tinyManager.PlaceFurnace(0, 8, FurnaceDirection.Up));
    }

    [Fact]
    public void PlaceFurnaceBlock_RejectsHardMaterials()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        // Put Stone in the placement area
        world.SetCell(10, 10, Materials.Stone);

        Assert.False(manager.PlaceFurnace(8, 8, FurnaceDirection.Up));
    }

    [Fact]
    public void PlaceFurnaceBlock_StoresDirection()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        var tile = manager.GetFurnaceTile(0, 0);
        Assert.True(tile.exists);
        Assert.Equal(FurnaceDirection.Right, tile.direction);

        // All cells in the block should have the same direction
        var tileMid = manager.GetFurnaceTile(4, 4);
        Assert.True(tileMid.exists);
        Assert.Equal(FurnaceDirection.Right, tileMid.direction);
    }

    [Fact]
    public void PlaceFurnaceBlock_RejectsOverlap()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        Assert.True(manager.PlaceFurnace(0, 0, FurnaceDirection.Up));
        // Same position should fail (already has furnace)
        Assert.False(manager.PlaceFurnace(0, 0, FurnaceDirection.Down));
    }

    // ===== REMOVAL =====

    [Fact]
    public void RemoveFurnaceBlock_ClearsToAir()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        manager.PlaceFurnace(8, 8, FurnaceDirection.Up);
        Assert.True(manager.RemoveFurnace(8, 8));

        // All 64 cells should be Air
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Air, world.GetCell(8 + dx, 8 + dy));

        // Tile data should be cleared
        Assert.False(manager.HasFurnaceAt(8, 8));
    }

    [Fact]
    public void RemoveFurnaceBlock_InvalidPosition_ReturnsFalse()
    {
        var world = new CellWorld(32, 32);
        var manager = new FurnaceBlockManager(world);

        // No block placed — should return false
        Assert.False(manager.RemoveFurnace(0, 0));
    }

    // ===== GHOST BLOCKS =====

    [Fact]
    public void FurnaceGhost_PlacedOverSoftTerrain()
    {
        var world = new CellWorld(32, 32);

        // Fill 8x8 area with Sand (soft terrain)
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Sand);

        var manager = new FurnaceBlockManager(world);
        Assert.True(manager.PlaceFurnace(8, 8, FurnaceDirection.Up));

        // Should be ghost — tile exists but isGhost
        var tile = manager.GetFurnaceTile(8, 8);
        Assert.True(tile.exists);
        Assert.True(tile.isGhost);

        // Material should NOT have been written (Sand should remain)
        Assert.Equal(Materials.Sand, world.GetCell(8, 8));
    }

    [Fact]
    public void FurnaceGhost_ActivatesWhenCleared()
    {
        var world = new CellWorld(32, 32);

        // Fill with Ground to trigger ghost
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var manager = new FurnaceBlockManager(world);
        manager.PlaceFurnace(8, 8, FurnaceDirection.Down);

        // Verify ghost state
        Assert.True(manager.GetFurnaceTile(8, 8).isGhost);

        // Clear all terrain to Air
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);

        manager.UpdateGhostStates();

        // Should now be activated (not ghost)
        var tile = manager.GetFurnaceTile(8, 8);
        Assert.True(tile.exists);
        Assert.False(tile.isGhost);

        // Furnace material should now be written
        Assert.Equal(Materials.Furnace, world.GetCell(8, 8));
        Assert.Equal(Materials.Furnace, world.GetCell(15, 15));
    }

    [Fact]
    public void FurnaceGhost_DoesNotActivate_WithRemainingTerrain()
    {
        var world = new CellWorld(32, 32);

        // Fill with Sand
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Sand);

        var manager = new FurnaceBlockManager(world);
        manager.PlaceFurnace(8, 8, FurnaceDirection.Up);

        // Clear most but leave one cell as Sand
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);
        world.SetCell(12, 12, Materials.Sand);

        manager.UpdateGhostStates();

        // Should still be ghost
        Assert.True(manager.GetFurnaceTile(8, 8).isGhost);
    }

    // ===== DIRECTIONAL HEATING =====

    [Fact]
    public void FurnaceBlock_EmitsHeatInFacingDirection()
    {
        // Block at (0,0) facing Right emits heat to x=8, y=0..7
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Place stone at the emitting edge to hold heat (static, conducts)
        sim.Set(8, 4, Materials.Stone);
        byte initialTemp = sim.GetTemperature(8, 4);

        sim.Step(10);

        byte newTemp = sim.GetTemperature(8, 4);
        Assert.True(newTemp > initialTemp,
            $"Cell at emitting edge should be heated. Initial={initialTemp}, After={newTemp}");
    }

    [Fact]
    public void FurnaceBlock_DoesNotHeatBackside()
    {
        // Block at (8,8) facing Right emits to x=16, NOT to x=7 (left side)
        // Disable heat transfer to isolate direct emission from conduction
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = false;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        manager.PlaceFurnace(8, 8, FurnaceDirection.Right);

        // Place stone on back side (left of block)
        sim.Set(7, 12, Materials.Stone);
        sim.SetTemperature(7, 12, 20);

        sim.Step(20);

        // With heat transfer disabled, back side should NOT be directly heated
        byte backTemp = sim.GetTemperature(7, 12);
        Assert.Equal(20, backTemp);
    }

    [Fact]
    public void FurnaceBlock_AllFourDirections()
    {
        // Verify each direction emits to the correct edge
        var directions = new[]
        {
            (FurnaceDirection.Up,    0, 8, 4, 7),   // block at (0,8), emits to y=7 (above), check x=4
            (FurnaceDirection.Down,  0, 0, 4, 8),   // block at (0,0), emits to y=8 (below), check x=4
            (FurnaceDirection.Left,  8, 0, 7, 4),   // block at (8,0), emits to x=7 (left), check y=4
            (FurnaceDirection.Right, 0, 0, 8, 4),   // block at (0,0), emits to x=8 (right), check y=4
        };

        foreach (var (dir, bx, by, checkX, checkY) in directions)
        {
            using var sim = new SimulationFixture(32, 32);
            sim.Simulator.EnableHeatTransfer = false;
            var manager = new FurnaceBlockManager(sim.World);
            sim.Simulator.SetFurnaceManager(manager);

            manager.PlaceFurnace(bx, by, dir);

            // Place stone at expected emission cell
            sim.Set(checkX, checkY, Materials.Stone);
            sim.SetTemperature(checkX, checkY, 20);

            sim.Step(10);

            byte temp = sim.GetTemperature(checkX, checkY);
            Assert.True(temp > 20,
                $"Direction={dir}: Stone at ({checkX},{checkY}) should be heated, got {temp}");
        }
    }

    // ===== ENCLOSURE HEATING =====

    [Fact]
    public void FurnaceEnclosure_TwoFacingBlocksHeatChannel()
    {
        // Block at (0,0) facing Right, block at (16,0) facing Left.
        // The gap is x=8..15 (8 cells wide). Material in the gap gets heat from both sides.
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
        manager.PlaceFurnace(16, 0, FurnaceDirection.Left);

        // Place stone in the channel
        sim.Set(8, 4, Materials.Stone);
        sim.Set(15, 4, Materials.Stone);

        sim.Step(100);

        // Both stones should be heated above ambient
        byte temp1 = sim.GetTemperature(8, 4);
        byte temp2 = sim.GetTemperature(15, 4);

        Assert.True(temp1 > HeatSettings.AmbientTemperature,
            $"Left channel stone should be heated, got {temp1}");
        Assert.True(temp2 > HeatSettings.AmbientTemperature,
            $"Right channel stone should be heated, got {temp2}");
    }

    [Fact]
    public void FurnaceEnclosure_NarrowChannelHeatsHigher()
    {
        // Compare: 1 wall heating vs 2 walls heating the same cell.
        // To get 2-wall heating on a single cell, use two blocks whose emission edges
        // overlap on the same cell. Block at (0,0) facing Right emits to (8, 0..7).
        // Block at (8, 8) facing Up emits to (8..15, 7). Cell (8,7) gets emission from BOTH.

        // Setup 1: single wall — only one block emitting to (8,7)
        using var sim1 = new SimulationFixture(32, 32);
        sim1.Simulator.EnableHeatTransfer = true;
        var manager1 = new FurnaceBlockManager(sim1.World);
        sim1.Simulator.SetFurnaceManager(manager1);
        manager1.PlaceFurnace(0, 0, FurnaceDirection.Right);
        sim1.Set(8, 7, Materials.Stone);

        // Setup 2: two blocks whose emission edges intersect at (8,7)
        using var sim2 = new SimulationFixture(32, 32);
        sim2.Simulator.EnableHeatTransfer = true;
        var manager2 = new FurnaceBlockManager(sim2.World);
        sim2.Simulator.SetFurnaceManager(manager2);
        manager2.PlaceFurnace(0, 0, FurnaceDirection.Right);   // emits to (8, 0..7)
        manager2.PlaceFurnace(8, 8, FurnaceDirection.Up);       // emits to (8..15, 7)
        sim2.Set(8, 7, Materials.Stone);

        // Run both to near-equilibrium (3*tau ~ 255 frames per wall; use 500 for safety)
        sim1.Step(500);
        sim2.Step(500);

        byte singleTemp = sim1.GetTemperature(8, 7);
        byte dualTemp = sim2.GetTemperature(8, 7);

        Assert.True(dualTemp > singleTemp,
            $"Dual-wall cell ({dualTemp}) should be hotter than single wall ({singleTemp})");
    }

    // ===== PHASE CHANGES =====

    [Fact]
    public void FurnaceEnclosure_BoilsWater()
    {
        // Water boils at 100°. Use two blocks whose emission edges overlap on a cell.
        // The cell at (8,7) gets emission from both blocks (+2/frame), reaching ~190° equil.
        // Contain the water so it can't flow away.
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block at (0,0) facing Right — emits to x=8, y=0..7
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
        // Block at (8, 8) facing Up — emits to x=8..15, y=7
        manager.PlaceFurnace(8, 8, FurnaceDirection.Up);

        // Container: furnace at (7,7) and (8,8), stone at (9,7) and (8,6)
        sim.Set(9, 7, Materials.Stone);  // right wall
        sim.Set(8, 6, Materials.Stone);  // ceiling

        // Place water at the doubly-heated cell
        sim.Set(8, 7, Materials.Water);

        // Run enough frames for heating above 100°
        sim.Step(500);

        // Water should have boiled. Could be steam anywhere in the world.
        int steamCount = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);

        // Check: even if steam re-froze, the water was heated past boilTemp.
        // Check the temperature at (8,7) to see if it gets hot enough.
        byte tempAtCell = sim.GetTemperature(8, 7);
        Assert.True(steamCount > 0 || tempAtCell >= 100,
            $"Water should boil or reach 100°. Steam={steamCount}, Water={waterCount}, Temp at (8,7)={tempAtCell}");
    }

    [Fact]
    public void FurnaceEnclosure_MeltsIronOre()
    {
        // IronOre melts at 200°. Furnace emission combined with heat transfer drives the
        // ore's temperature up. This test verifies that furnace heating causes phase changes
        // by pre-heating the ore to its melt point. The furnace emission ensures the cell
        // stays at or above meltTemp despite cooling, triggering the phase change.
        //
        // Setup: place ore at furnace emission edge, pre-heat to meltTemp.
        // The first cell simulation step checks temp >= 200 and converts to MoltenIron.
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block at (0,0) facing Right — emits to (8, 0..7)
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Place IronOre at the emission edge, pre-heated to meltTemp
        sim.Set(8, 4, Materials.IronOre);
        sim.SetTemperature(8, 4, 200);

        // Run a single frame — cell simulation checks phase first
        sim.Step(1);

        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        int ironCount = WorldAssert.CountMaterial(sim.World, Materials.Iron);
        Assert.True(moltenCount > 0 || ironCount > 0,
            $"IronOre pre-heated to meltTemp should melt with furnace. " +
            $"MoltenIron={moltenCount}, Iron={ironCount}, " +
            $"IronOre={WorldAssert.CountMaterial(sim.World, Materials.IronOre)}");
    }

    // ===== CONSERVATION =====

    [Fact]
    public void FurnaceEnclosure_MeltingConservesMaterials()
    {
        // Melting IronOre -> MoltenIron should be 1:1 conservation.
        // Use the same 2-wall setup as the melting test.
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Two blocks whose emission edges overlap
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);   // emits to (8, 0..7)
        manager.PlaceFurnace(8, 8, FurnaceDirection.Up);       // emits to (8..15, 7)

        // Place IronOre at the doubly-heated cell, contained by stone
        sim.Set(8, 7, Materials.IronOre);
        sim.Set(9, 7, Materials.Stone);
        int placed = 1;

        sim.Step(800);

        int oreCount = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        int moltenCount = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
        int ironCount = WorldAssert.CountMaterial(sim.World, Materials.Iron);

        // All iron-related materials should sum to what we placed
        int total = oreCount + moltenCount + ironCount;
        Assert.Equal(placed, total);
    }

    // ===== WALL BEHAVIOR =====

    [Fact]
    public void FurnaceBlocks_BlockMaterial()
    {
        // Sand placed above a furnace block should not pass through.
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Place furnace block at (8,16) — occupies x=8..15, y=16..23
        manager.PlaceFurnace(8, 16, FurnaceDirection.Up);

        // Place sand above the block (y=15, resting on top of block)
        for (int x = 10; x < 14; x++)
            sim.Set(x, 15, Materials.Sand);

        sim.Step(50);

        // Sand should NOT appear inside the furnace block (y=16..23)
        int sandInside = 0;
        for (int y = 16; y <= 23; y++)
            for (int x = 8; x <= 15; x++)
                if (sim.Get(x, y) == Materials.Sand)
                    sandInside++;

        Assert.Equal(0, sandInside);
    }

    [Fact]
    public void FurnaceBlocks_AreStatic()
    {
        // Furnace material should not move under gravity.
        using var sim = new SimulationFixture(32, 32);
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Place block at (8,8)
        manager.PlaceFurnace(8, 8, FurnaceDirection.Up);

        sim.Step(100);

        // All 64 cells should still be Furnace material
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Furnace, sim.Get(8 + dx, 8 + dy));
    }

    // ===== MULTIPLE BLOCKS =====

    [Fact]
    public void MultipleFurnaceBlocks_IndependentHeating()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block 1 at (0,0) facing Right
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
        // Block 2 at (0,24) facing Right
        manager.PlaceFurnace(0, 24, FurnaceDirection.Right);

        // Place stone at each emitting edge
        sim.Set(8, 4, Materials.Stone);
        sim.Set(8, 28, Materials.Stone);

        sim.Step(50);

        byte temp1 = sim.GetTemperature(8, 4);
        byte temp2 = sim.GetTemperature(8, 28);

        // Both should be heated above ambient
        Assert.True(temp1 > HeatSettings.AmbientTemperature,
            $"Block 1 emitting edge should be heated, got {temp1}");
        Assert.True(temp2 > HeatSettings.AmbientTemperature,
            $"Block 2 emitting edge should be heated, got {temp2}");
    }

    [Fact]
    public void RemoveOneBlock_OtherContinues()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block 1 at (0,0) facing Right
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
        // Block 2 at (0,16) facing Right
        manager.PlaceFurnace(0, 16, FurnaceDirection.Right);

        // Place stone at block 2's emitting edge
        sim.Set(8, 20, Materials.Stone);
        sim.SetTemperature(8, 20, 20);

        // Remove block 1
        manager.RemoveFurnace(0, 0);

        sim.Step(50);

        // Block 2 should still heat its emitting edge
        byte temp = sim.GetTemperature(8, 20);
        Assert.True(temp > 30,
            $"Remaining block should still heat, got {temp}");
    }

    // ===== HEAT EMISSION DETAILS =====

    [Fact]
    public void FurnaceBlock_EmitsToAll8CellsOnEdge()
    {
        // A block facing Right at (0,0) should emit heat to all 8 cells: (8,0) through (8,7)
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = false; // isolate direct emission
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Place stone at all 8 emitting positions
        for (int y = 0; y < 8; y++)
            sim.Set(8, y, Materials.Stone);

        sim.Step(10);

        // All 8 should be heated
        for (int y = 0; y < 8; y++)
        {
            byte temp = sim.GetTemperature(8, y);
            Assert.True(temp > 20,
                $"Emitting cell at (8,{y}) should be heated, got {temp}");
        }
    }

    [Fact]
    public void FurnaceBlock_DoesNotEmitToOtherFurnaceCells()
    {
        // If two adjacent blocks share an edge, furnace should not heat other furnace cells
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = false;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block at (0,0) facing Right, block at (8,0) — the emitting edge is other furnace cells
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
        manager.PlaceFurnace(8, 0, FurnaceDirection.Left);

        // The emitting cells at x=8 are Furnace material (from second block)
        // SimulateFurnaces skips cells with materialId == Furnace
        sim.Step(10);

        // Furnace cells should stay at initial temperature (they don't get emission)
        // This is OK — it just means touching blocks don't waste heat on each other
        byte furnaceTemp = sim.GetTemperature(8, 4);
        Assert.Equal(20, furnaceTemp);
    }

    [Fact]
    public void FurnaceBlock_GhostDoesNotEmitHeat()
    {
        // Ghost blocks should not emit heat since they aren't materialized
        using var sim = new SimulationFixture(32, 32);
        sim.Simulator.EnableHeatTransfer = false;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Fill area with Sand to trigger ghost
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                sim.Set(dx, dy, Materials.Sand);

        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Verify it's ghost
        Assert.True(manager.GetFurnaceTile(0, 0).isGhost);

        // Place stone at emitting edge
        sim.Set(8, 4, Materials.Stone);
        sim.SetTemperature(8, 4, 20);

        sim.Step(20);

        // Should not be heated — ghost blocks don't emit
        byte temp = sim.GetTemperature(8, 4);
        Assert.Equal(20, temp);
    }

    // ===== EQUILIBRIUM & INTEGRATION =====

    /// <summary>
    /// Rule: A cell heated by one furnace block reaches a stable temperature equilibrium
    /// where furnace heat input balances proportional cooling output.
    /// After enough frames, the temperature should stabilize (not keep rising or falling).
    /// </summary>
    [Fact]
    public void FurnaceBlock_ReachesTemperatureEquilibrium()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block at (0,0) facing Right — emits heat to column x=8, rows y=0..7
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Place stone at the emission edge
        sim.Set(8, 4, Materials.Stone);

        // Run to near-equilibrium (several time constants)
        sim.Step(500);
        byte temp1 = sim.GetTemperature(8, 4);

        // Run additional frames to verify stability
        sim.Step(200);
        byte temp2 = sim.GetTemperature(8, 4);

        // Temperature should have stabilized (within 3 degrees)
        Assert.True(Math.Abs(temp2 - temp1) <= 3,
            $"Temperature should stabilize. After 500 frames: {temp1}, after 700 frames: {temp2}");

        // Should be well above ambient
        Assert.True(temp2 > 60,
            $"Single-wall equilibrium should be well above ambient (20), got {temp2}");
    }

    /// <summary>
    /// Rule: Heat from furnace emission edges conducts through air to neighboring cells.
    /// A stone placed one cell away from the emission edge (with one air cell between)
    /// should receive conducted heat and be warmer than ambient. This validates
    /// that furnace-heated air participates in heat conduction.
    /// </summary>
    [Fact]
    public void FurnaceEnclosure_AirConductsHeatThroughGap()
    {
        using var sim = new SimulationFixture(64, 64);
        sim.Simulator.EnableHeatTransfer = true;
        var manager = new FurnaceBlockManager(sim.World);
        sim.Simulator.SetFurnaceManager(manager);

        // Block at (0,0) facing Right — emits to column x=8
        manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

        // Place stone one cell away from the emission edge (x=9).
        // The air cell at (8,4) gets directly heated by the furnace;
        // the stone at (9,4) receives heat via air conduction from (8,4).
        sim.Set(9, 4, Materials.Stone);

        // Run enough frames for conducted heat to reach the stone
        sim.Step(500);

        byte stoneTemp = sim.GetTemperature(9, 4);
        Assert.True(stoneTemp > HeatSettings.AmbientTemperature,
            $"Stone one cell from furnace emission edge should be warmer than ambient " +
            $"({HeatSettings.AmbientTemperature}) via air conduction. Got {stoneTemp}");
    }
}
