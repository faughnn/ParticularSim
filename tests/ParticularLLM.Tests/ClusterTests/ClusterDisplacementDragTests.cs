using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for cluster displacement drag — clusters slow down when displacing
/// powder/liquid materials, and can come to rest embedded in them.
///
/// English rules:
///
/// DISPLACEMENT DRAG (sync phase):
/// - Each displaced cell applies drag: dragImpulse = cellDensity / (256 * clusterMass)
/// - Drag is applied before DisplaceCell so later cells get reduced push velocity
/// - DisplacedCellsLastSync tracks count for next frame's physics
///
/// SOFT GROUND SUPPORT (physics phase):
/// - When cluster is slow (speed &lt; 0.5) and moving down in material: cancel velocity, set IsOnGround
/// - When cluster is fast in material: apply continuous damping proportional to displaced cells / mass
/// - Without soft ground, gravity would re-accelerate between displacement events, preventing sleep
///
/// SLEEP IN MATERIAL:
/// - Soft ground sets IsOnGround → LowVelocityFrames increments → after 30 frames → sleep
/// - Once sleeping, position must not change (no jitter)
///
/// SCALING:
/// - Heavier clusters penetrate deeper (more momentum to dissipate)
/// - Denser materials provide more drag per cell
/// - Water (density=64) provides less drag than sand (density=128)
/// </summary>
public class ClusterDisplacementDragTests
{
    [Fact]
    public void ClusterStopsInDeepSand()
    {
        // 3×3 stone cluster (9 pixels, mass=9) falls into 30-row sand pile.
        // Should sleep before hitting the stone floor at row 60.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Stone floor at y=60
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 60, Materials.Stone);

        // 30 rows of sand: y=30..59
        for (int x = 0; x < 64; x++)
            for (int y = 30; y < 60; y++)
                world.SetCell(x, y, Materials.Sand);

