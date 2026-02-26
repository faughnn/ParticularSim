# Falling Sand Simulation Test Harness — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Port the deterministic cell simulation from the Unity project (G:\Sandy) into a standalone C# project at G:\ParticularLLM that can be tested without Unity, then validate correctness with xUnit tests.

**Architecture:** Direct port of the cell simulation kernel (SimulateChunksJob, SimulateBeltsJob), world state (CellWorld), and structure managers (Belt/Lift/Wall) from Unity into plain C#. Replace NativeArray<T> with T[], remove Burst/Jobs attributes, replace Unity.Mathematics.math with System.Math. Cluster physics are stubbed (data structure only, no Physics2D). Single-threaded simulation — no chunk groups needed for correctness, just iterate all active chunks sequentially.

**Tech Stack:** .NET 8, xUnit, C# 12

---

## Project Structure

```
G:\ParticularLLM\
  ParticularLLM.sln
  docs/plans/                          ← this plan
  src/
    ParticularLLM/                     ← simulation library
      ParticularLLM.csproj
      Core/
        Cell.cs                        ← copy from Unity, no changes
        CellFlags.cs                   ← extracted from Cell.cs
        ChunkState.cs                  ← copy from Unity, no changes
        ChunkFlags.cs                  ← extracted from ChunkState.cs
        MaterialDef.cs                 ← ported (remove Color32)
        Materials.cs                   ← extracted from MaterialDef.cs, remove Color32
        PhysicsSettings.cs             ← copy, remove GetUnityGravity
        WorldUtils.cs                  ← copy from Unity, no changes
      World/
        CellWorld.cs                   ← ported (NativeArray→array, NativeList→List)
        CellSimulator.cs              ← merged scheduler + job logic (single-threaded)
        SimulateChunksLogic.cs        ← ported from SimulateChunksJob.cs (plain methods)
        SimulateBeltsLogic.cs         ← ported from SimulateBeltsJob.cs (plain methods)
      Structures/
        StructureType.cs               ← copy from Unity
        BeltTile.cs                    ← copy from Unity
        LiftTile.cs                    ← copy from Unity
        WallTile.cs                    ← copy from Unity
        BeltStructure.cs               ← copy from Unity
        LiftStructure.cs               ← copy from Unity
        BeltManager.cs                 ← ported (NativeHashMap→Dictionary, remove cluster forces)
        LiftManager.cs                 ← ported (NativeArray→array, remove cluster forces)
        WallManager.cs                 ← ported (NativeArray→array)
        StructureUtils.cs              ← ported (remove Vector2Int, TerrainColliderManager)
        IStructureManager.cs           ← ported (remove Color, Vector2Int)
      Clusters/
        ClusterPixel.cs                ← copy from Unity
        ClusterData.cs                 ← stub: plain class, no MonoBehaviour/Rigidbody2D
  tests/
    ParticularLLM.Tests/
      ParticularLLM.Tests.csproj
      Helpers/
        SimulationFixture.cs           ← test helper: create world, step, assert
        WorldAssert.cs                 ← assertion helpers: CountMaterial, AssertCellAt, DumpRegion
      CoreTests/
        MaterialTests.cs
        WorldUtilsTests.cs
      SimulationTests/
        PowderTests.cs
        LiquidTests.cs
        GasTests.cs
        DensityDisplacementTests.cs
        DeterminismTests.cs
      StructureTests/
        BeltPlacementTests.cs
        BeltSimulationTests.cs
        LiftPlacementTests.cs
        LiftSimulationTests.cs
        WallPlacementTests.cs
        GhostActivationTests.cs
      IntegrationTests/
        BeltLiftComboTests.cs
        ScenarioTests.cs
```

---

## Porting Rules (apply to every file)

These rules apply throughout all tasks. Do NOT deviate from them:

1. **Namespace:** Change `namespace FallingSand` → `namespace ParticularLLM`
2. **NativeArray<T>** → `T[]`
3. **NativeList<T>** → `List<T>`
4. **NativeHashMap<K,V>** → `Dictionary<K,V>`
5. **NativeHashSet<T>** → `HashSet<T>`
6. **Remove** all `using Unity.*` directives
7. **Remove** all `[BurstCompile]`, `[ReadOnly]`, `[NativeDisableParallelForRestriction]` attributes
8. **Remove** all `IJobParallelFor`, `JobHandle`, `Allocator.Persistent/Temp` references
9. **Replace** `Unity.Mathematics.math.min/max/abs` → `Math.Min/Max/Abs`
10. **Replace** `Color32` fields with a simple struct: `public struct Color32 { public byte r, g, b, a; }`
11. **Replace** `UnityEngine.Color` with nothing — ghost colors are rendering-only, remove them
12. **Replace** `UnityEngine.Vector2Int` with `(int x, int y)` tuples or a simple struct
13. **Replace** `UnityEngine.Vector2` with `(float x, float y)` tuples or `System.Numerics.Vector2`
14. **Replace** `Mathf.FloorToInt(x)` → `(int)MathF.Floor(x)`, `Mathf.Abs(x)` → `MathF.Abs(x)`
15. **Replace** `.IsCreated` checks on arrays → `!= null` checks
16. **Replace** `Allocator.Temp` + `.Dispose()` patterns → just use regular arrays/lists
17. **Keep** `[StructLayout(LayoutKind.Sequential)]` — it's System.Runtime, not Unity
18. **Keep** `[MethodImpl(MethodImplOptions.AggressiveInlining)]` — it's System.Runtime
19. **Remove** `TerrainColliderManager` parameter from structure managers — it's Unity Physics2D only
20. **Remove** `IClusterForceProvider` and `ApplyForcesToClusters` from structure managers — cluster forces are Unity Physics2D only

---

## Task 1: Project Scaffolding

**Files:**
- Create: `G:\ParticularLLM\ParticularLLM.sln`
- Create: `G:\ParticularLLM\src\ParticularLLM\ParticularLLM.csproj`
- Create: `G:\ParticularLLM\tests\ParticularLLM.Tests\ParticularLLM.Tests.csproj`

**Step 1: Create solution and projects**

```bash
cd /g/ParticularLLM
dotnet new sln --name ParticularLLM
mkdir -p src/ParticularLLM
mkdir -p tests/ParticularLLM.Tests
cd src/ParticularLLM
dotnet new classlib --name ParticularLLM --framework net8.0
rm Class1.cs
cd ../../tests/ParticularLLM.Tests
dotnet new xunit --name ParticularLLM.Tests --framework net8.0
rm UnitTest1.cs
cd ../..
dotnet sln add src/ParticularLLM/ParticularLLM.csproj
dotnet sln add tests/ParticularLLM.Tests/ParticularLLM.Tests.csproj
cd tests/ParticularLLM.Tests
dotnet add reference ../../src/ParticularLLM/ParticularLLM.csproj
```

**Step 2: Create directory structure**

```bash
cd /g/ParticularLLM/src/ParticularLLM
mkdir -p Core World Structures Clusters
cd /g/ParticularLLM/tests/ParticularLLM.Tests
mkdir -p Helpers CoreTests SimulationTests StructureTests IntegrationTests
```

**Step 3: Verify build**

Run: `cd /g/ParticularLLM && dotnet build`
Expected: Build succeeded with 0 errors

**Step 4: Verify test runner**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: 0 tests passed (empty project)

**Step 5: Commit**

```bash
cd /g/ParticularLLM
git init
git add -A
git commit -m "chore: scaffold solution with simulation library and xUnit test project"
```

---

## Task 2: Port Core Data Types

Port the pure structs and constants that have zero Unity dependencies.

**Files:**
- Create: `src/ParticularLLM/Core/Cell.cs`
- Create: `src/ParticularLLM/Core/CellFlags.cs`
- Create: `src/ParticularLLM/Core/ChunkState.cs`
- Create: `src/ParticularLLM/Core/ChunkFlags.cs`
- Create: `src/ParticularLLM/Core/WorldUtils.cs`
- Create: `src/ParticularLLM/Core/PhysicsSettings.cs`
- Create: `src/ParticularLLM/Core/Color32.cs`
- Create: `src/ParticularLLM/Core/MaterialDef.cs`
- Create: `src/ParticularLLM/Core/Materials.cs`
- Create: `src/ParticularLLM/Structures/StructureType.cs`
- Create: `src/ParticularLLM/Structures/BeltTile.cs`
- Create: `src/ParticularLLM/Structures/LiftTile.cs`
- Create: `src/ParticularLLM/Structures/WallTile.cs`
- Create: `src/ParticularLLM/Structures/BeltStructure.cs`
- Create: `src/ParticularLLM/Structures/LiftStructure.cs`
- Create: `src/ParticularLLM/Clusters/ClusterPixel.cs`
- Test: `tests/ParticularLLM.Tests/CoreTests/MaterialTests.cs`
- Test: `tests/ParticularLLM.Tests/CoreTests/WorldUtilsTests.cs`

