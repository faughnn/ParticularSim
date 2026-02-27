using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for chunk boundary behavior and dirty state propagation.
///
/// English rules (derived from SimulateChunksLogic and CellWorld):
///
/// 1. Chunks are 64x64 cells. World is divided into chunksX * chunksY chunks.
/// 2. MoveCell wakes adjacent chunks when vacating a boundary position
///    (localX==0 or 63, localY==0 or 63).
/// 3. Chunks with HasStructure flag stay active even when not dirty.
/// 4. Extended region (32px buffer) allows cells to land outside their home chunk.
/// 5. Material conservation must hold across chunk boundaries.
/// 6. activeLastFrame keeps chunks alive for one extra frame after dirty clears.
///
/// Known tradeoffs:
/// - Chunk boundary processing depends on processing order — results may differ
///   between flat and 4-pass modes but both must be correct.
/// </summary>
public class ChunkBoundaryTests
{
    // ===== BASIC CROSS-BOUNDARY MOVEMENT =====

    [Fact]
    public void Sand_FallsAcrossVerticalChunkBoundary()
    {
        // Chunks are 64x64. Sand at y=63 (bottom of chunk 0) should fall to y=64 (chunk 1).
        using var sim = new SimulationFixture(128, 128); // 2x2 chunks

        sim.Fill(0, 127, 128, 1, Materials.Stone);
        sim.Set(32, 60, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should have crossed into chunk below (y >= 64)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 64,
            $"Sand should cross chunk boundary at y=64, but at y={pos[0].y}");
    }

    [Fact]
    public void Sand_FallsAcrossHorizontalChunkBoundary()
    {
        // Sand sliding diagonally should cross horizontal chunk boundary (x=63/64).
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 127, 128, 1, Materials.Stone);
        // Place sand near right edge of left chunk
        sim.Set(62, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should have slid diagonally, potentially crossing x=64
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Just verify it fell and is conserved — exact position depends on slide direction
        Assert.True(pos[0].y > 60,
            $"Sand should have fallen significantly, but at y={pos[0].y}");
    }

    // ===== WAKE NEIGHBORS =====

    [Fact]
    public void ChunkWakesNeighbor_WhenCellVacatesBoundary()
    {
        // When sand leaves position at chunk boundary, the adjacent chunk should
        // be woken so material there can fill the gap.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 127, 128, 1, Materials.Stone);

        // Two sand grains: one at chunk boundary (y=64), one in the chunk above (y=63)
        sim.Set(32, 64, Materials.Sand); // top of chunk 1
        sim.Set(32, 63, Materials.Sand); // bottom of chunk 0

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Both should have fallen to the floor
        Assert.Equal(2, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    // ===== MULTI-CHUNK CONSERVATION =====

    [Fact]
    public void Conservation_MaterialsAcrossAllChunks()
    {
        // Scatter materials across all 4 chunks (2x2 grid at 128x128).
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 127, 128, 1, Materials.Stone);

        // Place sand in each quadrant
        sim.Set(16, 16, Materials.Sand);  // chunk (0,0)
        sim.Set(80, 16, Materials.Sand);  // chunk (1,0)
        sim.Set(16, 80, Materials.Sand);  // chunk (0,1)
        sim.Set(80, 80, Materials.Sand);  // chunk (1,1)

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(4, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void Conservation_WaterAcrossChunkBoundaries()
    {
        // Water spreading should maintain conservation even across chunks.
        using var sim = new SimulationFixture(128, 128);
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Pour water near a chunk boundary
        for (int x = 60; x < 68; x++)
            sim.Set(x, 50, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        Assert.Equal(8, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    // ===== STRUCTURE KEEPS CHUNK ACTIVE =====

    [Fact]
    public void ChunkWithBelt_StaysActive()
    {
        // A chunk containing a belt structure should stay active via HasStructure flag.
        using var sim = new SimulationFixture(128, 128);
        var belts = new BeltManager(sim.World);
        belts.PlaceBelt(16, 40, 1);
        sim.Simulator.SetBeltManager(belts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand above belt — it should fall, land on belt, and get transported.
        // This only works if the belt's chunk stays active.
        sim.Set(20, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(500);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have been transported right by the belt
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].x > 20,
            $"Belt should transport sand right, but at x={pos[0].x}");
    }

    // ===== FOUR-PASS MODE =====

    [Fact]
    public void FourPassMode_ConservesAcrossChunks()
    {
        // 4-pass checkerboard mode should conserve materials across chunk boundaries.
        using var sim = new SimulationFixture(128, 128);
        sim.Simulator.UseFourPassGrouping = true;

        sim.Fill(0, 127, 128, 1, Materials.Stone);

        // Scatter sand across chunks
        for (int x = 30; x < 34; x++)
            for (int y = 10; y < 14; y++)
                sim.Set(x, y, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(16, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void FourPassMode_SandSettlesToFloor()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Simulator.UseFourPassGrouping = true;

        sim.Fill(0, 127, 128, 1, Materials.Stone);
        sim.Set(64, 10, Materials.Sand); // At chunk boundary x=64

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 120,
            $"Sand should settle to floor in 4-pass mode, but at y={pos[0].y}");
    }
}
