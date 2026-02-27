using ParticularLLM;

namespace ParticularLLM.Tests.CoreTests;

/// <summary>
/// Contract: CellWorld is the core grid data structure.
/// - Constructor allocates width*height cells, all initialized to Air at room temperature.
/// - Chunk grid is ceil(width/64) x ceil(height/64).
/// - SetCell/GetCell provide bounds-safe access; out-of-bounds Set is a no-op, Get returns Air.
/// - SetCell resets velocity and flags (full cell replacement).
/// - SetCell marks the containing chunk dirty for simulation.
/// - MarkDirtyWithNeighbors wakes adjacent chunks when a cell near a boundary changes.
/// - ResetDirtyState clears dirty flags and records activeLastFrame for next-frame neighbor waking.
/// </summary>
public class CellWorldTests
{
    [Fact]
    public void Constructor_InitializesAllCellsToAir()
    {
        var world = new CellWorld(128, 64);
        for (int i = 0; i < world.cells.Length; i++)
            Assert.Equal(Materials.Air, world.cells[i].materialId);
    }

    [Fact]
    public void Constructor_CorrectDimensions()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(128, world.width);
        Assert.Equal(64, world.height);
        Assert.Equal(128 * 64, world.cells.Length);
    }

    [Fact]
    public void Constructor_CorrectChunkCounts()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(2, world.chunksX);
        Assert.Equal(1, world.chunksY);
    }

    [Fact]
    public void Constructor_NonAlignedWorld_RoundsUpChunks()
    {
        var world = new CellWorld(100, 100);
        Assert.Equal(2, world.chunksX);
        Assert.Equal(2, world.chunksY);
    }

    [Fact]
    public void SetCell_And_GetCell_RoundTrip()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 20, Materials.Sand);
        Assert.Equal(Materials.Sand, world.GetCell(10, 20));
    }

    [Fact]
    public void SetCell_OutOfBounds_DoesNothing()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(-1, 0, Materials.Sand);
        world.SetCell(0, -1, Materials.Sand);
        world.SetCell(128, 0, Materials.Sand);
        world.SetCell(0, 64, Materials.Sand);
        Assert.Equal(Materials.Air, world.GetCell(0, 0));
    }

    [Fact]
    public void GetCell_OutOfBounds_ReturnsAir()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(Materials.Air, world.GetCell(-1, 0));
        Assert.Equal(Materials.Air, world.GetCell(128, 0));
    }

    [Fact]
    public void SetCell_MarksDirty()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Sand);
        var chunk = world.chunks[0];
        Assert.True((chunk.flags & ChunkFlags.IsDirty) != 0);
    }

    [Fact]
    public void SetCell_ResetsVelocity()
    {
        var world = new CellWorld(128, 64);
        int idx = 10 * 128 + 10;
        var cell = world.cells[idx];
        cell.velocityX = 5;
        cell.velocityY = 5;
        cell.materialId = Materials.Sand;
        world.cells[idx] = cell;
        world.SetCell(10, 10, Materials.Water);
        var result = world.cells[idx];
        Assert.Equal(Materials.Water, result.materialId);
        Assert.Equal(0, result.velocityX);
        Assert.Equal(0, result.velocityY);
    }

    [Fact]
    public void MarkDirtyWithNeighbors_WakesAdjacentChunks()
    {
        var world = new CellWorld(128, 128);
        world.MarkDirtyWithNeighbors(63, 63);
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) != 0);  // (0,0)
        Assert.True((world.chunks[1].flags & ChunkFlags.IsDirty) != 0);  // (1,0) right
        Assert.True((world.chunks[2].flags & ChunkFlags.IsDirty) != 0);  // (0,1) below
    }

    [Fact]
    public void ResetDirtyState_ClearsDirtyAndSetsActiveLastFrame()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(10, 10, Materials.Sand);
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) != 0);
        world.ResetDirtyState();
        Assert.True((world.chunks[0].flags & ChunkFlags.IsDirty) == 0);
        Assert.Equal(1, world.chunks[0].activeLastFrame);
    }

    [Fact]
    public void CountActiveCells_CountsNonAir()
    {
        var world = new CellWorld(128, 64);
        world.SetCell(0, 0, Materials.Sand);
        world.SetCell(1, 0, Materials.Water);
        world.SetCell(2, 0, Materials.Stone);
        Assert.Equal(3, world.CountActiveCells());
    }

    [Fact]
    public void Materials_AreInitialized()
    {
        var world = new CellWorld(128, 64);
        Assert.Equal(256, world.materials.Length);
        Assert.Equal(BehaviourType.Powder, world.materials[Materials.Sand].behaviour);
    }
}
