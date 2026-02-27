# Sandy - Roadmap

## Vision

A falling sand puzzle game where players build machines from simple structures to transport and process materials. Physics does the work - players just arrange the pieces.

**Simple structures + physics = emergent machines**

---

## Core Loop

1. Bucket appears somewhere on the map requiring a specific material
2. Player must figure out how to get that material to the bucket
3. May require: transporting, processing, combining structures creatively
4. Filling the bucket unlocks new tools/structures/areas
5. Repeat with increasingly complex requirements

---

## What's Built

### Simulation Engine
- Cell world with 4-pass checkerboard chunk threading (Noita-style)
- Materials: Air, Stone, Sand, Water, Dirt, Ground, Wall (powder, liquid, gas, static behaviors)
- Density displacement between materials
- Fractional velocity accumulation (gravity = 17/256 per frame)
- Bottom-to-top processing with alternating X direction per row
- Dirty chunk optimization (inactive chunks skip simulation)
- Standalone test harness at `G:\ParticularLLM` (135 tests, headless validation)

### Structures (8x8 grid-snapped)
- **Belt** — horizontal material transport, cluster carrying, auto-merging, drag-to-place
- **Lift** — upward force zones, passable (materials flow through), vertical merging
- **Wall** — static blocker
- **Ghost mode** — structures placed through soft terrain, auto-activate when terrain clears
- **Piston** — 16x16 cluster pushing (early implementation)

### Clusters
- Rigid body simulation with compression/fracture system
- Cluster-to-world sync, anti-sleep system
- Piston-driven cluster pushing

### Player & Game
- Player character with movement (acceleration-based, coyote time, jump buffering)
- Shovel tool for digging Ground into Dirt
- Cell grab/drop system (right-click to pick up loose materials, release to drop)
- Item pickup system (walk over items to collect)
- Inventory menu (Tab/I) and 5-slot hotbar (number keys, scroll wheel)
- Tool range indicators (reach circle + cursor AoE circle)
- Structure placement controller (preview ghost, drag placement, ability-gated)
- Camera follow with dead zone, smoothing, and world-edge clamping

### Progression & Levels
- Bucket collection system (scans cell grid, fires events)
- Multi-objective sequential progression (prerequisite chaining, per-bucket counters)
- Ability unlock chain: dig → belts → lifts → walls
- Tutorial world (1920x1620 cells, 3 zones):
  - **Level 1** — dig dirt, carry to nearby bucket (unlocks belts)
  - **Level 2 "The Descent"** — belt dirt across stone barrier and down slope (unlocks lifts)
  - **Level 3 "The Ascent"** — (WIP) lift+belt dirt up through shaft to floating island

### Debug & Dev Tools
- Unified DebugOverlay system (F3/F4) with extensible sections
- Game debug panel (player position, objective progress, equipped item)
- Sandbox scene with material painting, debug spawning, belt/lift placement tools

---

## Active Work (February/March)

### Per-Material Physics Attributes — IN PROGRESS
Design is locked for powder (`density`, `restitution`, `stability`) and liquid (`density`, `restitution`, `spread`). Gas attributes still need design. Gravity remains a global constant.

**Prerequisite:** 2D material movement (Bresenham ray-march replacing the current 3-phase vertical-then-diagonal fallback) — which itself is blocked on fixing the `frameUpdated` chunk-edge stutter bug.

See: `February/PROGRESS-MaterialAttributes.md`, `February/PowderAndLiquidAttributeDesign.md`, `February/TwoDimensionalMaterialMovement.md`

---

## Open Bugs

### High Priority
| Bug | Summary |
|-----|---------|
| Cluster crack overwrites walls | `SyncClusterToWorld()` overwrites wall cells during fracture — no check for static structures |
| Clusters break too easily | Normal collisions (dropping, stacking) trigger fracture; threshold too low, no distinction from piston crushing |
| Piston placement deletes dirt | Piston placement clears 16x16 area to Air with no ghost mode — up to 256 cells lost (material conservation violation) |
| Player controller overhaul | Multiple interaction bugs: falls through ghost belts, clips through ground while digging, lift force not continuous, belt force applied while on lift |

