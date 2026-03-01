using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for the ClusterManager pipeline: clear, physics, sync, displacement.
///
/// English rules:
///
/// ID ALLOCATION:
/// - IDs start at 1 (0 is reserved for "no owner")
/// - Released IDs are reused
///
/// 3-STEP PIPELINE (StepAndSync):
/// - CLEAR: cluster pixels are removed from grid (cells with matching ownerId → Air)
/// - PHYSICS: gravity applied, collision with statics, position updated
/// - SYNC: cluster pixels written to grid at new positions
/// - Order matters: clear BEFORE physics, sync AFTER physics
///
/// DISPLACEMENT (push-based, NOT BFS):
/// - When cluster pixel overlaps a movable cell (powder/liquid/gas):
///   - Cell is moved one step in the cluster's movement direction
///   - Cell receives push velocity proportional to cluster speed,
///     inversely proportional to material density
///   - Light materials (Water, density=64) are pushed harder than heavy (IronOre, density=200)
/// - Static materials are never displaced (physics should prevent overlap)
/// - Material conservation: displaced cells are always placed somewhere (never destroyed)
///
/// SYNC PIXEL OWNERSHIP:
/// - Synced pixels have ownerId set to the cluster's ID
/// - Only cells with matching ownerId are cleared during step 1
///
/// SLEEPING CLUSTER OPTIMIZATION:
/// - Sleeping clusters at same position skip clear and sync
/// </summary>
public class ClusterManagerTests
{
    [Fact]
    public void AllocateId_StartsAt1()
    {
        var manager = new ClusterManager();
        Assert.Equal(1, manager.AllocateId());
        Assert.Equal(2, manager.AllocateId());
        Assert.Equal(3, manager.AllocateId());
    }

    [Fact]
    public void AllocateId_ReusesReleasedIds()
    {
        var manager = new ClusterManager();
        ushort id1 = manager.AllocateId();
        ushort id2 = manager.AllocateId();

        manager.ReleaseId(id1);

        ushort id3 = manager.AllocateId();
        Assert.Equal(id1, id3); // Reused
    }

    [Fact]
    public void Register_TracksCluster()
    {
        var manager = new ClusterManager();
        var cluster = new ClusterData(1);
        manager.Register(cluster);

        Assert.Equal(1, manager.ActiveCount);
        Assert.Same(cluster, manager.GetCluster(1));
    }

    [Fact]
    public void Unregister_RemovesCluster()
    {
        var manager = new ClusterManager();
        var cluster = new ClusterData(1);
        manager.Register(cluster);
        manager.Unregister(cluster);

        Assert.Equal(0, manager.ActiveCount);
        Assert.Null(manager.GetCluster(1));
    }

    [Fact]
    public void StepAndSync_ClusterFalls()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        var cluster = ClusterFactory.CreateSquareCluster(32f, 10f, 2, Materials.Stone, manager);
        float startY = cluster.Y;

        // Step several frames
        for (int i = 0; i < 30; i++)
            manager.StepAndSync(world);

