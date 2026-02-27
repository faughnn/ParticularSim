using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

/// <summary>
/// Contract: WorldUtils provides pure coordinate-conversion functions.
/// - CellIndex: (x, y, width) → flat array index = y * width + x
/// - CellToChunkX/Y: cell coordinate → chunk coordinate via integer division by 64
/// - ChunkToCellX/Y: chunk coordinate → cell origin via multiplication by 64
/// - CellToLocalX/Y: cell coordinate → position within chunk via modulo 64
/// - IsInBounds: checks 0 ≤ x &lt; width and 0 ≤ y &lt; height
/// </summary>
public class WorldUtilsTests
{
    [Theory]
    [InlineData(0, 0, 1024, 0)]
    [InlineData(5, 3, 1024, 3 * 1024 + 5)]
    [InlineData(1023, 511, 1024, 511 * 1024 + 1023)]
    public void CellIndex_CalculatesCorrectly(int x, int y, int width, int expected)
    {
        Assert.Equal(expected, WorldUtils.CellIndex(x, y, width));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(63, 0)]
    [InlineData(64, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    public void CellToChunkX_CorrectDivision(int cellX, int expectedChunk)
    {
        Assert.Equal(expectedChunk, WorldUtils.CellToChunkX(cellX));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void ChunkToCellX_CorrectMultiplication(int chunkX, int expectedCell)
    {
        Assert.Equal(expectedCell, WorldUtils.ChunkToCellX(chunkX));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(63, 63)]
    [InlineData(64, 0)]
    [InlineData(65, 1)]
    public void CellToLocalX_CorrectModulo(int cellX, int expected)
    {
        Assert.Equal(expected, WorldUtils.CellToLocalX(cellX));
    }

    [Theory]
    [InlineData(0, 0, 1024, 512, true)]
    [InlineData(-1, 0, 1024, 512, false)]
    [InlineData(1024, 0, 1024, 512, false)]
    [InlineData(0, -1, 1024, 512, false)]
    [InlineData(0, 512, 1024, 512, false)]
    [InlineData(1023, 511, 1024, 512, true)]
    public void IsInBounds_ChecksCorrectly(int x, int y, int w, int h, bool expected)
    {
        Assert.Equal(expected, WorldUtils.IsInBounds(x, y, w, h));
    }
}
