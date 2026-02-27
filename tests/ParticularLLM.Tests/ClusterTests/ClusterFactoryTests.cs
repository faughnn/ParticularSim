using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for ClusterFactory: creation, physics property calculation, world region extraction.
///
/// English rules:
///
/// CREATE FROM PIXELS:
/// - Cluster is positioned at the given cell coordinates
/// - All pixels are stored in the cluster
/// - Physics properties (mass, moment of inertia) are calculated from pixel positions
/// - Cluster is registered with the manager and gets a unique ID
///
/// CREATE FROM WORLD REGION:
/// - Extracts non-air cells from the specified region
/// - Extracted cells are cleared from the world (set to Air)
/// - Pixel local coordinates are relative to the region center
/// - Material conservation: extracted pixels equal cleared cells
/// - Returns null if no non-air cells found
///
/// PHYSICS PROPERTIES:
/// - Mass = number of pixels (each pixel contributes 1 unit)
/// - Moment of inertia calculated from pixel distances to center of mass
/// - Minimum moment of inertia is 0.1 (prevents instability)
///
/// SHAPE HELPERS:
/// - CreateSquareCluster: (2*half+1)² pixels centered at (0,0)
/// - CreateCircleCluster: pixels within radius centered at (0,0)
/// - CreateLShapeCluster: vertical bar + horizontal bar
/// </summary>
public class ClusterFactoryTests
{
    [Fact]
    public void CreateCluster_SetsPositionAndRegisters()
    {
        var manager = new ClusterManager();
        var pixels = new List<ClusterPixel>
        {
            new ClusterPixel(0, 0, Materials.Stone),
            new ClusterPixel(1, 0, Materials.Stone),
        };

        var cluster = ClusterFactory.CreateCluster(pixels, 10f, 20f, manager);

        Assert.NotNull(cluster);
        Assert.Equal(10f, cluster.X);
        Assert.Equal(20f, cluster.Y);
        Assert.Equal(2, cluster.PixelCount);
        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void CreateCluster_CalculatesMass()
    {
        var manager = new ClusterManager();
        var pixels = new List<ClusterPixel>();
        for (int i = 0; i < 25; i++)
            pixels.Add(new ClusterPixel((short)(i % 5), (short)(i / 5), Materials.Stone));

        var cluster = ClusterFactory.CreateCluster(pixels, 0, 0, manager);

        Assert.Equal(25f, cluster.Mass);
    }

    [Fact]
    public void CreateCluster_MomentOfInertia_Nonzero()
    {
        var manager = new ClusterManager();
        var cluster = ClusterFactory.CreateSquareCluster(0, 0, 4, Materials.Stone, manager);

        Assert.True(cluster.MomentOfInertia > 0, "MOI should be positive");
    }

    [Fact]
    public void CreateCluster_MomentOfInertia_LargerForWiderShapes()
    {
        var manager = new ClusterManager();

        var small = ClusterFactory.CreateSquareCluster(0, 0, 2, Materials.Stone, manager);
        var large = ClusterFactory.CreateSquareCluster(0, 0, 6, Materials.Stone, manager);

        Assert.True(large.MomentOfInertia > small.MomentOfInertia,
            $"Wider cluster should have higher MOI: small={small.MomentOfInertia:F2}, large={large.MomentOfInertia:F2}");
    }

    [Fact]
    public void CreateSquareCluster_CorrectPixelCount()
    {
        var manager = new ClusterManager();

        // size=2 → half=1, range -1..1, so 3×3 = 9 pixels
        var cluster = ClusterFactory.CreateSquareCluster(0, 0, 2, Materials.Stone, manager);
        Assert.Equal(9, cluster.PixelCount);

        // size=4 → half=2, range -2..2, so 5×5 = 25 pixels
        var cluster2 = ClusterFactory.CreateSquareCluster(0, 0, 4, Materials.Stone, manager);
        Assert.Equal(25, cluster2.PixelCount);
    }

    [Fact]
    public void CreateCircleCluster_ReasonablePixelCount()
    {
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateCircleCluster(0, 0, 3, Materials.Stone, manager);

        // Circle with radius 3: area ≈ π*9 ≈ 28, integer circle is close
        Assert.True(cluster.PixelCount >= 20 && cluster.PixelCount <= 35,
            $"Circle radius 3 should have ~28 pixels, got {cluster.PixelCount}");
    }

    [Fact]
    public void CreateLShapeCluster_HasPixels()
    {
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateLShapeCluster(0, 0, 6, Materials.Stone, manager);
        Assert.True(cluster.PixelCount > 0, "L-shape should have pixels");
    }

    [Fact]
    public void CreateClusterFromRegion_ExtractsCells()
    {
        var world = new CellWorld(64, 64);

        // Place a 3x3 block of sand
        for (int y = 10; y < 13; y++)
            for (int x = 10; x < 13; x++)
                world.SetCell(x, y, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(9, sandBefore);

        var manager = new ClusterManager();
        var cluster = ClusterFactory.CreateClusterFromRegion(world, 10, 10, 3, 3, manager);

        Assert.NotNull(cluster);
        Assert.Equal(9, cluster.PixelCount);

        // World should have no sand left (extracted into cluster)
        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(0, sandAfter);
    }

    [Fact]
    public void CreateClusterFromRegion_MaterialConservation()
    {
        var world = new CellWorld(64, 64);

        // Place mixed materials
        world.SetCell(20, 20, Materials.Stone);
        world.SetCell(21, 20, Materials.Sand);
        world.SetCell(20, 21, Materials.IronOre);

        var manager = new ClusterManager();
        var cluster = ClusterFactory.CreateClusterFromRegion(world, 20, 20, 2, 2, manager);

        Assert.NotNull(cluster);
        Assert.Equal(3, cluster.PixelCount);

        // Verify materials in pixels
        var materials = cluster.Pixels.Select(p => p.materialId).OrderBy(m => m).ToList();
        Assert.Contains(Materials.Stone, materials);
        Assert.Contains(Materials.Sand, materials);
        Assert.Contains(Materials.IronOre, materials);
    }

    [Fact]
    public void CreateClusterFromRegion_EmptyRegion_ReturnsNull()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateClusterFromRegion(world, 10, 10, 5, 5, manager);

        Assert.Null(cluster);
        Assert.Equal(0, manager.ActiveCount);
    }

    [Fact]
    public void CreateClusterFromRegion_ClearsOwnedCells()
    {
        var world = new CellWorld(64, 64);

        // Place cells that are already owned by another cluster — they should be skipped
        int index = 20 * 64 + 20;
        world.cells[index] = new Cell
        {
            materialId = Materials.Stone,
            ownerId = 999, // Owned by another cluster
        };
        world.SetCell(21, 20, Materials.Sand); // Not owned

        var manager = new ClusterManager();
        var cluster = ClusterFactory.CreateClusterFromRegion(world, 20, 20, 2, 1, manager);

        // Should only extract the unowned cell
        Assert.NotNull(cluster);
        Assert.Equal(1, cluster.PixelCount);
        Assert.Equal(Materials.Sand, cluster.Pixels[0].materialId);
    }

    [Fact]
    public void CreateClusterFromRegion_Position_IsRegionCenter()
    {
        var world = new CellWorld(64, 64);
        world.SetCell(10, 10, Materials.Stone);

        var manager = new ClusterManager();
        var cluster = ClusterFactory.CreateClusterFromRegion(world, 10, 10, 4, 4, manager);

        Assert.NotNull(cluster);
        // Center of region (10,10)-(13,13) is (12, 12)
        Assert.Equal(12f, cluster.X);
        Assert.Equal(12f, cluster.Y);
    }
}
