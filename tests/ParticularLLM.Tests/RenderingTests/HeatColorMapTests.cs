using ParticularLLM;
using ParticularLLM.Rendering;

namespace ParticularLLM.Tests.RenderingTests;

/// <summary>
/// Tests for HeatColorMap — the shared color computation for heat visualization.
///
/// English rules:
/// 1. Air colors are semi-transparent glows. Ambient temperature = fully transparent.
///    Hot = warm hues (yellow/orange/red/white), cold = blue. Alpha capped at ~115.
/// 2. Heatmap colors are full opacity (alpha = 255). Same spectrum but solid.
///    Ambient = near-black, hot = red/white, cold = blue.
/// 3. Furnace gradient runs from dark brown (back) to bright orange (emission edge)
///    with exponential weighting (power 2.5). Direction determines gradient axis.
/// </summary>
public class HeatColorMapTests
{
    // ===== AIR COLOR TESTS =====

    [Fact]
    public void AirColor_AtAmbient_IsFullyTransparent()
    {
        var color = HeatColorMap.TemperatureToAirColor(HeatSettings.AmbientTemperature);
        Assert.Equal(0, color.a);
    }

    [Fact]
    public void AirColor_AboveAmbient_HasWarmHue()
    {
        // Well above ambient — should be warm (r > b)
        var color = HeatColorMap.TemperatureToAirColor(150);
        Assert.True(color.r > color.b, $"Hot air should have r > b, got r={color.r} b={color.b}");
        Assert.True(color.a > 0, "Hot air should have visible alpha");
    }

    [Fact]
    public void AirColor_BelowAmbient_HasCoolHue()
    {
        // Below ambient — should be cool (b > r)
        var color = HeatColorMap.TemperatureToAirColor(5);
        Assert.True(color.b > color.r, $"Cold air should have b > r, got r={color.r} b={color.b}");
        Assert.True(color.a > 0, "Cold air should have visible alpha");
    }

    [Fact]
    public void AirColor_MaxOpacityCapped()
    {
        // Even at max temperature, alpha should be capped
        for (int t = 0; t <= 255; t++)
        {
            var color = HeatColorMap.TemperatureToAirColor((byte)t);
            Assert.True(color.a <= 128,
                $"Air alpha at temp {t} should be <= 128, got {color.a}");
        }
    }

    [Fact]
    public void AirColor_OpacityIncreasesWithDistanceFromAmbient_Hot()
    {
        // Farther from ambient = more visible
        var close = HeatColorMap.TemperatureToAirColor((byte)(HeatSettings.AmbientTemperature + 10));
        var far = HeatColorMap.TemperatureToAirColor((byte)(HeatSettings.AmbientTemperature + 100));
        Assert.True(far.a > close.a,
            $"Farther from ambient should have higher alpha: close={close.a} far={far.a}");
    }

    [Fact]
    public void AirColor_OpacityIncreasesWithDistanceFromAmbient_Cold()
    {
        // Farther below ambient = more visible
        var close = HeatColorMap.TemperatureToAirColor((byte)(HeatSettings.AmbientTemperature - 5));
        var far = HeatColorMap.TemperatureToAirColor((byte)(HeatSettings.AmbientTemperature - 15));
        Assert.True(far.a > close.a,
            $"Farther from ambient should have higher alpha: close={close.a} far={far.a}");
    }

    [Fact]
    public void AirColor_AtMaxTemp_ApproachesWhite()
    {
        // At 255 the glow should be near-white (all channels high)
        var color = HeatColorMap.TemperatureToAirColor(255);
        Assert.True(color.r > 200, $"Max temp air r should be high, got {color.r}");
        Assert.True(color.g > 200, $"Max temp air g should be high, got {color.g}");
        Assert.True(color.b > 200, $"Max temp air b should be high, got {color.b}");
    }

    // ===== HEATMAP COLOR TESTS =====

    [Fact]
    public void HeatmapColor_AtAmbient_IsDark_FullOpacity()
    {
        var color = HeatColorMap.TemperatureToHeatmapColor(HeatSettings.AmbientTemperature);
        Assert.Equal(255, color.a);
        Assert.True(color.r <= 30 && color.g <= 30 && color.b <= 30,
            $"Ambient heatmap should be near-black, got ({color.r},{color.g},{color.b})");
    }

