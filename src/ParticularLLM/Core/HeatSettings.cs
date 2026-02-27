namespace ParticularLLM;

public static class HeatSettings
{
    /// <summary>Temperature that all cells cool toward over time (room temperature).</summary>
    public const byte AmbientTemperature = 20;

    /// <summary>Temperature change per frame toward ambient (1 degree per frame).</summary>
    public const int CoolingRate = 1;

    /// <summary>
    /// Fraction of the temperature difference transferred to/from neighbors per frame.
    /// 64/256 = 25%. Must be &lt; 128 (50%) for numerical stability with in-place updates.
    /// Uses integer math: newTemp = oldTemp + (avgNeighbor - oldTemp) * ConductionRate / 256.
    /// </summary>
    public const int ConductionRate = 64;
}
