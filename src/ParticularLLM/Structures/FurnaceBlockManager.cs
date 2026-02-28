using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Manages furnace blocks in the world.
/// Handles 8x8 block placement/removal with ghost support for placing through terrain.
/// Each block has a direction indicating where it emits heat.
/// Heat emission is implemented in SimulateFurnaces (Task 5).
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
    /// Simulates furnace heat emission. Stub for now — implemented in Task 5.
    /// </summary>
    public void SimulateFurnaces(CellWorld world, int currentFrame)
    {
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