**Step 1: Write failing tests for core types**

```csharp
// tests/ParticularLLM.Tests/CoreTests/MaterialTests.cs
using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

public class MaterialTests
{
    [Fact]
    public void CreateDefaults_Returns256Materials()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(256, mats.Length);
    }

    [Fact]
    public void Air_IsStatic_ZeroDensity()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Static, mats[Materials.Air].behaviour);
        Assert.Equal(0, mats[Materials.Air].density);
    }

    [Fact]
    public void Sand_IsPowder_Density128()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Powder, mats[Materials.Sand].behaviour);
        Assert.Equal(128, mats[Materials.Sand].density);
    }

    [Fact]
    public void Water_IsLiquid_Density64_DispersionRate5()
    {
        var mats = Materials.CreateDefaults();
        Assert.Equal(BehaviourType.Liquid, mats[Materials.Water].behaviour);
        Assert.Equal(64, mats[Materials.Water].density);
        Assert.Equal(5, mats[Materials.Water].dispersionRate);
    }

    [Fact]
    public void Sand_DenserThanWater()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(mats[Materials.Sand].density > mats[Materials.Water].density);
    }

    [Fact]
    public void Dirt_DenserThanSand()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(mats[Materials.Dirt].density > mats[Materials.Sand].density);
    }

    [Fact]
    public void IsBelt_TrueForAllBeltMaterials()
    {
        Assert.True(Materials.IsBelt(Materials.Belt));
        Assert.True(Materials.IsBelt(Materials.BeltLeft));
        Assert.True(Materials.IsBelt(Materials.BeltRight));
        Assert.True(Materials.IsBelt(Materials.BeltLeftLight));
        Assert.True(Materials.IsBelt(Materials.BeltRightLight));
    }

    [Fact]
    public void IsBelt_FalseForNonBelt()
    {
        Assert.False(Materials.IsBelt(Materials.Air));
        Assert.False(Materials.IsBelt(Materials.Sand));
        Assert.False(Materials.IsBelt(Materials.Wall));
    }

    [Fact]
    public void IsLift_TrueForLiftMaterials()
    {
        Assert.True(Materials.IsLift(Materials.LiftUp));
        Assert.True(Materials.IsLift(Materials.LiftUpLight));
    }

    [Fact]
    public void IsSoftTerrain_CorrectMaterials()
    {
        Assert.True(Materials.IsSoftTerrain(Materials.Ground));
        Assert.True(Materials.IsSoftTerrain(Materials.Dirt));
        Assert.True(Materials.IsSoftTerrain(Materials.Sand));
        Assert.True(Materials.IsSoftTerrain(Materials.Water));
        Assert.False(Materials.IsSoftTerrain(Materials.Stone));
        Assert.False(Materials.IsSoftTerrain(Materials.Air));
    }

    [Fact]
    public void LiftMaterials_ArePassable()
    {
        var mats = Materials.CreateDefaults();
        Assert.True((mats[Materials.LiftUp].flags & MaterialFlags.Passable) != 0);
        Assert.True((mats[Materials.LiftUpLight].flags & MaterialFlags.Passable) != 0);
    }

    [Fact]
    public void Ground_IsDiggable()
    {
        var mats = Materials.CreateDefaults();
        Assert.True(Materials.IsDiggable(mats[Materials.Ground]));
        Assert.False(Materials.IsDiggable(mats[Materials.Stone]));
    }
}
```

```csharp
// tests/ParticularLLM.Tests/CoreTests/WorldUtilsTests.cs
using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

public class WorldUtilsTests
{
    [Theory]
    [InlineData(0, 0, 1024, 0)]
    [InlineData(5, 3, 1024, 3 * 1024 + 5)]
    [InlineData(1023, 511, 1024, 511 * 1024 + 1023)]
    public void CellIndex_CalculatesCorrectly(int x, int y, int width, int expected)
    {
        Assert.Equal(expected, WorldUtils.CellIndex(x, y, width));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(63, 0)]
    [InlineData(64, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    public void CellToChunkX_CorrectDivision(int cellX, int expectedChunk)
    {
        Assert.Equal(expectedChunk, WorldUtils.CellToChunkX(cellX));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void ChunkToCellX_CorrectMultiplication(int chunkX, int expectedCell)
    {
        Assert.Equal(expectedCell, WorldUtils.ChunkToCellX(chunkX));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(63, 63)]
    [InlineData(64, 0)]
    [InlineData(65, 1)]
    public void CellToLocalX_CorrectModulo(int cellX, int expected)
    {
        Assert.Equal(expected, WorldUtils.CellToLocalX(cellX));
    }

    [Theory]
    [InlineData(0, 0, 1024, 512, true)]
    [InlineData(-1, 0, 1024, 512, false)]
    [InlineData(1024, 0, 1024, 512, false)]
    [InlineData(0, -1, 1024, 512, false)]
    [InlineData(0, 512, 1024, 512, false)]
    [InlineData(1023, 511, 1024, 512, true)]
    public void IsInBounds_ChecksCorrectly(int x, int y, int w, int h, bool expected)
    {
        Assert.Equal(expected, WorldUtils.IsInBounds(x, y, w, h));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: Build errors (types don't exist yet)

**Step 3: Port all core data types**

Port from Unity source files, applying porting rules. Key changes per file:

**Cell.cs** — Copy verbatim from `G:\Sandy\Assets\Scripts\Simulation\Cell.cs`, only change namespace.

**CellFlags.cs** — Extract `CellFlags` class from Cell.cs into its own file.

**ChunkState.cs** — Copy verbatim, only change namespace.

**ChunkFlags.cs** — Extract `ChunkFlags` class from ChunkState.cs into its own file.

**WorldUtils.cs** — Copy verbatim, only change namespace.

**PhysicsSettings.cs** — Copy, change namespace, remove `GetUnityGravity()` method (it references `CoordinateUtils.CellToWorldScale` which is Unity rendering). Keep all constants.

**Color32.cs** — New minimal struct:
```csharp
namespace ParticularLLM;

public struct Color32
{
    public byte r, g, b, a;
    public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
}
```

**MaterialDef.cs** — Port `BehaviourType` enum, `MaterialFlags` class, and `MaterialDef` struct. Replace `UnityEngine.Color32` with our `Color32`. Change namespace.

**Materials.cs** — Extract `Materials` static class from Unity's MaterialDef.cs. Replace `new Color32(r,g,b,a)` calls with our Color32 constructor (identical signature). Change namespace.

**Structure types** (BeltTile, LiftTile, WallTile, BeltStructure, LiftStructure, StructureType) — Copy verbatim, only change namespace.

**ClusterPixel.cs** — Copy verbatim, only change namespace.

**Step 4: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All 13 tests pass

**Step 5: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: port core data types from Unity (Cell, Materials, ChunkState, etc.)"
```

---

## Task 3: Port CellWorld

Port the world state container. This is where NativeArray→array and NativeList→List conversions happen.

