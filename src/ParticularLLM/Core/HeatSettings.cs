namespace ParticularLLM;

public static class HeatSettings
{
    /// <summary>Temperature that all cells cool toward over time (room temperature).</summary>
    public const byte AmbientTemperature = 20;

    /// <summary>
    /// Accumulator threshold for sub-integer heating and cooling.
    /// Both heating and cooling accumulators increment per frame; when they reach this
    /// threshold, temperature changes by 1 degree. Higher = slower temperature changes.
    /// Time constant τ = Threshold / CoolingFactor frames.
    /// At 2048 with CoolingFactor=1: τ ≈ 34 sec at 60fps, 95% equilibrium in ~1.7 min.
    /// </summary>
    public const int AccumulatorThreshold = 2048;

    /// <summary>
    /// Proportional cooling factor. Per frame, cooling accumulator gains (temp - ambient) * CoolingFactor.
    /// When accumulator reaches AccumulatorThreshold, temperature drops 1 degree.
    /// </summary>
    public const int CoolingFactor = 1;

    /// <summary>
    /// Furnace heating accumulator increment per frame per emission source.
    /// Each frame a cell is in a furnace emission zone, its heating accumulator gains this value.
    /// When accumulator reaches AccumulatorThreshold, temperature rises 1 degree.
    /// Equilibrium: T = Ambient + N * FurnaceHeatRate / CoolingFactor
    /// where N = number of emission sources hitting the cell.
    /// With rate=102, cooling=1: 1-wall ~122°, 2-wall ~224°.
    /// </summary>
    public const int FurnaceHeatRate = 102;

    /// <summary>
    /// How many cells deep each furnace block emits heat (uniform, no falloff).
    /// With depth 4, two facing blocks across an 8-cell gap overlap perfectly in the middle.
    /// </summary>
    public const int FurnaceEmissionDepth = 4;
}
