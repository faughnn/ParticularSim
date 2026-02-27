using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Manages all furnaces in the world.
/// Furnaces are rectangular structures with 1-cell-thick walls and a hollow interior.
/// When heating, the furnace increases the temperature of all non-Air interior cells
/// each frame. Phase changes are handled by the existing material reactions system.
/// </summary>
public class FurnaceManager
{
    private readonly CellWorld world;
    private readonly int worldWidth;
    private readonly int worldHeight;

    private readonly List<FurnaceStructure> furnaces = new();
    private readonly Dictionary<ushort, int> idToIndex = new();
    private ushort nextId = 1;

    public IReadOnlyList<FurnaceStructure> Furnaces => furnaces;

    public FurnaceManager(CellWorld world)
    {
        this.world = world;
        this.worldWidth = world.width;
        this.worldHeight = world.height;
    }

    /// <summary>
    /// Places a furnace at the specified cell position with the given dimensions.
    /// Walls are 1 cell thick. Interior is left as-is (typically Air).
    /// Returns the furnace ID, or 0 if placement failed.
    /// </summary>
    public ushort PlaceFurnace(int x, int y, int width, int height,
        byte heatOutput = 5, byte maxTemp = 255)
    {
        // Minimum size: 3x3 (1 wall + 1 interior + 1 wall)
        if (width < 3 || height < 3)
            return 0;

        // Check bounds
        if (x < 0 || y < 0 || x + width > worldWidth || y + height > worldHeight)
            return 0;

        // Check all perimeter cells are Air (placement requires clear space)
        for (int cy = y; cy < y + height; cy++)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                // Only check perimeter cells (walls)
                bool isPerimeter = cx == x || cx == x + width - 1 ||
                                   cy == y || cy == y + height - 1;
                if (isPerimeter)
                {
                    byte existing = world.GetCell(cx, cy);
                    if (existing != Materials.Air)
                        return 0;
                }
            }
        }

        // Place furnace wall material on perimeter
        for (int cy = y; cy < y + height; cy++)
        {
            for (int cx = x; cx < x + width; cx++)
            {
                bool isPerimeter = cx == x || cx == x + width - 1 ||
                                   cy == y || cy == y + height - 1;
                if (isPerimeter)
                {
                    world.SetCell(cx, cy, Materials.Furnace);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        ushort id = nextId++;
        var furnace = new FurnaceStructure
        {
            id = id,
            x = x,
            y = y,
            width = width,
            height = height,
            heatOutput = heatOutput,
            maxTemp = maxTemp,
            state = FurnaceState.Heating,
        };

        idToIndex[id] = furnaces.Count;
        furnaces.Add(furnace);

        // Mark chunks as having structure
        StructureUtils.MarkChunksHasStructure(world, x, y, width, height);

        return id;
    }

    /// <summary>
    /// Removes a furnace by ID. Clears all wall cells to Air.
    /// Returns true if the furnace was found and removed.
    /// </summary>
    public bool RemoveFurnace(ushort id)
    {
        if (!idToIndex.TryGetValue(id, out int index))
            return false;

        var furnace = furnaces[index];

        // Clear perimeter cells
        for (int cy = furnace.y; cy < furnace.y + furnace.height; cy++)
        {
            for (int cx = furnace.x; cx < furnace.x + furnace.width; cx++)
            {
                bool isPerimeter = cx == furnace.x || cx == furnace.x + furnace.width - 1 ||
                                   cy == furnace.y || cy == furnace.y + furnace.height - 1;
                if (isPerimeter)
                {
                    world.SetCell(cx, cy, Materials.Air);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        // Remove from list (swap with last for O(1) removal)
        int lastIndex = furnaces.Count - 1;
        if (index != lastIndex)
        {
            var lastFurnace = furnaces[lastIndex];
            furnaces[index] = lastFurnace;
            idToIndex[lastFurnace.id] = index;
        }
        furnaces.RemoveAt(lastIndex);
        idToIndex.Remove(id);

        // Update chunk structure flags
        StructureUtils.UpdateChunksStructureFlag(
            world, furnace.x, furnace.y, furnace.width, furnace.height,
            worldWidth, worldHeight,
            (cx, cy) => world.GetCell(cx, cy) == Materials.Furnace);

        return true;
    }

    /// <summary>
    /// Sets the state of a furnace by ID.
    /// </summary>
    public bool SetState(ushort id, FurnaceState state)
    {
        if (!idToIndex.TryGetValue(id, out int index))
            return false;

        var furnace = furnaces[index];
        furnace.state = state;
        furnaces[index] = furnace;
        return true;
    }

    /// <summary>
    /// Gets a furnace by ID.
    /// </summary>
    public FurnaceStructure? GetFurnace(ushort id)
    {
        if (!idToIndex.TryGetValue(id, out int index))
            return null;
        return furnaces[index];
    }

    /// <summary>
    /// Applies heat to all furnace interiors. Called once per frame,
    /// before heat transfer and cell simulation.
    /// </summary>
    public void SimulateFurnaces(CellWorld world)
    {
        for (int i = 0; i < furnaces.Count; i++)
        {
            var furnace = furnaces[i];
            if (furnace.state != FurnaceState.Heating)
                continue;

            // Interior bounds (exclude 1-cell-thick walls)
            int minX = furnace.x + 1;
            int maxX = furnace.x + furnace.width - 2;
            int minY = furnace.y + 1;
            int maxY = furnace.y + furnace.height - 2;

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    int idx = cy * world.width + cx;
                    Cell cell = world.cells[idx];

                    if (cell.materialId == Materials.Air)
                        continue;

                    // Apply heat, capped at maxTemp
                    int newTemp = Math.Min(
                        cell.temperature + furnace.heatOutput,
                        furnace.maxTemp);
                    cell.temperature = (byte)newTemp;
                    world.cells[idx] = cell;
                }
            }
        }
    }
}
