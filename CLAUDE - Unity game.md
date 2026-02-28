# Claude Code Settings

**Always use the Opus model for any subagents that are launched.**

**Never commit to git without asking the user first.** Always confirm before running `git commit`, `git push`, or other commands that modify git history.

---

# Development Guidelines

## Architecture Philosophy

**Systems, Not Patches**

- Build unified systems that handle all cases, not individual fixes for individual problems
- One source of truth - if logic exists, it lives in ONE place
- No special-case rules for specific scenarios
- If a "fix" only addresses one situation, step back and design a system that handles ALL similar situations
- When something doesn't work, ask: "What system is missing?" not "What patch can I add?"

**Example:**
- BAD: Adding `GetClusterNetForce()` to ClusterManager that loops over cells and calls ForceZoneManager
- GOOD: Adding `GetNetForceInArea()` to ForceZoneManager itself, so ANY system needing area queries uses the same logic

**Material Conservation**

- Materials must NEVER silently vanish. Total material quantity must be preserved.
- Materials can change shape, move, or be redistributed, but must not disappear unless explicitly intended (e.g., burning, dissolving, or an explicit destroy action).
- If an operation can't place all materials (congested area, out of bounds), retain the unplaced materials and give the player a way to retry — never discard them.

**Questions to ask before implementing:**
1. Does this logic already exist somewhere? (Don't duplicate)
2. Where should this logic live? (Single responsibility)
3. Will other systems need this? (Design for reuse)
4. Am I adding a special case or extending a system? (Prefer the latter)

---

## Separation of Concerns: Game vs Simulation

The codebase has two distinct layers that must remain separate:

**Simulation Layer** (`Assets/Scripts/Simulation/`)
- Cell world, materials, physics
- Clusters and rigid body simulation
- Belts, structures, terrain colliders
- Rendering of the cell world
- Reusable by any scene (Sandbox, Game, etc.)

**Sandbox Layer** (`Assets/Scripts/` - SandboxController, etc.)
- Material painting and brush tools
- Debug cluster spawning
- Belt placement tools
- Development/testing utilities

**Game Layer** (`Assets/Scripts/Game/`)
- Player character and controls
- Game-specific mechanics and rules
- Camera follow, UI, scoring, etc.

**Rules:**
- Changes to materials, cell physics, belts, clusters, or world simulation → `Assets/Scripts/Simulation/`
- Changes to painting tools, debug spawning, or sandbox features → Sandbox layer
- Changes to player, game mechanics, or game-specific features → `Assets/Scripts/Game/`
- Simulation code must NEVER reference Sandbox or Game code
- Sandbox and Game use `SimulationManager` to access simulation systems

**Scene Controllers:**
- `SandboxController` - Sandbox scene, painting tools, debug spawning
- `GameController` - Game scene, player spawning, game logic

---

## Chunk Threading Architecture

The simulation uses a **4-pass checkerboard pattern** to enable parallel chunk processing without race conditions.

**Chunk Groups:**
```
A B A B A B A B
C D C D C D C D
A B A B A B A B
```

Group assignment: `group = (chunkX & 1) + ((chunkY & 1) << 1)`
- Group A (0): even X, even Y
- Group B (1): odd X, even Y
- Group C (2): even X, odd Y
- Group D (3): odd X, odd Y

**Why 4 Groups?**
- Each chunk (64x64 cells) processes an extended region with a 32-cell buffer
- Same-group chunks are 2 chunks apart (128 cells)
- Their extended regions (64 + 32 + 32 = 128 cells wide) leave no overlap
- This ensures same-group chunks can READ but not WRITE adjacent cells
- Cell velocity is capped at 16 cells/frame (plus max spread of 11), safely within the 64-cell half-gap

**Execution:**
1. Groups A, B, C, D are scheduled sequentially (with dependencies)
2. Within each group, chunks run in parallel across worker threads
3. Each chunk processes bottom-to-top with alternating X direction per row

**Dirty Chunk Optimization:**
Only "active" chunks are simulated. A chunk is active if:
- It has the `IsDirty` flag set (something moved into/within it)
- It was active last frame (`activeLastFrame != 0`)
- It contains a structure (`HasStructure` flag)

When a cell moves, `MarkDirtyInternal()` is called for both old and new positions. Cells that don't move do NOT mark dirty, allowing chunks to go inactive.

**Key Files:**
- `CellSimulatorJobbed.cs` - Orchestrates the 4-pass scheduling
- `CellWorld.cs` - `CollectChunkGroups()` assigns chunks to groups
- `SimulateChunksJob.cs` - Burst-compiled job that processes each chunk

---

## Debug Overlay System

All debug visualization and metrics display goes through the unified `DebugOverlay` system in `Assets/Scripts/Debug/`.

**To add new debug information:**
1. Create a new class in `Assets/Scripts/Debug/Sections/` that extends `DebugSectionBase`
2. Implement `SectionName`, `Priority`, `DrawGUI()`, and optionally `DrawGizmos()` and `UpdateCachedValues()`
3. Register it in `SandboxController.Start()` via `debugOverlay.RegisterSection(new YourSection(...))`

**DO NOT:**
- Create standalone `OnGUI()` methods for debug display
- Use `Debug.DrawLine()` directly for visualization (use gizmos in a debug section)
- Create new MonoBehaviours just for debug visualization

**Controls:**
- F3: Toggle overlay visibility
- F4: Toggle gizmos visibility

---

## Bug Tracking

When requested to log a bug, create a markdown file in `G:\Sandy\DevPlans\Bugs\`.

**Naming convention:** `{STATUS}-{BugName}.md`
- `OPEN-BugName.md` - Active bug, not yet fixed
- `FIXED-BugName.md` - Bug has been resolved
- `REJECTED-BugName.md` - Not a bug, or won't fix

**After implementing a fix or working on a bug/feature, rename the file to reflect its new status** (e.g., `OPEN-BugName.md` → `FIXED-BugName.md`).

---

## Coordinate System

The codebase uses two coordinate systems:

**Cell Grid (Simulation)**
- Origin `(0,0)` at **top-left**
- **Y+ = down** (row 0 is top, row N is bottom)
- Integer coordinates
- Used by: cell simulation, materials, structures

**Unity World (Rendering/Physics)**
- Origin `(0,0)` at **center**
- **Y+ = up** (standard Unity)
- Float coordinates
- Used by: Unity transforms, Physics2D, camera

**Conversion (`CoordinateUtils.cs`)**
- `CellToWorldScale = 2` — each cell is 2×2 world units
- `CellToWorld(cellX, cellY)` — convert cell → world
- `WorldToCell(worldPos)` — convert world → cell (floor)
- `WorldToCellRounded(worldPos)` — convert world → cell (nearest)

**Key formulas:**
```
worldX = cellX * 2 - worldWidth
worldY = worldHeight - cellY * 2
```

---

## Material System

Materials define how cells behave and render. Each cell stores a `materialId` (byte) indexing into the material definitions array.

**Key files:**
- `MaterialDef.cs` — struct definition and `Materials` static class

**Behaviour Types (`BehaviourType`):**
| Type | Movement |
|------|----------|
| `Static` | Never moves (stone, structures) |
| `Powder` | Falls, piles diagonally (sand, dirt) |
| `Liquid` | Falls, spreads horizontally (water, oil) |
| `Gas` | Rises, disperses (steam, smoke) |

**Material Flags (`MaterialFlags`):**
- `Diggable` — can be excavated by player
- `Passable` — cells/physics can pass through (used by lifts)
- `Flammable`, `ConductsHeat`, `Conductive`, `Corrodes` — future use

**Key Materials:**
| ID | Name | Behaviour | Notes |
|----|------|-----------|-------|
| 0 | Air | Static | Empty space |
| 1 | Stone | Static | Immovable solid |
| 2 | Sand | Powder | Standard falling sand |
| 3 | Water | Liquid | Spreads, density 64 |
| 17 | Dirt | Powder | Heavy, high slide resistance |
| 18 | Ground | Static | Diggable terrain |
| 21 | Wall | Static | Wall structure material |

**Soft Terrain** — materials that structures can "ghost" through:
- Ground, Dirt, Sand, Water (checked via `Materials.IsSoftTerrain()`)

---

## Structure System

Structures are 8×8 block entities managed separately from the cell grid. Each structure type has its own manager class.

**Structure Types (`StructureType`):**
| Type | Manager | Behavior |
|------|---------|----------|
| `Belt` | `BeltManager` | Moves powder/liquid horizontally, solid |
| `Lift` | `LiftManager` | Applies upward force, **passable** (materials flow through) |
| `Wall` | `WallManager` | Purely static blocker, solid |

**Common patterns:**
- All structures use 8×8 cell blocks, snapped to grid
- Placement checks for Air or soft terrain
- **Ghost mode**: structures placed through soft terrain wait for terrain to clear before activating
- Ghost structures block material flow (except lifts, which remain passable)
- Each manager tracks ghost block origins for efficient activation checks

**Tile storage:**
- `BeltManager` uses `NativeHashMap<int, BeltTile>` (sparse)
- `LiftManager` and `WallManager` use `NativeArray<T>` (dense, parallel to cells)

**Key methods on managers:**
- `Place*(x, y, ...)` — place an 8×8 block (returns false if blocked)
- `Remove*(x, y)` — remove an 8×8 block
- `UpdateGhostStates()` — activate ghost blocks where terrain cleared
- `GetGhostBlockPositions(List<Vector2Int>)` — for rendering ghost overlays
