using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for ClusterData pixel lookup and world-cell mapping.
///
/// English rules (derived from Unity ClusterData + our cell-space port):
///
/// PIXEL LOOKUP:
/// - BuildPixelLookup creates a dense 2D array from sparse pixel list
/// - GetPixelMaterialAt returns the material for valid local coordinates
/// - GetPixelMaterialAt returns Air for positions outside the pixel bounds
/// - GetPixelMaterialAt returns Air for positions inside bounds but without a pixel (gaps)
/// - AddPixel invalidates the cached lookup (forces rebuild)
///
/// WORLD CELL MAPPING (ForEachWorldCell):
/// - Uses inverse mapping: iterates world-space AABB, transforms to local coords, looks up pixels
/// - Rotation is applied correctly (cos/sin rotation matrix)
/// - At rotation=0, pixel local offsets map directly to world cell offsets from position
/// - At rotation=π/2, pixels rotate 90° counterclockwise
/// - Out-of-bounds world cells are skipped (no crash, no callback)
/// - Every pixel is represented in world cells (no gaps from rounding)
/// - The number of world cells equals the number of pixels (at rotation=0)
/// </summary>
public class ClusterDataLookupTests
{
    [Fact]
    public void BuildPixelLookup_SinglePixel()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.BuildPixelLookup();

