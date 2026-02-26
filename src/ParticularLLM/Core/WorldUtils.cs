using System.Runtime.CompilerServices;

namespace ParticularLLM;

public static class WorldUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CellIndex(int x, int y, int width) => y * width + x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CellToChunkX(int cx) => cx >> 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CellToChunkY(int cy) => cy >> 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChunkToCellX(int chunkX) => chunkX << 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChunkToCellY(int chunkY) => chunkY << 6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CellToLocalX(int cx) => cx & 63;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CellToLocalY(int cy) => cy & 63;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ChunkIndex(int chunkX, int chunkY, int chunksX) => chunkY * chunksX + chunkX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInBounds(int x, int y, int width, int height)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }
}