**Files:**
- Create: `src/ParticularLLM/World/CellWorld.cs`
- Test: `tests/ParticularLLM.Tests/CoreTests/CellWorldTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ParticularLLM.Tests/CoreTests/CellWorldTests.cs
using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

public class CellWorldTests
{
    [Fact]
    public void Constructor_InitializesAllCellsToAir()
    {
        var world = new CellWorld(128, 64);
        for (int i = 0; i < world.cells.Length; i++)
            Assert.Equal(Materials.Air, world.cells[i].materialId);
    }

    [Fact]
    public void Constructor_CorrectDimensions()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(128, world.width);
        Assert.Equal(64, world.height);
        Assert.Equal(128 * 64, world.cells.Length);
    }

    [Fact]
    public void Constructor_CorrectChunkCounts()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(2, world.chunksX);  // 128 / 64
        Assert.Equal(1, world.chunksY);  // 64 / 64
    }

    [Fact]
    public void Constructor_NonAlignedWorld_RoundsUpChunks()
    {
        var world = new CellWorld(100, 100);
        Assert.Equal(2, world.chunksX);  // ceil(100/64) = 2
        Assert.Equal(2, world.chunksY);
    }

    [Fact]
    public void SetCell_And_GetCell_RoundTrip()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 20, Materials.Sand);
        Assert.Equal(Materials.Sand, world.GetCell(10, 20));
    }

    [Fact]
    public void SetCell_OutOfBounds_DoesNothing()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(-1, 0, Materials.Sand);
        world.SetCell(0, -1, Materials.Sand);
        world.SetCell(128, 0, Materials.Sand);
        world.SetCell(0, 64, Materials.Sand);
        // No crash, no change
        Assert.Equal(Materials.Air, world.GetCell(0, 0));
    }

    [Fact]
    public void GetCell_OutOfBounds_ReturnsAir()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(Materials.Air, world.GetCell(-1, 0));
        Assert.Equal(Materials.Air, world.GetCell(128, 0));
    }

    [Fact]
    public void SetCell_MarksDirty()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Sand);
        var chunk = world.chunks[0];  // chunk (0,0) contains cell (10,10)
        Assert.True((chunk.flags & ChunkFlags.IsDirty) != 0);
    }

    [Fact]
    public void SetCell_ResetsVelocity()
    {
        var world = new CellWorld(128, 64);
        // Manually give a cell velocity
        int idx = 10 * 128 + 10;
        var cell = world.cells[idx];
        cell.velocityX = 5;
        cell.velocityY = 5;
        cell.materialId = Materials.Sand;
        world.cells[idx] = cell;

        // SetCell should reset velocities
        world.SetCell(10, 10, Materials.Water);
        var result = world.cells[idx];
        Assert.Equal(Materials.Water, result.materialId);
        Assert.Equal(0, result.velocityX);
        Assert.Equal(0, result.velocityY);
    }

    [Fact]
    public void MarkDirtyWithNeighbors_WakesAdjacentChunks()
    {
        // World with 4 chunks (128x128, 2x2 chunks)
        var world = new CellWorld(128, 128);

        // Cell at (63, 63) = local (63,63) in chunk (0,0) — near right and bottom edge
        world.MarkDirtyWithNeighbors(63, 63);

        // Should wake chunk (0,0), (1,0), (0,1)
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) != 0);  // (0,0)
        Assert.True((world.chunks[1].flags & ChunkFlags.IsDirty) != 0);  // (1,0) right
        Assert.True((world.chunks[2].flags & ChunkFlags.IsDirty) != 0);  // (0,1) below
    }

    [Fact]
    public void ResetDirtyState_ClearsDirtyAndSetsActiveLastFrame()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Sand);
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) != 0);

        world.ResetDirtyState();

        // Dirty cleared, but activeLastFrame set
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) == 0);
        Assert.Equal(1, world.chunks[0].activeLastFrame);
    }

    [Fact]
    public void CountActiveCells_CountsNonAir()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(0, 0, Materials.Sand);
        world.SetCell(1, 0, Materials.Water);
        world.SetCell(2, 0, Materials.Stone);
        Assert.Equal(3, world.CountActiveCells());
    }

    [Fact]
    public void Materials_AreInitialized()
    {
        var world = new CellWorld(128, 64);
        // Should have 256 material defs loaded
        Assert.Equal(256, world.materials.Length);
        Assert.Equal(BehaviourType.Powder, world.materials[Materials.Sand].behaviour);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: Build error — CellWorld doesn't exist

**Step 3: Port CellWorld**

Port from `G:\Sandy\Assets\Scripts\Simulation\CellWorld.cs`. Key changes:
- `NativeArray<Cell> cells` → `Cell[] cells`
- `NativeArray<MaterialDef> materials` → `MaterialDef[] materials`
- `NativeArray<ChunkState> chunks` → `ChunkState[] chunks`
- Remove `using Unity.Collections`, `using Unity.Jobs`
- Remove `NativeList<int>` parameters from `CollectChunkGroups` → use `List<int>`
- Remove `Dispose()` (no native allocations to free) — or keep it as empty for interface compat
- Remove `GetActiveChunks(NativeArray<int>)` (not needed without jobs)
- Remove `.IsCreated` checks
- Constructor: `new Cell[width * height]` instead of `new NativeArray<Cell>(..., Allocator.Persistent)`
- `CollectChunkGroups` takes `List<int>` groups instead of `NativeList<int>`

**Step 4: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All CellWorld tests and Material tests pass

**Step 5: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: port CellWorld with array-based storage"
```

---

## Task 4: Port Cell Simulation Kernel

This is the big one — porting SimulateChunksJob logic into a plain C# class. Single-threaded, no jobs.

**Files:**
- Create: `src/ParticularLLM/World/SimulateChunksLogic.cs`
- Create: `src/ParticularLLM/World/CellSimulator.cs`
- Test: `tests/ParticularLLM.Tests/Helpers/SimulationFixture.cs`
- Test: `tests/ParticularLLM.Tests/Helpers/WorldAssert.cs`
- Test: `tests/ParticularLLM.Tests/SimulationTests/PowderTests.cs`

**Step 1: Write test helpers**

```csharp
// tests/ParticularLLM.Tests/Helpers/SimulationFixture.cs
using ParticularLLM;

namespace ParticularLLM.Tests.Helpers;

/// <summary>
/// Creates a small simulation world and provides methods to step and inspect it.
/// Default size: 64x64 (one chunk). Use larger sizes for multi-chunk tests.
/// </summary>
public class SimulationFixture : IDisposable
{
    public CellWorld World { get; }
    public CellSimulator Simulator { get; }

    public SimulationFixture(int width = 64, int height = 64)
    {
        World = new CellWorld(width, height);
        Simulator = new CellSimulator();
    }

    /// <summary>
    /// Advance simulation by N frames.
    /// </summary>
    public void Step(int frames = 1)
    {
        for (int i = 0; i < frames; i++)
            Simulator.Simulate(World);
    }

    /// <summary>
    /// Place a single cell at (x, y).
    /// </summary>
    public void Set(int x, int y, byte materialId)
    {
        World.SetCell(x, y, materialId);
    }

    /// <summary>
    /// Fill a rectangular region with a material.
    /// </summary>
    public void Fill(int x, int y, int w, int h, byte materialId)
    {
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                World.SetCell(x + dx, y + dy, materialId);
    }

    /// <summary>
    /// Get the material at (x, y).
    /// </summary>
    public byte Get(int x, int y) => World.GetCell(x, y);

    /// <summary>
    /// Get the full Cell struct at (x, y).
    /// </summary>
    public Cell GetCell(int x, int y)
    {
        if (x < 0 || x >= World.width || y < 0 || y >= World.height)
            return default;
        return World.cells[y * World.width + x];
    }

    public void Dispose() { }
}
```

```csharp
// tests/ParticularLLM.Tests/Helpers/WorldAssert.cs
using ParticularLLM;
using Xunit;

namespace ParticularLLM.Tests.Helpers;

/// <summary>
/// Custom assertion helpers for simulation state.
/// </summary>
public static class WorldAssert
{
    /// <summary>
    /// Assert that a cell at (x, y) contains the expected material.
    /// </summary>
    public static void CellIs(CellWorld world, int x, int y, byte expectedMaterial)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual == expectedMaterial,
            $"Expected material {expectedMaterial} at ({x},{y}), got {actual}.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    /// <summary>
    /// Assert that a cell at (x, y) is Air.
    /// </summary>
    public static void IsAir(CellWorld world, int x, int y)
    {
        CellIs(world, x, y, Materials.Air);
    }

    /// <summary>
    /// Assert that a cell at (x, y) is NOT Air.
    /// </summary>
    public static void IsNotAir(CellWorld world, int x, int y)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual != Materials.Air,
            $"Expected non-air at ({x},{y}), but got Air.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    /// <summary>
    /// Count cells of a specific material in a rectangular region.
    /// </summary>
    public static int CountMaterial(CellWorld world, int x, int y, int w, int h, byte materialId)
    {
        int count = 0;
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                if (world.GetCell(x + dx, y + dy) == materialId)
                    count++;
        return count;
    }

    /// <summary>
    /// Count ALL cells of a specific material in the entire world.
    /// </summary>
    public static int CountMaterial(CellWorld world, byte materialId)
    {
        int count = 0;
        for (int i = 0; i < world.cells.Length; i++)
            if (world.cells[i].materialId == materialId)
                count++;
        return count;
    }

    /// <summary>
    /// Dump a region of the world as ASCII art for debugging.
    /// Materials: .=Air #=Stone :=Sand ~=Water ^=Steam !=Dirt G=Ground ==Belt |=Lift W=Wall ?=Other
    /// </summary>
    public static string DumpRegion(CellWorld world, int x, int y, int w, int h)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  Region ({x},{y}) to ({x+w-1},{y+h-1}):");
        for (int dy = 0; dy < h; dy++)
        {
            sb.Append("  ");
            for (int dx = 0; dx < w; dx++)
            {
                byte mat = world.GetCell(x + dx, y + dy);
                sb.Append(MaterialChar(mat));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static char MaterialChar(byte mat) => mat switch
    {
        Materials.Air => '.',
        Materials.Stone => '#',
        Materials.Sand => ':',
        Materials.Water => '~',
        Materials.Steam => '^',
        Materials.Oil => 'o',
        Materials.Dirt => '!',
        Materials.Ground => 'G',
        Materials.Wall => 'W',
        Materials.LiftUp or Materials.LiftUpLight => '|',
        _ when Materials.IsBelt(mat) => '=',
        _ => '?',
    };
}
```

