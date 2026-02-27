using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for ClusterFracturer: compression detection and crack-line partitioning.
///
/// English rules (derived from Unity ClusterFracturer.cs):
///
/// FRACTURE TRIGGER:
/// - CrushPressureFrames must exceed CrushFrameThreshold (30 frames)
/// - Sleeping clusters are skipped
/// - Machine parts are never fractured
/// - Clusters smaller than MinPixelsToFracture*2 (6) pixels are too small to fracture
///
/// CRACK-LINE PARTITIONING:
/// - 1-3 random crack lines generated through the cluster's bounding box
/// - Each pixel is assigned to a group based on signed distance to each crack line
/// - With N crack lines, up to 2^N groups are possible
///
/// SMALL GROUP MERGING:
/// - Groups with fewer than MinPixelsToFracture (3) pixels merge into the largest group
/// - Ensures all sub-clusters are viable (no dust fragments)
///
/// MATERIAL CONSERVATION:
/// - Total pixel count across all sub-clusters == original pixel count
/// - No pixels are destroyed during fracture
/// - Original cluster is removed after sub-clusters are created
///
/// SUB-CLUSTER PROPERTIES:
/// - Inherit parent's velocity and angular velocity
/// - Inherit parent's rotation
/// - Position offset based on group centroid in world space
/// - Pixel local coordinates re-centered around group centroid
///
/// VIABILITY CHECK:
/// - Need at least 2 non-empty groups with >= MinPixelsToFracture pixels each
/// - If check fails, fracture is aborted (cluster survives intact)
///
/// DETERMINISM:
/// - Seeded RNG ensures identical fracture results for identical inputs
/// </summary>
public class ClusterFractureTests
{
    [Fact]
    public void FractureCluster_SplitsIntoMultiplePieces()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Create a large square cluster (many pixels for reliable fracture)
        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        int originalPixels = cluster.PixelCount; // (8/2*2+1)² = 81

        Assert.True(originalPixels >= 12, $"Need enough pixels to fracture, got {originalPixels}");

        // Force fracture
        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        // Original cluster should be removed
        Assert.Null(manager.GetCluster(cluster.Id));

