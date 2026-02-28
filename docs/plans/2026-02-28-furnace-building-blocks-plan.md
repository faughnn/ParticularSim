# Furnace Building Blocks Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace rectangular furnace structures with 8x8 building blocks, add per-material heat conduction (including air), and proportional cooling for emergent furnace gameplay.

**Architecture:** Three independent changes compose: (1) heat system gets per-material conduction rates + proportional cooling, (2) furnace becomes a WallManager-style block placer with directional heat emission, (3) old rectangular furnace code is deleted. Changes are layered — each task builds on the previous.

**Tech Stack:** C# / .NET, xUnit tests, existing SimulationFixture helpers

**Design doc:** `docs/plans/2026-02-28-furnace-building-blocks-design.md`

---

### Task 1: Add conductionRate to MaterialDef

Add a per-material conduction rate field so different materials conduct heat at different speeds.

**Files:**
- Modify: `src/ParticularLLM/Core/MaterialDef.cs:24-44`
- Modify: `src/ParticularLLM/Core/Materials.cs:75+` (all material definitions)

**Step 1: Add field to MaterialDef**

In `MaterialDef.cs`, add after the `restitution` field:

```csharp
public byte conductionRate;  // Heat conduction speed (0-255). Used as conductionRate/256 blend factor. 0 = no conduction even if ConductsHeat set.
```

**Step 2: Set conduction rates in Materials.cs**

Add `conductionRate` to every material definition that has `ConductsHeat`. Also add `ConductsHeat` flag and a low rate to Air:

| Material | conductionRate | Notes |
|----------|---------------|-------|
| Air | 8 | Slow conductor (newly added ConductsHeat flag) |
| Stone | 64 | Good conductor (same as old global rate) |
| Water | 48 | Good conductor |
| Steam | 32 | Moderate |
| IronOre | 48 | Moderate |
| MoltenIron | 64 | Excellent (liquid metal) |
| Iron | 64 | Excellent |
| Coal | 32 | Moderate |
| Ground | 32 | Moderate |
| Furnace | 64 | Good conductor |

Air's material definition changes from:
```csharp
defs[Air] = new MaterialDef
{
    density = 0, stability = 0,
    behaviour = BehaviourType.Static, flags = MaterialFlags.None,
    baseColour = new Color32(20, 20, 30, 255), colourVariation = 0,
};
```
to:
```csharp
defs[Air] = new MaterialDef
{
    density = 0, stability = 0,
    behaviour = BehaviourType.Static, flags = MaterialFlags.ConductsHeat,
    baseColour = new Color32(20, 20, 30, 255), colourVariation = 0,
    conductionRate = 8,
};
```

For all other conducting materials, add the `conductionRate` field to their existing definitions. Non-conducting materials (Sand, Oil, Ash, Smoke, Belt variants, Dirt, Lift, Wall, Piston variants) keep `conductionRate = 0` (default).

**Step 3: Verify compilation**

Run: `dotnet build`
Expected: success (field added, values set, no behavior change yet)

**Step 4: Commit**

```
feat: add conductionRate field to MaterialDef
```

---

### Task 2: Per-material conduction in HeatTransferSystem

Update the heat transfer loop to use each material's `conductionRate` instead of the global `HeatSettings.ConductionRate`.

**Files:**
- Modify: `src/ParticularLLM/World/HeatTransferSystem.cs:19-97`
- Modify: `tests/ParticularLLM.Tests/SimulationTests/HeatTransferTests.cs`

**Step 1: Write failing test — air conducts heat**

Add to `HeatTransferTests.cs`:

```csharp
[Fact]
public void Air_ConductsHeatSlowly()
{
    // Air now has ConductsHeat with low conductionRate=8.
    // A hot stone cell adjacent to air should lose some heat into the air.
    using var sim = new SimulationFixture(16, 16);
    sim.Simulator.EnableHeatTransfer = true;
    sim.Set(8, 8, Materials.Stone);
    sim.SetTemperature(8, 8, 200);
    // Cell (9, 8) is Air — should now conduct

    sim.Step(20);

    // Air cell should have warmed above ambient
    byte airTemp = sim.GetTemperature(9, 8);
    Assert.True(airTemp > HeatSettings.AmbientTemperature,
        $"Air should conduct heat slowly, temp={airTemp}");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "Air_ConductsHeatSlowly"`