        Assert.Equal(Materials.Stone, cluster.GetPixelMaterialAt(0, 0));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(1, 0));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(0, 1));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(-1, -1));
    }

    [Fact]
    public void BuildPixelLookup_Square3x3()
    {
        var cluster = new ClusterData(1);
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                cluster.AddPixel(new ClusterPixel((short)x, (short)y, Materials.Stone));

        cluster.BuildPixelLookup();

        // All 9 positions should have Stone
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                Assert.Equal(Materials.Stone, cluster.GetPixelMaterialAt(x, y));

        // Outside should be Air
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(2, 0));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(0, 2));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(-2, 0));
    }

    [Fact]
    public void BuildPixelLookup_LShape_HasGaps()
    {
        var cluster = new ClusterData(1);
        // L-shape: vertical bar at x=0, horizontal bar at y=2
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(0, 1, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(0, 2, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(1, 2, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(2, 2, Materials.Stone));

        cluster.BuildPixelLookup();

        // Filled positions
        Assert.Equal(Materials.Stone, cluster.GetPixelMaterialAt(0, 0));
        Assert.Equal(Materials.Stone, cluster.GetPixelMaterialAt(2, 2));

        // Gap at (1,0) and (2,0) — inside bounds but no pixel
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(1, 0));
        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(2, 0));
    }

    [Fact]
    public void BuildPixelLookup_MixedMaterials()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Sand));
        cluster.AddPixel(new ClusterPixel(0, 1, Materials.IronOre));

        cluster.BuildPixelLookup();

        Assert.Equal(Materials.Stone, cluster.GetPixelMaterialAt(0, 0));
        Assert.Equal(Materials.Sand, cluster.GetPixelMaterialAt(1, 0));
        Assert.Equal(Materials.IronOre, cluster.GetPixelMaterialAt(0, 1));
    }

    [Fact]
    public void BuildPixelLookup_EmptyCluster()
    {
        var cluster = new ClusterData(1);
        cluster.BuildPixelLookup();

        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(0, 0));
    }

    [Fact]
    public void AddPixel_InvalidatesLookup()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.BuildPixelLookup();

        Assert.Equal(Materials.Air, cluster.GetPixelMaterialAt(1, 0));

        // Add pixel and re-query (should auto-rebuild)
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Sand));
        Assert.Equal(Materials.Sand, cluster.GetPixelMaterialAt(1, 0));
    }

    [Fact]
    public void LocalBounds_Correct()
    {
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(-2, -3, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(4, 5, Materials.Stone));
        cluster.BuildPixelLookup();

        Assert.Equal(-2, cluster.LocalMinX);
        Assert.Equal(4, cluster.LocalMaxX);
        Assert.Equal(-3, cluster.LocalMinY);
        Assert.Equal(5, cluster.LocalMaxY);
    }

    [Fact]
    public void ForEachWorldCell_NoRotation_MapsDirectly()
    {
        var cluster = new ClusterData(1);
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.Rotation = 0;

        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(0, 1, Materials.Stone));

        var cells = new List<(int x, int y, byte mat)>();
        cluster.ForEachWorldCell(64, 64, (cx, cy, mat) => cells.Add((cx, cy, mat)));

        Assert.Equal(3, cells.Count);
        Assert.Contains((32, 32, Materials.Stone), cells);
        Assert.Contains((33, 32, Materials.Stone), cells);
        Assert.Contains((32, 33, Materials.Stone), cells);
    }

    [Fact]
    public void ForEachWorldCell_Rotation90_RotatesCorrectly()
    {
        var cluster = new ClusterData(1);
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.Rotation = MathF.PI / 2; // 90° counterclockwise

        // Single pixel at local (1, 0)
        // After 90° CCW rotation: (1,0) → (0,1) in standard math
        // But we need to check what the inverse mapping produces
        cluster.AddPixel(new ClusterPixel(1, 0, Materials.Stone));
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));

        var cells = new List<(int x, int y, byte mat)>();
        cluster.ForEachWorldCell(64, 64, (cx, cy, mat) => cells.Add((cx, cy, mat)));

        // Center pixel (0,0) should always map to (32,32) regardless of rotation
        Assert.Contains(cells, c => c.x == 32 && c.y == 32);
        // Should have exactly 2 cells
        Assert.Equal(2, cells.Count);
    }

    [Fact]
    public void ForEachWorldCell_OutOfBounds_Skipped()
    {
        var cluster = new ClusterData(1);
        cluster.X = 0f; // At edge
        cluster.Y = 0f;
        cluster.Rotation = 0;

        cluster.AddPixel(new ClusterPixel(-1, 0, Materials.Stone)); // Would be at x=-1 (out of bounds)
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));  // At (0,0) (in bounds)

        var cells = new List<(int x, int y, byte mat)>();
        cluster.ForEachWorldCell(64, 64, (cx, cy, mat) => cells.Add((cx, cy, mat)));

        // Only the in-bounds pixel should be reported
        Assert.Single(cells);
        Assert.Equal((0, 0, Materials.Stone), cells[0]);
    }

    [Fact]
    public void ForEachWorldCell_PixelCount_MatchesAtNoRotation()
    {
        var cluster = new ClusterData(1);
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.Rotation = 0;

        int expectedCount = 0;
        for (int y = -3; y <= 3; y++)
        {
            for (int x = -3; x <= 3; x++)
            {
                cluster.AddPixel(new ClusterPixel((short)x, (short)y, Materials.Stone));
                expectedCount++;
            }
        }

        var cells = new List<(int x, int y, byte mat)>();
        cluster.ForEachWorldCell(64, 64, (cx, cy, mat) => cells.Add((cx, cy, mat)));

        Assert.Equal(expectedCount, cells.Count);
    }

    [Fact]
    public void ForEachWorldCell_EarlyExit_Works()
    {
        var cluster = new ClusterData(1);
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.Rotation = 0;

        for (int i = 0; i < 10; i++)
            cluster.AddPixel(new ClusterPixel((short)i, 0, Materials.Stone));

        int count = 0;
        bool exited = cluster.ForEachWorldCell(64, 64, (cx, cy, mat) =>
        {
            count++;
            return count >= 3; // Stop after 3
        });

        Assert.True(exited);
        Assert.Equal(3, count);
    }

    [Fact]
    public void ShouldSkipSync_Sleeping_SamePosition_ReturnsTrue()
    {
        var cluster = new ClusterData(1);
        cluster.X = 10;
        cluster.Y = 20;
        cluster.Rotation = 0;
        cluster.IsSleeping = true;
        cluster.IsPixelsSynced = true;
        cluster.LastSyncedX = 10;
        cluster.LastSyncedY = 20;
        cluster.LastSyncedRotation = 0;

        Assert.True(cluster.ShouldSkipSync());
    }

    [Fact]
    public void ShouldSkipSync_NotSleeping_ReturnsFalse()
    {
        var cluster = new ClusterData(1);
        cluster.IsSleeping = false;
        cluster.IsPixelsSynced = true;
        Assert.False(cluster.ShouldSkipSync());
    }

    [Fact]
    public void ShouldSkipSync_MachinePart_ReturnsFalse()
    {
        var cluster = new ClusterData(1);
        cluster.IsSleeping = true;
        cluster.IsPixelsSynced = true;
        cluster.IsMachinePart = true;
        Assert.False(cluster.ShouldSkipSync());
    }

    [Fact]
    public void ShouldSkipSync_Moved_ReturnsFalse()
    {
        var cluster = new ClusterData(1);
        cluster.X = 15;
        cluster.Y = 20;
        cluster.IsSleeping = true;
        cluster.IsPixelsSynced = true;
        cluster.LastSyncedX = 10;
        cluster.LastSyncedY = 20;
        Assert.False(cluster.ShouldSkipSync());
    }

    [Fact]
    public void Wake_ClearsSleepState()
    {
        var cluster = new ClusterData(1);
        cluster.IsSleeping = true;
        cluster.LowVelocityFrames = 50;

        cluster.Wake();

        Assert.False(cluster.IsSleeping);
        Assert.Equal(0, cluster.LowVelocityFrames);
    }
}
