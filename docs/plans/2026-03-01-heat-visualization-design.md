# Heat Visualization Design

## Problem

The HTML viewer only renders material IDs as flat colors. Temperature data exists per cell (0-255 byte) but is never exported or visualized. This makes all heat-related scenarios (furnace emission, heat conduction, material reactions) impossible to visually review — 13 of 17 furnace scenarios failed review with "can't see where the heat is going."

## Design Decisions

### Heat on Air Cells (Normal Mode)

Materials always render at their true base color — no tinting. Heat is visualized only on **air cells** as a faint, semi-transparent glow.

- **Below ambient (0-19°C)**: faint blue glow
- **At ambient (20°C)**: fully transparent (invisible)
- **Above ambient (21-255°C)**: yellow → orange → red → white glow
- **Max opacity ~40-50%**: heat glow must be faint enough to never be confused with solid materials

This preserves material identity (solid pixels = materials, faint glow = heat) while making heat fields visible in the empty space around hot objects.

### Full Heatmap Toggle (Press H)

A debug view where **all cells** are colored purely by temperature at full opacity. Same color spectrum (blue → transparent → yellow → red → white) but solid. Material identity is hidden. Small "HEAT" label shown when active.

### Furnace Block Directional Gradient

Each 8x8 furnace block renders with a smooth gradient from dark brown (back/cold side) to bright orange (emission edge). The gradient follows an **exponential curve weighted toward dark** — first 5-6 pixels are dark brown shades, last 2-3 pixels ramp to bright orange. This clearly indicates emission direction without misleading that the whole block is a heat source.

Static visual — does not change with actual temperature.

### Data Pipeline

Expand frame capture from 1 byte/cell (materialId) to **2 bytes/cell** (materialId + temperature). Version flag in the binary header for backwards compatibility with old single-byte captures. Gzip compression handles the extra data well since most cells sit at ambient temperature.

### Color Logic in C# (Unity Portability)

All color mapping logic lives in a shared C# class (`HeatColorMap`), not in viewer JavaScript. The HTML exporter calls this class to generate lookup tables (256-entry arrays) and embeds them as JSON in the viewer HTML. The viewer JS indexes into pre-built arrays — no color math in JavaScript.

When porting to Unity, the same C# class drives shader uniforms or texture lookups. One source of truth for all renderers.

#### HeatColorMap API

- `TemperatureToAirColor(byte temp)` → RGBA (faint, semi-transparent for air cells)
- `TemperatureToHeatmapColor(byte temp)` → RGBA (full opacity for heatmap toggle)
- `FurnaceGradientColor(FurnaceDirection dir, int localX, int localY)` → RGB (exponential dark-weighted gradient)

### Color Spectrum

| Temperature | Color | Context |
|-------------|-------|---------|
| 0°C | Blue | Cold zone |
| 10°C | Light blue | Below ambient |
| 20°C | Transparent | Ambient (invisible) |
| 50°C | Faint yellow | Warm |
| 100°C | Orange | Hot (water boils) |
| 200°C | Red | Very hot (iron melts) |
| 255°C | White | Maximum |

### Furnace Gradient Palette

| Position | Color | Role |
|----------|-------|------|
| Back edge (col 0-1) | Dark brown ~(80, 40, 20) | Cold/structural |
| Mid (col 2-5) | Gradual warm ~(100-130, 50-60, 25-30) | Transition (still dark) |
| Near emission (col 6) | Orange ~(180, 100, 35) | Approaching emission |
| Emission edge (col 7) | Bright orange ~(220, 140, 40) | Heat output side |

Exponential weighting: most of the visual change happens in the last 2-3 pixels.
