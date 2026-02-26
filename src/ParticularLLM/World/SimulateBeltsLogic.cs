using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ParticularLLM;

/// <summary>
/// Plain C# port of SimulateBeltsJob (Burst/Jobs removed).
/// Each call to SimulateBelt processes one belt, moving cells on its surface horizontally.
/// </summary>
public class SimulateBeltsLogic
{
    public Cell[] cells = null!;
    public ChunkState[] chunks = null!;
    public MaterialDef[] materials = null!;
    public BeltStructure[] belts = null!; // Array of belt structures to process
    public Dictionary<int, BeltTile>? beltTiles;
    public int width;
    public int height;
    public int chunksX;
    public int chunksY;
    public ushort currentFrame;

    public void SimulateBelt(int beltIndex)
    {
        BeltStructure belt = belts[beltIndex];

        // Check if this belt should move this frame (based on speed and frame offset)
        if ((currentFrame - belt.frameOffset) % belt.speed != 0)
            return;

        // The surface row where cells sit is just above the belt (tileY - 1)
        // In this coordinate system, Y=0 is at top, Y increases downward
        // So "above" means smaller Y value
        int surfaceY = belt.tileY - 1;

        // Skip if surface is out of bounds
        if (surfaceY < 0 || surfaceY >= height)
            return;

        // Scan from minX to maxX + Width - 1 (the full span of the belt)
        int scanMinX = belt.minX;
        int scanMaxX = belt.maxX + BeltStructure.Width - 1;

        // Move cells in belt direction
        // Process columns in opposite direction of movement to avoid double-moving
        if (belt.direction > 0)
        {
            // Moving right: scan columns from right to left
            for (int x = scanMaxX; x >= scanMinX; x--)
            {
                MoveColumn(x, surfaceY, belt.direction);
            }
        }
        else
        {
            // Moving left: scan columns from left to right
            for (int x = scanMinX; x <= scanMaxX; x++)
            {
                MoveColumn(x, surfaceY, belt.direction);
            }
        }
    }

    /// <summary>
    /// Moves an entire column of cells above the belt surface.
    /// Scans from surface upward and moves all movable cells.
    /// </summary>
    private void MoveColumn(int x, int surfaceY, sbyte direction)
    {
        if (x < 0 || x >= width)
            return;

        // Skip ghost belt tiles (they shouldn't transport materials)
        int beltRowY = surfaceY + 1; // First row of belt block
        if (beltTiles != null)
        {
            int tileKey = beltRowY * width + x;
            if (beltTiles.TryGetValue(tileKey, out BeltTile bt) && bt.isGhost)
                return;
        }

        int targetX = x + direction;
        if (targetX < 0 || targetX >= width)
            return;

        // Scan upward from surface (decreasing Y) and move all cells in this column
        for (int y = surfaceY; y >= 0; y--)
        {
            int index = y * width + x;
            Cell cell = cells[index];

            // Stop scanning if we hit air (top of pile)
            if (cell.materialId == Materials.Air)
                break;

            // Skip belt tiles (stop scanning at belt surface)
            if (IsBeltMaterial(cell.materialId))
                break;

            // Skip cells owned by clusters (rigid bodies)
            if (cell.ownerId != 0)
                continue;

            MaterialDef mat = materials[cell.materialId];

            // Only move powder and liquid
            if (mat.behaviour != BehaviourType.Powder && mat.behaviour != BehaviourType.Liquid)
                continue;

            int targetIndex = y * width + targetX;
            Cell targetCell = cells[targetIndex];

            // Can only move into air
            if (targetCell.materialId != Materials.Air)
                continue;

            // Swap cells
            cells[index] = targetCell;
            cells[targetIndex] = cell;

            // Mark dirty for rendering
            MarkDirty(x, y);
            MarkDirty(targetX, y);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBeltMaterial(byte materialId)
    {
        return materialId == Materials.Belt ||
               materialId == Materials.BeltLeft ||
               materialId == Materials.BeltRight ||
               materialId == Materials.BeltLeftLight ||
               materialId == Materials.BeltRightLight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MarkDirty(int x, int y)
    {
        int chunkX = x >> 6; // x / 64
        int chunkY = y >> 6; // y / 64

        if (chunkX < 0 || chunkX >= chunksX || chunkY < 0 || chunkY >= chunksY)
            return;

        int chunkIndex = chunkY * chunksX + chunkX;

        ChunkState chunk = chunks[chunkIndex];
        chunk.flags |= ChunkFlags.IsDirty;

        int localX = x & 63; // x % 64
        int localY = y & 63; // y % 64

        // Update dirty bounds (race conditions on min/max are acceptable - worst case is extra work)
        if (localX < chunk.minX) chunk.minX = (ushort)localX;
        if (localX > chunk.maxX) chunk.maxX = (ushort)localX;
        if (localY < chunk.minY) chunk.minY = (ushort)localY;
        if (localY > chunk.maxY) chunk.maxY = (ushort)localY;

        chunks[chunkIndex] = chunk;
    }
}
