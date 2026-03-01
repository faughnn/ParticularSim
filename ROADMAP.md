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
- Materials: Air, Stone, Sand, Water, Dirt, Ground, Oil, Steam, IronOre, MoltenIron, Iron, Coal, Ash, Smoke, Wall (powder, liquid, gas, static behaviors)
- Per-material physics attributes: density, stability, restitution, spread, airDrag, conductionRate, phase-change temperatures
- 2D material movement via Bresenham ray-march (traces full velocity vector through grid)
- Density displacement between materials
- Fractional velocity accumulation (gravity = 17/256 per frame)
- Bottom-to-top processing with alternating X direction per row
- Dirty chunk optimization (inactive chunks skip simulation)
- Standalone test harness at `G:\ParticularLLM` (516 tests, headless validation)

### Structures (8x8 grid-snapped)
- **Belt** — horizontal material transport, cluster carrying, auto-merging, drag-to-place
- **Lift** — upward force zones, passable (materials flow through), vertical merging
- **Wall** — static blocker
- **Ghost mode** — structures placed through soft terrain, auto-activate when terrain clears
- **Furnace** — 8x8 block, directional heat emission (depth-4), sub-integer accumulator, ghost mode
- **Piston** — 16x16 block, 3-second motor cycle (dwell/extend/dwell/retract), cell chain pushing, plate clusters

### Clusters
- Rigid body simulation: gravity, velocity, pixel-level collision detection
- 3-step pipeline: CLEAR (remove old pixels) → PHYSICS (integrate velocity, detect collisions) → SYNC (write to grid)
- Push-based displacement: cluster velocity transferred to displaced cells, scaled by density ratio
- Crack-line fracture: seeded RNG, 1-3 crack lines, pixel partitioning, material conservation
- Cluster-cluster collision with 1D momentum exchange
- Sleep detection (30 low-speed frames on ground)
- Piston-driven cluster pushing with crush pressure tracking