**Step 2: Write failing powder tests**

```csharp
// tests/ParticularLLM.Tests/SimulationTests/PowderTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class PowderTests
{
    [Fact]
    public void Sand_FallsDown_OneFrameWithGravity()
    {
        // Sand starts high, after enough frames it should have fallen
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);
        sim.Step(1);
        // After 1 frame sand shouldn't have moved yet (velocity starts at 0, needs ~15 frames to accelerate)
        // But the chunk is dirty so it will be processed
        // Sand with velocity 0: fractional gravity (17) added, no overflow yet
        WorldAssert.CellIs(sim.World, 32, 10, Materials.Sand);
    }

    [Fact]
    public void Sand_EventuallyFalls_After20Frames()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);
        sim.Step(20);
        // After 20 frames, gravity should have kicked in
        WorldAssert.IsAir(sim.World, 32, 10);  // Original position is now air
    }

    [Fact]
    public void Sand_FallsToBottom()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 0, Materials.Sand);  // Top of world
        sim.Step(500);  // Plenty of time to reach bottom
        // Sand should be at bottom row (y=63)
        WorldAssert.CellIs(sim.World, 32, 63, Materials.Sand);
    }

    [Fact]
    public void Sand_StopsOnStone()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 50, 64, 1, Materials.Stone);  // Stone floor at y=50
        sim.Set(32, 10, Materials.Sand);
        sim.Step(500);
        // Sand should rest just above the stone floor
        WorldAssert.CellIs(sim.World, 32, 49, Materials.Sand);
    }

    [Fact]
    public void Sand_PilesDiagonally()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 60, 64, 4, Materials.Stone);  // Floor
        // Drop 5 grains of sand in the same column
        for (int i = 0; i < 5; i++)
            sim.Set(32, i, Materials.Sand);

        sim.Step(500);  // Let them all settle

        // Count total sand — should be exactly 5 (material conservation)
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // At least some sand should have spread diagonally (not all in one column)
        int sandInCol32 = WorldAssert.CountMaterial(sim.World, 32, 0, 1, 60, Materials.Sand);
        Assert.True(sandInCol32 < 5, "Sand should spread diagonally, not stack in one column");
    }

    [Fact]
    public void Sand_MaterialConservation_LargeAmount()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 60, 64, 4, Materials.Stone);  // Floor
        int placed = 0;
        for (int x = 20; x < 44; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Sand);
                placed++;
            }
        }

        sim.Step(500);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Sand_DoesNotMoveWhenSupported()
    {
        // Sand resting on stone should not move
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);  // Floor at bottom
        sim.Set(32, 62, Materials.Sand);
        sim.Step(50);
        WorldAssert.CellIs(sim.World, 32, 62, Materials.Sand);
    }

    [Fact]
    public void Dirt_PilesSteeper_HigherSlideResistance()
    {
        // Dirt has slideResistance=50, sand has 0
        // Dirt should form steeper piles
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);  // Floor

        // Drop a column of dirt
        for (int y = 0; y < 30; y++)
            sim.Set(64, y, Materials.Dirt);
        sim.Step(1000);

        int dirtCount = WorldAssert.CountMaterial(sim.World, Materials.Dirt);
        Assert.Equal(30, dirtCount);  // Conservation

        // Dirt should be more concentrated near column 64 than sand would be
        int nearCenter = WorldAssert.CountMaterial(sim.World, 60, 0, 9, 120, Materials.Dirt);
        Assert.True(nearCenter > 15, $"Dirt should pile steeply near center, but only {nearCenter}/30 in 9-wide zone");
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: Build errors — CellSimulator doesn't exist

**Step 4: Implement SimulateChunksLogic**

Port `SimulateChunksJob` from `G:\Sandy\Assets\Scripts\Simulation\Jobs\SimulateChunksJob.cs`. Key changes:

- Remove `[BurstCompile]`, `IJobParallelFor`, `Execute(int jobIndex)`, all `[ReadOnly]`/`[NativeDisableParallelForRestriction]` attributes
- Change `NativeArray<Cell> cells` → `Cell[] cells` (etc. for all arrays)
- Change `NativeHashMap<int, BeltTile> beltTiles` → `Dictionary<int, BeltTile> beltTiles` (nullable)
- Replace `beltTiles.IsCreated` → `beltTiles != null`
- Replace `liftTiles.IsCreated` → `liftTiles != null`
- Replace `wallTiles.IsCreated` → `wallTiles != null`
- Replace `math.min/max/abs` → `Math.Min/Max/Abs`
- Make `SimulateChunk(int chunkIndex)` public
- Keep all simulation logic IDENTICAL — same variable names, same formulas, same control flow

The class should be a plain struct/class with public fields matching the job's fields, and a `SimulateChunk(int chunkIndex)` method.

**Step 5: Implement CellSimulator**

Port the orchestration from `CellSimulatorJobbed.cs` as a simple single-threaded loop:

```csharp
// src/ParticularLLM/World/CellSimulator.cs
namespace ParticularLLM;

public class CellSimulator
{
    private readonly List<int> activeChunks = new();

    public void Simulate(CellWorld world,
        LiftManager? liftManager = null,
        BeltManager? beltManager = null,
        WallManager? wallManager = null)
    {
        world.currentFrame++;

        // Collect all active chunks (no groups needed — single-threaded)
        CollectActiveChunks(world);

        // Create simulation logic with current world state
        var logic = new SimulateChunksLogic
        {
            cells = world.cells,
            chunks = world.chunks,
            materials = world.materials,
            liftTiles = liftManager?.LiftTiles,
            beltTiles = beltManager?.GetBeltTiles(),
            wallTiles = wallManager?.WallTiles,
            width = world.width,
            height = world.height,
            chunksX = world.chunksX,
            chunksY = world.chunksY,
            currentFrame = world.currentFrame,
            fractionalGravity = PhysicsSettings.FractionalGravity,
            gravity = PhysicsSettings.CellGravityAccel,
            maxVelocity = PhysicsSettings.MaxVelocity,
            liftForce = PhysicsSettings.LiftForce,
            liftExitLateralForce = PhysicsSettings.LiftExitLateralForce,
        };

        // Simulate all active chunks sequentially
        foreach (int chunkIndex in activeChunks)
            logic.SimulateChunk(chunkIndex);

        // Belt simulation (after cell sim, matching Unity order)
        if (beltManager != null)
            beltManager.SimulateBelts(world, world.currentFrame);

        // Reset dirty state for next frame
        world.ResetDirtyState();
    }

    private void CollectActiveChunks(CellWorld world)
    {
        activeChunks.Clear();
        for (int i = 0; i < world.chunks.Length; i++)
        {
            var chunk = world.chunks[i];
            bool isActive = (chunk.flags & ChunkFlags.IsDirty) != 0
                         || chunk.activeLastFrame != 0
                         || (chunk.flags & ChunkFlags.HasStructure) != 0;
            if (isActive)
                activeChunks.Add(i);
        }
    }
}
```

Note: The `BeltManager.SimulateBelts` method signature will need to accept `CellWorld` (we'll adjust in Task 6). For now, leave the belt simulation call commented out or pass the arrays directly.

**Step 6: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All powder tests pass

**Step 7: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: port cell simulation kernel (powder physics, gravity, diagonal slide)"
```

---

## Task 5: Port Liquid and Gas Simulation + Density Tests

