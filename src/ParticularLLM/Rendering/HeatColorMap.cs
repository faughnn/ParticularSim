namespace ParticularLLM.Rendering;

/// <summary>
/// Computes all heat visualization colors for the simulation.
/// Single source of truth — used by both the test harness HTML viewer and Unity.
/// </summary>
public static class HeatColorMap
{
    private const byte Ambient = HeatSettings.AmbientTemperature; // 20

    // Furnace gradient endpoints
    private const byte BackR = 80, BackG = 40, BackB = 20;
    private const byte EmitR = 220, EmitG = 140, EmitB = 40;

    /// <summary>
    /// Computes the air glow color for a given temperature.
    /// Semi-transparent overlay for normal rendering mode.
    /// At ambient: fully transparent. Alpha scales linearly with distance from ambient, capped at 115.
    /// Below ambient: blue glow. Above ambient: yellow -> orange -> red -> white.
    /// </summary>
    public static Color32 TemperatureToAirColor(byte temperature)
    {
        ComputeHeatColor(temperature, out byte r, out byte g, out byte b);

        // Alpha: linear with distance from ambient, capped at 115
        int distance = Math.Abs(temperature - Ambient);
        int maxDistance = Math.Max(255 - (int)Ambient, (int)Ambient); // 235
        byte alpha = (byte)Math.Min(115, distance * 115 / maxDistance);

        return new Color32(r, g, b, alpha);
    }

    /// <summary>
    /// Computes the heatmap color for a given temperature.
    /// Full-opacity solid color for heatmap toggle mode.
    /// At ambient: near-black (20, 20, 20). Same spectrum as air colors.
    /// </summary>
    public static Color32 TemperatureToHeatmapColor(byte temperature)
    {
        ComputeHeatColor(temperature, out byte r, out byte g, out byte b);

        // At ambient, use near-black instead of the computed color
        if (temperature == Ambient)
            return new Color32(20, 20, 20, 255);

        return new Color32(r, g, b, 255);
    }

    /// <summary>
    /// Computes the shared color spectrum for a temperature.
    /// Below ambient: blue. Above ambient: yellow -> orange -> red -> white.
    /// </summary>
    private static void ComputeHeatColor(byte temperature, out byte r, out byte g, out byte b)
    {
        if (temperature <= Ambient)
        {
            // Cold: blue glow. Intensity scales with how far below ambient.
            // At temp 0: full blue (40, 60, 255)
            // At ambient: black (0, 0, 0)
            float t = Ambient > 0 ? (float)(Ambient - temperature) / Ambient : 0f;
            r = (byte)(40 * t);
            g = (byte)(60 * t);
            b = (byte)(255 * t);
        }
        else
        {
            // Hot: yellow -> orange -> red -> white
            // Normalize to 0..1 range above ambient
            float t = (float)(temperature - Ambient) / (255 - Ambient);

            if (t < 0.33f)
            {
                // Yellow zone: ramp from black to yellow
                float s = t / 0.33f;
                r = (byte)(255 * s);
                g = (byte)(200 * s);
                b = 0;
            }
            else if (t < 0.66f)
            {
                // Orange zone: yellow to red-orange
                float s = (t - 0.33f) / 0.33f;
                r = 255;
                g = (byte)(200 - 150 * s); // 200 -> 50
                b = 0;
            }
            else
            {
                // Red to white zone
                float s = (t - 0.66f) / 0.34f;
                r = 255;
                g = (byte)(50 + 205 * s);  // 50 -> 255
                b = (byte)(255 * s);        // 0 -> 255
            }
        }
    }

    /// <summary>
    /// Computes the furnace block gradient color for a pixel at local coordinates.
    /// Gradient runs from dark brown at the back wall to bright orange at the emission edge.
    /// Uses exponential curve (power 2.5) — most visual change in last 2-3 pixels.
    /// </summary>
    /// <param name="direction">Furnace emission direction</param>
    /// <param name="localX">X within the 8x8 furnace block (0-7)</param>
    /// <param name="localY">Y within the 8x8 furnace block (0-7)</param>
    public static Color32 FurnaceGradientColor(FurnaceDirection direction, int localX, int localY)
    {
        // Determine gradient position (0 = back, 7 = emission edge)
        int pos;
        switch (direction)
        {
            case FurnaceDirection.Right:
                pos = localX;        // 0=back, 7=emission
                break;
            case FurnaceDirection.Left:
                pos = 7 - localX;    // 7=back, 0=emission
                break;
            case FurnaceDirection.Down:
                pos = localY;        // 0=back, 7=emission
                break;
            case FurnaceDirection.Up:
                pos = 7 - localY;    // 7=back, 0=emission
                break;
            default:
                pos = 0;
                break;
        }

        // Exponential curve: t = (pos/7)^2.5
        // Weighted toward dark — most change in the last few pixels
        double normalized = pos / 7.0;
        double t = Math.Pow(normalized, 2.5);

        byte r = (byte)(BackR + (EmitR - BackR) * t);
        byte g = (byte)(BackG + (EmitG - BackG) * t);
        byte b = (byte)(BackB + (EmitB - BackB) * t);

        return new Color32(r, g, b, 255);
    }

    /// <summary>
    /// Pre-generates a 256-entry lookup table of air glow colors.
    /// Each entry is [r, g, b, a]. Used for embedding in HTML/JS viewers.
    /// </summary>
    public static byte[][] GenerateAirColorTable()
    {
        var table = new byte[256][];
        for (int t = 0; t < 256; t++)
        {
            var c = TemperatureToAirColor((byte)t);
            table[t] = new byte[] { c.r, c.g, c.b, c.a };
        }
        return table;
    }

    /// <summary>
    /// Pre-generates a 256-entry lookup table of heatmap colors.
    /// Each entry is [r, g, b, a]. Used for embedding in HTML/JS viewers.
    /// </summary>
    public static byte[][] GenerateHeatmapColorTable()
    {
        var table = new byte[256][];
        for (int t = 0; t < 256; t++)
        {
            var c = TemperatureToHeatmapColor((byte)t);
            table[t] = new byte[] { c.r, c.g, c.b, c.a };
        }
        return table;
    }

    /// <summary>
    /// Pre-generates an 8-entry lookup table for a furnace gradient in the given direction.
    /// Each entry is [r, g, b]. Index corresponds to position along the gradient axis.
    /// For Right: index = localX. For Left: index = localX. For Down: index = localY. For Up: index = localY.
    /// </summary>
    public static byte[][] GenerateFurnaceGradient(FurnaceDirection direction)
    {
        var table = new byte[8][];
        for (int i = 0; i < 8; i++)
        {
            int lx, ly;
            switch (direction)
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
                    lx = 0; ly = 0; break;
            }
            var c = FurnaceGradientColor(direction, lx, ly);
            table[i] = new byte[] { c.r, c.g, c.b };
        }
        return table;
    }
}
