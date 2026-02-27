using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Meta-property: Checkerboard chunk grouping for parallel processing.
///
/// The world is divided into 64x64 chunks. For parallelism, chunks are assigned to 4 groups
/// in a checkerboard pattern (A/B/C/D) such that no two same-group chunks share an edge.
/// This lets same-group chunks be processed in parallel without data races.
///
/// Pattern:  A B A B
///           C D C D
///           A B A B
///
/// Group = (chunkX &amp; 1) + ((chunkY &amp; 1) &lt;&lt; 1):
///   A = even X, even Y
///   B = odd X, even Y
///   C = even X, odd Y
///   D = odd X, odd Y
///
/// Invariants:
/// - Every active chunk is assigned to exactly one group (no duplicates, no omissions)
/// - Same-group chunks are at least 2 apart in X or Y (no shared edges)
/// - Inactive (clean, no structures) chunks are not assigned to any group
/// - Both flat and 4-pass orderings conserve materials (but may produce different final states)
/// </summary>
public class CheckerboardGroupTests
{
    [Fact]
    public void GroupAssignment_CorrectCheckerboardPattern()
    {
        // 4x4 chunks (256x256 world)
        var world = new CellWorld(256, 256);
        var gA = new List<int>();
        var gB = new List<int>();
        var gC = new List<int>();
        var gD = new List<int>();

        // Mark all chunks dirty so they all get collected
        for (int i = 0; i < world.chunks.Length; i++)
        {
            var chunk = world.chunks[i];
            chunk.flags |= ChunkFlags.IsDirty;
            world.chunks[i] = chunk;
        }

        world.CollectChunkGroups(gA, gB, gC, gD);

        // Expected pattern for 4x4:
        // A B A B   (row 0: even Y)
        // C D C D   (row 1: odd Y)
        // A B A B   (row 2: even Y)
        // C D C D   (row 3: odd Y)

        // Group A: even X, even Y → (0,0), (2,0), (0,2), (2,2)
        Assert.Equal(4, gA.Count);
        Assert.Contains(0 * 4 + 0, gA);  // chunk (0,0)
        Assert.Contains(0 * 4 + 2, gA);  // chunk (2,0)
        Assert.Contains(2 * 4 + 0, gA);  // chunk (0,2)
        Assert.Contains(2 * 4 + 2, gA);  // chunk (2,2)

        // Group B: odd X, even Y → (1,0), (3,0), (1,2), (3,2)
        Assert.Equal(4, gB.Count);
        Assert.Contains(0 * 4 + 1, gB);
        Assert.Contains(0 * 4 + 3, gB);
        Assert.Contains(2 * 4 + 1, gB);
        Assert.Contains(2 * 4 + 3, gB);

        // Group C: even X, odd Y → (0,1), (2,1), (0,3), (2,3)
        Assert.Equal(4, gC.Count);
        Assert.Contains(1 * 4 + 0, gC);
        Assert.Contains(1 * 4 + 2, gC);
        Assert.Contains(3 * 4 + 0, gC);
        Assert.Contains(3 * 4 + 2, gC);

        // Group D: odd X, odd Y → (1,1), (3,1), (1,3), (3,3)
        Assert.Equal(4, gD.Count);
        Assert.Contains(1 * 4 + 1, gD);
        Assert.Contains(1 * 4 + 3, gD);
        Assert.Contains(3 * 4 + 1, gD);
        Assert.Contains(3 * 4 + 3, gD);
    }