**Files:**
- Test: `tests/ParticularLLM.Tests/SimulationTests/LiquidTests.cs`
- Test: `tests/ParticularLLM.Tests/SimulationTests/GasTests.cs`
- Test: `tests/ParticularLLM.Tests/SimulationTests/DensityDisplacementTests.cs`
- Test: `tests/ParticularLLM.Tests/SimulationTests/DeterminismTests.cs`

The liquid and gas logic is already in SimulateChunksLogic from Task 4. We just need tests.

**Step 1: Write liquid tests**

```csharp
// tests/ParticularLLM.Tests/SimulationTests/LiquidTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class LiquidTests
{
    [Fact]
    public void Water_FallsDown()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Water);
        sim.Step(200);
        WorldAssert.CellIs(sim.World, 32, 63, Materials.Water);
    }

    [Fact]
    public void Water_SpreadsHorizontally()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);  // Floor
        sim.Set(32, 10, Materials.Water);
        sim.Step(500);

        // Water should have spread, not just be in one cell
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);  // Conservation: still 1 water cell
        // But it should be at the floor level
        WorldAssert.CellIs(sim.World, 32, 62, Materials.Water);
    }

    [Fact]
    public void Water_FillsContainer()
    {
        // Create a U-shaped container
        using var sim = new SimulationFixture();
        sim.Fill(20, 50, 1, 14, Materials.Stone);   // Left wall
        sim.Fill(43, 50, 1, 14, Materials.Stone);   // Right wall
        sim.Fill(20, 63, 24, 1, Materials.Stone);   // Floor

        // Pour 10 water cells
        for (int i = 0; i < 10; i++)
            sim.Set(32, i, Materials.Water);

        sim.Step(1000);

        // All 10 should still exist
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(10, waterCount);

        // Water should be at the bottom of the container
        int waterInContainer = WorldAssert.CountMaterial(sim.World, 21, 50, 22, 13, Materials.Water);
        Assert.Equal(10, waterInContainer);
    }

    [Fact]
    public void Water_MaterialConservation()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        int placed = 0;
        for (int x = 50; x < 78; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                sim.Set(x, y, Materials.Water);
                placed++;
            }
        }

        sim.Step(1000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(placed, remaining);
    }
}
```

**Step 2: Write gas tests**

```csharp
// tests/ParticularLLM.Tests/SimulationTests/GasTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class GasTests
{
    [Fact]
    public void Steam_RisesUp()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 50, Materials.Steam);
        sim.Step(200);
        // Steam should have risen (lower Y value)
        WorldAssert.IsAir(sim.World, 32, 50);
    }

    [Fact]
    public void Steam_RisesToTop()
    {
        using var sim = new SimulationFixture();
        sim.Set(32, 60, Materials.Steam);
        sim.Step(500);
        // Steam should be at or near top
        WorldAssert.CellIs(sim.World, 32, 0, Materials.Steam);
    }

    [Fact]
    public void Steam_MaterialConservation()
    {
        using var sim = new SimulationFixture();
        int placed = 5;
        for (int i = 0; i < placed; i++)
            sim.Set(30 + i, 50, Materials.Steam);

        sim.Step(200);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.Equal(placed, remaining);
    }
}
```

**Step 3: Write density displacement tests**

```csharp
// tests/ParticularLLM.Tests/SimulationTests/DensityDisplacementTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class DensityDisplacementTests
{
    [Fact]
    public void Sand_SinksThroughWater()
    {
        // Sand (density 128) should displace Water (density 64)
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);   // Floor
        sim.Fill(30, 50, 5, 12, Materials.Water);   // Water pool
        sim.Set(32, 40, Materials.Sand);             // Sand above water

        sim.Step(500);

        // Sand should be at bottom (just above stone), water should be above sand
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, sandCount);
        Assert.Equal(60, waterCount);  // 5 * 12 = 60 water cells
    }

    [Fact]
    public void Water_DoesNotSinkThroughSand()
    {
        // Water is lighter than sand — should NOT displace sand
        using var sim = new SimulationFixture();
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Fill(30, 58, 5, 5, Materials.Sand);   // Sand pile
        sim.Set(32, 50, Materials.Water);           // Water above sand

        sim.Step(500);

        // Both should still exist (material conservation)
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(25, sandCount);
        Assert.Equal(1, waterCount);
    }

    [Fact]
    public void Stone_BlocksEverything()
    {
        using var sim = new SimulationFixture();
        sim.Fill(0, 40, 64, 1, Materials.Stone);  // Stone floor
        sim.Set(32, 10, Materials.Sand);

        sim.Step(500);

        // Sand should be above stone, stone should not move
        WorldAssert.CellIs(sim.World, 32, 39, Materials.Sand);
        Assert.Equal(64, WorldAssert.CountMaterial(sim.World, Materials.Stone));
    }
}
```

**Step 4: Write determinism tests**

```csharp
// tests/ParticularLLM.Tests/SimulationTests/DeterminismTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class DeterminismTests
{
    [Fact]
    public void SameSetup_ProducesIdenticalState()
    {
        // Run the same scenario twice, compare byte-for-byte
        byte[] state1 = RunScenario();
        byte[] state2 = RunScenario();

        Assert.Equal(state1.Length, state2.Length);
        for (int i = 0; i < state1.Length; i++)
        {
            Assert.True(state1[i] == state2[i],
                $"States diverged at cell index {i / 11} (byte offset {i % 11})");
        }
    }

    [Fact]
    public void ComplexScenario_Deterministic()
    {
        byte[] state1 = RunComplexScenario();
        byte[] state2 = RunComplexScenario();

        Assert.Equal(state1.Length, state2.Length);
        for (int i = 0; i < state1.Length; i++)
        {
            Assert.True(state1[i] == state2[i],
                $"Complex scenario diverged at cell index {i / 11} (byte offset {i % 11})");
        }
    }

    private static byte[] RunScenario()
    {
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Drop sand and water
        for (int x = 30; x < 50; x++)
            for (int y = 0; y < 10; y++)
                sim.Set(x, y, Materials.Sand);

        for (int x = 60; x < 80; x++)
            for (int y = 0; y < 5; y++)
                sim.Set(x, y, Materials.Water);

        sim.Step(300);
        return SnapshotCells(sim.World);
    }

    private static byte[] RunComplexScenario()
    {
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);
        sim.Fill(30, 100, 1, 20, Materials.Stone);   // Pillar
        sim.Fill(60, 80, 30, 1, Materials.Stone);     // Shelf

        // Mixed materials
        for (int x = 40; x < 55; x++)
            for (int y = 0; y < 8; y++)
                sim.Set(x, y, (y % 2 == 0) ? Materials.Sand : Materials.Water);

        sim.Set(50, 30, Materials.Steam);
        sim.Set(51, 30, Materials.Steam);
        sim.Set(52, 30, Materials.Steam);

        sim.Step(500);
        return SnapshotCells(sim.World);
    }

    private static byte[] SnapshotCells(CellWorld world)
    {
        // Serialize all cell data to bytes for comparison
        int cellSize = 11; // sizeof(Cell)
        byte[] snapshot = new byte[world.cells.Length * cellSize];
        for (int i = 0; i < world.cells.Length; i++)
        {
            var cell = world.cells[i];
            int offset = i * cellSize;
            snapshot[offset + 0] = cell.materialId;
            snapshot[offset + 1] = cell.flags;
            snapshot[offset + 2] = (byte)cell.velocityX;
            snapshot[offset + 3] = (byte)cell.velocityY;
            snapshot[offset + 4] = cell.temperature;
            snapshot[offset + 5] = cell.structureId;
            snapshot[offset + 6] = (byte)(cell.ownerId & 0xFF);
            snapshot[offset + 7] = (byte)(cell.ownerId >> 8);
            snapshot[offset + 8] = cell.velocityFracX;
            snapshot[offset + 9] = cell.velocityFracY;
            snapshot[offset + 10] = cell.frameUpdated;
        }
        return snapshot;
    }
}
```

**Step 5: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All tests pass

