namespace ParticularLLM;

public static class HeatSettings
{
    /// <summary>Temperature that all cells cool toward over time (room temperature).</summary>
    public const byte AmbientTemperature = 20;

    /// <summary>
    /// Proportional cooling factor. Per frame, cooling accumulator gains (temp - ambient) * CoolingFactor.
    /// When accumulator reaches 256, temperature drops 1 degree. Uses ushort accumulator for sub-integer precision.
    /// Value of 3 gives equilibrium: 1 furnace wall ~105°, 2 walls ~190°, 3 walls 255°.
    /// </summary>
    public const int CoolingFactor = 3;

    /// <summary>Degrees added per emission pulse to the cell adjacent to a furnace block's facing direction.</summary>
    public const int FurnaceHeatOutput = 1;

    /// <summary>Frames between furnace heat emission pulses. Higher = slower heating.</summary>
    public const int FurnaceHeatInterval = 1;
}