Expected: FAIL (air currently doesn't conduct because HeatTransferSystem checks ConductsHeat flag, which Air now has, but still uses global ConductionRate)

Actually — Air now HAS the ConductsHeat flag (from Task 1), so the existing code will already conduct heat through Air at the global rate of 64 (25%). The test might actually pass. If so, the failing behavior is that it conducts TOO FAST. Either way, proceed to implement per-material rates.

**Step 3: Update HeatTransferSystem to use per-material conductionRate**

In `SimulateHeat`, change line 63-64 from:
```csharp
int avgTemp = totalTemp / conductingNeighbors;
int newTemp = cell.temperature +
    (avgTemp - cell.temperature) * HeatSettings.ConductionRate / 256;
```
to:
```csharp
int avgTemp = totalTemp / conductingNeighbors;
int newTemp = cell.temperature +
    (avgTemp - cell.temperature) * mat.conductionRate / 256;
```

This uses the material's own conduction rate instead of the global constant.

Also update `AddNeighborTemp` to use the NEIGHBOR's conduction rate for weighted averaging. The simplest correct approach: keep AddNeighborTemp as-is (it just checks ConductsHeat flag), but use the current cell's rate for blending. This means each material blends toward its neighbor average at its own rate — physically correct (the material determines how fast IT conducts, not how fast its neighbors conduct).

**Step 4: Update the exact-value test**

The test `ConductionRate_25PercentBlend` (line 267) expects exact value 177 based on global rate 64. With per-material rates, Stone has `conductionRate=64` (same value), so this test should still pass. Verify.

If Stone's rate is 64 and the formula is `cell.temperature + (avgTemp - cell.temperature) * mat.conductionRate / 256`, the math is identical to before for Stone-to-Stone conduction.

**Step 5: Write test for different conduction rates**

```csharp
[Fact]
public void PerMaterialConduction_AirConductsSlowerThanStone()
{
    // Air (rate=8) should conduct heat much slower than Stone (rate=64).
    using var sim = new SimulationFixture(32, 16);
    sim.Simulator.EnableHeatTransfer = true;

    // Setup 1: Stone chain (fast conduction)
    sim.Set(4, 8, Materials.Stone);
    sim.Set(5, 8, Materials.Stone);
    sim.Set(6, 8, Materials.Stone);
    sim.SetTemperature(4, 8, 200);

    // Setup 2: Stone-Air-Stone chain (slow conduction through air)
    sim.Set(14, 8, Materials.Stone);
    // (15, 8) is Air — conducts slowly
    sim.Set(16, 8, Materials.Stone);
    sim.SetTemperature(14, 8, 200);

    sim.Step(10);

    // Stone at end of stone chain should be warmer than stone at end of air chain
    byte stoneFar = sim.GetTemperature(6, 8);
    byte airFar = sim.GetTemperature(16, 8);
    Assert.True(stoneFar > airFar,
        $"Stone chain far end ({stoneFar}) should be warmer than air chain ({airFar})");
}
```

**Step 6: Run all heat transfer tests**

Run: `dotnet test --filter "HeatTransferTests"`
Expected: all pass. Key tests to watch:
- `ConductionRate_25PercentBlend` — should still pass (Stone rate = 64 = old global)
- `NonConductor_KeepsTemperature` — Sand still non-conducting, passes
- `NonConductor_BlocksHeatSpread` — Wall still non-conducting, passes
- `Air_ConductsHeatSlowly` — Air now conducts, passes
- `PerMaterialConduction_AirConductsSlowerThanStone` — verifies rates differ, passes

**Step 7: Commit**

```
feat: per-material heat conduction rates, air conducts heat slowly
```

---

### Task 3: Proportional cooling with accumulator

Replace flat cooling (1°/frame) with proportional cooling (hotter cells cool faster). Uses a ushort accumulator array for sub-integer precision with byte temperatures.

**Files:**
- Modify: `src/ParticularLLM/World/HeatTransferSystem.cs`
- Modify: `src/ParticularLLM/Core/HeatSettings.cs`
- Modify: `tests/ParticularLLM.Tests/SimulationTests/HeatTransferTests.cs`

**Step 1: Write failing test — proportional cooling**

```csharp
[Fact]
public void ProportionalCooling_HotterCoolsFaster()
{
    // A cell at 200° should cool faster per frame than a cell at 50°.
    using var sim = new SimulationFixture(16, 16);
    sim.Simulator.EnableHeatTransfer = true;

    sim.Set(4, 8, Materials.Stone);
    sim.Set(12, 8, Materials.Stone);
    sim.SetTemperature(4, 8, 200);
    sim.SetTemperature(12, 8, 50);

    sim.Step(1);

    byte hotDrop = (byte)(200 - sim.GetTemperature(4, 8));
    byte coldDrop = (byte)(50 - sim.GetTemperature(12, 8));

    Assert.True(hotDrop > coldDrop,
        $"Hot cell should cool more ({hotDrop}) than cold cell ({coldDrop})");
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "ProportionalCooling_HotterCoolsFaster"`
Expected: FAIL (both cool by exactly 1°/frame currently)

**Step 3: Add coolingFactor to HeatSettings**

In `HeatSettings.cs`, replace `CoolingRate`:
```csharp
public static class HeatSettings
{
    public const byte AmbientTemperature = 20;

    /// <summary>
    /// Proportional cooling factor. Cooling per frame = (temp - ambient) * CoolingFactor / 256.
    /// Uses ushort accumulator for sub-integer precision.
    /// Value of 3 gives: 1 wall equilibrium ~105°, 2 walls ~190°, 3 walls ~255°.
    /// </summary>
    public const int CoolingFactor = 3;
}
```

Remove the old `CoolingRate` and `ConductionRate` constants (ConductionRate is now per-material, CoolingRate replaced by CoolingFactor).

**Step 4: Implement proportional cooling with accumulator**

In `HeatTransferSystem.cs`, add a cooling accumulator array:

```csharp
private ushort[] coolingAccum = Array.Empty<ushort>();
```

In `SimulateHeat`, allocate it alongside tempBuffer:
```csharp
if (coolingAccum.Length < totalCells)
    coolingAccum = new ushort[totalCells];
```

Replace the flat cooling block (lines 67-72):
```csharp
// Cool toward ambient
if (newTemp > HeatSettings.AmbientTemperature)
    newTemp = Math.Max(HeatSettings.AmbientTemperature,
        newTemp - HeatSettings.CoolingRate);
else if (newTemp < HeatSettings.AmbientTemperature)
    newTemp = Math.Min(HeatSettings.AmbientTemperature,
        newTemp + HeatSettings.CoolingRate);
```

With proportional cooling using accumulator:
```csharp
// Proportional cooling toward ambient (Newton's law)
if (newTemp > HeatSettings.AmbientTemperature)
{
    int diff = newTemp - HeatSettings.AmbientTemperature;
    coolingAccum[idx] += (ushort)(diff * HeatSettings.CoolingFactor);
    int degrees = coolingAccum[idx] / 256;
    if (degrees > 0)
    {
        coolingAccum[idx] -= (ushort)(degrees * 256);
        newTemp = Math.Max(HeatSettings.AmbientTemperature, newTemp - degrees);
    }
}
else if (newTemp < HeatSettings.AmbientTemperature)
{
    int diff = HeatSettings.AmbientTemperature - newTemp;
    coolingAccum[idx] += (ushort)(diff * HeatSettings.CoolingFactor);
    int degrees = coolingAccum[idx] / 256;
    if (degrees > 0)
    {
        coolingAccum[idx] -= (ushort)(degrees * 256);
        newTemp = Math.Min(HeatSettings.AmbientTemperature, newTemp + degrees);
    }
}
```

**Step 5: Update affected tests**

Tests that expect exact cooling values need adjustment:

1. `ConductionRate_25PercentBlend` — Previously expected exactly 177. Recalculate:
   - Conduction: 200 + (110-200)*64/256 = 200 - 22 = 178
   - Proportional cooling: (178-20)*3 = 474 → 474/256 = 1 degree, remainder 218
   - Result: 178 - 1 = 177. Same answer! Test passes.

2. `HotConductor_EventuallyReachesAmbient` — 500 frames may not be enough with proportional cooling (last degree takes ~85 frames). Increase to 1000 frames.

3. `AdjacentConductors_Equilibrate_OverTime` — Same issue. Increase to 1000 frames.

**Step 6: Run all heat transfer tests**

Run: `dotnet test --filter "HeatTransferTests"`
Expected: all pass

**Step 7: Run all tests to check for regressions**

Run: `dotnet test`
Expected: all pass (furnace tests may need attention if cooling behavior changed enough to affect temperature assertions)

**Step 8: Commit**

```
feat: proportional cooling (Newton's law) with accumulator precision
```

---

### Task 4: Create FurnaceBlockTile and FurnaceBlockManager

Replace the rectangular FurnaceManager with a WallManager-style block system. Furnace blocks are solid 8x8 blocks (like walls) that emit heat directionally.

**Files:**
- Create: `src/ParticularLLM/Structures/FurnaceBlockTile.cs`
- Create: `src/ParticularLLM/Structures/FurnaceBlockManager.cs`

**Step 1: Write failing tests for furnace block placement**

Create new test file or add to existing. Tests will fail because FurnaceBlockManager doesn't exist yet:

```csharp
[Fact]
public void PlaceFurnaceBlock_Creates8x8SolidBlock()
{
    using var sim = new SimulationFixture(32, 32);
    var manager = new FurnaceBlockManager(sim.World);

    bool placed = manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
    Assert.True(placed);

    // All 64 cells should be Furnace material
    for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            Assert.Equal(Materials.Furnace, sim.Get(x, y));
}

[Fact]
public void PlaceFurnaceBlock_SnapsToGrid()
{
    using var sim = new SimulationFixture(32, 32);
    var manager = new FurnaceBlockManager(sim.World);

    manager.PlaceFurnace(3, 5, FurnaceDirection.Right);

    // Should snap to (0, 0)
    Assert.True(manager.HasFurnaceAt(0, 0));
    Assert.True(manager.HasFurnaceAt(7, 7));
    Assert.False(manager.HasFurnaceAt(8, 0));
}
```

**Step 2: Create FurnaceBlockTile.cs**

```csharp
namespace ParticularLLM;

public enum FurnaceDirection : byte
{
    Up = 0,
    Right = 1,
    Down = 2,
    Left = 3,
}

public struct FurnaceBlockTile
{
    public bool exists;
    public bool isGhost;
    public FurnaceDirection direction;
}
```

**Step 3: Create FurnaceBlockManager.cs**

Follow `WallManager.cs` exactly, with these differences:
- `PlaceFurnace(int x, int y, FurnaceDirection direction)` — takes direction parameter
- Material written is `Materials.Furnace` instead of `Materials.Wall`
- Tile stores `direction`
- `SimulateFurnaces(CellWorld world, int currentFrame)` method for directional heat emission (implemented in Task 5)

The placement, removal, ghost, HasStructureAt, and chunk marking logic is identical to WallManager. Copy the pattern directly.

Key methods:
- `PlaceFurnace(int x, int y, FurnaceDirection direction)` → bool
- `RemoveFurnace(int x, int y)` → bool
- `HasFurnaceAt(int x, int y)` → bool
- `HasStructureAt(int x, int y)` → bool (IStructureManager)
- `GetFurnaceTile(int x, int y)` → FurnaceBlockTile
- `UpdateGhostStates()` (IStructureManager)
- `GetGhostBlockPositions(List<(int x, int y)>)` (IStructureManager)
- `SimulateFurnaces(CellWorld world, int currentFrame)` — stub for now, implemented in Task 5

**Step 4: Run placement tests**

Run: `dotnet test --filter "PlaceFurnaceBlock"`
Expected: pass

**Step 5: Write and verify removal tests**

```csharp
[Fact]
public void RemoveFurnaceBlock_ClearsToAir()
{
    using var sim = new SimulationFixture(32, 32);
    var manager = new FurnaceBlockManager(sim.World);
    manager.PlaceFurnace(0, 0, FurnaceDirection.Right);

    Assert.True(manager.RemoveFurnace(0, 0));

    for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            Assert.Equal(Materials.Air, sim.Get(x, y));

    Assert.False(manager.HasFurnaceAt(0, 0));
}
```

**Step 6: Write ghost block tests**

```csharp
[Fact]
public void FurnaceGhost_PlacedOverSoftTerrain()
{
    using var sim = new SimulationFixture(32, 32);
    var manager = new FurnaceBlockManager(sim.World);

    // Place sand in the area
    sim.Set(3, 3, Materials.Sand);

    bool placed = manager.PlaceFurnace(0, 0, FurnaceDirection.Right);
    Assert.True(placed);

    // Should be ghost — sand still there, no furnace material written
    Assert.Equal(Materials.Sand, sim.Get(3, 3));
    var tile = manager.GetFurnaceTile(0, 0);
    Assert.True(tile.exists);
    Assert.True(tile.isGhost);
}
```

**Step 7: Commit**

```
feat: add FurnaceBlockTile and FurnaceBlockManager (placement, removal, ghost)
```

---

### Task 5: Directional heat emission

Furnace blocks emit heat to the adjacent cell in their facing direction. Uses frame-skip for pacing (emit every N frames).

**Files:**
- Modify: `src/ParticularLLM/Structures/FurnaceBlockManager.cs`
- Modify: `src/ParticularLLM/Core/HeatSettings.cs`
- New tests in furnace test file

**Step 1: Add heat emission constants to HeatSettings**

```csharp
/// <summary>Degrees added per emission pulse to the cell adjacent to a furnace block's facing direction.</summary>
public const int FurnaceHeatOutput = 1;

/// <summary>Frames between furnace heat emission pulses. Higher = slower heating.</summary>
public const int FurnaceHeatInterval = 1;
```

Start with interval=1 (every frame) for easier testing. Tune later for 1-2 minute pacing.

**Step 2: Write failing test — directional emission**

```csharp
[Fact]
public void FurnaceBlock_EmitsHeatInFacingDirection()
{
    using var sim = new SimulationFixture(32, 32);
    sim.Simulator.EnableHeatTransfer = true;
    var manager = new FurnaceBlockManager(sim.World);
    sim.Simulator.SetFurnaceManager(manager);

    // Place furnace block at (8,8) facing Right
    manager.PlaceFurnace(8, 8, FurnaceDirection.Right);

    // Place stone to the right of the furnace block (x=16, adjacent to right edge)
    sim.Set(16, 12, Materials.Stone);
    // Place stone to the left (x=7, adjacent to left edge)
    sim.Set(7, 12, Materials.Stone);

    sim.Step(10);

    byte rightTemp = sim.GetTemperature(16, 12);
    byte leftTemp = sim.GetTemperature(7, 12);

    // Right side (facing direction) should be heated
    Assert.True(rightTemp > HeatSettings.AmbientTemperature,
        $"Cell in facing direction should be heated, got {rightTemp}");
    // Left side should NOT be directly heated (only via conduction through furnace material)
    Assert.True(rightTemp > leftTemp,
        $"Facing side ({rightTemp}) should be hotter than back ({leftTemp})");
}
```

**Step 3: Implement SimulateFurnaces**

In `FurnaceBlockManager.cs`, implement `SimulateFurnaces(CellWorld world, int currentFrame)`:

The logic iterates all furnace block origins. For each block:
1. Check frame-skip: `currentFrame % HeatSettings.FurnaceHeatInterval != 0` → skip
2. Get the block's direction from any tile in the block
3. Determine the "emitting edge" — the row or column of cells on the facing side
4. For each cell on the emitting edge, find the adjacent cell in the facing direction (outside the block)
5. If that adjacent cell exists and is not a furnace cell, add `FurnaceHeatOutput` to its temperature

For a block at `(gridX, gridY)` facing Right:
- Emitting edge: x = gridX + BlockSize (i.e., x = gridX + 8), y from gridY to gridY + 7
- Each cell `(gridX+8, gridY+dy)` gets heated if it's in bounds and not furnace material

For Left: emitting edge x = gridX - 1
For Up: emitting edge y = gridY - 1
For Down: emitting edge y = gridY + BlockSize

Need to track block origins efficiently. Add a `HashSet<int> blockOrigins` (similar to `ghostBlockOrigins`) that tracks ALL placed blocks (not just ghosts). On place: add origin key. On remove: remove origin key.

```csharp
public void SimulateFurnaces(CellWorld world, int currentFrame)
{
    if (blockOrigins.Count == 0) return;
    if (currentFrame % HeatSettings.FurnaceHeatInterval != 0) return;

    foreach (int blockKey in blockOrigins)
    {
        int gridX = blockKey % width;
        int gridY = blockKey / width;

        // Skip ghost blocks (not materialized yet)
        if (furnaceTiles[blockKey].isGhost) continue;

        FurnaceDirection dir = furnaceTiles[blockKey].direction;

        // Determine emitting edge cells and offset
        int dx = 0, dy = 0;
        int edgeStartX, edgeStartY, edgeCount;
        bool horizontal; // true = edge runs along X, false = along Y

        switch (dir)
        {
            case FurnaceDirection.Right:
                dx = 1; edgeStartX = gridX + BlockSize; edgeStartY = gridY;
                edgeCount = BlockSize; horizontal = false;
                break;
            case FurnaceDirection.Left:
                dx = -1; edgeStartX = gridX - 1; edgeStartY = gridY;
                edgeCount = BlockSize; horizontal = false;
                break;
            case FurnaceDirection.Down:
                dy = 1; edgeStartX = gridX; edgeStartY = gridY + BlockSize;
                edgeCount = BlockSize; horizontal = true;
                break;
            case FurnaceDirection.Up:
                dy = -1; edgeStartX = gridX; edgeStartY = gridY - 1;
                edgeCount = BlockSize; horizontal = true;
                break;
            default: continue;
        }

        for (int i = 0; i < edgeCount; i++)
        {
            int cx = horizontal ? edgeStartX + i : edgeStartX;
            int cy = horizontal ? edgeStartY : edgeStartY + i;

            if (cx < 0 || cx >= width || cy < 0 || cy >= height) continue;

            int idx = cy * width + cx;
            Cell cell = world.cells[idx];

            // Don't heat other furnace cells
            if (cell.materialId == Materials.Furnace) continue;

            int newTemp = Math.Min(cell.temperature + HeatSettings.FurnaceHeatOutput, 255);
            cell.temperature = (byte)newTemp;
            world.cells[idx] = cell;
        }
    }
}
```

**Step 4: Run directional emission test**

Run: `dotnet test --filter "FurnaceBlock_EmitsHeatInFacingDirection"`
Expected: pass

**Step 5: Write enclosure test — multiple blocks create higher temperature**

```csharp
[Fact]
public void FurnaceEnclosure_NarrowChannelHeatsHigher()
{
    // Two furnace blocks facing each other (narrow channel) should heat
    // the space between them more than a single block.
    using var sim = new SimulationFixture(64, 64);
    sim.Simulator.EnableHeatTransfer = true;
    var manager = new FurnaceBlockManager(sim.World);
    sim.Simulator.SetFurnaceManager(manager);

    // Single block facing right at (8, 24)
    manager.PlaceFurnace(8, 24, FurnaceDirection.Right);
    sim.Set(16, 28, Materials.Stone);  // Stone adjacent to right edge

    // Two blocks facing each other at (8, 40) and (24, 40)
    manager.PlaceFurnace(8, 40, FurnaceDirection.Right);
    manager.PlaceFurnace(24, 40, FurnaceDirection.Left);
    sim.Set(16, 44, Materials.Stone);  // Stone in the channel between them

    sim.Step(200);

    byte singleTemp = sim.GetTemperature(16, 28);
    byte channelTemp = sim.GetTemperature(16, 44);

    Assert.True(channelTemp > singleTemp,
        $"Channel between two blocks ({channelTemp}) should be hotter than single block ({singleTemp})");
}
```

**Step 6: Commit**

```
feat: directional furnace heat emission with frame-skip pacing
```

---

### Task 6: Wire FurnaceBlockManager into CellSimulator and delete old code

**Files:**
- Modify: `src/ParticularLLM/World/CellSimulator.cs:18,36,114-116`
- Delete: `src/ParticularLLM/Structures/FurnaceManager.cs`
- Delete: `src/ParticularLLM/Structures/FurnaceStructure.cs`

**Step 1: Update CellSimulator**

Change line 18:
```csharp
private FurnaceManager? _furnaceManager;
```
to:
```csharp
private FurnaceBlockManager? _furnaceManager;
```

Change line 36:
```csharp
public void SetFurnaceManager(FurnaceManager manager) => _furnaceManager = manager;
```
to:
```csharp
public void SetFurnaceManager(FurnaceBlockManager manager) => _furnaceManager = manager;
```

Change lines 114-116 to pass currentFrame:
```csharp
if (_furnaceManager != null)
    _furnaceManager.SimulateFurnaces(world, world.currentFrame);
```

**Step 2: Delete old files**

Delete:
- `src/ParticularLLM/Structures/FurnaceManager.cs`
- `src/ParticularLLM/Structures/FurnaceStructure.cs`

Keep `FurnaceState` enum only if used elsewhere. If only used by FurnaceStructure, delete it too (the new system has no state — always on). Move `FurnaceDirection` enum to `FurnaceBlockTile.cs` if not already there.

**Step 3: Verify compilation**

Run: `dotnet build`
Expected: compilation errors in FurnaceTests.cs (references old API). That's expected — tests are rewritten in Task 7.

**Step 4: Commit (with tests temporarily broken)**

```
refactor: replace FurnaceManager with FurnaceBlockManager in CellSimulator
```

---

### Task 7: Rewrite FurnaceTests

Replace all tests to use the new FurnaceBlockManager API and block-based behavior.

**Files:**
- Rewrite: `tests/ParticularLLM.Tests/StructureTests/FurnaceTests.cs`

**Step 1: Rewrite all tests**

The test file should cover:

**Placement:**
- `PlaceFurnaceBlock_Creates8x8SolidBlock` — all 64 cells are Furnace material
- `PlaceFurnaceBlock_SnapsToGrid` — placement at (3,5) snaps to (0,0)
- `PlaceFurnaceBlock_RejectsOutOfBounds` — block extending past world edge
- `PlaceFurnaceBlock_RejectsHardMaterials` — stone in placement area
- `PlaceFurnaceBlock_StoresDirection` — tile has correct direction

**Removal:**
- `RemoveFurnaceBlock_ClearsToAir` — all 64 cells become Air
- `RemoveFurnaceBlock_InvalidPosition_ReturnsFalse` — no block at position

**Ghost blocks:**
- `FurnaceGhost_PlacedOverSoftTerrain` — sand triggers ghost
- `FurnaceGhost_ActivatesWhenCleared` — ghost becomes solid when terrain clears

**Directional heating:**
- `FurnaceBlock_EmitsHeatInFacingDirection` — heats adjacent cell in direction
- `FurnaceBlock_DoesNotHeatBackside` — back side not directly heated
- `FurnaceBlock_DirectionUp/Right/Down/Left` — verify each direction works

**Enclosure behavior:**
- `FurnaceEnclosure_NarrowChannelHeatsHigher` — two facing blocks > one block
- `FurnaceEnclosure_ThreeWallsReachHighTemperature` — corner configuration

**Phase changes (via heat emission + conduction):**
- `FurnaceEnclosure_MeltsIronOre` — iron ore in narrow channel reaches meltTemp
- `FurnaceEnclosure_BoilsWater` — water reaches boilTemp
- `FurnaceEnclosure_Conservation` — material count preserved through phase changes

**Wall behavior:**
- `FurnaceBlocks_BlockMaterial` — sand doesn't pass through furnace blocks
- `FurnaceBlocks_AreStatic` — blocks don't move under gravity
- `FurnaceBlocks_ConductHeat` — heat diffuses through furnace material

**Multiple blocks:**
- `MultipleFurnaceBlocks_IndependentHeating` — blocks at different positions both emit
- `RemoveOneBlock_OtherContinues` — removal doesn't affect other blocks

**Step 2: Run all tests**

Run: `dotnet test`
Expected: all pass including new furnace tests + existing heat transfer tests

**Step 3: Commit**

```
test: rewrite furnace tests for block-based system
```

---

### Task 8: Integration tests and tuning

End-to-end tests for enclosure scenarios and heat pacing.

**Files:**
- Add tests to: `tests/ParticularLLM.Tests/StructureTests/FurnaceTests.cs` or new integration test file
- Tune: `src/ParticularLLM/Core/HeatSettings.cs` constants

**Step 1: Enclosure heating scenario**

```csharp
[Fact]
public void FurnaceEnclosure_FullSmeltingScenario()
{
    // Build a U-shaped enclosure from furnace blocks, drop iron ore in,
    // verify it melts after sufficient heating time.
    using var sim = new SimulationFixture(64, 64);
    sim.Simulator.EnableHeatTransfer = true;
    var manager = new FurnaceBlockManager(sim.World);
    sim.Simulator.SetFurnaceManager(manager);

    // Left wall facing right
    manager.PlaceFurnace(8, 16, FurnaceDirection.Right);
    manager.PlaceFurnace(8, 24, FurnaceDirection.Right);
    // Right wall facing left
    manager.PlaceFurnace(24, 16, FurnaceDirection.Left);
    manager.PlaceFurnace(24, 24, FurnaceDirection.Left);
    // Floor facing up
    manager.PlaceFurnace(8, 32, FurnaceDirection.Up);
    manager.PlaceFurnace(16, 32, FurnaceDirection.Up);
    manager.PlaceFurnace(24, 32, FurnaceDirection.Up);

    // Place iron ore inside the enclosure (on the floor)
    sim.Set(18, 31, Materials.IronOre);

    // Run simulation — tune frame count based on HeatSettings
    int frames = HeatSettings.FurnaceHeatInterval * 200;
    sim.Step(frames);

    // Check for melting
    int molten = WorldAssert.CountMaterial(sim.World, Materials.MoltenIron);
    int iron = WorldAssert.CountMaterial(sim.World, Materials.Iron);
    int ore = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
    Assert.True(molten + iron > 0,
        $"Iron ore should melt in furnace enclosure. Ore={ore}, Molten={molten}, Iron={iron}");
}
```

**Step 2: Temperature equilibrium test**

```csharp
[Fact]
public void FurnaceEnclosure_ReachesEquilibrium()
{
    // A sealed enclosure should reach a temperature equilibrium where
    // furnace heat input equals proportional cooling.
    using var sim = new SimulationFixture(64, 64);
    sim.Simulator.EnableHeatTransfer = true;
    var manager = new FurnaceBlockManager(sim.World);
    sim.Simulator.SetFurnaceManager(manager);

    // Small sealed enclosure with stone inside
    manager.PlaceFurnace(8, 8, FurnaceDirection.Right);
    manager.PlaceFurnace(24, 8, FurnaceDirection.Left);
    manager.PlaceFurnace(8, 16, FurnaceDirection.Up);
    manager.PlaceFurnace(16, 16, FurnaceDirection.Up);
    manager.PlaceFurnace(24, 16, FurnaceDirection.Up);

    sim.Set(16, 12, Materials.Stone);  // Inside the enclosure

    // Run to equilibrium
    sim.Step(2000);
    byte temp1 = sim.GetTemperature(16, 12);

    sim.Step(500);
    byte temp2 = sim.GetTemperature(16, 12);

    // Temperature should have stabilized (within 2 degrees)
    int diff = Math.Abs(temp2 - temp1);
    Assert.True(diff <= 2,
        $"Temperature should stabilize. Was {temp1} after 2000 frames, {temp2} after 2500. Diff={diff}");
}
```

**Step 3: Tune heat constants**

Based on test results, adjust `HeatSettings`:
- `FurnaceHeatOutput` — degrees per emission pulse
- `FurnaceHeatInterval` — frames between pulses
- `CoolingFactor` — proportional cooling aggressiveness

Target: well-built enclosure reaches iron-melting temperature (200°) in 1-2 minutes (3600-7200 frames at 60fps).

The equilibrium temperature for a cell adjacent to N furnace-facing-edges:
```
Teq = AmbientTemperature + (N * FurnaceHeatOutput * 256) / (CoolingFactor * FurnaceHeatInterval)
```

For 2 walls at 190°: `190 = 20 + (2 * 1 * 256) / (CoolingFactor * Interval)`
→ `170 = 512 / (CoolingFactor * Interval)`
→ `CoolingFactor * Interval = 3.01`

With `CoolingFactor=3, Interval=1`: Teq = 20 + 512/3 = 190° ✓
Time constant τ = 256 / CoolingFactor = 85 frames = 1.4 seconds (too fast)

With `CoolingFactor=3, Interval=14`: Teq = 20 + 512/42 = 32° (too cold!)

The equilibrium and time-to-equilibrium can't be independently tuned with these three constants alone. Document this tradeoff and pick values that give interesting gameplay. The exact constants will be refined through playtesting.

**Step 4: Run full test suite**

Run: `dotnet test`
Expected: all pass

**Step 5: Commit**

```
feat: furnace enclosure integration tests and heat constant tuning
```

---

## Summary of All Changes

| File | Action | Description |
|------|--------|-------------|
| `MaterialDef.cs` | Modify | Add `conductionRate` field |
| `Materials.cs` | Modify | Set per-material conduction rates, Air gets ConductsHeat |
| `HeatSettings.cs` | Modify | Replace CoolingRate with CoolingFactor, add furnace constants |
| `HeatTransferSystem.cs` | Modify | Per-material conduction, proportional cooling with accumulator |
| `FurnaceBlockTile.cs` | Create | Tile struct with direction |
| `FurnaceBlockManager.cs` | Create | Block-based furnace manager (WallManager pattern + heat emission) |
| `CellSimulator.cs` | Modify | Swap FurnaceManager → FurnaceBlockManager |
| `FurnaceManager.cs` | Delete | Replaced by FurnaceBlockManager |
| `FurnaceStructure.cs` | Delete | No longer needed (includes FurnaceState enum) |
| `HeatTransferTests.cs` | Modify | Add air conduction + proportional cooling tests |
| `FurnaceTests.cs` | Rewrite | All tests adapted for block-based API |

## Known Limitations & Future Work

- **Equilibrium vs pacing tradeoff**: With 3 constants (heatOutput, interval, coolingFactor), equilibrium temperature and time-to-equilibrium can't be independently tuned. Higher-resolution temperatures (ushort) would fix this.
- **Dirty region optimization**: Heat transfer processes all cells. Could skip ambient-temperature regions for performance.
- **Fuel-powered heating**: Future evolution — furnace blocks become insulating only, heat comes from burning materials inside.
- **Temperature display**: No visual indicator of temperature in the test harness (would need viewer scenario).