**Step 6: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: add liquid, gas, density, and determinism tests"
```

---

## Task 6: Port Structure Managers (Belt, Lift, Wall)

**Files:**
- Create: `src/ParticularLLM/Structures/StructureUtils.cs`
- Create: `src/ParticularLLM/Structures/IStructureManager.cs`
- Create: `src/ParticularLLM/Structures/BeltManager.cs`
- Create: `src/ParticularLLM/Structures/LiftManager.cs`
- Create: `src/ParticularLLM/Structures/WallManager.cs`
- Create: `src/ParticularLLM/World/SimulateBeltsLogic.cs`
- Test: `tests/ParticularLLM.Tests/StructureTests/BeltPlacementTests.cs`
- Test: `tests/ParticularLLM.Tests/StructureTests/LiftPlacementTests.cs`
- Test: `tests/ParticularLLM.Tests/StructureTests/WallPlacementTests.cs`

**Step 1: Write failing placement tests**

```csharp
// tests/ParticularLLM.Tests/StructureTests/BeltPlacementTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class BeltPlacementTests
{
    [Fact]
    public void PlaceBelt_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(10, 10, 1));
        Assert.True(belts.HasBeltAt(8, 8)); // Snapped to grid
    }

    [Fact]
    public void PlaceBelt_SnapsToGrid()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(10, 10, 1); // Should snap to (8, 8)
        Assert.True(belts.HasBeltAt(8, 8));
        Assert.True(belts.HasBeltAt(15, 15)); // Still within 8x8 block
        Assert.False(belts.HasBeltAt(7, 8));  // Outside block
        Assert.False(belts.HasBeltAt(16, 8)); // Outside block
    }

    [Fact]
    public void PlaceBelt_OnStone_Fails()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Stone);
        var belts = new BeltManager(world);
        Assert.False(belts.PlaceBelt(10, 10, 1));
    }

    [Fact]
    public void PlaceBelt_OnSoftTerrain_CreatesGhost()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(8, 8, Materials.Ground); // Soft terrain in the block
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(8, 8, 1));
        // Belt should be ghost (terrain not cleared yet)
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);
    }

    [Fact]
    public void PlaceBelt_WritesBeltMaterialToCells()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);  // Right-moving belt at (8,8)

        // All 64 cells should be belt materials
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.True(Materials.IsBelt(world.GetCell(8 + dx, 8 + dy)),
                    $"Cell ({8+dx},{8+dy}) should be belt material");
    }

    [Fact]
    public void PlaceBelt_Overlap_Fails()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        Assert.True(belts.PlaceBelt(8, 8, 1));
        Assert.False(belts.PlaceBelt(8, 8, -1)); // Same position
    }

    [Fact]
    public void RemoveBelt_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);
        Assert.True(belts.RemoveBelt(8, 8));
        Assert.False(belts.HasBeltAt(8, 8));

        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Air, world.GetCell(8 + dx, 8 + dy));
    }

    [Fact]
    public void AdjacentBelts_SameDirection_Merge()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);   // First block at (8,8)
        belts.PlaceBelt(16, 8, 1);  // Adjacent block at (16,8), same direction
        Assert.Equal(1, belts.BeltCount); // Should merge into one belt
    }

    [Fact]
    public void AdjacentBelts_DifferentDirection_DontMerge()
    {
        var world = new CellWorld(128, 64);
        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);   // Right
        belts.PlaceBelt(16, 8, -1); // Left
        Assert.Equal(2, belts.BeltCount);
    }
}
```

```csharp
// tests/ParticularLLM.Tests/StructureTests/LiftPlacementTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class LiftPlacementTests
{
    [Fact]
    public void PlaceLift_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        Assert.True(lifts.PlaceLift(10, 10));
        Assert.True(lifts.HasLiftAt(8, 8));
    }

    [Fact]
    public void PlaceLift_WritesPassableMaterialToCells()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        // All cells should be lift materials (LiftUp or LiftUpLight)
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.True(Materials.IsLift(world.GetCell(8 + dx, 8 + dy)),
                    $"Cell ({8+dx},{8+dy}) should be lift material");
    }

    [Fact]
    public void AdjacentLifts_Vertically_Merge()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);    // Top block
        lifts.PlaceLift(8, 16);   // Below (same X, adjacent Y)
        Assert.Equal(1, lifts.LiftCount);
    }

    [Fact]
    public void AdjacentLifts_Horizontally_DontMerge()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        lifts.PlaceLift(16, 8);  // Side by side — lifts only merge vertically
        Assert.Equal(2, lifts.LiftCount);
    }

    [Fact]
    public void RemoveLift_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        Assert.True(lifts.RemoveLift(8, 8));
        Assert.False(lifts.HasLiftAt(8, 8));
    }

    [Fact]
    public void RemoveLift_MiddleOfMerged_SplitsInTwo()
    {
        var world = new CellWorld(128, 64);
        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);
        lifts.PlaceLift(8, 16);
        lifts.PlaceLift(8, 24);
        Assert.Equal(1, lifts.LiftCount); // All merged

        lifts.RemoveLift(8, 16);  // Remove middle
        Assert.Equal(2, lifts.LiftCount); // Split into 2
    }
}
```

```csharp
// tests/ParticularLLM.Tests/StructureTests/WallPlacementTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class WallPlacementTests
{
    [Fact]
    public void PlaceWall_InAir_Succeeds()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        Assert.True(walls.PlaceWall(10, 10));
        Assert.True(walls.HasWallAt(8, 8));
    }

    [Fact]
    public void PlaceWall_WritesWallMaterial()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Wall, world.GetCell(8 + dx, 8 + dy));
    }

    [Fact]
    public void PlaceWall_OnStone_Fails()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Stone);
        var walls = new WallManager(world);
        Assert.False(walls.PlaceWall(10, 10));
    }

    [Fact]
    public void RemoveWall_ClearsToAir()
    {
        var world = new CellWorld(128, 64);
        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);
        Assert.True(walls.RemoveWall(8, 8));
        Assert.False(walls.HasWallAt(8, 8));
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                Assert.Equal(Materials.Air, world.GetCell(8 + dx, 8 + dy));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: Build errors — managers don't exist

**Step 3: Port StructureUtils and IStructureManager**

From `G:\Sandy\Assets\Scripts\Structures\StructureUtils.cs`:
- Remove `List<Vector2Int>` → `List<(int x, int y)>`
- Remove `NativeHashSet<int>` → `HashSet<int>`
- Remove `NativeArray<T>` → `T[]`
- Remove `Allocator.Temp`, `.Dispose()`
- Remove `TerrainColliderManager` parameter from `MarkChunksDirtyForBlock`

From `G:\Sandy\Assets\Scripts\Structures\IStructureManager.cs`:
- Remove `Color GhostColor` property
- Remove `List<Vector2Int>` → `List<(int x, int y)>`

**Step 4: Port BeltManager**

From `G:\Sandy\Assets\Scripts\Structures\BeltManager.cs`:
- `NativeHashMap<int, BeltTile>` → `Dictionary<int, BeltTile>`
- `NativeHashMap<ushort, BeltStructure>` → `Dictionary<ushort, BeltStructure>`
- `NativeList<BeltStructure>` → `List<BeltStructure>`
- `NativeHashSet<int>` → `HashSet<int>`
- Remove `IClusterForceProvider` implementation and `ApplyForcesToClusters` method
- Remove `ClusterRestingOnBelt` method
- Remove `TerrainColliderManager` from constructor and `UpdateGhostStates`
- Remove `Dispose()` (no native allocations)
- Remove `ScheduleSimulateBelts` (job scheduling) — keep `SimulateBelts` (plain loop version)
- Adapt `SimulateBelts` to accept `CellWorld` instead of reading from `world` field
- Remove `ghostColor`, `GhostColor`, ghost rendering concerns
- `RemoveAtSwapBack(i)` → remove element and adjust (use `RemoveAt` or swap-back manually)
- `.ToNativeArray(Allocator.Temp)` + `.Dispose()` → `.ToArray()` or `.ToList()`
- `beltTiles[posKey]` read → `beltTiles[posKey]` (same syntax for Dictionary)
- `beltTiles.TryGetValue(key, out val)` → same syntax
- `beltTiles.ContainsKey(key)` → same syntax
- `beltTiles.Remove(key)` + `beltTiles.Add(key, val)` → `beltTiles[key] = val`

**Step 5: Port LiftManager**

From `G:\Sandy\Assets\Scripts\Structures\LiftManager.cs`:
- `NativeArray<LiftTile>` → `LiftTile[]`
- `NativeHashMap` → `Dictionary`, `NativeList` → `List`, `NativeHashSet` → `HashSet`
- Remove `IClusterForceProvider`, `ApplyForcesToClusters`, `ApplyLiftForce(Rigidbody2D)`, `ClusterInLiftZone`, `PositionInLiftZone`
- Remove `Physics2D.gravity`, `Rigidbody2D.AddForce` references
- Remove `Dispose()`, `Allocator` references, `.IsCreated`
- Remove ghost rendering color
- Replace `Mathf.FloorToInt` → `(int)MathF.Floor`
- `.ToNativeArray(Allocator.Temp)` + `.Dispose()` → `.ToArray()`