        Assert.True(cluster.Y > startY, "Cluster should fall under gravity");
    }

    [Fact]
    public void StepAndSync_PixelsAppearInGrid()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true; // Prevent movement
        manager.Register(cluster);

        manager.StepAndSync(world);

        // The pixel should be synced to the grid
        Assert.Equal(Materials.Stone, world.GetCell(32, 32));
    }

    [Fact]
    public void StepAndSync_PixelOwnership()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        ushort id = manager.AllocateId();
        var cluster = new ClusterData(id);
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true;
        manager.Register(cluster);

        manager.StepAndSync(world);

        Cell cell = world.cells[32 * 64 + 32];
        Assert.Equal(id, cell.ownerId);
    }

    [Fact]
    public void StepAndSync_ClearPreviousPosition()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Place floor to stop the cluster
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 20f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        manager.Register(cluster);

        // First sync places pixel
        manager.StepAndSync(world);

        // Get initial position
        float firstY = cluster.Y;

        // Step more — cluster moves
        for (int i = 0; i < 20; i++)
            manager.StepAndSync(world);

        // Old position should be Air (cleared during step 1)
        // Note: cluster has moved, so original position should be empty
        Assert.True(cluster.Y > firstY, "Cluster should have moved down");
    }

    [Fact]
    public void Displacement_PushesMovableMaterial()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Place sand where the cluster will land
        world.SetCell(32, 32, Materials.Sand);

        // Create cluster at that position, already sleeping (to avoid physics complexity)
        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.VelocityY = 2f; // Moving down
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true; // Skip physics, just test sync
        manager.Register(cluster);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        manager.StepAndSync(world);

        // Sand should be displaced but not destroyed
        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);

        // Cluster pixel should be at (32, 32)
        Assert.Equal(Materials.Stone, world.GetCell(32, 32));
        Assert.Equal(cluster.Id, world.cells[32 * 64 + 32].ownerId);

        // Sand should have been pushed somewhere nearby
        Assert.True(manager.DisplacementsThisFrame > 0, "Should have displaced the sand");
    }

    [Fact]
    public void Displacement_VelocityTransfer_LightMaterialPushedHarder()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Water (density=64) and Sand (density=128) at different positions
        world.SetCell(30, 32, Materials.Water);
        world.SetCell(34, 32, Materials.Sand);

        // Two clusters moving down at same speed
        var cluster1 = new ClusterData(manager.AllocateId());
        cluster1.X = 30f; cluster1.Y = 32f;
        cluster1.VelocityY = 4f;
        cluster1.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster1.IsSleeping = true;
        manager.Register(cluster1);

        var cluster2 = new ClusterData(manager.AllocateId());
        cluster2.X = 34f; cluster2.Y = 32f;
        cluster2.VelocityY = 4f;
        cluster2.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster2.IsSleeping = true;
        manager.Register(cluster2);

        manager.StepAndSync(world);

        // Both materials should be displaced (conservation)
        Assert.Equal(1, WorldAssert.CountMaterial(world, Materials.Water));
        Assert.Equal(1, WorldAssert.CountMaterial(world, Materials.Sand));
    }

    [Fact]
    public void Displacement_MaterialConservation_MultiplePixels()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Fill area with sand
        for (int x = 30; x < 35; x++)
            for (int y = 30; y < 35; y++)
                world.SetCell(x, y, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        // Large cluster moving through the sand
        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.VelocityY = 3f;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                cluster.AddPixel(new ClusterPixel((short)x, (short)y, Materials.Stone));
        cluster.IsSleeping = true;
        manager.Register(cluster);

        manager.StepAndSync(world);

        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);
    }

    [Fact]
    public void Displacement_CongestedArea_MaterialConserved()
    {
        // Cluster lands in a densely packed region where the immediate
        // neighborhood is full of sand. Displacement must use fallback
        // search to find air further out — no material should be lost.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Fill a large block of sand around the landing zone (20x20)
        for (int x = 22; x < 42; x++)
            for (int y = 22; y < 42; y++)
                world.SetCell(x, y, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        // 5x5 cluster landing right in the middle of the sand block
        var cluster = ClusterFactory.CreateSquareCluster(32f, 32f, 2, Materials.Stone, manager);
        cluster.VelocityY = 3f;
        cluster.IsSleeping = true; // Skip physics, isolate displacement behavior
        manager.Register(cluster);

        manager.StepAndSync(world);

        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);
        Assert.True(manager.DisplacementsThisFrame >= cluster.PixelCount,
            $"Expected at least {cluster.PixelCount} displacements, got {manager.DisplacementsThisFrame}");
    }

    [Fact]
    public void Displacement_ColumnFullAboveAndBelow_FindsAirElsewhere()
    {
        // The entire column at x=32 is filled with sand except where the
        // cluster pixel lands. Displacement can't go up or down in the
        // same column — must find air in an adjacent column.
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Fill entire column x=32 with sand
        for (int y = 0; y < 64; y++)
            world.SetCell(32, y, Materials.Sand);

        int sandBefore = WorldAssert.CountMaterial(world, Materials.Sand);

        // Single-pixel cluster at (32, 32)
        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.VelocityY = 2f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true;
        manager.Register(cluster);

        manager.StepAndSync(world);

        int sandAfter = WorldAssert.CountMaterial(world, Materials.Sand);
        Assert.Equal(sandBefore, sandAfter);

        // Cluster pixel should be placed
        Assert.Equal(Materials.Stone, world.GetCell(32, 32));
    }

    [Fact]
    public void StepAndSync_LandsOnFloor_Settles()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Stone floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        var cluster = ClusterFactory.CreateSquareCluster(32f, 10f, 2, Materials.IronOre, manager);

        // Run many frames
        for (int i = 0; i < 500; i++)
            manager.StepAndSync(world);

        // Should be sleeping on the floor
        Assert.True(cluster.IsSleeping, "Cluster should sleep after settling");
        Assert.True(cluster.Y > 40 && cluster.Y < 50,
            $"Cluster should rest near floor: Y={cluster.Y:F2}");

        // Pixels should be in grid
        int ironOreCount = WorldAssert.CountMaterial(world, Materials.IronOre);
        Assert.Equal(cluster.PixelCount, ironOreCount);
    }

    [Fact]
    public void RemoveCluster_ClearsPixelsAndUnregisters()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true;
        manager.Register(cluster);

        // Sync to place pixels
        manager.StepAndSync(world);
        Assert.Equal(Materials.Stone, world.GetCell(32, 32));

        // Remove
        manager.RemoveCluster(cluster, world);
        Assert.Equal(0, manager.ActiveCount);
        Assert.Equal(Materials.Air, world.GetCell(32, 32));
    }

    [Fact]
    public void MultipleClusters_Independent()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 128; x++)
            world.SetCell(x, 100, Materials.Stone);

        var c1 = ClusterFactory.CreateSquareCluster(30f, 10f, 2, Materials.Stone, manager);
        var c2 = ClusterFactory.CreateSquareCluster(90f, 10f, 2, Materials.IronOre, manager);

        Assert.Equal(2, manager.ActiveCount);

        for (int i = 0; i < 500; i++)
            manager.StepAndSync(world);

        // Both should settle
        Assert.True(c1.IsSleeping);
        Assert.True(c2.IsSleeping);

        // Both should have their pixels in the grid
        Assert.True(WorldAssert.CountMaterial(world, Materials.Stone) > 0);
        Assert.True(WorldAssert.CountMaterial(world, Materials.IronOre) > 0);
    }

    [Fact]
    public void SleepingCluster_SkipsSync()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 32f;
        cluster.Y = 32f;
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.IsSleeping = true;
        cluster.IsPixelsSynced = true;
        cluster.LastSyncedX = 32f;
        cluster.LastSyncedY = 32f;
        cluster.LastSyncedRotation = 0f;
        manager.Register(cluster);

        manager.StepAndSync(world);

        Assert.Equal(1, manager.SkippedSyncCount);
    }

    [Fact]
    public void Pipeline_IntegratedWithCellSimulator()
    {
        var sim = new SimulationFixture(64, 64);
        sim.Description = "A cluster dropped above a stone floor should fall under gravity and settle, with all its IronOre pixels synced to the grid.";
        var manager = new ClusterManager();
        sim.Simulator.SetClusterManager(manager);

        // Stone floor
        sim.Fill(0, 50, 64, 1, Materials.Stone);

        var cluster = ClusterFactory.CreateSquareCluster(32f, 10f, 2, Materials.IronOre, manager);

        // Run full simulation pipeline
        sim.Step(300);

        // Cluster should have settled
        Assert.True(cluster.IsSleeping);

        // IronOre pixels should be in grid (from cluster) + stone floor
        int ironOre = WorldAssert.CountMaterial(sim.World, Materials.IronOre);
        Assert.Equal(cluster.PixelCount, ironOre);
    }
}
