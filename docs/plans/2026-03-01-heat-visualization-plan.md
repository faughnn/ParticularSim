# Heat Visualization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add temperature visualization to the HTML viewer so heat-related scenarios (furnace, conduction, reactions) can be visually reviewed.

**Architecture:** Temperature data is captured alongside materialId in the frame binary (2 bytes/cell). A shared C# `HeatColorMap` class computes all color mappings. The HTML exporter embeds pre-computed lookup tables as JSON. The viewer JS indexes arrays — no color math in JavaScript. Furnace block metadata (positions + directions) is captured in the binary and embedded per scenario for directional gradient rendering.

**Tech Stack:** C# (.NET), JavaScript (Canvas API), GZip compression

**Design doc:** `docs/plans/2026-03-01-heat-visualization-design.md`

---

### Task 1: Create HeatColorMap.cs

**Files:**
- Create: `src/ParticularLLM/Rendering/HeatColorMap.cs`
- Reference: `src/ParticularLLM/Core/Color32.cs`
- Reference: `src/ParticularLLM/Structures/FurnaceBlockTile.cs` (for `FurnaceDirection` enum)
- Test: `tests/ParticularLLM.Tests/RenderingTests/HeatColorMapTests.cs`

This is the shared color logic that both the viewer and Unity will use.

**Step 1: Write failing tests for temperature-to-air-color mapping**

```csharp
using ParticularLLM.Rendering;

namespace ParticularLLM.Tests.RenderingTests;

public class HeatColorMapTests
{
    [Fact]
    public void AirColor_AtAmbient_IsFullyTransparent()
    {
        var color = HeatColorMap.TemperatureToAirColor(HeatSettings.AmbientTemperature);
        Assert.Equal(0, color.a);
    }

    [Fact]
    public void AirColor_AboveAmbient_HasWarmHue()
    {
        var color = HeatColorMap.TemperatureToAirColor(100);
        Assert.True(color.a > 0, "Should be visible");
        Assert.True(color.r > color.b, "Warm colors should have more red than blue");
    }

    [Fact]
    public void AirColor_BelowAmbient_HasCoolHue()
    {
        var color = HeatColorMap.TemperatureToAirColor(0);
        Assert.True(color.a > 0, "Should be visible");
        Assert.True(color.b > color.r, "Cool colors should have more blue than red");
    }

    [Fact]
    public void AirColor_MaxOpacity_IsCapped()
    {
        // Even at max temperature, air opacity should be faint (~40-50%)
        var color = HeatColorMap.TemperatureToAirColor(255);
        Assert.True(color.a <= 128, $"Air opacity should be capped low, was {color.a}");
        Assert.True(color.a > 0, "Should still be visible");
    }

    [Fact]
    public void AirColor_OpacityIncreasesWithDistanceFromAmbient()
    {
        var warm = HeatColorMap.TemperatureToAirColor(100);
        var hot = HeatColorMap.TemperatureToAirColor(200);
        Assert.True(hot.a >= warm.a, "Hotter should be more visible");
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ParticularLLM.Tests --filter "FullyQualifiedName~HeatColorMapTests" -v n`
Expected: Build error — `HeatColorMap` does not exist yet.

**Step 3: Write HeatColorMap with temperature-to-air-color**

```csharp
using ParticularLLM.Core;

namespace ParticularLLM.Rendering;

public static class HeatColorMap
{
    private const byte Ambient = HeatSettings.AmbientTemperature; // 20
    private const byte MaxAirAlpha = 115; // ~45% opacity cap

    /// <summary>
    /// Temperature to RGBA for air cells in normal mode.
    /// Faint, semi-transparent glow. Invisible at ambient.
    /// </summary>
    public static Color32 TemperatureToAirColor(byte temperature)
    {
        if (temperature == Ambient) return new Color32(0, 0, 0, 0);

        if (temperature < Ambient)
        {
            // Cold: blue glow
            float t = (Ambient - temperature) / (float)Ambient; // 0..1
            byte alpha = (byte)(t * MaxAirAlpha);
            byte b = (byte)(120 + t * 135); // 120..255
            byte g = (byte)(80 * (1 - t));
            return new Color32(20, g, b, alpha);
        }
        else
        {
            // Hot: yellow -> orange -> red -> white
            float t = (temperature - Ambient) / (float)(255 - Ambient); // 0..1
            byte alpha = (byte)(t * MaxAirAlpha);

            byte r, g, b;
            if (t < 0.33f)
            {
                // Yellow range
                float s = t / 0.33f;
                r = (byte)(200 + s * 55);
                g = (byte)(180 + s * 55);
                b = (byte)(40 * (1 - s));
            }
            else if (t < 0.66f)
            {
                // Orange to red range
                float s = (t - 0.33f) / 0.33f;
                r = 255;
                g = (byte)(235 - s * 200);
                b = 0;
            }
            else
            {
                // Red to white range
                float s = (t - 0.66f) / 0.34f;
                r = 255;
                g = (byte)(35 + s * 220);
                b = (byte)(s * 255);
            }
            return new Color32(r, g, b, alpha);
        }
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ParticularLLM.Tests --filter "FullyQualifiedName~HeatColorMapTests" -v n`
Expected: All 5 tests pass.

