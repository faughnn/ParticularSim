using System;
using System.Collections.Generic;

namespace ParticularLLM;

/// <summary>
/// Shared utility methods for structure managers.
/// Eliminates duplication of grid snapping, chunk marking, and ghost position queries.
/// </summary>
public static class StructureUtils
{
    /// <summary>
    /// Snaps a coordinate to a block-aligned grid.
    /// </summary>
    public static int SnapToGrid(int coord, int blockSize)
    {
        if (coord < 0)
            return ((coord - blockSize + 1) / blockSize) * blockSize;
        return (coord / blockSize) * blockSize;
    }

    /// <summary>
    /// Sets the HasStructure flag on all chunks overlapping the given cell area.
    /// </summary>
    public static void MarkChunksHasStructure(CellWorld world, int cellX, int cellY, int areaWidth, int areaHeight)
    {
        int startChunkX = cellX / CellWorld.ChunkSize;
        int startChunkY = cellY / CellWorld.ChunkSize;
        int endChunkX = (cellX + areaWidth - 1) / CellWorld.ChunkSize;
        int endChunkY = (cellY + areaHeight - 1) / CellWorld.ChunkSize;

        for (int cy = startChunkY; cy <= endChunkY; cy++)
        {
            for (int cx = startChunkX; cx <= endChunkX; cx++)
            {
                if (cx >= 0 && cx < world.chunksX && cy >= 0 && cy < world.chunksY)
                {
                    int chunkIndex = cy * world.chunksX + cx;
                    ChunkState chunk = world.chunks[chunkIndex];
                    chunk.flags |= ChunkFlags.HasStructure;
                    world.chunks[chunkIndex] = chunk;
                }
            }
        }
    }

    /// <summary>
    /// Re-evaluates the HasStructure flag for chunks overlapping the given cell area.
    /// Uses the provided predicate to check if a tile exists at each position.
    /// </summary>
    public static void UpdateChunksStructureFlag(CellWorld world, int cellX, int cellY,
        int areaWidth, int areaHeight, int worldWidth, int worldHeight,
        Func<int, int, bool> hasTileAt)
    {
        int startChunkX = cellX / CellWorld.ChunkSize;
        int startChunkY = cellY / CellWorld.ChunkSize;
        int endChunkX = (cellX + areaWidth - 1) / CellWorld.ChunkSize;
        int endChunkY = (cellY + areaHeight - 1) / CellWorld.ChunkSize;

        for (int chunkY = startChunkY; chunkY <= endChunkY; chunkY++)
        {
            for (int chunkX = startChunkX; chunkX <= endChunkX; chunkX++)
            {
                if (chunkX >= 0 && chunkX < world.chunksX && chunkY >= 0 && chunkY < world.chunksY)
                {
                    UpdateSingleChunkStructureFlag(world, chunkX, chunkY, worldWidth, worldHeight, hasTileAt);
                }
            }
        }
    }

    private static void UpdateSingleChunkStructureFlag(CellWorld world, int chunkX, int chunkY,
        int worldWidth, int worldHeight, Func<int, int, bool> hasTileAt)
    {
        int chunkStartX = chunkX * CellWorld.ChunkSize;
        int chunkStartY = chunkY * CellWorld.ChunkSize;
        int chunkEndX = Math.Min(chunkStartX + CellWorld.ChunkSize, worldWidth);
        int chunkEndY = Math.Min(chunkStartY + CellWorld.ChunkSize, worldHeight);

        bool hasStructure = false;
        for (int y = chunkStartY; y < chunkEndY && !hasStructure; y++)
        {
            for (int x = chunkStartX; x < chunkEndX && !hasStructure; x++)
            {
                if (hasTileAt(x, y))
                    hasStructure = true;
            }
        }

        int chunkIndex = chunkY * world.chunksX + chunkX;
        ChunkState chunk = world.chunks[chunkIndex];

        if (hasStructure)
            chunk.flags |= ChunkFlags.HasStructure;
        else
            chunk.flags &= unchecked((byte)~ChunkFlags.HasStructure);

        world.chunks[chunkIndex] = chunk;
    }

    /// <summary>
    /// Populates a list with grid-snapped positions of all ghost block origins.
    /// </summary>
    public static void GetGhostBlockPositions(HashSet<int> ghostBlockOrigins, int worldWidth, List<(int x, int y)> positions)
    {
        foreach (int blockKey in ghostBlockOrigins)
        {
            int gridY = blockKey / worldWidth;
            int gridX = blockKey % worldWidth;
            positions.Add((gridX, gridY));
        }
    }
}