    [Fact]
    public void SameGroupChunks_AreAtLeastTwoApart()
    {
        // 8x8 chunks (512x512 world)
        var world = new CellWorld(512, 512);
        var gA = new List<int>();
        var gB = new List<int>();
        var gC = new List<int>();
        var gD = new List<int>();

        for (int i = 0; i < world.chunks.Length; i++)
        {
            var chunk = world.chunks[i];
            chunk.flags |= ChunkFlags.IsDirty;
            world.chunks[i] = chunk;
        }

        world.CollectChunkGroups(gA, gB, gC, gD);

        // For each group, verify all chunk pairs are at least 2 apart in both X and Y
        foreach (var group in new[] { gA, gB, gC, gD })
        {
            for (int i = 0; i < group.Count; i++)
            {
                int ax = group[i] % world.chunksX;
                int ay = group[i] / world.chunksX;
                for (int j = i + 1; j < group.Count; j++)
                {
                    int bx = group[j] % world.chunksX;
                    int by = group[j] / world.chunksX;

                    int dx = Math.Abs(ax - bx);
                    int dy = Math.Abs(ay - by);

                    // Same-group chunks must differ by at least 2 in X or Y (or both)
                    // They can share an axis (dx=0) if they differ by 2+ on the other
                    Assert.True(dx >= 2 || dy >= 2,
                        $"Same-group chunks ({ax},{ay}) and ({bx},{by}) are too close: dx={dx}, dy={dy}");
                }
            }
        }
    }

    [Fact]
    public void AllActiveChunks_AreAssignedToExactlyOneGroup()
    {
        var world = new CellWorld(384, 256); // 6x4 chunks
        var gA = new List<int>();
        var gB = new List<int>();
        var gC = new List<int>();
        var gD = new List<int>();

        // Mark all chunks dirty
        for (int i = 0; i < world.chunks.Length; i++)
        {
            var chunk = world.chunks[i];
            chunk.flags |= ChunkFlags.IsDirty;
            world.chunks[i] = chunk;
        }

        world.CollectChunkGroups(gA, gB, gC, gD);

        int totalAssigned = gA.Count + gB.Count + gC.Count + gD.Count;
        Assert.Equal(world.chunks.Length, totalAssigned);

        // No duplicates across groups
        var all = new HashSet<int>();
        foreach (int idx in gA) Assert.True(all.Add(idx), $"Chunk {idx} appears in multiple groups");
        foreach (int idx in gB) Assert.True(all.Add(idx), $"Chunk {idx} appears in multiple groups");
        foreach (int idx in gC) Assert.True(all.Add(idx), $"Chunk {idx} appears in multiple groups");
        foreach (int idx in gD) Assert.True(all.Add(idx), $"Chunk {idx} appears in multiple groups");
    }

    [Fact]
    public void FourPassAndFlat_BothConserveMaterials_ButMayDiffer()
    {
        // Chunk processing order affects results (later chunks see earlier chunks' writes).
        // Flat and 4-pass orderings are both valid but produce different states.
        // What matters: both conserve materials and are individually deterministic.
        var sim1 = new SimulationFixture(256, 256);
        sim1.Simulator.UseFourPassGrouping = false;
        var sim2 = new SimulationFixture(256, 256);
        sim2.Simulator.UseFourPassGrouping = true;

        // Same setup in both
        foreach (var sim in new[] { sim1, sim2 })
        {
            sim.Fill(0, 240, 256, 16, Materials.Stone);
            for (int x = 60; x < 70; x++)
                for (int y = 0; y < 5; y++)
                    sim.Set(x, y, Materials.Sand);
            for (int x = 130; x < 140; x++)
                for (int y = 0; y < 5; y++)
                    sim.Set(x, y, Materials.Water);
        }

        sim1.Step(200);
        sim2.Step(200);

        // Both must conserve materials
        Assert.Equal(50, WorldAssert.CountMaterial(sim1.World, Materials.Sand));
        Assert.Equal(50, WorldAssert.CountMaterial(sim1.World, Materials.Water));
        Assert.Equal(50, WorldAssert.CountMaterial(sim2.World, Materials.Sand));
        Assert.Equal(50, WorldAssert.CountMaterial(sim2.World, Materials.Water));
    }