**Step 5: Add tests for heatmap mode colors**

```csharp
[Fact]
public void HeatmapColor_AtAmbient_IsDark()
{
    var color = HeatColorMap.TemperatureToHeatmapColor(20);
    Assert.Equal(255, color.a); // Full opacity in heatmap mode
    Assert.True(color.r < 50 && color.g < 50 && color.b < 50, "Ambient should be near-black");
}

[Fact]
public void HeatmapColor_Hot_IsFullOpacity()
{
    var color = HeatColorMap.TemperatureToHeatmapColor(200);
    Assert.Equal(255, color.a);
    Assert.True(color.r > 200, "Hot should be red");
}

[Fact]
public void HeatmapColor_Cold_IsBlue()
{
    var color = HeatColorMap.TemperatureToHeatmapColor(0);
    Assert.Equal(255, color.a);
    Assert.True(color.b > color.r, "Cold should be blue");
}
```

**Step 6: Implement TemperatureToHeatmapColor**

Add to `HeatColorMap.cs`:

```csharp
/// <summary>
/// Temperature to RGBA for heatmap toggle mode.
/// Full opacity, same spectrum as air but solid.
/// </summary>
public static Color32 TemperatureToHeatmapColor(byte temperature)
{
    if (temperature == Ambient)
        return new Color32(20, 20, 20, 255);

    if (temperature < Ambient)
    {
        float t = (Ambient - temperature) / (float)Ambient;
        byte b = (byte)(40 + t * 215);
        byte g = (byte)(40 * (1 - t));
        return new Color32(20, g, b, 255);
    }
    else
    {
        float t = (temperature - Ambient) / (float)(255 - Ambient);
        byte r, g, b;
        if (t < 0.33f)
        {
            float s = t / 0.33f;
            r = (byte)(200 + s * 55);
            g = (byte)(180 + s * 55);
            b = (byte)(40 * (1 - s));
        }
        else if (t < 0.66f)
        {
            float s = (t - 0.33f) / 0.33f;
            r = 255;
            g = (byte)(235 - s * 200);
            b = 0;
        }
        else
        {
            float s = (t - 0.66f) / 0.34f;
            r = 255;
            g = (byte)(35 + s * 220);
            b = (byte)(s * 255);
        }
        return new Color32(r, g, b, 255);
    }
}
```

**Step 7: Run tests**

Run: `dotnet test tests/ParticularLLM.Tests --filter "FullyQualifiedName~HeatColorMapTests" -v n`
Expected: All 8 tests pass.

**Step 8: Add tests for furnace gradient**

```csharp
[Fact]
public void FurnaceGradient_Right_EmissionEdgeIsBrightest()
{
    var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 0, 0);
    var edge = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 7, 0);
    Assert.True(edge.r > back.r, "Emission edge should be brighter");
}

[Fact]
public void FurnaceGradient_Left_EmissionEdgeIsAtX0()
{
    var edge = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Left, 0, 0);
    var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Left, 7, 0);
    Assert.True(edge.r > back.r, "Left-facing: x=0 is emission edge");
}

[Fact]
public void FurnaceGradient_Up_EmissionEdgeIsAtY0()
{
    var edge = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Up, 0, 0);
    var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Up, 0, 7);
    Assert.True(edge.r > back.r, "Up-facing: y=0 is emission edge");
}

[Fact]
public void FurnaceGradient_WeightedDark_FirstPixelsAreSimilar()
{
    // Exponential weighting: first few pixels should be close in value (all dark)
    var p0 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 0, 0);
    var p3 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 0);
    var p7 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 7, 0);

    int diffFirstHalf = Math.Abs(p3.r - p0.r);
    int diffSecondHalf = Math.Abs(p7.r - p3.r);
    Assert.True(diffSecondHalf > diffFirstHalf,
        $"Gradient should change more in second half ({diffSecondHalf}) than first ({diffFirstHalf})");
}

[Fact]
public void FurnaceGradient_YDoesNotAffect_WhenDirectionIsHorizontal()
{
    // For right/left facing, all rows should be identical
    var row0 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 0);
    var row5 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 5);
    Assert.Equal(row0.r, row5.r);
    Assert.Equal(row0.g, row5.g);
    Assert.Equal(row0.b, row5.b);
}
```

