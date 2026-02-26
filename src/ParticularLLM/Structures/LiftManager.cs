using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Manages all lifts in the world.
/// Handles 8x8 block placement/removal, lift merging, and force application.
/// Lifts are hollow force zones - material passes through and experiences upward force.
/// </summary>
public class LiftManager : IStructureManager
{
    private readonly CellWorld world;
    private readonly int width;
    private readonly int height;

    // Lift tile storage: parallel array to cells for O(1) lookup in simulation
    private LiftTile[] liftTiles;

    // Lift structure storage: id -> structure data
    private Dictionary<ushort, LiftStructure> liftLookup;

    // List of all lifts for iteration
    private List<LiftStructure> lifts;

    // Next available lift ID
    private ushort nextLiftId = 1;

    // Tracked ghost block origins (gridY * width + gridX) for O(1) iteration
    private HashSet<int> ghostBlockOrigins;

    // Default lift force (gravity is 17, so 20 gives net -3 upward)
    public const byte DefaultLiftForce = 20;

    public LiftTile[] LiftTiles => liftTiles;

    public LiftManager(CellWorld world, int initialCapacity = 64)
    {
        this.world = world;
        this.width = world.width;
        this.height = world.height;

        liftTiles = new LiftTile[width * height];
        liftLookup = new Dictionary<ushort, LiftStructure>(initialCapacity);
        lifts = new List<LiftStructure>(initialCapacity);
        ghostBlockOrigins = new HashSet<int>();
    }

    /// <summary>
    /// Snaps a coordinate to the 8x8 grid.
    /// </summary>
    public static int SnapToGrid(int coord) => StructureUtils.SnapToGrid(coord, LiftStructure.Width);

