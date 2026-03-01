using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Manages furnace blocks in the world.
/// Handles 8x8 block placement/removal with ghost support for placing through terrain.
/// Each block has a direction indicating where it emits heat.
/// Heat emission is implemented in SimulateFurnaces.
/// </summary>
public class FurnaceBlockManager : IStructureManager
{
    private readonly CellWorld world;
    private readonly int width;
    private readonly int height;

    // Furnace tile storage: parallel array to cells for O(1) lookup
    private FurnaceBlockTile[] furnaceTiles;

    // Tracked ghost block origins (gridY * width + gridX) for iteration
    private HashSet<int> ghostBlockOrigins;

    // ALL placed block origins (gridY * width + gridX) — needed for heat emission
    private HashSet<int> blockOrigins;

    // Sub-integer heating accumulator (one per cell, mirrors cooling accumulator pattern)
    private ushort[] heatingAccum;

    // Block dimensions (same as walls/lifts/belts)
    public const int BlockSize = 8;

    public FurnaceBlockTile[] FurnaceTiles => furnaceTiles;

    /// <summary>
    /// All placed block origin keys (gridY * width + gridX).
    /// Used by heat emission to iterate over all furnace blocks.
    /// </summary>
    public HashSet<int> BlockOrigins => blockOrigins;

    public FurnaceBlockManager(CellWorld world, int initialCapacity = 64)
    {
        this.world = world;
        this.width = world.width;
        this.height = world.height;

        furnaceTiles = new FurnaceBlockTile[width * height];
        ghostBlockOrigins = new HashSet<int>();
        blockOrigins = new HashSet<int>();
        heatingAccum = new ushort[width * height];
    }

    /// <summary>
    /// Snaps a coordinate to the 8x8 grid.
    /// </summary>
    public static int SnapToGrid(int coord) => StructureUtils.SnapToGrid(coord, BlockSize);