**Step 6: Port WallManager**

From `G:\Sandy\Assets\Scripts\Structures\WallManager.cs`:
- `NativeArray<WallTile>` → `WallTile[]`
- `NativeHashSet<int>` → `HashSet<int>`
- Remove `TerrainColliderManager`, `Dispose()`, ghost rendering color
- `.ToNativeArray(Allocator.Temp)` + `.Dispose()` → `.ToArray()`

**Step 7: Port SimulateBeltsLogic**

Port from `G:\Sandy\Assets\Scripts\Simulation\Jobs\SimulateBeltsJob.cs` as a plain class with a `SimulateBelt(int beltIndex)` method. Same porting rules as SimulateChunksLogic.

**Step 8: Wire belt simulation into CellSimulator**

Update `CellSimulator.Simulate()` to call `beltManager.SimulateBelts(world, currentFrame)` after cell simulation.

**Step 9: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All placement tests pass

**Step 10: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: port structure managers (Belt, Lift, Wall) with placement/removal/merge"
```

---

## Task 7: Structure Simulation Tests

Test that structures actually affect the simulation — belts move cells, lifts push cells up, walls block cells.

**Files:**
- Test: `tests/ParticularLLM.Tests/StructureTests/BeltSimulationTests.cs`
- Test: `tests/ParticularLLM.Tests/StructureTests/LiftSimulationTests.cs`
- Test: `tests/ParticularLLM.Tests/StructureTests/GhostActivationTests.cs`

**Step 1: Write belt simulation tests**

```csharp
// tests/ParticularLLM.Tests/StructureTests/BeltSimulationTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class BeltSimulationTests
{
    private SimulationFixture CreateFixtureWithBelt(int beltX, int beltY, sbyte direction)
    {
        var sim = new SimulationFixture(128, 64);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(beltX, beltY, direction);
        sim.Simulator.SetBeltManager(belts);
        return sim;
    }

    [Fact]
    public void Belt_MovesSandRight()
    {
        using var sim = CreateFixtureWithBelt(16, 40, 1); // Right-moving belt
        int surfaceY = 40 - 1; // Surface is one row above belt top
        sim.Set(20, surfaceY, Materials.Sand);

        sim.Step(100);

        // Sand should have moved to the right
        WorldAssert.IsAir(sim.World, 20, surfaceY);
        // Sand should be somewhere to the right of 20
        bool foundRight = false;
        for (int x = 21; x < 128; x++)
        {
            if (sim.Get(x, surfaceY) == Materials.Sand || sim.Get(x, surfaceY + 1) == Materials.Sand)
            {
                foundRight = true;
                break;
            }
        }
        Assert.True(foundRight, "Sand should have moved right on the belt");
    }

    [Fact]
    public void Belt_MovesSandLeft()
    {
        using var sim = CreateFixtureWithBelt(40, 40, -1); // Left-moving belt
        int surfaceY = 40 - 1;
        sim.Set(44, surfaceY, Materials.Sand);

        sim.Step(100);

        WorldAssert.IsAir(sim.World, 44, surfaceY);
    }

    [Fact]
    public void Belt_MovesWater()
    {
        using var sim = CreateFixtureWithBelt(16, 40, 1);
        int surfaceY = 40 - 1;
        sim.Set(20, surfaceY, Materials.Water);

        sim.Step(100);

        // Water should have moved right (and possibly spread)
        int waterLeft = WorldAssert.CountMaterial(sim.World, 16, 0, 5, 64, Materials.Water);
        Assert.Equal(0, waterLeft); // No water in the leftmost 5 columns of belt
    }

    [Fact]
    public void Belt_MaterialConservation()
    {
        using var sim = CreateFixtureWithBelt(16, 40, 1);
        int surfaceY = 40 - 1;
        int placed = 0;
        for (int x = 16; x < 24; x++)
        {
            sim.Set(x, surfaceY, Materials.Sand);
            placed++;
        }

        sim.Step(200);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }
}
```

**Step 2: Write lift simulation tests**

```csharp
// tests/ParticularLLM.Tests/StructureTests/LiftSimulationTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

public class LiftSimulationTests
{
    [Fact]
    public void Lift_PushesSandUpward()
    {
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);

        // Place a tall lift column
        lifts.PlaceLift(32, 40);
        lifts.PlaceLift(32, 48);
        lifts.PlaceLift(32, 56);
        lifts.PlaceLift(32, 64);
        sim.Simulator.SetLiftManager(lifts);

        // Drop sand into the lift from below
        sim.Set(34, 70, Materials.Sand);  // Below lift, will fall then enter

        sim.Step(500);

        // Sand should have risen above the original position
        // It enters the lift, gets pushed up, and exits at the top
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);
    }

    [Fact]
    public void Lift_PushesWaterUpward()
    {
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80);
        lifts.PlaceLift(32, 88);
        lifts.PlaceLift(32, 96);
        sim.Simulator.SetLiftManager(lifts);

        // Place water inside the lift zone
        sim.Set(34, 90, Materials.Water);

        sim.Step(500);

        int waterCount = WorldAssert.CountMaterial(sim.World, Materials.Water);
        Assert.Equal(1, waterCount);

        // Water should have risen above y=80 (top of lift)
        int waterAboveLift = WorldAssert.CountMaterial(sim.World, 32, 0, 8, 80, Materials.Water);
        Assert.True(waterAboveLift >= 1, "Water should have been pushed above the lift");
    }

    [Fact]
    public void Lift_MaterialConservation()
    {
        var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80);
        lifts.PlaceLift(32, 88);
        lifts.PlaceLift(32, 96);
        sim.Simulator.SetLiftManager(lifts);

        int placed = 0;
        for (int y = 90; y < 100; y++)
        {
            for (int dx = 0; dx < 4; dx++)
            {
                sim.Set(32 + dx, y, Materials.Sand);
                placed++;
            }
        }

        sim.Step(1000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }
}
```

**Step 3: Write ghost activation tests**

```csharp
// tests/ParticularLLM.Tests/StructureTests/GhostActivationTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.StructureTests;

public class GhostActivationTests
{
    [Fact]
    public void GhostBelt_ActivatesWhenTerrainCleared()
    {
        var world = new CellWorld(128, 64);
        // Fill 8x8 area with Ground
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var belts = new BeltManager(world);
        belts.PlaceBelt(8, 8, 1);  // Should be ghost

        // Verify ghost
        Assert.True(belts.TryGetBeltTile(8, 8, out var tile));
        Assert.True(tile.isGhost);

        // Clear terrain
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);

        // Update ghost states
        belts.UpdateGhostStates();

        // Should now be active
        Assert.True(belts.TryGetBeltTile(8, 8, out var activatedTile));
        Assert.False(activatedTile.isGhost);

        // Belt material should be written to cells
        Assert.True(Materials.IsBelt(world.GetCell(8, 8)));
    }

    [Fact]
    public void GhostWall_ActivatesWhenTerrainCleared()
    {
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var walls = new WallManager(world);
        walls.PlaceWall(8, 8);

        Assert.True(walls.GetWallTile(8, 8).isGhost);

        // Clear terrain
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Air);

        walls.UpdateGhostStates();

        Assert.False(walls.GetWallTile(8, 8).isGhost);
        Assert.Equal(Materials.Wall, world.GetCell(8, 8));
    }

    [Fact]
    public void GhostLift_ActivatesWhenGroundCleared_AllowsPowder()
    {
        // Lifts activate when no Ground remains, even with powder present
        var world = new CellWorld(128, 64);
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Ground);

        var lifts = new LiftManager(world);
        lifts.PlaceLift(8, 8);

        Assert.True(lifts.GetLiftTile(8, 8).isGhost);

        // Replace ground with sand (not air) — lifts should still activate
        for (int dy = 0; dy < 8; dy++)
            for (int dx = 0; dx < 8; dx++)
                world.SetCell(8 + dx, 8 + dy, Materials.Sand);

        lifts.UpdateGhostStates();

        // Should activate because no Ground remains (sand is OK for lifts)
        Assert.False(lifts.GetLiftTile(8, 8).isGhost);
    }
}
```

**Step 4: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All tests pass

Note: `CellSimulator` needs `SetBeltManager(BeltManager)` and `SetLiftManager(LiftManager)` methods, or accept them as constructor/method parameters. Update the `Simulate` method signature or add setter methods to support this. The test fixtures need to wire managers into the simulator before stepping.

**Step 5: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: port structure managers and add simulation tests for belts, lifts, walls"
```