    /// <summary>
    /// Places an 8x8 lift block at the specified position.
    /// Position is snapped to 8x8 grid. Adjacent lifts merge vertically.
    /// </summary>
    public bool PlaceLift(int x, int y)
    {
        // Snap to 8x8 grid
        int gridX = SnapToGrid(x);
        int gridY = SnapToGrid(y);

        // Check bounds for entire 8x8 block
        if (!world.IsInBounds(gridX, gridY) ||
            !world.IsInBounds(gridX + LiftStructure.Width - 1, gridY + LiftStructure.Height - 1))
            return false;

        // Check if entire 8x8 area is placeable (Air, lift materials, or soft terrain)
        bool anyGhost = false;
        for (int dy = 0; dy < LiftStructure.Height; dy++)
        {
            for (int dx = 0; dx < LiftStructure.Width; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int posKey = cy * width + cx;

                if (liftTiles[posKey].liftId != 0)
                    return false;

                byte existingMaterial = world.GetCell(cx, cy);
                // Allow Air or existing lift materials (for state recovery)
                if (existingMaterial == Materials.Air ||
                    Materials.IsLift(existingMaterial))
                    continue;

                if (Materials.IsSoftTerrain(existingMaterial))
                {
                    anyGhost = true;
                    continue;
                }

                // Hard material (Stone, Wall, belt, etc.) -- reject
                return false;
            }
        }

        // Check for adjacent lifts to merge (same X, vertically adjacent)
        ushort topLiftId = 0;
        ushort bottomLiftId = 0;
        LiftStructure topLift = default;
        LiftStructure bottomLift = default;

        // Check top neighbor (at gridX, gridY - Height)
        int topY = gridY - LiftStructure.Height;
        if (topY >= 0)
        {
            int topPosKey = topY * width + gridX;
            LiftTile topTile = liftTiles[topPosKey];
            if (topTile.liftId != 0 && liftLookup.TryGetValue(topTile.liftId, out topLift))
            {
                topLiftId = topTile.liftId;
            }
        }

        // Check bottom neighbor (at gridX, gridY + Height)
        int bottomY = gridY + LiftStructure.Height;
        if (bottomY < height)
        {
            int bottomPosKey = bottomY * width + gridX;
            LiftTile bottomTile = liftTiles[bottomPosKey];
            if (bottomTile.liftId != 0 && liftLookup.TryGetValue(bottomTile.liftId, out bottomLift))
            {
                bottomLiftId = bottomTile.liftId;
            }
        }

        ushort liftId;
        LiftStructure lift;

        if (topLiftId != 0 && bottomLiftId != 0 && topLiftId != bottomLiftId)
        {
            // Merging three lifts: top + new + bottom
            // Extend top lift to include new block and bottom lift
            lift = topLift;
            lift.maxY = bottomLift.maxY;
            liftId = topLiftId;

            // Update all tiles from bottom lift to point to top lift
            UpdateLiftTileIds(gridX, bottomLift.minY, bottomLift.maxY + LiftStructure.Height - 1, topLiftId);

            // Remove bottom lift structure
            RemoveLiftStructure(bottomLiftId);

            // Update top lift in lookup
            liftLookup[topLiftId] = lift;
            UpdateLiftInList(topLiftId, lift);
        }
        else if (topLiftId != 0)
        {
            // Extend top lift downward to include new block
            lift = topLift;
            lift.maxY = gridY;
            liftId = topLiftId;

            liftLookup[topLiftId] = lift;
            UpdateLiftInList(topLiftId, lift);
        }
        else if (bottomLiftId != 0)
        {
            // Extend bottom lift upward to include new block
            lift = bottomLift;
            lift.minY = gridY;
            liftId = bottomLiftId;

            liftLookup[bottomLiftId] = lift;
            UpdateLiftInList(bottomLiftId, lift);
        }
        else
        {
            // Create new lift structure
            liftId = nextLiftId++;
            lift = new LiftStructure
            {
                id = liftId,
                tileX = gridX,
                minY = gridY,
                maxY = gridY,
                liftForce = DefaultLiftForce,
            };

            liftLookup.Add(liftId, lift);
            lifts.Add(lift);
        }

        // Fill the 8x8 area with lift tiles and place lift materials for rendering
        for (int dy = 0; dy < LiftStructure.Height; dy++)
        {
            for (int dx = 0; dx < LiftStructure.Width; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int posKey = cy * width + cx;

                // Place lift material with arrow pattern for visualization
                byte liftMaterial = GetLiftMaterialForPattern(cx, cy);

                liftTiles[posKey] = new LiftTile
                {
                    liftId = liftId,
                    materialId = liftMaterial,
                    isGhost = anyGhost,
                };

                if (!anyGhost)
                {
                    world.SetCell(cx, cy, liftMaterial);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        if (anyGhost)
            ghostBlockOrigins.Add(gridY * width + gridX);

        // Mark chunks as having structure so they stay active
        MarkChunksHasStructure(gridX, gridY, LiftStructure.Width, LiftStructure.Height);

        return true;
    }

    /// <summary>
    /// Removes the 8x8 lift block at the specified position.
    /// Position is snapped to 8x8 grid. May split a merged lift into two.
    /// </summary>
    public bool RemoveLift(int x, int y)
    {
        // Snap to 8x8 grid
        int gridX = SnapToGrid(x);
        int gridY = SnapToGrid(y);

        int posKey = gridY * width + gridX;

        // Check if there's a lift at this grid position
        LiftTile tile = liftTiles[posKey];
        if (tile.liftId == 0)
            return false;

        ushort liftId = tile.liftId;
        if (!liftLookup.TryGetValue(liftId, out LiftStructure lift))
            return false;

        // Clear the 8x8 area of lift tiles
        bool tileIsGhost = tile.isGhost;
        LiftTile emptyTile = default;
        for (int dy = 0; dy < LiftStructure.Height; dy++)
        {
            for (int dx = 0; dx < LiftStructure.Width; dx++)
            {
                int cx = gridX + dx;
                int cy = gridY + dy;
                int cellPosKey = cy * width + cx;

                liftTiles[cellPosKey] = emptyTile;

                if (!tileIsGhost)
                {
                    // Only clear cell material for non-ghost tiles (ghost tiles have terrain)
                    world.SetCell(cx, cy, Materials.Air);
                    world.MarkDirty(cx, cy);
                }
            }
        }

        if (tileIsGhost)
            ghostBlockOrigins.Remove(gridY * width + gridX);

        // Handle lift splitting
        bool hasTopPart = lift.minY < gridY;
        bool hasBottomPart = lift.maxY > gridY;

        if (hasTopPart && hasBottomPart)
        {
            // Split into two lifts: keep top part in existing lift, create new for bottom
            LiftStructure topLift = lift;
            topLift.maxY = gridY - LiftStructure.Height;

            liftLookup[liftId] = topLift;
            UpdateLiftInList(liftId, topLift);

            // Create new lift for bottom part
            ushort newLiftId = nextLiftId++;
            LiftStructure bottomLift = new LiftStructure
            {
                id = newLiftId,
                tileX = lift.tileX,
                minY = gridY + LiftStructure.Height,
                maxY = lift.maxY,
                liftForce = lift.liftForce,
            };

            liftLookup.Add(newLiftId, bottomLift);
            lifts.Add(bottomLift);

            // Update tiles in bottom part to point to new lift
            UpdateLiftTileIds(lift.tileX, bottomLift.minY, bottomLift.maxY + LiftStructure.Height - 1, newLiftId);
        }
        else if (hasTopPart)
        {
            // Only top part remains
            LiftStructure topLift = lift;
            topLift.maxY = gridY - LiftStructure.Height;

            liftLookup[liftId] = topLift;
            UpdateLiftInList(liftId, topLift);
        }
        else if (hasBottomPart)
        {
            // Only bottom part remains
            LiftStructure bottomLift = lift;
            bottomLift.minY = gridY + LiftStructure.Height;

            liftLookup[liftId] = bottomLift;
            UpdateLiftInList(liftId, bottomLift);
        }
        else
        {
            // This was the only block in the lift, remove entirely
            RemoveLiftStructure(liftId);
        }

        // Update chunk structure flags (may clear flag if no lifts remain)
        UpdateChunksStructureFlag(gridX, gridY, LiftStructure.Width, LiftStructure.Height);

        return true;
    }

    /// <summary>
    /// Checks if there's a lift tile at the specified position.
    /// </summary>
    public bool HasLiftAt(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return false;
        return liftTiles[y * width + x].liftId != 0;
    }

    public bool HasStructureAt(int x, int y) => HasLiftAt(x, y);

    /// <summary>
    /// Gets the lift tile at the specified position.
    /// </summary>
    public LiftTile GetLiftTile(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return default;
        return liftTiles[y * width + x];
    }

    /// <summary>
    /// Gets the lift structure by ID, if it exists.
    /// </summary>
    public bool TryGetLift(ushort liftId, out LiftStructure lift)
    {
        return liftLookup.TryGetValue(liftId, out lift);
    }

    /// <summary>
    /// Gets all lift structures for iteration.
    /// </summary>
    public List<LiftStructure> GetLifts() => lifts;

    /// <summary>
    /// Gets the number of lift structures.
    /// </summary>
    public int LiftCount => lifts.Count;

    // 8x8 upward arrow pattern (1 = light, 0 = dark)
    private static readonly byte[] ArrowPattern =
    {
        0, 0, 0, 1, 1, 0, 0, 0,
        0, 0, 1, 1, 1, 1, 0, 0,
        0, 1, 1, 1, 1, 1, 1, 0,
        1, 1, 0, 1, 1, 0, 1, 1,
        0, 0, 0, 1, 1, 0, 0, 0,
        0, 0, 0, 1, 1, 0, 0, 0,
        0, 0, 0, 1, 1, 0, 0, 0,
        0, 0, 0, 1, 1, 0, 0, 0,
    };

    /// <summary>
    /// Gets the material ID for a lift cell based on position.
    /// </summary>
    private static byte GetLiftMaterialForPattern(int x, int y)
    {
        int localX = ((x % 8) + 8) % 8;
        int localY = ((y % 8) + 8) % 8;

        bool isLight = ArrowPattern[localY * 8 + localX] == 1;

        return isLight ? Materials.LiftUpLight : Materials.LiftUp;
    }

    private void UpdateLiftTileIds(int tileX, int minY, int maxY, ushort newLiftId)
    {
        for (int cy = minY; cy <= maxY; cy++)
        {
            for (int dx = 0; dx < LiftStructure.Width; dx++)
            {
                int cx = tileX + dx;
                int posKey = cy * width + cx;
                LiftTile existing = liftTiles[posKey];
                existing.liftId = newLiftId;
                liftTiles[posKey] = existing;
            }
        }
    }

    private void UpdateLiftInList(ushort liftId, LiftStructure newLift)
    {
        for (int i = 0; i < lifts.Count; i++)
        {
            if (lifts[i].id == liftId)
            {
                lifts[i] = newLift;
                return;
            }
        }
    }

    private void RemoveLiftStructure(ushort liftId)
    {
        if (!liftLookup.ContainsKey(liftId))
            return;

        liftLookup.Remove(liftId);

        for (int i = 0; i < lifts.Count; i++)
        {
            if (lifts[i].id == liftId)
            {
                lifts[i] = lifts[^1];
                lifts.RemoveAt(lifts.Count - 1);
                break;
            }
        }
    }

    private void MarkChunksHasStructure(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.MarkChunksHasStructure(world, cellX, cellY, areaWidth, areaHeight);
    }

    private void UpdateChunksStructureFlag(int cellX, int cellY, int areaWidth, int areaHeight)
    {
        StructureUtils.UpdateChunksStructureFlag(world, cellX, cellY, areaWidth, areaHeight, width, height, HasLiftAt);
    }

    /// <summary>
    /// Checks all ghost lift tiles and activates blocks where terrain has been cleared.
    /// A ghost lift block activates when no Ground cells remain (powder/liquid is OK since
    /// lifts are hollow force zones -- those materials will be pushed through).
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

            // Check that no Static terrain (Ground) remains in the block.
            // Powder (Sand, Dirt) and Liquid (Water) are allowed -- lifts push them through.
            bool hasBlockingTerrain = false;
            for (int dy = 0; dy < LiftStructure.Height && !hasBlockingTerrain; dy++)
            {
                for (int dx = 0; dx < LiftStructure.Width && !hasBlockingTerrain; dx++)
                {
                    byte mat = world.GetCell(gridX + dx, gridY + dy);
                    if (mat == Materials.Ground)
                        hasBlockingTerrain = true;
                }
            }

            if (hasBlockingTerrain) continue;

            // Activate: clear ghost, write lift material only to Air cells
            for (int dy = 0; dy < LiftStructure.Height; dy++)
            {
                for (int dx = 0; dx < LiftStructure.Width; dx++)
                {
                    int cx = gridX + dx;
                    int cy = gridY + dy;
                    int posKey = cy * width + cx;

                    LiftTile updated = liftTiles[posKey];
                    updated.isGhost = false;
                    liftTiles[posKey] = updated;

                    // Only write lift material to Air cells -- leave powder/liquid in place
                    // (MoveCell in SimulateChunksLogic restores lift material when they move out)
                    byte existingMat = world.GetCell(cx, cy);
                    if (existingMat == Materials.Air)
                    {
                        world.SetCell(cx, cy, updated.materialId);
                    }

                    world.MarkDirty(cx, cy);
                }
            }

            // Remove from tracked ghost set
            ghostBlockOrigins.Remove(blockKey);
        }
    }

    /// <summary>
    /// Populates a list with grid-snapped positions of all ghost lift blocks.
    /// </summary>
    public void GetGhostBlockPositions(List<(int x, int y)> positions)
    {
        StructureUtils.GetGhostBlockPositions(ghostBlockOrigins, width, positions);
    }
}