    [Fact]
    public void HeatmapColor_Hot_IsRed_FullOpacity()
    {
        // At a hot temperature (e.g. 200), should be red-ish
        var color = HeatColorMap.TemperatureToHeatmapColor(200);
        Assert.Equal(255, color.a);
        Assert.True(color.r > color.b, $"Hot heatmap should have r > b, got r={color.r} b={color.b}");
    }

    [Fact]
    public void HeatmapColor_Cold_IsBlue_FullOpacity()
    {
        var color = HeatColorMap.TemperatureToHeatmapColor(5);
        Assert.Equal(255, color.a);
        Assert.True(color.b > color.r, $"Cold heatmap should have b > r, got r={color.r} b={color.b}");
    }

    [Fact]
    public void HeatmapColor_MaxTemp_ApproachesWhite()
    {
        var color = HeatColorMap.TemperatureToHeatmapColor(255);
        Assert.Equal(255, color.a);
        Assert.True(color.r > 200 && color.g > 200 && color.b > 200,
            $"Max temp heatmap should be near-white, got ({color.r},{color.g},{color.b})");
    }

    // ===== FURNACE GRADIENT TESTS =====

    [Fact]
    public void FurnaceGradient_Right_EmissionEdgeIsBrightest()
    {
        // Direction Right: gradient along X, emission at x=7
        var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 0, 4);
        var emission = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 7, 4);
        int backBrightness = back.r + back.g + back.b;
        int emissionBrightness = emission.r + emission.g + emission.b;
        Assert.True(emissionBrightness > backBrightness,
            $"Emission edge should be brighter: back={backBrightness} emission={emissionBrightness}");
    }

    [Fact]
    public void FurnaceGradient_Left_EmissionEdgeIsBrightest()
    {
        // Direction Left: gradient along X, emission at x=0
        var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Left, 7, 4);
        var emission = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Left, 0, 4);
        int backBrightness = back.r + back.g + back.b;
        int emissionBrightness = emission.r + emission.g + emission.b;
        Assert.True(emissionBrightness > backBrightness,
            $"Emission edge should be brighter: back={backBrightness} emission={emissionBrightness}");
    }

    [Fact]
    public void FurnaceGradient_Down_EmissionEdgeIsBrightest()
    {
        // Direction Down: gradient along Y, emission at y=7
        var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Down, 4, 0);
        var emission = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Down, 4, 7);
        int backBrightness = back.r + back.g + back.b;
        int emissionBrightness = emission.r + emission.g + emission.b;
        Assert.True(emissionBrightness > backBrightness,
            $"Emission edge should be brighter: back={backBrightness} emission={emissionBrightness}");
    }

    [Fact]
    public void FurnaceGradient_Up_EmissionEdgeIsBrightest()
    {
        // Direction Up: gradient along Y, emission at y=0
        var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Up, 4, 7);
        var emission = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Up, 4, 0);
        int backBrightness = back.r + back.g + back.b;
        int emissionBrightness = emission.r + emission.g + emission.b;
        Assert.True(emissionBrightness > backBrightness,
            $"Emission edge should be brighter: back={backBrightness} emission={emissionBrightness}");
    }

    [Fact]
    public void FurnaceGradient_ExponentialWeighting_SecondHalfChangesMore()
    {
        // With power 2.5 exponential, the first 4 pixels should change less than the last 4
        var colors = new Color32[8];
        for (int i = 0; i < 8; i++)
            colors[i] = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, i, 0);

        int firstHalfDelta = Brightness(colors[3]) - Brightness(colors[0]);
        int secondHalfDelta = Brightness(colors[7]) - Brightness(colors[4]);

        Assert.True(secondHalfDelta > firstHalfDelta,
            $"Second half should change more: first={firstHalfDelta} second={secondHalfDelta}");
    }

    [Fact]
    public void FurnaceGradient_HorizontalDirection_YDoesNotAffect()
    {
        // For Right direction, changing Y should not change the color
        var colorY0 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 0);
        var colorY5 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 5);
        var colorY7 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 3, 7);

        Assert.Equal(colorY0.r, colorY5.r);
        Assert.Equal(colorY0.g, colorY5.g);
        Assert.Equal(colorY0.b, colorY5.b);

        Assert.Equal(colorY0.r, colorY7.r);
        Assert.Equal(colorY0.g, colorY7.g);
        Assert.Equal(colorY0.b, colorY7.b);
    }

    [Fact]
    public void FurnaceGradient_VerticalDirection_XDoesNotAffect()
    {
        // For Down direction, changing X should not change the color
        var colorX0 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Down, 0, 3);
        var colorX5 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Down, 5, 3);
        var colorX7 = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Down, 7, 3);

        Assert.Equal(colorX0.r, colorX5.r);
        Assert.Equal(colorX0.g, colorX5.g);
        Assert.Equal(colorX0.b, colorX5.b);

        Assert.Equal(colorX0.r, colorX7.r);
        Assert.Equal(colorX0.g, colorX7.g);
        Assert.Equal(colorX0.b, colorX7.b);
    }

    [Fact]
    public void FurnaceGradient_BackColor_IsDarkBrown()
    {
        // Back of furnace should be near dark brown (80, 40, 20)
        var back = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 0, 0);
        Assert.InRange(back.r, 70, 90);
        Assert.InRange(back.g, 30, 50);
        Assert.InRange(back.b, 10, 30);
    }

    [Fact]
    public void FurnaceGradient_EmissionColor_IsBrightOrange()
    {
        // Emission edge should be near bright orange (220, 140, 40)
        var emission = HeatColorMap.FurnaceGradientColor(FurnaceDirection.Right, 7, 0);
        Assert.InRange(emission.r, 200, 240);
        Assert.InRange(emission.g, 120, 160);
        Assert.InRange(emission.b, 20, 60);
    }

    // ===== TABLE GENERATION TESTS =====

    [Fact]
    public void GenerateAirColorTable_Has256Entries()
    {
        var table = HeatColorMap.GenerateAirColorTable();
        Assert.Equal(256, table.Length);
        foreach (var entry in table)
            Assert.Equal(4, entry.Length);
    }

    [Fact]
    public void GenerateHeatmapColorTable_Has256Entries()
    {
        var table = HeatColorMap.GenerateHeatmapColorTable();
        Assert.Equal(256, table.Length);
        foreach (var entry in table)
            Assert.Equal(4, entry.Length);
    }

    [Fact]
    public void GenerateFurnaceGradient_Has8Entries()
    {
        var table = HeatColorMap.GenerateFurnaceGradient(FurnaceDirection.Right);
        Assert.Equal(8, table.Length);
        foreach (var entry in table)
            Assert.Equal(3, entry.Length);
    }

    [Fact]
    public void GenerateAirColorTable_MatchesDirectMethod()
    {
        var table = HeatColorMap.GenerateAirColorTable();
        for (int t = 0; t < 256; t++)
        {
            var direct = HeatColorMap.TemperatureToAirColor((byte)t);
            Assert.Equal(direct.r, table[t][0]);
            Assert.Equal(direct.g, table[t][1]);
            Assert.Equal(direct.b, table[t][2]);
            Assert.Equal(direct.a, table[t][3]);
        }
    }

    [Fact]
    public void GenerateHeatmapColorTable_MatchesDirectMethod()
    {
        var table = HeatColorMap.GenerateHeatmapColorTable();
        for (int t = 0; t < 256; t++)
        {
            var direct = HeatColorMap.TemperatureToHeatmapColor((byte)t);
            Assert.Equal(direct.r, table[t][0]);
            Assert.Equal(direct.g, table[t][1]);
            Assert.Equal(direct.b, table[t][2]);
            Assert.Equal(direct.a, table[t][3]);
        }
    }

    [Fact]
    public void GenerateFurnaceGradient_MatchesDirectMethod()
    {
        foreach (FurnaceDirection dir in Enum.GetValues<FurnaceDirection>())
        {
            var table = HeatColorMap.GenerateFurnaceGradient(dir);
            for (int i = 0; i < 8; i++)
            {
                // For the gradient table, the index is the position along the gradient axis
                // We need to figure out the corresponding localX/localY
                int lx, ly;
                switch (dir)
                {
                    case FurnaceDirection.Right:
                        lx = i; ly = 0; break;
                    case FurnaceDirection.Left:
                        lx = i; ly = 0; break;
                    case FurnaceDirection.Down:
                        lx = 0; ly = i; break;
                    case FurnaceDirection.Up:
                        lx = 0; ly = i; break;
                    default:
                        throw new InvalidOperationException();
                }
                var direct = HeatColorMap.FurnaceGradientColor(dir, lx, ly);
                Assert.Equal(direct.r, table[i][0]);
                Assert.Equal(direct.g, table[i][1]);
                Assert.Equal(direct.b, table[i][2]);
            }
        }
    }

    private static int Brightness(Color32 c) => c.r + c.g + c.b;
}