**Step 9: Implement FurnaceGradientColor**

Add to `HeatColorMap.cs`:

```csharp
// Furnace gradient palette (dark back → bright emission edge)
private static readonly Color32 FurnaceBack = new(80, 40, 20, 255);
private static readonly Color32 FurnaceFront = new(220, 140, 40, 255);

/// <summary>
/// Furnace block gradient color based on direction and local position (0-7).
/// Exponential curve weighted toward dark — most visual change in last 2-3 pixels.
/// </summary>
public static Color32 FurnaceGradientColor(FurnaceDirection direction, int localX, int localY)
{
    // Determine gradient position (0=back, 7=emission edge) based on direction
    int pos = direction switch
    {
        FurnaceDirection.Right => localX,
        FurnaceDirection.Left => 7 - localX,
        FurnaceDirection.Down => localY,
        FurnaceDirection.Up => 7 - localY,
        _ => 0,
    };

    // Exponential curve: t = (pos/7)^2.5, weighted toward dark end
    float linear = pos / 7f;
    float t = MathF.Pow(linear, 2.5f);

    byte r = (byte)(FurnaceBack.r + t * (FurnaceFront.r - FurnaceBack.r));
    byte g = (byte)(FurnaceBack.g + t * (FurnaceFront.g - FurnaceBack.g));
    byte b = (byte)(FurnaceBack.b + t * (FurnaceFront.b - FurnaceBack.b));
    return new Color32(r, g, b, 255);
}
```

**Step 10: Run all HeatColorMap tests**

Run: `dotnet test tests/ParticularLLM.Tests --filter "FullyQualifiedName~HeatColorMapTests" -v n`
Expected: All 13 tests pass.

**Step 11: Add lookup table generation methods**

These are used by the HTML exporter to pre-compute tables and embed as JSON.

```csharp
/// <summary>
/// Generate 256-entry air heat color lookup table for embedding in viewer.
/// Returns array of [r, g, b, a] for each temperature 0-255.
/// </summary>
public static byte[][] GenerateAirColorTable()
{
    var table = new byte[256][];
    for (int i = 0; i < 256; i++)
    {
        var c = TemperatureToAirColor((byte)i);
        table[i] = new byte[] { c.r, c.g, c.b, c.a };
    }
    return table;
}

/// <summary>
/// Generate 256-entry heatmap color lookup table for embedding in viewer.
/// Returns array of [r, g, b, a] for each temperature 0-255.
/// </summary>
public static byte[][] GenerateHeatmapColorTable()
{
    var table = new byte[256][];
    for (int i = 0; i < 256; i++)
    {
        var c = TemperatureToHeatmapColor((byte)i);
        table[i] = new byte[] { c.r, c.g, c.b, c.a };
    }
    return table;
}

/// <summary>
/// Generate furnace gradient lookup for one direction: 8 entries [r, g, b].
/// </summary>
public static byte[][] GenerateFurnaceGradient(FurnaceDirection direction)
{
    var table = new byte[8][];
    for (int i = 0; i < 8; i++)
    {
        // Use i as both x and y depending on direction (the method handles it)
        var c = direction switch
        {
            FurnaceDirection.Right or FurnaceDirection.Left =>
                FurnaceGradientColor(direction, i, 0),
            _ => FurnaceGradientColor(direction, 0, i),
        };
        table[i] = new byte[] { c.r, c.g, c.b };
    }
    return table;
}
```

**Step 12: Commit**

```bash
git add src/ParticularLLM/Rendering/HeatColorMap.cs tests/ParticularLLM.Tests/RenderingTests/HeatColorMapTests.cs
git commit -m "feat: add HeatColorMap for temperature visualization color logic"
```

---

### Task 2: Expand capture format to v2 (materialId + temperature)