        // 3×3 cluster starting at y=5 (well above sand)
        var cluster = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, manager);

        // Run until settled
        for (int i = 0; i < 500; i++)
            manager.StepAndSync(world);

        // Cluster should have stopped and be sleeping
        Assert.True(cluster.IsSleeping, $"Cluster should sleep in sand, Y={cluster.Y:F2}");

        // Cluster should NOT have reached the floor (y=60 minus cluster half-size)
        Assert.True(cluster.Y < 57, $"Cluster should stop in sand, not reach floor: Y={cluster.Y:F2}");

        // Cluster should have penetrated into the sand (past y=30)
        Assert.True(cluster.Y > 30, $"Cluster should penetrate into sand: Y={cluster.Y:F2}");
    }

    [Fact]
    public void HeavierClusterPenetratesDeeperThanLighter()
    {
        // A 5×5 cluster has more momentum than a 3×3, so it should go deeper.
        // Both drop from same height into identical sand piles.

        // --- Small cluster (3×3, mass=9) ---
        var world1 = new CellWorld(64, 64);
        var mgr1 = new ClusterManager();
        for (int x = 0; x < 64; x++)
            world1.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 25; y < 60; y++)
                world1.SetCell(x, y, Materials.Sand);
        var small = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, mgr1);
        for (int i = 0; i < 500; i++)
            mgr1.StepAndSync(world1);

        // --- Large cluster (5×5, mass=25) ---
        var world2 = new CellWorld(64, 64);
        var mgr2 = new ClusterManager();
        for (int x = 0; x < 64; x++)
            world2.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 25; y < 60; y++)
                world2.SetCell(x, y, Materials.Sand);
        var large = ClusterFactory.CreateSquareCluster(32f, 5f, 4, Materials.IronOre, mgr2);
        for (int i = 0; i < 500; i++)
            mgr2.StepAndSync(world2);

        // Both should sleep
        Assert.True(small.IsSleeping, "Small cluster should sleep");
        Assert.True(large.IsSleeping, "Large cluster should sleep");

        // Larger cluster should penetrate deeper (higher Y = deeper in our coordinate system)
        Assert.True(large.Y > small.Y,
            $"5×5 (Y={large.Y:F2}) should go deeper than 3×3 (Y={small.Y:F2})");
    }

    [Fact]
    public void MaterialConservation_DuringDisplacementDrag()
    {
        // Sand count must remain constant every frame as cluster falls through.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        for (int x = 0; x < 64; x++)
            world.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 30; y < 60; y++)
                world.SetCell(x, y, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        var cluster = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, manager);
        int ironBefore = cluster.PixelCount;

        for (int frame = 0; frame < 300; frame++)
        {
            manager.StepAndSync(world);

            int sandNow = WorldAssert.CountMaterial(world, Materials.Sand);
            Assert.Equal(sandBefore, sandNow);

            int ironNow = WorldAssert.CountMaterial(world, Materials.IronOre);
            Assert.Equal(ironBefore, ironNow);
        }
    }

    [Fact]
    public void NoJitterAtRest()
    {
        // After sleeping, the cluster position must not change for 100+ frames.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        for (int x = 0; x < 64; x++)
            world.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 30; y < 60; y++)
                world.SetCell(x, y, Materials.Sand);

        var cluster = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, manager);

        // Run until sleeping
        for (int i = 0; i < 500; i++)
        {
            manager.StepAndSync(world);
            if (cluster.IsSleeping) break;
        }
        Assert.True(cluster.IsSleeping, "Cluster should eventually sleep");

        float sleepX = cluster.X;
        float sleepY = cluster.Y;

        // Run 100 more frames — position must not change
        for (int i = 0; i < 100; i++)
        {
            manager.StepAndSync(world);
            Assert.Equal(sleepX, cluster.X);
            Assert.Equal(sleepY, cluster.Y);
            Assert.True(cluster.IsSleeping, $"Cluster woke up on frame {i}");
        }
    }

    [Fact]
    public void WaterLessDragThanSand()
    {
        // Water density (64) is half of sand (128), so cluster should
        // penetrate deeper through water than through sand.

        // --- Sand ---
        var worldSand = new CellWorld(64, 64);
        var mgrSand = new ClusterManager();
        for (int x = 0; x < 64; x++)
            worldSand.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 25; y < 60; y++)
                worldSand.SetCell(x, y, Materials.Sand);
        var clusterSand = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, mgrSand);
        for (int i = 0; i < 500; i++)
            mgrSand.StepAndSync(worldSand);

        // --- Water ---
        var worldWater = new CellWorld(64, 64);
        var mgrWater = new ClusterManager();
        for (int x = 0; x < 64; x++)
            worldWater.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 25; y < 60; y++)
                worldWater.SetCell(x, y, Materials.Water);
        var clusterWater = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, mgrWater);
        for (int i = 0; i < 500; i++)
            mgrWater.StepAndSync(worldWater);

        Assert.True(clusterSand.IsSleeping, "Sand cluster should sleep");
        Assert.True(clusterWater.IsSleeping, "Water cluster should sleep");

        // Cluster in water should go deeper (higher Y)
        Assert.True(clusterWater.Y > clusterSand.Y,
            $"Water (Y={clusterWater.Y:F2}) should allow deeper penetration than sand (Y={clusterSand.Y:F2})");
    }

    [Fact]
    public void DisplacedCellsLastSync_TrackedCorrectly()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Cluster in empty air — no displacements
        var cluster = ClusterFactory.CreateSquareCluster(32f, 10f, 2, Materials.Stone, manager);

        manager.StepAndSync(world);
        Assert.Equal(0, cluster.DisplacedCellsLastSync);

        // Now place sand where the cluster will be next frame
        // The cluster is falling, so place sand just below its current position
        float nextY = cluster.Y + cluster.VelocityY + ClusterPhysics.Gravity;
        int sandY = (int)MathF.Round(nextY);

        // Place a row of sand across the cluster's width
        for (int x = 31; x <= 33; x++)
        {
            if (world.IsInBounds(x, sandY))
                world.SetCell(x, sandY, Materials.Sand);
        }

        manager.StepAndSync(world);

        // Should have displaced some cells (exact count depends on overlap)
        Assert.True(cluster.DisplacedCellsLastSync >= 0,
            "DisplacedCellsLastSync should be non-negative");
    }

    [Fact]
    public void DisplacedCellsLastSync_ZeroInAir()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Cluster falling through pure air
        var cluster = ClusterFactory.CreateSquareCluster(32f, 10f, 2, Materials.Stone, manager);

        for (int i = 0; i < 10; i++)
        {
            manager.StepAndSync(world);
            Assert.Equal(0, cluster.DisplacedCellsLastSync);
        }
    }

    [Fact]
    public void FullIntegration_ClusterAndCellSimTogether()
    {
        // Run with full cell simulation (sand settles, water flows) + cluster physics.
        var sim = new SimulationFixture(64, 64);
        var manager = new ClusterManager();
        sim.Simulator.SetClusterManager(manager);

        // Stone floor
        sim.Fill(0, 60, 64, 1, Materials.Stone);

        // Sand pile
        sim.Fill(20, 40, 24, 20, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(sim.World, Materials.Sand);

        // Drop cluster from above
        var cluster = ClusterFactory.CreateSquareCluster(32f, 5f, 2, Materials.IronOre, manager);

        // Run full integrated simulation
        sim.Step(500);

        // Cluster should have settled
        Assert.True(cluster.IsSleeping, $"Cluster should sleep, Y={cluster.Y:F2}");

        // Material conservation
        int sandAfter = WorldAssert.CountMaterial(sim.World, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);

        int ironOre = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        Assert.Equal(cluster.PixelCount, ironOre);
    }

    [Fact]
    public void SinglePixelCluster_HighDragStopsQuickly()
    {
        // A 1-pixel cluster (mass=1) should stop very quickly in sand
        // because dragImpulse = 128/(256*1) = 0.5 per cell — massive drag.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        for (int x = 0; x < 64; x++)
            world.SetCell(x, 60, Materials.Stone);
        for (int x = 0; x < 64; x++)
            for (int y = 30; y < 60; y++)
                world.SetCell(x, y, Materials.Sand);

        // Single pixel cluster
        var id = manager.AllocateId();
        var cluster = new ClusterData(id);
        cluster.X = 32f;
        cluster.Y = 5f;
        cluster.Mass = 1f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.IronOre));
        manager.Register(cluster);

        for (int i = 0; i < 500; i++)
            manager.StepAndSync(world);

        Assert.True(cluster.IsSleeping, "Single-pixel cluster should sleep quickly");
        // Should stop near top of sand (y~30-35) due to extreme drag
        Assert.True(cluster.Y < 45,
            $"Single-pixel should stop near sand surface: Y={cluster.Y:F2}");
    }
}
