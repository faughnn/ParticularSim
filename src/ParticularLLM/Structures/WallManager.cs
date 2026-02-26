using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Manages all walls in the world.
/// Handles 8x8 block placement/removal with ghost support for placing through terrain.
/// Walls are purely static - no simulation behavior, no per-frame updates.
/// </summary>
public class WallManager : IStructureManager
{
    private readonly CellWorld world;
    private readonly int width;
    private readonly int height;

    // Wall tile storage: parallel array to cells for O(1) lookup
    private WallTile[] wallTiles;

    // Tracked ghost block origins (gridY * width + gridX) for iteration
    private HashSet<int> ghostBlockOrigins;

    // Block dimensions (same as lifts/belts)
    public const int BlockSize = 8;

    public WallTile[] WallTiles => wallTiles;

    public WallManager(CellWorld world, int initialCapacity = 64)
    {
        this.world = world;
        this.width = world.width;
        this.height = world.height;

        wallTiles = new WallTile[width * height];
        ghostBlockOrigins = new HashSet<int>();
    }

    /// <summary>
    /// Snaps a coordinate to the 8x8 grid.
    /// </summary>
    public static int SnapToGrid(int coord) => StructureUtils.SnapToGrid(coord, BlockSize);

    /// <summary>
    /// Places an 8x8 wall block at the specified position.
    /// Position is snapped to 8x8 grid. Returns true if placed successfully.
    /// </summary>
    public bool PlaceWall(int x, int y)
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

                // Already has a wall here -- reject
                if (wallTiles[posKey].exists)
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

        // Place wall tiles and materials
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int posKey = cy * width + cx;

                wallTiles[posKey] = new WallTile
                {
                    exists = true,
                    isGhost = anyGhost,
                };

                // Only write wall material if not ghost
                if (!anyGhost)
                {
                    world.SetCell(cx, cy, Materials.Wall);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        if (anyGhost)
            ghostBlockOrigins.Add(gridY * width + gridX);

        // Mark chunks as having structure so they stay active
        MarkChunksHasStructure(gridX, gridY, BlockSize, BlockSize);

        return true;
    }

    /// <summary>
    /// Removes the 8x8 wall block at the specified position.
    /// Position is snapped to 8x8 grid. Returns true if removed successfully.
    /// </summary>
    public bool RemoveWall(int x, int y)
    {
        // Snap to 8x8 grid
        int gridX = SnapToGrid(x);
        int gridY = SnapToGrid(y);

        int posKey = gridY * width + gridX;

        // Check if there's a wall at this grid position
        if (!wallTiles[posKey].exists)
            return false;

        bool tileIsGhost = wallTiles[posKey].isGhost;

        // Clear the 8x8 area
        WallTile emptyTile = default;
        for (int dy = 0; dy < BlockSize; dy++)
        {
            for (int dx = 0; dx < BlockSize; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int cellPosKey = cy * width + cx;

                wallTiles[cellPosKey] = emptyTile;

                // Only clear cell material for non-ghost tiles
                if (!tileIsGhost)
                {
                    world.SetCell(cx, cy, Materials.Air);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        if (tileIsGhost)
            ghostBlockOrigins.Remove(gridY * width + gridX);

        // Update chunk structure flags
        UpdateChunksStructureFlag(gridX, gridY, BlockSize, BlockSize);

        return true;
    }

    /// <summary>
    /// Checks if there's a wall tile at the specified position.
    /// </summary>
    public bool HasWallAt(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return false;
        return wallTiles[y * width + x].exists;
    }

    public bool HasStructureAt(int x, int y) => HasWallAt(x, y);

    /// <summary>
    /// Gets the wall tile at the specified position.
    /// </summary>
    public WallTile GetWallTile(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return default;
        return wallTiles[y * width + x];
    }

    /// <summary>
    /// Checks all ghost wall tiles and activates blocks where terrain has been cleared.
    /// A ghost wall block activates when all 64 cells are Air.
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
            // CanMoveTo uses source-aware ghost blocking: external material is
            // blocked from entering, but material inside can move within/out.
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

            // Activate: clear ghost, write wall material
            for (int dy = 0; dy < BlockSize; dy++)
            {
                for (int dx = 0; dx < BlockSize; dx++)
                {
                    int cx = gridX + dx;
                    int cy = gridY + dy;
                    int wallPosKey = cy * width + cx;

                    WallTile updated = wallTiles[wallPosKey];
                    updated.isGhost = false;
                    wallTiles[wallPosKey] = updated;

                    world.SetCell(cx, cy, Materials.Wall);

                    world.MarkDirty(cx, cy);
                }
            }

            // Remove from tracked ghost set
            ghostBlockOrigins.Remove(blockKey);
        }
    }

    /// <summary>
    /// Populates a list with grid-snapped positions of all ghost wall blocks.
    /// </summary>
    public void GetGhostBlockPositions(List<(int x, int y)> positions)
    {
        StructureUtils.GetGhostBlockPositions(ghostBlockOrigins, width, positions);
    }

    private void MarkChunksHasStructure(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.MarkChunksHasStructure(world, cellX, cellY, areaWidth, areaHeight);
    }

    private void UpdateChunksStructureFlag(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.UpdateChunksStructureFlag(world, cellX, cellY, areaWidth, areaHeight, width, height, HasWallAt);
    }
}
