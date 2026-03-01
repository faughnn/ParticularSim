# Sorting Primitives: Fan & Magnet

Two force-field primitives for material sorting. Both follow the same principle: apply force to cells in a zone. The player combines them with belts, pistons, walls, and gravity to build sorting machines.

## Design Philosophy

A magnet just attracts. A fan just blows. What happens after — deflection, accumulation, clearing, collection — is the player's problem to solve by combining primitives. No special sorting modes, no built-in capture-and-release. Systems, not patches.

## Fan / Blower

### Structure
- 8x8 block, grid-snapped (consistent with all structures)
- Directional: Right, Left, Up, Down (like furnace)
- Ghost mode support for placing through terrain

### Behavior
Applies continuous force in its facing direction to all materials in its wind zone.

**Force formula:**
```
windAcceleration = baseFanForce * airDrag / density
```

Materials with high airDrag and low density get pushed the most. Materials with low airDrag and high density barely move.

**Current material response (sorted by push magnitude):**

| Material | Density | AirDrag | Relative Push |
|----------|---------|---------|---------------|
| Ash      | 15      | 50      | Huge          |
| Coal     | 100     | 20      | Low-medium    |
| Sand     | 128     | 25      | Low           |
| Dirt     | 140     | 18      | Lower         |
| IronOre  | 200     | 12      | Barely moves  |

Liquids (Water, Oil, MoltenIron) and gases (Steam, Smoke) have no airDrag currently. May need airDrag values added for fan interaction, or a default behavior for liquids/gas in wind (e.g., gas always affected strongly, liquids resist based on density).

### Wind Zone
- Extends from fan face outward
- Width: 8 cells (matching block face)
- Depth: 16-24 cells (tunable via settings)
- Force decreases linearly with distance from face

### Implementation
- Same zone-iteration pattern as furnace heat emission
- During cell simulation, check if cell is in any fan's wind zone
- Apply velocity to cell based on wind formula and material properties
- Structure: `FanManager.cs` following BeltManager/LiftManager pattern

### Emergent Uses
- **Air classifier**: belt drops mixed material off edge, fan blows sideways, bins below catch at different distances
- **Ventilation**: push smoke/steam out of enclosed areas
- **Material clearing**: blow light debris away from work area

---

## Magnet

### Structure
- 8x8 block, grid-snapped
- Directional: Right, Left, Up, Down (like furnace and fan)
- Ghost mode support

### New Flag
Add `MaterialFlags.Magnetic` to the flags enum.

**Magnetic materials:**
- IronOre (powder, density 200) — primary sorting target

**Not magnetic (physically correct):**
- MoltenIron — above Curie temperature, loses ferromagnetism
- Iron — static behavior (can't move), but even if it could, solid iron is magnetic in reality. Fine to add the flag if Iron ever becomes moveable.
- All non-ferrous materials (Sand, Coal, Ash, Dirt, Water, Oil, etc.)

### Behavior
Applies continuous force toward its face on materials with `MaterialFlags.Magnetic` flag only.

**Force formula:**
```
magnetAcceleration = baseMagnetForce / density  (only if Magnetic flag set)
```

Direction: always toward the magnet face. Non-magnetic materials completely unaffected.

### Magnetic Field Zone
- Extends from magnet face outward
- Width: 8 cells (matching block face)
- Depth: 12-16 cells (tunable via settings)
- Force decreases with distance (linear falloff)

### Implementation
- Same zone-iteration pattern as furnace heat emission and fan wind
- During structure simulation step, iterate cells in magnetic zone
- For each cell with Magnetic flag, apply velocity toward magnet face
- Structure: `MagnetManager.cs` following same pattern

### Emergent Uses
- **Freefall deflection**: material drops off belt, magnet deflects iron ore sideways, sand falls straight — two bins below
- **Overhead capture + piston clearing**: magnet above belt captures iron ore against its face, piston periodically pushes accumulated ore into a side bin
- **Selective lifting**: magnet faces up below a mixture, pulls iron ore down through lighter materials

---

## Combined Sorting Pipeline

These primitives combine with existing structures (belt, furnace, piston, wall) to create full sorting machines:

```
Raw ore mixture (IronOre + Sand + Coal + Ash)
         │
    ┌────┤
    │MAG │  ←── magnet deflects IronOre sideways
    │NET │
    └────┤
         │
    ┌────┴────┐
    │Iron│Rest │
    │ Ore│    │
    └────┘    │
         ┌────┤
         │FAN │  ←── fan blows light materials further
         └────┤
              │
    ┌─────┬───┴───┐
    │Coal │ Sand  │ Ash blown
    │     │       │ far away
    └─────┴───────┘
```

Final stage: furnace smelts separated IronOre into MoltenIron, which freezes into Iron.

## Open Questions
- Should fan affect liquids and gases? If so, what airDrag values?
- Should there be a `baseMagnetForce` setting or is it fixed?
- Should fan and magnet have power/fuel requirements, or always-on?