---

## Task 8: Integration Tests and Scenario Tests

Test complex multi-system interactions: belt-lift combos, material flowing through lift to belt, large scenarios.

**Files:**
- Test: `tests/ParticularLLM.Tests/IntegrationTests/BeltLiftComboTests.cs`
- Test: `tests/ParticularLLM.Tests/IntegrationTests/ScenarioTests.cs`

**Step 1: Write integration tests**

```csharp
// tests/ParticularLLM.Tests/IntegrationTests/BeltLiftComboTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

public class BeltLiftComboTests
{
    [Fact]
    public void Sand_OnBelt_FallsIntoBeltEnd()
    {
        // Sand on a belt gets carried to the edge, then falls off
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);  // Floor

        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(24, 80, 1);  // Right-moving belt
        belts.PlaceBelt(32, 80, 1);  // Extended belt
        sim.Simulator.SetBeltManager(belts);

        // Drop sand onto belt surface
        int surfaceY = 80 - 1;
        sim.Set(26, surfaceY - 10, Materials.Sand);  // Above belt, will fall to surface

        sim.Step(500);

        // Sand should have fallen to belt, moved right, fallen off
        int sandCount = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(1, sandCount);

        // Sand should be below the belt (fell off the right end)
        int sandBelowBelt = WorldAssert.CountMaterial(sim.World, 0, 80, 128, 48, Materials.Sand);
        Assert.Equal(1, sandBelowBelt);
    }

    [Fact]
    public void WallBlocksSandFalling()
    {
        var sim = new SimulationFixture(128, 128);
        var walls = new WallManager(sim.World);
        walls.PlaceWall(32, 80);  // Wall block at (32, 80) through (39, 87)
        sim.Simulator.SetWallManager(walls);

        // Drop sand directly above the wall
        sim.Set(35, 50, Materials.Sand);

        sim.Step(500);

        // Sand should rest on top of the wall (y = 79)
        WorldAssert.CellIs(sim.World, 35, 79, Materials.Sand);
    }

    [Fact]
    public void FullPipeline_MaterialConservation()
    {
        // Complex scenario: sand falls, hits belt, gets carried, falls into container
        var sim = new SimulationFixture(256, 256);
        sim.Fill(0, 240, 256, 16, Materials.Stone);  // Floor

        var belts = new BeltManager(sim.World);
        var walls = new WallManager(sim.World);

        // Right-moving belt chain
        for (int x = 40; x < 120; x += 8)
            belts.PlaceBelt(x, 160, 1);

        // Container walls at belt end
        walls.PlaceWall(120, 160);  // Right wall of container
        walls.PlaceWall(120, 168);
        walls.PlaceWall(120, 176);

        sim.Simulator.SetBeltManager(belts);
        sim.Simulator.SetWallManager(walls);

        // Drop sand above the belt start
        int placed = 0;
        for (int y = 100; y < 110; y++)
        {
            for (int x = 42; x < 48; x++)
            {
                sim.Set(x, y, Materials.Sand);
                placed++;
            }
        }

        sim.Step(2000);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(placed, remaining);
    }
}
```

```csharp
// tests/ParticularLLM.Tests/IntegrationTests/ScenarioTests.cs
using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.IntegrationTests;

public class ScenarioTests
{
    [Fact]
    public void LargeWorld_SandAndWater_MaterialConservation()
    {
        // Simulate a large world with mixed materials
        var sim = new SimulationFixture(512, 256);
        sim.Fill(0, 240, 512, 16, Materials.Stone);

        int sandPlaced = 0;
        int waterPlaced = 0;

        // Sand pile
        for (int x = 100; x < 150; x++)
        {
            for (int y = 0; y < 20; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandPlaced++;
            }
        }

        // Water pool
        for (int x = 200; x < 250; x++)
        {
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterPlaced++;
            }
        }

        sim.Step(1000);

        Assert.Equal(sandPlaced, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterPlaced, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    [Fact]
    public void MultiChunk_SandCrossesChunkBoundaries()
    {
        // Sand should cross chunk boundaries correctly
        var sim = new SimulationFixture(192, 128);  // 3x2 chunks
        sim.Fill(0, 120, 192, 8, Materials.Stone);

        // Place sand right at chunk boundary (x=63-64)
        sim.Set(63, 10, Materials.Sand);
        sim.Set(64, 10, Materials.Sand);

        sim.Step(500);

        // Both grains should have fallen to the floor
        Assert.Equal(2, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void GravityConsistency_AllPowdersFallAtSameRate()
    {
        // Sand and Dirt should both fall (both are Powder behaviour)
        var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(30, 10, Materials.Sand);
        sim.Set(60, 10, Materials.Dirt);

        sim.Step(300);

        // Both should be at the floor
        WorldAssert.IsAir(sim.World, 30, 10);
        WorldAssert.IsAir(sim.World, 60, 10);
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }
}
```

**Step 2: Run tests**

Run: `cd /g/ParticularLLM && dotnet test --verbosity normal`
Expected: All tests pass

**Step 3: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: add integration and scenario tests (belt-lift combos, material conservation)"
```

---

## Task 9: Cluster Data Stub

Minimal cluster data structure for future testing of cluster-grid interactions. No physics.

**Files:**
- Create: `src/ParticularLLM/Clusters/ClusterData.cs`
- Test: `tests/ParticularLLM.Tests/CoreTests/ClusterDataTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/ParticularLLM.Tests/CoreTests/ClusterDataTests.cs
using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

public class ClusterDataTests
{
    [Fact]
    public void ClusterData_HoldsPixels()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Stone));
        Assert.Equal(2, cluster.PixelCount);
    }

    [Fact]
    public void ClusterData_TracksPosition()
    {
        var cluster = new ClusterData(1);
        cluster.X = 100f;
        cluster.Y = 50f;
        cluster.Rotation = 0.5f;

        Assert.Equal(100f, cluster.X);
        Assert.Equal(50f, cluster.Y);
        Assert.Equal(0.5f, cluster.Rotation);
    }
}
```

**Step 2: Implement ClusterData stub**

```csharp
// src/ParticularLLM/Clusters/ClusterData.cs
namespace ParticularLLM;

/// <summary>
/// Stub cluster data. In Unity, this is a MonoBehaviour with Rigidbody2D.
/// Here it's a plain class holding pixel data and position for grid sync testing.
/// </summary>
public class ClusterData
{
    public ushort Id { get; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rotation { get; set; }  // Radians
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }

    private readonly List<ClusterPixel> _pixels = new();
    public IReadOnlyList<ClusterPixel> Pixels => _pixels;
    public int PixelCount => _pixels.Count;

    public ClusterData(ushort id) { Id = id; }

    public void AddPixel(ClusterPixel pixel) => _pixels.Add(pixel);
}
```

**Step 3: Run tests**

Run: `cd /g/ParticularLLM && dotnet test`
Expected: All tests pass

**Step 4: Commit**

```bash
cd /g/ParticularLLM
git add -A
git commit -m "feat: add cluster data stub for future grid-sync testing"
```

---

## Summary of Deliverables

After all 9 tasks, the project at `G:\ParticularLLM` will contain:

- A standalone C# simulation library that mirrors the Unity cell simulation logic
- Pure data types (Cell, ChunkState, MaterialDef, etc.) shared 1:1 with Unity
- Cell physics (powder, liquid, gas, gravity, density displacement) ported line-for-line
- Structure managers (Belt, Lift, Wall) with placement, removal, merging, ghost activation
- Belt and lift simulation effects on cells
- Cluster data stub for future expansion
- ~50+ xUnit tests covering:
  - Core data type correctness
  - World state management
  - Powder falling, piling, slide resistance
  - Liquid falling, spreading, container filling
  - Gas rising
  - Density displacement (sand sinks through water)
  - Determinism (same setup → identical byte-for-byte state)
  - Structure placement, removal, merging, ghost activation
  - Belt and lift simulation effects
  - Multi-system integration (belt-lift combos)
  - Material conservation at every level
  - Cross-chunk boundary correctness

The test suite can be run with `dotnet test` in under 30 seconds.