    [Fact]
    public void FourPassMode_MaterialConservation_MultiChunk()
    {
        // Large multi-chunk world with 4-pass mode
        var sim = new SimulationFixture(256, 256);
        sim.Simulator.UseFourPassGrouping = true;
        sim.Fill(0, 240, 256, 16, Materials.Stone);

        int sandPlaced = 0;
        int waterPlaced = 0;

        // Sand spanning multiple chunks
        for (int x = 50; x < 200; x += 3)
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandPlaced++;
            }

        // Water in a different area
        for (int x = 10; x < 40; x++)
            for (int y = 0; y < 5; y++)
            {
                sim.Set(x, y, Materials.Water);
                waterPlaced++;
            }

        sim.Step(500);

        Assert.Equal(sandPlaced, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(waterPlaced, WorldAssert.CountMaterial(sim.World, Materials.Water));
    }

    [Fact]
    public void FourPassMode_Deterministic()
    {
        byte[] state1 = RunWithMode(useFourPass: true);
        byte[] state2 = RunWithMode(useFourPass: true);

        Assert.Equal(state1.Length, state2.Length);
        for (int i = 0; i < state1.Length; i++)
        {
            Assert.True(state1[i] == state2[i],
                $"4-pass mode not deterministic at byte {i}");
        }
    }

    [Fact]
    public void FourPassMode_CrossChunkBoundary_SandFalls()
    {
        // Sand placed at chunk boundary should fall correctly in 4-pass mode
        var sim = new SimulationFixture(192, 128); // 3x2 chunks
        sim.Simulator.UseFourPassGrouping = true;
        sim.Fill(0, 120, 192, 8, Materials.Stone);

        // Place sand right at chunk boundaries (x=63/64 and x=127/128)
        sim.Set(63, 10, Materials.Sand);
        sim.Set(64, 10, Materials.Sand);
        sim.Set(127, 10, Materials.Sand);
        sim.Set(128, 10, Materials.Sand);

        sim.Step(500);

        Assert.Equal(4, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void InactiveChunks_NotAssignedToAnyGroup()
    {
        var world = new CellWorld(256, 256); // 4x4 chunks

        // Only dirty one chunk
        world.SetCell(10, 10, Materials.Sand);

        var gA = new List<int>();
        var gB = new List<int>();
        var gC = new List<int>();
        var gD = new List<int>();

        world.CollectChunkGroups(gA, gB, gC, gD);

        int total = gA.Count + gB.Count + gC.Count + gD.Count;
        Assert.Equal(1, total); // Only the one dirty chunk
    }

    private static byte[] RunWithMode(bool useFourPass)
    {
        var sim = new SimulationFixture(256, 256);
        sim.Simulator.UseFourPassGrouping = useFourPass;
        sim.Fill(0, 240, 256, 16, Materials.Stone);

        // Place materials across multiple chunks
        for (int x = 60; x < 70; x++)
            for (int y = 0; y < 5; y++)
                sim.Set(x, y, Materials.Sand);

        for (int x = 130; x < 140; x++)
            for (int y = 0; y < 5; y++)
                sim.Set(x, y, Materials.Water);

        sim.Step(200);

        // Snapshot all cell data
        byte[] snapshot = new byte[sim.World.cells.Length * 11];
        for (int i = 0; i < sim.World.cells.Length; i++)
        {
            var cell = sim.World.cells[i];
            int offset = i * 11;
            snapshot[offset + 0] = cell.materialId;
            snapshot[offset + 1] = cell.flags;
            snapshot[offset + 2] = (byte)cell.velocityX;
            snapshot[offset + 3] = (byte)cell.velocityY;
            snapshot[offset + 4] = cell.temperature;
            snapshot[offset + 5] = cell.structureId;
            snapshot[offset + 6] = (byte)(cell.ownerId & 0xFF);
            snapshot[offset + 7] = (byte)(cell.ownerId >> 8);
            snapshot[offset + 8] = cell.velocityFracX;
            snapshot[offset + 9] = cell.velocityFracY;
            snapshot[offset + 10] = cell.frameUpdated;
        }
        return snapshot;
    }
}