**Files:**
- Modify: `tests/ParticularLLM.Tests/Helpers/SimulationFixture.cs` (CaptureCurrentFrame ~line 81, WriteCaptureFile ~line 288)
- Reference: `src/ParticularLLM/Core/Cell.cs`

**Step 1: Update CaptureCurrentFrame to capture 2 bytes per cell**

In `SimulationFixture.cs`, modify `CaptureCurrentFrame()`:

```csharp
private void CaptureCurrentFrame()
{
    if (!_capturing || _capturedFrameCount >= MaxCaptureFrames) return;

    int cellCount = World.width * World.height;
    var buffer = new byte[cellCount * 2]; // v2: materialId + temperature
    for (int j = 0; j < cellCount; j++)
    {
        buffer[j * 2] = World.cells[j].materialId;
        buffer[j * 2 + 1] = World.cells[j].temperature;
    }

    _captureStream!.Write(buffer, 0, buffer.Length);
    _capturedFrameCount++;
}
```

**Step 2: Update WriteCaptureFile to write v2 format with magic marker**

The v2 binary format:
```
Magic marker: "PLv2" (4 bytes: 0x50, 0x4C, 0x76, 0x32)
width (int32)
height (int32)
frameCount (int32)
descLen (int32)
descBytes (UTF-8)
furnaceBlockCount (int32)
  for each: gridX (int16), gridY (int16), direction (byte)
gzip body: 2 bytes per cell per frame (materialId, temperature)
```