### Heat & Processing
- Double-buffered heat conduction between neighboring cells (per-material conductionRate)
- Proportional cooling toward ambient (Newton's law)
- Material reactions: melting, freezing, boiling, burning (temperature-triggered phase changes)
- Fire spread to flammable neighbors, probabilistic fuel consumption

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

### Visual Review (HTML Viewer)
- Standalone CLI app pre-simulates scenarios, exports self-contained animated HTML
- 30 scenarios across core physics, structures, and interactions
- Review queue with auto-re-queue when tagged source files change
- CLI: `--html`, `--status`, `--import`, `--list`

### Debug & Dev Tools
- Unified DebugOverlay system (F3/F4) with extensible sections
- Game debug panel (player position, objective progress, equipped item)
- Sandbox scene with material painting, debug spawning, belt/lift placement tools

---

## Active Work

Phase 2 (Physics & Materials) is complete in the test harness. Next work is either:
- Porting Phase 2 systems back to Unity
- Phase 3 (Primitives & Building) — mostly Unity-side

---

## Open Bugs

### Resolved (in test harness)
| Bug | Resolution |
|-----|------------|
| Cluster crack overwrites walls | `SyncClusterToWorld()` now checks `matDef.behaviour != BehaviourType.Static` — static cells (walls, stone) are never overwritten |
| Displacement BFS slowdown | BFS displacement replaced with physics-based push system — displacement uses cluster velocity direction, no per-pixel search |
| Clusters break too easily | Fracture now requires CrushPressureFrames > 30 sustained frames — normal collisions don't trigger fracture |

### High Priority (Unity-side)
| Bug | Summary |
|-----|---------|
| Piston placement deletes dirt | Piston placement clears 16x16 area to Air with no ghost mode — up to 256 cells lost (material conservation violation) |
| Player controller overhaul | Multiple interaction bugs: falls through ghost belts, clips through ground while digging, lift force not continuous, belt force applied while on lift |

### Medium Priority (Unity-side)
| Bug | Summary |
|-----|---------|
| Material falls back into lift | Material hitting solid above lift falls straight back down in an oscillation loop — no horizontal dispersal for rising cells |
| Lift fountain lateral force no effect | `velocityX` set on lift exit but never consumed during free-fall — material exits in a straight column |
| Ghost lift invisible behind dirt | Ghost overlay removed but lift material only written to Air cells, so dirt renders in its place |
| Lift structure too opaque | Active lifts render fully opaque — can't see materials flowing through them |
| Grabbed material lost on drop | Cells that can't be placed in congested areas are silently destroyed |
| Massive FPS drop on first structure | ~80% FPS drop when first belt/lift is placed — expensive system activating unconditionally |

### Low Priority
| Bug | Summary |
|-----|---------|
| Hotbar wall ability mapping | Structure unlock requirements hardcoded in switch statement instead of on item definitions |
| Player controller mixed responsibilities | Movement + inventory in one MonoBehaviour; should split into PlayerMovement + PlayerInventory |
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
| Piston | Linear push/pull | **Done** (16x16, motor cycle, cell chain pushing, plate clusters) |
| Plate | Solid surface, can be moved | Not started |
| Motor/Axle | Provides rotation | Not started |
| Weight | Has mass, falls | Not started |
| Hinge | Allows rotation around a point | Not started |
| Spring | Stores and releases force | Not started |
| Fan/Blower | Directional wind force (sorts by density/airDrag) | Not started — [design](docs/plans/2026-03-01-sorting-primitives-design.md) |
| Magnet | Attracts Magnetic materials toward face | Not started — [design](docs/plans/2026-03-01-sorting-primitives-design.md) |
| Grate | Surface with holes | Not started |
| Trigger/Release | Timing mechanism | Not started |

### Machines (player-built from primitives)
Crushing machines especially should be emergent:
- Jaw Crusher = Piston + Plate
- Stamp Mill = Weight + Release + Guides
- Roll Crusher = Two Motors + Two Drums

Sorting machines from fan + magnet + existing structures:
- Air Classifier = Belt + Fan + Collection Bins
- Magnetic Separator = Belt + Magnet + Collection Bins
- Full Ore Processing = Magnetic Separator → Air Classifier → Furnace

### Heat/Processing — DONE (in test harness)

All three systems implemented and tested:

| System | Summary | Status |
|--------|---------|--------|
| Furnace | 8x8 block structure, directional heat emission (depth-4), ghost mode | **Done** |
| Heat transfer | Double-buffered conduction between neighbors, per-material conductionRate, Newton's cooling | **Done** |
| Material reactions | Temperature-triggered melting, freezing, boiling, burning with fire spread | **Done** |

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

### Sorting/Separating

Sorting is emergent from force-field primitives + existing structures, not from dedicated sorting machines.

| Method | How it works | Primitives needed |
|--------|-------------|-------------------|
| Air classification | Fan blows mixed freefall; light stuff lands far, heavy near | Fan + Belt + Walls |
| Magnetic separation | Magnet deflects ferrous materials during freefall | Magnet + Belt + Walls |
| Density settling | Heavy sinks through light in liquid medium | Already emergent (density displacement) |
| Heat separation | Melt one material, other stays solid | Furnace (already built) |
| Combustion separation | Burn flammable material away | Furnace + flammable material (already built) |

Future (requires new primitives): Cyclone (Motor), Vibrating Screen (Motor + Plate)

---

## Rough Phases

### Phase 1: Foundation — MOSTLY DONE (remaining items are Unity-side)
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

### Phase 2: Physics & Materials — DONE
- [x] 2D material movement (Bresenham ray-march)
- [x] Per-material physics attributes (powder, liquid, gas)
- [x] Heat transfer & material reactions
- [x] Furnace structure
- [x] Cluster system — port to test harness with deterministic rigid body solver
- [x] Cluster-material interaction — replace BFS displacement with physics-based push
- [x] Cluster fracture system — crack-line partitioning with seeded RNG, material conservation
- [x] Piston system — port motor, cell chain pushing, plate mechanics

#### Cluster-Material Interaction Design

Replace the current BFS displacement hack with physically correct behavior:

- **Cluster into air** — place pixels, no conflict.
- **Cluster into powder/liquid** — push cells in the cluster's movement direction. Transfer cluster velocity to displaced cells, scaled by density ratio. Heavier clusters plow through light materials; dense materials resist and slow the cluster. Displaced cells move naturally via cell simulation on subsequent frames. No teleportation.
- **Cluster into gas** — trivially displaced, negligible resistance to cluster.
- **Cluster into static material (stone, wall, structures)** — cluster cannot displace it. Physics colliders prevent overlap. Cluster stops, bounces, or fractures on impact. Static material is never moved.

Key principles:
- Direction of displacement matches cluster movement direction (not random BFS)
- Velocity transfer is proportional to cluster speed and inversely proportional to material density
- No searching for empty spots — give displaced cells velocity and let cell sim handle the rest
- Material conservation guaranteed (cells are pushed, never destroyed)

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
