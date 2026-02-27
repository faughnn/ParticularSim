namespace ParticularLLM;

/// <summary>
/// Double-buffered heat diffusion between adjacent cells.
/// Heat flows from hot to cold cells. Only cells with the ConductsHeat flag participate.
/// All cells gradually cool toward ambient temperature.
///
/// Pipeline position: after furnace heating, before cell simulation.
/// </summary>
public class HeatTransferSystem
{
    // Temporary buffer for double-buffered temperature updates
    private byte[] tempBuffer = Array.Empty<byte>();

    /// <summary>
    /// Run one frame of heat diffusion across the entire world.
    /// Uses double buffering: reads current temperatures, writes to temp buffer, then copies back.
    /// </summary>
    public void SimulateHeat(CellWorld world)
    {
        int width = world.width;
        int height = world.height;
        int totalCells = width * height;
        var cells = world.cells;
        var materials = world.materials;

        // Ensure temp buffer is allocated
        if (tempBuffer.Length < totalCells)
            tempBuffer = new byte[totalCells];

        // Pass 1: compute new temperatures into temp buffer
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                Cell cell = cells[idx];
                MaterialDef mat = materials[cell.materialId];

                // Non-conducting materials keep their temperature (no diffusion)
                if ((mat.flags & MaterialFlags.ConductsHeat) == 0)
                {
                    tempBuffer[idx] = cell.temperature;
                    continue;
                }

                // Average temperature with conducting neighbors
                int totalTemp = cell.temperature;
                int conductingNeighbors = 1;

                // Check 4 cardinal neighbors
                if (x > 0)
                    AddNeighborTemp(cells, materials, idx - 1, ref totalTemp, ref conductingNeighbors);
                if (x < width - 1)
                    AddNeighborTemp(cells, materials, idx + 1, ref totalTemp, ref conductingNeighbors);
                if (y > 0)
                    AddNeighborTemp(cells, materials, idx - width, ref totalTemp, ref conductingNeighbors);
                if (y < height - 1)
                    AddNeighborTemp(cells, materials, idx + width, ref totalTemp, ref conductingNeighbors);

                // Blend toward neighbor average
                int avgTemp = totalTemp / conductingNeighbors;
                int newTemp = cell.temperature +
                    (avgTemp - cell.temperature) * HeatSettings.ConductionRate / 256;

                // Cool toward ambient
                if (newTemp > HeatSettings.AmbientTemperature)
                    newTemp = Math.Max(HeatSettings.AmbientTemperature,
                        newTemp - HeatSettings.CoolingRate);
                else if (newTemp < HeatSettings.AmbientTemperature)
                    newTemp = Math.Min(HeatSettings.AmbientTemperature,
                        newTemp + HeatSettings.CoolingRate);

                tempBuffer[idx] = (byte)Math.Clamp(newTemp, 0, 255);
            }
        }

        // Pass 2: copy new temperatures back to cells
        for (int i = 0; i < totalCells; i++)
        {
            cells[i].temperature = tempBuffer[i];
        }
    }

    private static void AddNeighborTemp(Cell[] cells, MaterialDef[] materials,
        int neighborIdx, ref int totalTemp, ref int count)
    {
        Cell neighbor = cells[neighborIdx];
        MaterialDef neighborMat = materials[neighbor.materialId];

        if ((neighborMat.flags & MaterialFlags.ConductsHeat) != 0)
        {
            totalTemp += neighbor.temperature;
            count++;
        }
    }
}