### Medium Priority
| Bug | Summary |
|-----|---------|
| Material falls back into lift | Material hitting solid above lift falls straight back down in an oscillation loop — no horizontal dispersal for rising cells |
| Lift fountain lateral force no effect | `velocityX` set on lift exit but never consumed during free-fall — material exits in a straight column |
| Ghost lift invisible behind dirt | Ghost overlay removed but lift material only written to Air cells, so dirt renders in its place |
| Lift structure too opaque | Active lifts render fully opaque — can't see materials flowing through them |
| Displacement BFS slowdown | Cluster sync triggers independent BFS per overlapping pixel — 100 overlaps = 100 searches per frame |
| Grabbed material lost on drop | Cells that can't be placed in congested areas are silently destroyed |
| Massive FPS drop on first structure | ~80% FPS drop when first belt/lift is placed — expensive system activating unconditionally |

### Low Priority
| Bug | Summary |
|-----|---------|
| Hotbar wall ability mapping | Structure unlock requirements hardcoded in switch statement instead of on item definitions |
| Player controller mixed responsibilities | Movement + inventory in one MonoBehaviour; should split into PlayerMovement + PlayerInventory |
| ClusterManager god class | Mostly fixed but duplicated bounding-box computation remains in 5+ locations |
| Ghost blocking uses ambient state | `currentCellIdx` is implicit mutable state rather than explicit parameter — fragile but no runtime bugs |
| Material clumps at top of lifts | Material piles at lift exit faster than it can spread — related to missing horizontal dispersal |
| Dirty rects unused | Per-chunk dirty rectangles tracked but never read by simulation or renderer |

---

## Backlog — Refactors & Extensions

| Plan | Summary |
|------|---------|
| Structure system refactor | Extract duplicated boilerplate (grid snap, chunk flags, ghost mode, placement, removal) across Belt/Lift/WallManager into shared utilities |
| Tool system refactor | `ITool` interface so new tools don't require touching 8-10 files |
| Item & hotbar refactor | Move per-item knowledge from switch statements into `ItemDefinition` fields |
| Material system extensions | Replace hardcoded whitelists (`IsSoftTerrain`, etc.) with `MaterialFlags` checks; add `displayName` |
| Level system extensions | Heightmap/noise terrain generation, level selection, extensible objectives |
| Progression system extensions | Objective type discriminator beyond "collect N of material X" |
| Wedge tile placement | Diagonal half-cell structure for redirecting material at lift exits |

---

## Future Systems

### Primitives (given to player)

| Primitive | What it does | Status |
|-----------|--------------|--------|
| Belt | Moves things horizontally | **Done** |
| Lift | Applies upward force | **Done** |
| Wall | Static blocker | **Done** |
| Piston | Linear push/pull | **Early** (16x16 cluster push, has bugs) |
| Plate | Solid surface, can be moved | Not started |
| Motor/Axle | Provides rotation | Not started |
| Weight | Has mass, falls | Not started |
| Hinge | Allows rotation around a point | Not started |
| Spring | Stores and releases force | Not started |
| Grate | Surface with holes | Not started |
| Trigger/Release | Timing mechanism | Not started |

### Machines (player-built from primitives)
Crushing machines especially should be emergent:
- Jaw Crusher = Piston + Plate
- Stamp Mill = Weight + Release + Guides
- Roll Crusher = Two Motors + Two Drums

### Heat/Processing — PLANNED

Detailed designs exist for all three systems:

| System | Summary | Plan |
|--------|---------|------|
| Furnace | Rectangular structure, heats interior cells per frame | `Features/PLANNED-FurnaceSystem.md` |
| Heat transfer | Ambient conduction between neighbors, double-buffered Burst jobs, gradual cooling | `Features/PLANNED-HeatTransfer.md` |
| Material reactions | Temperature-triggered burning, melting, freezing, boiling | `Features/PLANNED-MaterialReactions.md` |