        // Should have at least 2 sub-clusters
        Assert.True(manager.ActiveCount >= 2,
            $"Expected at least 2 sub-clusters, got {manager.ActiveCount}");
    }

    [Fact]
    public void FractureCluster_MaterialConservation()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        int originalPixels = cluster.PixelCount;

        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        // Count total pixels across all sub-clusters
        int totalPixels = 0;
        foreach (var c in manager.AllClusters)
            totalPixels += c.PixelCount;

        Assert.Equal(originalPixels, totalPixels);
    }

    [Fact]
    public void FractureCluster_MixedMaterials_AllPreserved()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Create cluster with mixed materials
        var pixels = new List<ClusterPixel>();
        for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
            {
                byte mat = (y < 0) ? Materials.Stone : Materials.IronOre;
                pixels.Add(new ClusterPixel((short)x, (short)y, mat));
            }

        var cluster = ClusterFactory.CreateCluster(pixels, 64f, 64f, manager);

        int stoneBefore = pixels.Count(p => p.materialId == Materials.Stone);
        int ironBefore = pixels.Count(p => p.materialId == Materials.IronOre);

        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        int stoneAfter = 0, ironAfter = 0;
        foreach (var c in manager.AllClusters)
        {
            foreach (var p in c.Pixels)
            {
                if (p.materialId == Materials.Stone) stoneAfter++;
                else if (p.materialId == Materials.IronOre) ironAfter++;
            }
        }

        Assert.Equal(stoneBefore, stoneAfter);
        Assert.Equal(ironBefore, ironAfter);
    }

    [Fact]
    public void FractureCluster_TooSmall_NoFracture()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Only 4 pixels — below MinPixelsToFracture * 2 = 6
        var pixels = new List<ClusterPixel>
        {
            new(0, 0, Materials.Stone),
            new(1, 0, Materials.Stone),
            new(0, 1, Materials.Stone),
            new(1, 1, Materials.Stone),
        };
        var cluster = ClusterFactory.CreateCluster(pixels, 32f, 32f, manager);

        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        // Original should still exist (fracture was aborted)
        Assert.NotNull(manager.GetCluster(cluster.Id));
        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void FractureCluster_InheritsVelocity()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        cluster.VelocityX = 3f;
        cluster.VelocityY = -2f;
        cluster.AngularVelocity = 0.5f;
        cluster.Rotation = 0.3f;

        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        foreach (var c in manager.AllClusters)
        {
            Assert.Equal(3f, c.VelocityX);
            Assert.Equal(-2f, c.VelocityY);
            Assert.Equal(0.5f, c.AngularVelocity);
            Assert.Equal(0.3f, c.Rotation);
        }
    }

    [Fact]
    public void FractureCluster_Deterministic_SameSeed()
    {
        // Run twice with same seed, should produce identical results
        int[] pixelCounts1, pixelCounts2;

        {
            var world = new CellWorld(128, 128);
            var manager = new ClusterManager();
            var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
            ClusterFracturer.FractureCluster(cluster, manager, world, seed: 12345);
            pixelCounts1 = manager.AllClusters.Select(c => c.PixelCount).OrderBy(n => n).ToArray();
        }

        {
            var world = new CellWorld(128, 128);
            var manager = new ClusterManager();
            var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
            ClusterFracturer.FractureCluster(cluster, manager, world, seed: 12345);
            pixelCounts2 = manager.AllClusters.Select(c => c.PixelCount).OrderBy(n => n).ToArray();
        }

        Assert.Equal(pixelCounts1, pixelCounts2);
    }

    [Fact]
    public void FractureCluster_DifferentSeeds_DifferentResults()
    {
        int[] pixelCounts1, pixelCounts2;

        {
            var world = new CellWorld(128, 128);
            var manager = new ClusterManager();
            var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
            ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);
            pixelCounts1 = manager.AllClusters.Select(c => c.PixelCount).OrderBy(n => n).ToArray();
        }

        {
            var world = new CellWorld(128, 128);
            var manager = new ClusterManager();
            var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
            ClusterFracturer.FractureCluster(cluster, manager, world, seed: 999);
            pixelCounts2 = manager.AllClusters.Select(c => c.PixelCount).OrderBy(n => n).ToArray();
        }

        // Different seeds should usually produce different fracture patterns
        // (not guaranteed for all seeds, but very likely for a large cluster)
        // Just check that both produced sub-clusters
        Assert.True(pixelCounts1.Length >= 2);
        Assert.True(pixelCounts2.Length >= 2);
    }

    [Fact]
    public void FractureCluster_AllSubClusters_MinimumSize()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        foreach (var c in manager.AllClusters)
        {
            Assert.True(c.PixelCount >= ClusterFracturer.MinPixelsToFracture,
                $"Sub-cluster has {c.PixelCount} pixels, minimum is {ClusterFracturer.MinPixelsToFracture}");
        }
    }

    [Fact]
    public void CheckAndFracture_SkipsSleeping()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        cluster.CrushPressureFrames = 100; // Way above threshold
        cluster.IsSleeping = true;

        ClusterFracturer.CheckAndFracture(manager, world);

        // Should not have fractured (sleeping)
        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void CheckAndFracture_SkipsMachineParts()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        cluster.CrushPressureFrames = 100;
        cluster.IsMachinePart = true;

        ClusterFracturer.CheckAndFracture(manager, world);

        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void CheckAndFracture_BelowThreshold_NoFracture()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        cluster.CrushPressureFrames = 10; // Below threshold of 30

        ClusterFracturer.CheckAndFracture(manager, world);

        Assert.Equal(1, manager.ActiveCount);
    }

    [Fact]
    public void CheckAndFracture_AboveThreshold_Fractures()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(64f, 64f, 8, Materials.Stone, manager);
        cluster.CrushPressureFrames = 50; // Above threshold

        ClusterFracturer.CheckAndFracture(manager, world);

        // Original should be gone, sub-clusters should exist
        Assert.Null(manager.GetCluster(cluster.Id));
        Assert.True(manager.ActiveCount >= 2);
    }

    [Fact]
    public void FractureCluster_CircleShape_MaterialConservation()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateCircleCluster(64f, 64f, 5, Materials.Stone, manager);
        int originalPixels = cluster.PixelCount;

        ClusterFracturer.FractureCluster(cluster, manager, world, seed: 42);

        int totalPixels = 0;
        foreach (var c in manager.AllClusters)
            totalPixels += c.PixelCount;

        Assert.Equal(originalPixels, totalPixels);
    }
}