Modify `WriteCaptureFile()` to write the new format. The key changes:
- Write magic "PLv2" first
- After description, write furnace block metadata (need access to simulator's FurnaceBlockManager)
- Frame data is 2 bytes/cell instead of 1

The fixture needs access to furnace block data. Add a method or field to expose it. Check if `CellSimulator` exposes the furnace manager — if so, iterate `blockOrigins` and `furnaceTiles` to extract metadata.

```csharp
private void WriteCaptureFile()
{
    try
    {
        Directory.CreateDirectory(CaptureDir!);

        int cellCount = World.width * World.height;
        int bytesPerFrame = cellCount * 2; // v2: 2 bytes per cell
        byte[] rawFrames = _captureStream!.ToArray();
        int totalFrames = _capturedFrameCount;

        // Subsample if over limit
        byte[] outputFrames;
        int outputFrameCount;
        if (totalFrames > MaxCaptureFrames)
        {
            outputFrameCount = MaxCaptureFrames;
            outputFrames = new byte[outputFrameCount * bytesPerFrame];
            for (int i = 0; i < outputFrameCount; i++)
            {
                int srcFrame = i == 0 ? 0 : (int)((long)i * (totalFrames - 1) / (outputFrameCount - 1));
                Buffer.BlockCopy(rawFrames, srcFrame * bytesPerFrame, outputFrames, i * bytesPerFrame, bytesPerFrame);
            }
        }
        else
        {
            outputFrameCount = totalFrames;
            outputFrames = rawFrames;
        }

        string filePath = Path.Combine(CaptureDir!, $"{_captureKey}.bin");
        using var fs = File.Create(filePath);

        // Magic marker "PLv2"
        fs.Write(new byte[] { 0x50, 0x4C, 0x76, 0x32 });

        // Header: width, height, frameCount
        fs.Write(BitConverter.GetBytes(World.width));
        fs.Write(BitConverter.GetBytes(World.height));
        fs.Write(BitConverter.GetBytes(outputFrameCount));

        // Description
        var descBytes = System.Text.Encoding.UTF8.GetBytes(Description ?? "");
        fs.Write(BitConverter.GetBytes(descBytes.Length));
        if (descBytes.Length > 0)
            fs.Write(descBytes);

        // Furnace block metadata
        WriteFurnaceMetadata(fs);

        // Gzip body (2 bytes per cell per frame)
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(outputFrames, 0, outputFrameCount * bytesPerFrame);
        }
    }
    catch
    {
        // Capture failures must never break tests
    }
}

private void WriteFurnaceMetadata(FileStream fs)
{
    // Get furnace blocks from the simulator if available
    var furnaceBlocks = GetFurnaceBlocks();
    fs.Write(BitConverter.GetBytes(furnaceBlocks.Count));
    foreach (var (gridX, gridY, direction) in furnaceBlocks)
    {
        fs.Write(BitConverter.GetBytes((short)gridX));
        fs.Write(BitConverter.GetBytes((short)gridY));
        fs.WriteByte((byte)direction);
    }
}
```

The `GetFurnaceBlocks()` method needs to extract furnace metadata from the simulator. Check what's accessible — the fixture likely has a `Simulator` field. The `FurnaceBlockManager` has `blockOrigins` (HashSet of grid keys) and `furnaceTiles` array. You may need to add a public method like `GetPlacedBlocks()` to `FurnaceBlockManager` that returns a list of `(int gridX, int gridY, FurnaceDirection direction)`.

**Step 3: Run existing tests to verify nothing breaks**

Run: `dotnet test tests/ParticularLLM.Tests -v n`
Expected: All existing tests pass (capture format change shouldn't break test assertions, only the .bin output).

**Step 4: Commit**

```bash
git add tests/ParticularLLM.Tests/Helpers/SimulationFixture.cs src/ParticularLLM/Structures/FurnaceBlockManager.cs
git commit -m "feat: capture temperature + furnace metadata in v2 binary format"
```

---

### Task 3: Update CaptureReader for v2 format

**Files:**
- Modify: `src/ParticularLLM.Viewer/CaptureReader.cs` (~line 47, ReadCaptureFile)
- Modify: `src/ParticularLLM.Viewer/HtmlExporter.cs` (~line 26, ScenarioData record)

**Step 1: Add furnace block data to ScenarioData**

In `HtmlExporter.cs`, update the ScenarioData record:

```csharp
public record FurnaceBlockInfo(int GridX, int GridY, byte Direction);

public record ScenarioData(
    string Name, string Category, string Description,
    int Width, int Height, int FrameCount, string CompressedBase64,
    string[] Tags,
    bool HasTemperature = false,
    FurnaceBlockInfo[] FurnaceBlocks = null
);
```

**Step 2: Update CaptureReader to detect and read v2 format**

In `CaptureReader.cs`, modify `ReadCaptureFile()`:

```csharp
private static ScenarioData? ReadCaptureFile(string filePath)
{
    try
    {
        using var fs = File.OpenRead(filePath);

        // Read first 4 bytes to detect version
        var magic = new byte[4];
        if (fs.Read(magic, 0, 4) < 4) return null;

        bool isV2 = magic[0] == 0x50 && magic[1] == 0x4C
                  && magic[2] == 0x76 && magic[3] == 0x32; // "PLv2"

        int width, height, frameCount;

        if (isV2)
        {
            // v2: magic already read, next is width/height/frameCount
            var header = new byte[12];
            if (fs.Read(header, 0, 12) < 12) return null;
            width = BitConverter.ToInt32(header, 0);
            height = BitConverter.ToInt32(header, 4);
            frameCount = BitConverter.ToInt32(header, 8);
        }
        else
        {
            // v1: first 4 bytes were width
            width = BitConverter.ToInt32(magic, 0);
            var rest = new byte[8];
            if (fs.Read(rest, 0, 8) < 8) return null;
            height = BitConverter.ToInt32(rest, 0);
            frameCount = BitConverter.ToInt32(rest, 4);
        }

        if (width <= 0 || height <= 0 || frameCount <= 0) return null;

        // Read description
        string description = "";
        var descLenBytes = new byte[4];
        if (fs.Read(descLenBytes, 0, 4) == 4)
        {
            int descLen = BitConverter.ToInt32(descLenBytes, 0);
            if (descLen > 0 && descLen < 100_000)
            {
                var descBytes = new byte[descLen];
                if (fs.Read(descBytes, 0, descLen) == descLen)
                    description = System.Text.Encoding.UTF8.GetString(descBytes);
            }
        }

        // Read furnace metadata (v2 only)
        FurnaceBlockInfo[] furnaceBlocks = null;
        if (isV2)
        {
            var countBytes = new byte[4];
            if (fs.Read(countBytes, 0, 4) == 4)
            {
                int count = BitConverter.ToInt32(countBytes, 0);
                if (count > 0 && count < 10_000)
                {
                    furnaceBlocks = new FurnaceBlockInfo[count];
                    for (int i = 0; i < count; i++)
                    {
                        var blockData = new byte[5]; // int16 + int16 + byte
                        fs.Read(blockData, 0, 5);
                        int gx = BitConverter.ToInt16(blockData, 0);
                        int gy = BitConverter.ToInt16(blockData, 2);
                        byte dir = blockData[4];
                        furnaceBlocks[i] = new FurnaceBlockInfo(gx, gy, dir);
                    }
                }
            }
        }

        // Read gzip body and base64 encode
        using var bodyMs = new MemoryStream();
        fs.CopyTo(bodyMs);
        string compressedBase64 = Convert.ToBase64String(bodyMs.ToArray());

        // Derive metadata from filename
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        var (category, name) = ParseFileName(fileName);
        var tags = InferTags(fileName);
        if (string.IsNullOrEmpty(description))
            description = $"Test: {fileName}";

        return new ScenarioData(name, category, description,
            width, height, frameCount, compressedBase64, tags,
            HasTemperature: isV2,
            FurnaceBlocks: furnaceBlocks);
    }
    catch
    {
        return null;
    }
}
```

**Step 3: Build and verify**

Run: `dotnet build src/ParticularLLM.Viewer`
Expected: Builds successfully.

**Step 4: Commit**

```bash
git add src/ParticularLLM.Viewer/CaptureReader.cs src/ParticularLLM.Viewer/HtmlExporter.cs
git commit -m "feat: read v2 capture format with temperature and furnace metadata"
```

---

### Task 4: Embed heat color tables in HtmlExporter

**Files:**
- Modify: `src/ParticularLLM.Viewer/HtmlExporter.cs` (~line 7 Export method, ~line 260 EmbedColorTable, ~line 283 EmbedScenarioData)
- Reference: `src/ParticularLLM/Rendering/HeatColorMap.cs`

**Step 1: Add heat color table embedding**

Add a new method to `HtmlExporter.cs`:

```csharp
private static string EmbedHeatTables()
{
    var sb = new StringBuilder();

    // Air heat colors: 256 entries of [r, g, b, a]
    var airTable = HeatColorMap.GenerateAirColorTable();
    sb.Append("const HEAT_AIR = [");
    for (int i = 0; i < 256; i++)
    {
        var c = airTable[i];
        sb.Append($"[{c[0]},{c[1]},{c[2]},{c[3]}]");
        if (i < 255) sb.Append(',');
    }
    sb.AppendLine("];");

    // Heatmap mode colors: 256 entries of [r, g, b]
    var heatmapTable = HeatColorMap.GenerateHeatmapColorTable();
    sb.Append("const HEAT_MAP = [");
    for (int i = 0; i < 256; i++)
    {
        var c = heatmapTable[i];
        sb.Append($"[{c[0]},{c[1]},{c[2]}]");
        if (i < 255) sb.Append(',');
    }
    sb.AppendLine("];");

    // Furnace gradients: 4 directions × 8 positions, each [r, g, b]
    sb.AppendLine("const FURNACE_GRAD = {");
    foreach (FurnaceDirection dir in Enum.GetValues<FurnaceDirection>())
    {
        var grad = HeatColorMap.GenerateFurnaceGradient(dir);
        sb.Append($"  {(int)dir}:[");
        for (int i = 0; i < 8; i++)
        {
            sb.Append($"[{grad[i][0]},{grad[i][1]},{grad[i][2]}]");
            if (i < 7) sb.Append(',');
        }
        sb.AppendLine("],");
    }
    sb.AppendLine("};");

    // Furnace material ID constant for JS
    sb.AppendLine($"const MAT_FURNACE = {Materials.Furnace};");
    sb.AppendLine($"const MAT_AIR = {Materials.Air};");

    return sb.ToString();
}
```

**Step 2: Update EmbedScenarioData to include furnace blocks and hasTemperature flag**

```csharp
private static string EmbedScenarioData(List<ScenarioData> scenarios)
{
    var sb = new StringBuilder();
    sb.AppendLine("const SCENARIOS = [");
    for (int i = 0; i < scenarios.Count; i++)
    {
        var s = scenarios[i];
        sb.Append("  {");
        sb.Append($"name:\"{EscapeJs(s.Name)}\",");
        sb.Append($"category:\"{EscapeJs(s.Category)}\",");
        sb.Append($"desc:\"{EscapeJs(s.Description)}\",");
        sb.Append($"w:{s.Width},h:{s.Height},frames:{s.FrameCount},");
        sb.Append($"hasTemp:{(s.HasTemperature ? "true" : "false")},");

        // Furnace block metadata
        if (s.FurnaceBlocks != null && s.FurnaceBlocks.Length > 0)
        {
            sb.Append("furnaces:[");
            for (int f = 0; f < s.FurnaceBlocks.Length; f++)
            {
                var fb = s.FurnaceBlocks[f];
                sb.Append($"[{fb.GridX},{fb.GridY},{fb.Direction}]");
                if (f < s.FurnaceBlocks.Length - 1) sb.Append(',');
            }
            sb.Append("],");
        }

        sb.Append($"data:\"");
        sb.Append(s.CompressedBase64);
        sb.Append("\"}");
        if (i < scenarios.Count - 1) sb.Append(',');
        sb.AppendLine();
    }
    sb.AppendLine("];");
    return sb.ToString();
}
```

**Step 3: Wire EmbedHeatTables into Export method**

In the `Export()` method, add the call after `EmbedColorTable`:

```csharp
sb.Append(EmbedColorTable(colorTable));
sb.Append(EmbedHeatTables());  // NEW
sb.Append(EmbedScenarioData(captured));
```

**Step 4: Build and verify**

Run: `dotnet build src/ParticularLLM.Viewer`
Expected: Builds successfully.

**Step 5: Commit**

```bash
git add src/ParticularLLM.Viewer/HtmlExporter.cs
git commit -m "feat: embed heat color tables and furnace metadata in viewer HTML"
```

---

### Task 5: Update viewer.html — parse v2 frame data

**Files:**
- Modify: `src/ParticularLLM.Viewer/HtmlExporter.cs` (the PlayerScript() method, which contains all the viewer JavaScript, starting ~line 323)

All JavaScript changes happen inside the `PlayerScript()` C# string in `HtmlExporter.cs`.

**Step 1: Update decompress/loadScenario to handle 2-byte frame data**

The viewer needs to know bytes-per-cell. For v2 scenarios (`hasTemp: true`), each cell is 2 bytes. For v1, it's 1 byte.

Add to the global state section:

```javascript
let bytesPerCell = 1; // 1 for v1, 2 for v2 (materialId + temperature)
let heatMode = false; // Toggle with H key
let furnaceLookup = null; // Map<cellIndex, [r,g,b]> for furnace gradient
```

In `loadScenario()`, after decompressing, set bytesPerCell:

```javascript
bytesPerCell = s.hasTemp ? 2 : 1;
```

Also in `loadScenario()`, build the furnace gradient lookup:

```javascript
// Build furnace gradient lookup
furnaceLookup = new Map();
if (s.furnaces && typeof FURNACE_GRAD !== 'undefined') {
  for (const [gx, gy, dir] of s.furnaces) {
    const grad = FURNACE_GRAD[dir];
    // dir: 0=Up, 1=Right, 2=Down, 3=Left
    for (let ly = 0; ly < 8; ly++) {
      for (let lx = 0; lx < 8; lx++) {
        const cx = gx + lx;
        const cy = gy + ly;
        if (cx < 0 || cx >= s.w || cy < 0 || cy >= s.h) continue;
        const idx = cy * s.w + cx;
        // Gradient position depends on direction
        let pos;
        if (dir === 1) pos = lx;       // Right
        else if (dir === 3) pos = 7 - lx; // Left
        else if (dir === 2) pos = ly;   // Down
        else pos = 7 - ly;              // Up
        furnaceLookup.set(idx, grad[pos]);
      }
    }
  }
}
```

**Step 2: Update renderFrame() for heat visualization**

Replace the rendering loop:

```javascript
function renderFrame() {
  const s = SCENARIOS[currentIdx];
  const cellCount = s.w * s.h;
  const frameOffset = currentFrame * cellCount * bytesPerCell;
  const pixels = imageData.data;

  const counts = new Map();
  for (let i = 0; i < cellCount; i++) {
    const matId = frameData[frameOffset + i * bytesPerCell];
    const temp = bytesPerCell >= 2 ? frameData[frameOffset + i * bytesPerCell + 1] : 20;
    const ci = matId < COLORS.length ? matId : 0;
    const pi = i * 4;

    if (heatMode && typeof HEAT_MAP !== 'undefined') {
      // Heatmap toggle: all cells colored by temperature
      const hc = HEAT_MAP[temp];
      pixels[pi]     = hc[0];
      pixels[pi + 1] = hc[1];
      pixels[pi + 2] = hc[2];
      pixels[pi + 3] = 255;
    } else if (matId === MAT_FURNACE && furnaceLookup && furnaceLookup.has(i)) {
      // Furnace gradient
      const fc = furnaceLookup.get(i);
      pixels[pi]     = fc[0];
      pixels[pi + 1] = fc[1];
      pixels[pi + 2] = fc[2];
      pixels[pi + 3] = 255;
    } else if (matId === MAT_AIR && typeof HEAT_AIR !== 'undefined') {
      // Air cell with heat glow
      const hc = HEAT_AIR[temp];
      if (hc[3] > 0) {
        // Blend heat color over air background
        const a = hc[3] / 255;
        pixels[pi]     = Math.round(COLORS[0][0] * (1 - a) + hc[0] * a);
        pixels[pi + 1] = Math.round(COLORS[0][1] * (1 - a) + hc[1] * a);
        pixels[pi + 2] = Math.round(COLORS[0][2] * (1 - a) + hc[2] * a);
        pixels[pi + 3] = 255;
      } else {
        pixels[pi]     = COLORS[0][0];
        pixels[pi + 1] = COLORS[0][1];
        pixels[pi + 2] = COLORS[0][2];
        pixels[pi + 3] = 255;
      }
    } else {
      // Normal material color
      pixels[pi]     = COLORS[ci][0];
      pixels[pi + 1] = COLORS[ci][1];
      pixels[pi + 2] = COLORS[ci][2];
      pixels[pi + 3] = 255;
    }

    if (matId !== 0) counts.set(matId, (counts.get(matId) || 0) + 1);
  }

  // (rest of renderFrame stays the same: offscreen draw, frame display, legend)
  // ...
}
```

**Step 3: Add H key binding for heatmap toggle**

In the keyboard handler, add:

```javascript
case 'h': case 'H':
  heatMode = !heatMode;
  renderFrame();
  break;
```

**Step 4: Add HEAT indicator in the header when heatmap mode is active**

In `renderFrame()`, after the legend update, add:

```javascript
// Heat mode indicator
const title = document.getElementById('scenario-title');
const baseName = SCENARIOS[currentIdx].name;
title.textContent = heatMode ? baseName + ' [HEAT]' : baseName;
```

**Step 5: Add heat mode button to the controls bar**

In the HTML body, add a button in the controls section (in `HtmlBody()` method):

```html
<button id="btn-heat" onclick="toggleHeat()" title="H">🌡 Heat [H]</button>
```

Add the `toggleHeat()` function:

```javascript
function toggleHeat() {
  heatMode = !heatMode;
  document.getElementById('btn-heat').className = heatMode ? 'active' : '';
  renderFrame();
}
```

**Step 6: Build and run viewer export to verify**

Run: `dotnet build src/ParticularLLM.Viewer`
Expected: Builds. Then run a full export to generate viewer.html and manually verify in browser.

Run: `dotnet run --project src/ParticularLLM.Viewer -- --html --all`

**Step 7: Commit**

```bash
git add src/ParticularLLM.Viewer/HtmlExporter.cs
git commit -m "feat: heat visualization in viewer - air glow, furnace gradient, heatmap toggle"
```

---

### Task 6: Run full test suite and verify no regressions

**Files:** None (verification only)

**Step 1: Run all tests**

Run: `dotnet test tests/ParticularLLM.Tests -v n`
Expected: All existing tests pass. The capture format change should not break any test assertions (tests don't read .bin files, only the viewer does).

**Step 2: Run viewer export**

Run: `dotnet run --project src/ParticularLLM.Viewer -- --html --all`
Expected: Generates `viewer.html` successfully.

**Step 3: Manual browser verification**

Open `viewer.html` in a browser and check:
1. Non-heat scenarios (sand, water, etc.) look identical to before
2. Furnace scenarios show directional gradient on furnace blocks
3. Air cells near furnaces show faint heat glow
4. Pressing H toggles heatmap mode
5. HEAT indicator shows in title when active
6. Pressing H again returns to normal view

**Step 4: Commit any fixes**

If any issues found during manual testing, fix and commit.

---

### Task 7: Final integration — re-export and import review

**Step 1: Re-run all tests with capture enabled to regenerate .bin files**

This ensures all captures use the new v2 format with temperature data.

Run: `dotnet test tests/ParticularLLM.Tests -v n`

**Step 2: Export viewer HTML**

Run: `dotnet run --project src/ParticularLLM.Viewer -- --html --all`

**Step 3: Commit all changes**

```bash
git add -A
git commit -m "feat: heat visualization complete - air glow, furnace gradients, heatmap toggle"
```