### Transport (future)

| Structure | Effect |
|-----------|--------|
| Bucket Elevator | Chain of buckets lifts vertically |
| Catapult/Launcher | Flings clusters or material globs |
| Chute/Slide | Angled surface, gravity does the work |
| Trapdoor | Drops collected material when triggered |

### Flow Control (future)

| Structure | Effect |
|-----------|--------|
| Hopper | Funnels material into narrow stream |
| Gate/Valve | Blocks or allows flow |
| Airlock | Two doors, never both open |
| Buffer Tank | Stores material, releases steadily |
| Splitter | Divides one stream into two |

### Power/Motion (future)

| Structure | Effect |
|-----------|--------|
| Waterwheel | Falling water/material spins it, powers things |
| Windmill | Moving air spins it |
| Weight/Counterweight | Falling weight pulls something up |
| Pendulum | Swinging weight, periodic motion |
| Spring | Stores energy, releases with force |

### Buckets (Goals)

Basic bucket system is implemented. Future bucket types:

| Bucket | Behavior | Teaches |
|--------|----------|---------|
| Bin | Accepts anything, counts target material | Basic transport (**Done**) |
| Filter Bucket | Wrong material passes through | That sorting exists |
| Purity Bucket | Needs 90%+ correct to complete | Build actual sorters |
| Fragile Bucket | Empties itself if contaminated | Precision matters |
| Volatile Bucket | Explodes on wrong material | High stakes |

### Sorting/Separating (future)

| Structure | Sorts by |
|-----------|----------|
| Vibrating Screen | Shakes - small stuff falls through |
| Cyclone | Spins - heavy stuff flung outward |
| Settling Tank | Density - dense sinks, light floats |
| Magnetic Drum | Material - pulls iron off a belt |
| Air Classifier | Weight - fan blows light particles away |
| Trommel | Size - rotating drum with holes |

---

## Rough Phases

### Phase 1: Foundation — MOSTLY DONE
- [x] Player character & movement
- [x] Basic tools (shovel)
- [x] Carrying/dropping materials (grab/drop system)
- [x] Bucket goal system
- [x] Inventory & hotbar
- [x] Structure placement (belts, lifts, walls)
- [x] Tutorial levels 1 & 2
- [ ] Level 3 "The Ascent" (WIP)
- [ ] Fix player-structure interaction bugs (controller overhaul)
- [ ] Fix lift exit behavior (material falls back in, no lateral spread)

### Phase 2: Physics & Materials
- [ ] 2D material movement (Bresenham ray-march)
- [ ] Per-material physics attributes (powder, liquid, gas)
- [ ] Heat transfer & material reactions
- [ ] Furnace structure
- [ ] Fix cluster fracture system (breaks too easily, overwrites walls)

### Phase 3: Primitives & Building
- [ ] Piston improvements (ghost mode, material conservation)
- [ ] Plate, Hinge, Motor/Axle
- [ ] Weight, Spring, Grate
- [ ] Trigger/Release mechanisms
- [ ] Players can combine primitives into machines

### Phase 4: Progression & Content
- [ ] More bucket types (filter, purity, fragile, volatile)
- [ ] Sorting/separating structures
- [ ] Multi-stage puzzle levels
- [ ] Unlock chain for new primitives
- [ ] Map expansion or level selection

### Phase 5: Polish & Emergence
- Tune physics for satisfying machines
- Edge cases and weird combos
- Let players break things in fun ways

---

## Inspiration
- Besiege (build machines from parts)
- Powder Toy / Noita (falling sand physics)
- Poly Bridge (physics puzzle solving)
- Factorio (automation satisfaction)

---

## Resolved Questions
- **How does carrying work?** Grab/drop system — right-click picks up loose cells, release drops them with spiral placement
- **How do players place structures?** Preview ghost with drag placement, ability-gated through progression unlocks
- **Pre-built maps or procedural?** Pre-built tutorial world with defined zones; level system extensible for future content