    /// <summary>
    /// Places an 8x8 furnace block at the specified position.
    /// Position is snapped to 8x8 grid. Returns true if placed successfully.
    /// </summary>
    public bool PlaceFurnace(int x, int y, FurnaceDirection direction)
    {
        // Snap to 8x8 grid
        int gridX = SnapToGrid(x);
        int gridY = SnapToGrid(y);

        // Check bounds for entire 8x8 block
        if (!world.IsInBounds(gridX, gridY) ||
            !world.IsInBounds(gridX + BlockSize - 1, gridY + BlockSize - 1))
            return false;

        // Check if entire 8x8 area is placeable (Air, or soft terrain for ghost)
        bool anyGhost = false;
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int posKey = cy * width + cx;

                // Already has a furnace here -- reject
                if (furnaceTiles[posKey].exists)
                    return false;

                byte existingMaterial = world.GetCell(cx, cy);

                // Air is always placeable
                if (existingMaterial == Materials.Air)
                    continue;

                // Soft terrain -- will be ghost
                if (Materials.IsSoftTerrain(existingMaterial))
                {
                    anyGhost = true;
                    continue;
                }

                // Hard material (Stone, other structures, etc.) -- reject
                return false;
            }
        }

        // Place furnace tiles and materials
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int posKey = cy * width + cx;

                furnaceTiles[posKey] = new FurnaceBlockTile
                {
                    exists = true,
                    isGhost = anyGhost,
                    direction = direction,
                };

                // Only write furnace material if not ghost
                if (!anyGhost)
                {
                    world.SetCell(cx, cy, Materials.Furnace);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        int blockKey = gridY * width + gridX;
        blockOrigins.Add(blockKey);

        if (anyGhost)
            ghostBlockOrigins.Add(blockKey);

        // Mark chunks as having structure so they stay active
        MarkChunksHasStructure(gridX, gridY, BlockSize, BlockSize);

        return true;
    }

    /// <summary>
    /// Removes the 8x8 furnace block at the specified position.
    /// Position is snapped to 8x8 grid. Returns true if removed successfully.
    /// </summary>
    public bool RemoveFurnace(int x, int y)
    {
        // Snap to 8x8 grid
        int gridX = SnapToGrid(x);
        int gridY = SnapToGrid(y);

        int posKey = gridY * width + gridX;

        // Check if there's a furnace at this grid position
        if (!furnaceTiles[posKey].exists)
            return false;

        bool tileIsGhost = furnaceTiles[posKey].isGhost;

        // Clear the 8x8 area
        FurnaceBlockTile emptyTile = default;
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int cellPosKey = cy * width + cx;

                furnaceTiles[cellPosKey] = emptyTile;

                // Only clear cell material for non-ghost tiles
                if (!tileIsGhost)
                {
                    world.SetCell(cx, cy, Materials.Air);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        int blockKey = gridY * width + gridX;
        blockOrigins.Remove(blockKey);

        if (tileIsGhost)
            ghostBlockOrigins.Remove(blockKey);

        // Update chunk structure flags
        UpdateChunksStructureFlag(gridX, gridY, BlockSize, BlockSize);

        return true;
    }

    /// <summary>
    /// Checks if there's a furnace tile at the specified position.
    /// </summary>
    public bool HasFurnaceAt(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return false;
        return furnaceTiles[y * width + x].exists;
    }

    public bool HasStructureAt(int x, int y) => HasFurnaceAt(x, y);

    /// <summary>
    /// Gets the furnace tile at the specified position.
    /// </summary>
    public FurnaceBlockTile GetFurnaceTile(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return default;
        return furnaceTiles[y * width + x];
    }

    /// <summary>
    /// Checks all ghost furnace tiles and activates blocks where terrain has been cleared.
    /// A ghost furnace block activates when all 64 cells are Air.
    /// </summary>
    public void UpdateGhostStates()
    {
        if (ghostBlockOrigins.Count == 0) return;

        // Copy to array so we can modify the set while iterating
        var blockKeys = ghostBlockOrigins.ToArray();

        for (int b = 0; b < blockKeys.Length; b++)
        {
            int blockKey = blockKeys[b];
            int gridY = blockKey / width;
            int gridX = blockKey % width;

            // Check if ALL 64 cells are Air.
            bool allClear = true;
            for (int dy = 0; dy < BlockSize && allClear; dy++)
            {
                for (int dx = 0; dx < BlockSize && allClear; dx++)
                {
                    byte mat = world.GetCell(gridX + dx, gridY + dy);
                    if (mat != Materials.Air)
                        allClear = false;
                }
            }

            if (!allClear) continue;

            // Activate: clear ghost, write furnace material
            for (int dy = 0; dy < BlockSize; dy++)
            {
                for (int dx = 0; dx < BlockSize; dx++)
                {
                    int cx = gridX + dx;
                    int cy = gridY + dy;
                    int furnacePosKey = cy * width + cx;

                    FurnaceBlockTile updated = furnaceTiles[furnacePosKey];
                    updated.isGhost = false;
                    furnaceTiles[furnacePosKey] = updated;

                    world.SetCell(cx, cy, Materials.Furnace);

                    world.MarkDirty(cx, cy);
                }
            }

            // Remove from tracked ghost set
            ghostBlockOrigins.Remove(blockKey);
        }
    }

    /// <summary>
    /// Populates a list with grid-snapped positions of all ghost furnace blocks.
    /// </summary>
    public void GetGhostBlockPositions(List<(int x, int y)> positions)
    {
        StructureUtils.GetGhostBlockPositions(ghostBlockOrigins, width, positions);
    }

    /// <summary>
    /// Returns the position and direction of all placed furnace blocks (including ghosts).
    /// Used for serialization/capture metadata.
    /// </summary>
    public List<(int gridX, int gridY, FurnaceDirection direction)> GetPlacedBlocks()
    {
        var result = new List<(int gridX, int gridY, FurnaceDirection direction)>();
        foreach (int blockKey in blockOrigins)
        {
            int gridX = blockKey % width;
            int gridY = blockKey / width;
            var tile = furnaceTiles[blockKey];
            if (tile.exists)
                result.Add((gridX, gridY, tile.direction));
        }
        return result;
    }

    /// <summary>
    /// Simulates furnace heat emission. Each non-ghost furnace block emits heat
    /// to cells in its facing direction, up to FurnaceEmissionDepth cells deep.
    /// Uses a sub-integer accumulator: each frame adds FurnaceHeatRate per emission source,
    /// and when the accumulator reaches AccumulatorThreshold, temperature rises 1 degree.
    /// </summary>
    public void SimulateFurnaces(CellWorld world)
    {
        if (blockOrigins.Count == 0) return;

        int depth = HeatSettings.FurnaceEmissionDepth;

        foreach (int blockKey in blockOrigins)
        {
            int gridX = blockKey % width;
            int gridY = blockKey / width;

            // Skip ghost blocks — they aren't materialized yet
            if (furnaceTiles[blockKey].isGhost) continue;

            FurnaceDirection dir = furnaceTiles[blockKey].direction;

            // Emit heat to BlockSize cells across × depth cells deep
            for (int i = 0; i < BlockSize; i++)
            {
                for (int d = 0; d < depth; d++)
                {
                    int cx, cy;
                    switch (dir)
                    {
                        case FurnaceDirection.Right:
                            cx = gridX + BlockSize + d; cy = gridY + i; break;
                        case FurnaceDirection.Left:
                            cx = gridX - 1 - d; cy = gridY + i; break;
                        case FurnaceDirection.Down:
                            cx = gridX + i; cy = gridY + BlockSize + d; break;
                        case FurnaceDirection.Up:
                            cx = gridX + i; cy = gridY - 1 - d; break;
                        default: continue;
                    }

                    if (cx < 0 || cx >= width || cy < 0 || cy >= height) continue;

                    int idx = cy * width + cx;
                    Cell cell = world.cells[idx];

                    // Don't heat other furnace cells
                    if (cell.materialId == Materials.Furnace) continue;

                    // Accumulator-based sub-integer heating
                    heatingAccum[idx] += (ushort)HeatSettings.FurnaceHeatRate;
                    if (heatingAccum[idx] >= HeatSettings.AccumulatorThreshold)
                    {
                        int degrees = heatingAccum[idx] / HeatSettings.AccumulatorThreshold;
                        heatingAccum[idx] -= (ushort)(degrees * HeatSettings.AccumulatorThreshold);
                        int newTemp = Math.Min(cell.temperature + degrees, 255);
                        cell.temperature = (byte)newTemp;
                        world.cells[idx] = cell;
                    }
                }
            }
        }
    }

    private void MarkChunksHasStructure(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.MarkChunksHasStructure(world, cellX, cellY, areaWidth, areaHeight);
    }

    private void UpdateChunksStructureFlag(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.UpdateChunksStructureFlag(world, cellX, cellY, areaWidth, areaHeight, width, height, HasFurnaceAt);
    }
}
