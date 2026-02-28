# Furnace Building Blocks Design

## Summary

Replace the current rectangular furnace structure with furnace **building blocks** — 8x8 grid-snapped blocks the player places individually to construct any enclosure shape. Combined with per-material heat conduction (including slow air conduction) and proportional cooling, this creates emergent furnace gameplay where enclosure design directly controls temperature.

Belts are not integrated with furnaces. The player simply builds belts near furnace gaps to carry materials in and out. The systems are fully independent.

## Section 1: Core Concept

- **Furnace blocks** are an 8x8 grid-snapped building material (same grid as walls and belts)
- Each block has a **cardinal direction** (Up/Down/Left/Right) set at placement — only emits heat in that direction
- Other sides act as insulating walls
- **Always on** — furnace blocks emit heat whenever placed, no toggle
- Player builds any enclosure shape, leaving gaps for material input/output
- Air conducts heat slowly (**per-material conduction rates**) — enclosed spaces naturally trap heat
- **Proportional cooling** (Newton's law) prevents runaway heating and creates natural temperature equilibrium
- Heat system uses dirty regions to skip ambient-temperature areas

## Section 2: Heating Mechanics

### Per-Material Conduction Rates

Replace the global `ConductionRate = 64` with per-material values:

| Material | Rate | Blend/frame | Notes |
|----------|------|-------------|-------|
| Air | 8 | ~3% | Poor conductor, allows slow propagation |
| Water | 48 | ~19% | Good conductor |
| Steam | 32 | ~12% | Gas, moderate |
| Stone | 64 | 25% | Solid mineral |
| Iron | 64 | 25% | Metal, excellent |
| MoltenIron | 64 | 25% | Liquid metal |
| IronOre | 48 | ~19% | Mineral, moderate |
| Coal | 32 | ~12% | Organic, moderate |
| Furnace | 64 | 25% | Designed to conduct |
| Ground | 32 | ~12% | Soil, moderate |

### Proportional Cooling (Newton's Law)

Replace flat `CoolingRate = 1` with:

```
coolingLoss = (temp - ambient) * coolingFactor / 256
```

Hotter cells cool faster. This creates natural equilibrium where heat input = cooling loss.

### Frame-Skip Heating

Furnace blocks emit +1° every N frames (like belt `speed`). With N ~30, a well-built enclosure takes 1-2 minutes to reach iron-melting temperature (200°). N is tunable.

Proportional cooling is also frame-skipped to stay balanced with the slow heating rate.

### Directional Emission

Each furnace cell checks its block's facing direction and emits heat to the adjacent cell in that direction only. If the adjacent cell is another furnace cell, it skips (no self-heating).

### Equilibrium by Enclosure Design

Temperature equilibrium depends on how many furnace walls surround a cell:

- **1 wall**: ~100° — enough to boil water
- **2 walls** (narrow channel): ~190° — near iron melting threshold
- **3 walls** (corner): 255° — maximum temperature
- Player must design tight enclosures for high temperatures

## Section 3: Placement Rules & Structure

### FurnaceBlockManager

Follows the `WallManager` pattern:

- **Grid**: 8x8 blocks, same grid as walls and belts
- **Direction**: Cardinal direction set at placement time
- **Placement rules**: Same as walls
  - Air cells: always placeable
  - Soft terrain (sand, water, dirt, ground): ghost block
  - Hard materials (stone, other structures): rejected
- **Ghost blocks**: Same as walls — placed through terrain, activates when all 64 cells clear to air
- **Material**: `Materials.Furnace` (ID 24, static, ConductsHeat) — reuse existing
- **Removal**: Clears 8x8 block to air, updates chunk flags
- **No merging** (unlike belts): Each block is independent

### Tile Storage

```
FurnaceBlockTile { exists, isGhost, direction }
```

Parallel array indexed by position key (same pattern as WallTile).

### Replaces

- `FurnaceStructure` (rectangular bounds, interior tracking, state on/off)
- `FurnaceManager` (bounding-box interior heating)

Both are replaced entirely by the block-based approach.

### Structure Interaction

- Cannot overlap with walls, belts, lifts, pistons (placement rejected)
- No special integration code with other structures

## Section 4: Material Flow & Belt Interaction

No special integration code. Material flow is emergent from existing systems.

### Input/Output Pattern (player-designed)

```
         Belt ──→ dumps ore
              ████████
    furnace → █      █ ← furnace
    (facing → █  ore  █ ← facing)
    right)    █ falls █   left)
              █  ↓↓↓  █
              █molten █
              ████  ████
                  ↓
           molten flows out onto Belt
```

1. Belt carries raw material to a gap in the furnace wall
2. Material falls off belt end into the interior (existing gravity)
3. Material contacts furnace walls, heats up over 1-2 minutes
4. Phase change occurs (iron ore → molten iron at 200°)
5. Molten iron flows down and pools at the bottom (existing liquid physics)
6. Liquid flows out through a bottom/side gap
7. Another belt picks up the product

### Emergent Challenges for the Player

- Where to put gaps (input high, output low for gravity)
- How narrow to build (narrower = hotter but less throughput)
- How to prevent heat escaping through gaps (small gaps, angled channels)
- How to keep products from solidifying on exit (molten iron freezes at 150° — output path may need furnace-lined channels)

## Section 5: Testing Strategy

### Replace FurnaceManager Tests

Current tests assume rectangular furnaces with interior bounds. New tests verify block-based placement and per-cell heat emission.

### New Heat System Tests

- Per-material conduction rates work correctly
- Proportional cooling reaches equilibrium (not flat-rate)
- Frame-skip heating at correct intervals
- Air conducts heat slowly between conducting neighbors
- Enclosed space reaches higher equilibrium than open space

### Furnace Block Tests

- 8x8 placement, ghost blocks, removal (mirrors wall test patterns)
- Directional heat emission (only heats in facing direction)
- Material conservation through phase changes inside enclosures

### Integration / Emergent Behavior Tests

- U-shaped enclosure with iron ore → verify melting occurs after sufficient frames
- Narrow vs wide enclosure → narrow reaches higher temperature
- Enclosure with gap → lower equilibrium than sealed
- Belt delivers material to gap, material enters enclosure via gravity
- Liquid product exits through bottom gap

### Regression

Existing heat transfer tests must still pass with per-material conduction. Tests that relied on old FurnaceManager rectangular behavior are replaced, not weakened.

## Implementation Scope

### Changes to Existing Systems

1. **Heat transfer**: Per-material conduction rates, proportional cooling, frame-skip cooling
2. **MaterialDef**: Add `conductionRate` field
3. **Materials.cs**: Set conduction rates per material, make air conducting

### New Code

4. **FurnaceBlockManager**: Place/remove/ghost (follows WallManager pattern)
5. **FurnaceBlockTile**: Tile struct with direction field
6. **Directional heat emission**: In simulation loop, furnace cells emit heat in facing direction

### Removed Code

7. **FurnaceStructure**: Rectangular bounds, interior tracking, state
8. **FurnaceManager**: Bounding-box interior heating

### Not in Scope

- Unity rendering, UI, player controls (Phase 3)
- Fuel-powered heating (future evolution)
- Belt-furnace integration code (not needed — systems are independent)
