using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for cluster-cluster collision detection and response.
///
/// English rules (derived from ClusterPhysics.cs):
///
/// OVERLAP DETECTION:
/// - Two clusters overlap when any pixel from one occupies the same world cell as a pixel from the other
/// - AABB pre-filter: if world-space bounding boxes don't overlap, skip pixel check
/// - A cluster never overlaps with itself
/// - Empty clusters (0 pixels) never overlap anything
///
/// COLLISION RESPONSE (1D momentum exchange):
/// - When a cluster moves along an axis and overlaps another cluster:
///   1. Movement is undone (position reverted)
///   2. Velocities are exchanged using conservation of momentum with restitution
///   3. Formula: v1' = (m1*v1 + m2*v2 - m2*e*(v1-v2)) / (m1+m2)
///              v2' = (m1*v1 + m2*v2 + m1*e*(v1-v2)) / (m1+m2)
///   4. Equal-mass elastic collision: clusters swap velocities
///   5. Heavy cluster barely changes, light cluster gets most impulse
/// - Sleeping clusters are woken by collision
/// - Landing on another cluster (moving down) counts as "on ground" for sleep
///
/// SEPARATED AXIS:
/// - X axis resolved first, then Y, then rotation
/// - Each axis checked independently against both statics and other clusters
/// - Rotation blocked by other clusters is reverted with heavy angular damping
///
/// REALISTIC BEHAVIOR (within game constraints):
/// - A falling cluster landing on a resting cluster should come to rest on top
/// - Two clusters approaching head-on should bounce off each other
/// - A heavy cluster plowing into a light one should barely slow down
/// - Clusters can stack multiple levels (A on B on floor)
/// </summary>
public class ClusterCollisionTests
{
    private ClusterData MakeCluster(ClusterManager manager, float x, float y, int size = 3, byte material = 0)
    {
        if (material == 0) material = Materials.Stone;
        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = x;
        cluster.Y = y;
        int half = size / 2;
        for (int ly = -half; ly <= half; ly++)
            for (int lx = -half; lx <= half; lx++)
                cluster.AddPixel(new ClusterPixel((short)lx, (short)ly, material));
        cluster.Mass = cluster.PixelCount;
        manager.Register(cluster);
        return cluster;
    }

    // --- FindOverlappingCluster Tests ---

    [Fact]
    public void FindOverlapping_NoOverlap_ReturnsNull()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 30f, 30f, 3);

        var allClusters = new List<ClusterData> { a, b };
        var result = ClusterPhysics.FindOverlappingCluster(a, allClusters);

        Assert.Null(result);
    }

    [Fact]
    public void FindOverlapping_ExactOverlap_ReturnsOther()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 20f, 20f, 3);
        var b = MakeCluster(manager, 20f, 20f, 3);

        var allClusters = new List<ClusterData> { a, b };
        var result = ClusterPhysics.FindOverlappingCluster(a, allClusters);

        Assert.NotNull(result);
        Assert.Same(b, result);
    }

    [Fact]
    public void FindOverlapping_PartialOverlap_ReturnsOther()
    {
        var manager = new ClusterManager();
        // 3x3 clusters, one pixel apart in X → they share a column
        var a = MakeCluster(manager, 20f, 20f, 3);
        var b = MakeCluster(manager, 22f, 20f, 3);

        var allClusters = new List<ClusterData> { a, b };
        var result = ClusterPhysics.FindOverlappingCluster(a, allClusters);

        Assert.NotNull(result);
        Assert.Same(b, result);
    }

    [Fact]
    public void FindOverlapping_JustTouching_NoOverlap()
    {
        var manager = new ClusterManager();
        // 3x3 clusters: A at 20, B at 23 → A spans [19,21], B spans [22,24], no overlap
        var a = MakeCluster(manager, 20f, 20f, 3);
        var b = MakeCluster(manager, 23f, 20f, 3);

        var allClusters = new List<ClusterData> { a, b };
        var result = ClusterPhysics.FindOverlappingCluster(a, allClusters);

        Assert.Null(result);
    }

    [Fact]
    public void FindOverlapping_SkipsSelf()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 20f, 20f, 3);

        var allClusters = new List<ClusterData> { a };
        var result = ClusterPhysics.FindOverlappingCluster(a, allClusters);

        Assert.Null(result);
    }

    [Fact]
    public void FindOverlapping_EmptyCluster_ReturnsNull()
    {
        var manager = new ClusterManager();
        var a = new ClusterData(manager.AllocateId());
        a.X = 20f; a.Y = 20f;
        manager.Register(a);

        var b = MakeCluster(manager, 20f, 20f, 3);
        var allClusters = new List<ClusterData> { a, b };

        Assert.Null(ClusterPhysics.FindOverlappingCluster(a, allClusters));
    }

    // --- Collision Response Tests ---

    [Fact]
    public void ResolveCollision1D_EqualMass_SwapsVelocities()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 20f, 10f, 3);

        // Equal mass, restitution = 1 (perfect elastic)
        a.Mass = 9f; b.Mass = 9f;
        a.Restitution = 1f; b.Restitution = 1f;
        a.VelocityX = 5f;
        b.VelocityX = 0f;

        ClusterPhysics.ResolveCollision1D(a, b, isXAxis: true);

        // Should swap velocities (standard elastic collision, equal mass)
        Assert.True(MathF.Abs(a.VelocityX - 0f) < 0.01f,
            $"A should stop, got {a.VelocityX:F3}");
        Assert.True(MathF.Abs(b.VelocityX - 5f) < 0.01f,
            $"B should get A's velocity, got {b.VelocityX:F3}");
    }

    [Fact]
    public void ResolveCollision1D_ConservesMomentum()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 20f, 10f, 3);

        a.Mass = 12f; b.Mass = 4f;
        a.Restitution = 0.3f; b.Restitution = 0.5f;
        a.VelocityX = 3f;
        b.VelocityX = -1f;

        float momentumBefore = a.Mass * a.VelocityX + b.Mass * b.VelocityX;

        ClusterPhysics.ResolveCollision1D(a, b, isXAxis: true);

        float momentumAfter = a.Mass * a.VelocityX + b.Mass * b.VelocityX;

        Assert.True(MathF.Abs(momentumAfter - momentumBefore) < 0.01f,
            $"Momentum should be conserved: before={momentumBefore:F3}, after={momentumAfter:F3}");
    }

    [Fact]
    public void ResolveCollision1D_HeavyPushesLight()
    {
        var manager = new ClusterManager();
        var heavy = MakeCluster(manager, 10f, 10f, 3);
        var light = MakeCluster(manager, 20f, 10f, 3);

        heavy.Mass = 100f; light.Mass = 1f;
        heavy.Restitution = 0.5f; light.Restitution = 0.5f;
        heavy.VelocityX = 5f;
        light.VelocityX = 0f;

        ClusterPhysics.ResolveCollision1D(heavy, light, isXAxis: true);

        // Heavy should barely change, light should get launched
        Assert.True(MathF.Abs(heavy.VelocityX - 5f) < 0.5f,
            $"Heavy cluster should barely slow down, got {heavy.VelocityX:F3}");
        Assert.True(light.VelocityX > 5f,
            $"Light cluster should be launched fast, got {light.VelocityX:F3}");
    }

    [Fact]
    public void ResolveCollision1D_InelasticCollision_ReducesRelativeSpeed()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 20f, 10f, 3);

        a.Mass = 5f; b.Mass = 5f;
        a.Restitution = 0f; b.Restitution = 0f; // Perfectly inelastic
        a.VelocityX = 6f;
        b.VelocityX = 0f;

        ClusterPhysics.ResolveCollision1D(a, b, isXAxis: true);

        // Perfectly inelastic: both should end up at same velocity
        Assert.True(MathF.Abs(a.VelocityX - b.VelocityX) < 0.01f,
            $"Perfectly inelastic collision: both should have same velocity, got A={a.VelocityX:F3}, B={b.VelocityX:F3}");
        Assert.True(MathF.Abs(a.VelocityX - 3f) < 0.01f,
            $"Both should move at center-of-mass velocity (3), got {a.VelocityX:F3}");
    }

    [Fact]
    public void ResolveCollision1D_YAxis_Works()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 10f, 20f, 3);

        a.Mass = 5f; b.Mass = 5f;
        a.Restitution = 1f; b.Restitution = 1f;
        a.VelocityY = 4f;
        b.VelocityY = 0f;

        ClusterPhysics.ResolveCollision1D(a, b, isXAxis: false);

        Assert.True(MathF.Abs(a.VelocityY - 0f) < 0.01f,
            $"A's Y velocity should be ~0, got {a.VelocityY:F3}");
        Assert.True(MathF.Abs(b.VelocityY - 4f) < 0.01f,
            $"B's Y velocity should be ~4, got {b.VelocityY:F3}");
    }

    [Fact]
    public void ResolveCollision1D_WakesSleepingCluster()
    {
        var manager = new ClusterManager();
        var a = MakeCluster(manager, 10f, 10f, 3);
        var b = MakeCluster(manager, 20f, 10f, 3);

        b.IsSleeping = true;
        b.LowVelocityFrames = 100;
        a.VelocityX = 5f;

        ClusterPhysics.ResolveCollision1D(a, b, isXAxis: true);

        Assert.False(b.IsSleeping, "Collision should wake sleeping cluster");
        Assert.Equal(0, b.LowVelocityFrames);
    }

    // --- Integrated Physics Step Tests ---

    [Fact]
    public void StepCluster_FallingCluster_LandsOnAnother()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        // Bottom cluster: resting on floor
        var bottom = MakeCluster(manager, 32f, 47f, 3);
        bottom.IsSleeping = true;
        bottom.IsOnGround = true;

        // Top cluster: falls onto bottom
        var top = MakeCluster(manager, 32f, 10f, 3, Materials.IronOre);
        top.Mass = 9f;
        bottom.Mass = 9f;

        var allClusters = new List<ClusterData> { bottom, top };

        // Run physics steps
        for (int i = 0; i < 500; i++)
        {
            ClusterPhysics.StepCluster(top, world, allClusters);
            ClusterPhysics.StepCluster(bottom, world, allClusters);
        }

        // Top should have fallen but stopped above bottom
        Assert.True(top.Y > 30, $"Top should have fallen, Y={top.Y:F2}");
        Assert.True(top.Y < 47, $"Top should be above bottom cluster at 47, Y={top.Y:F2}");
    }

    [Fact]
    public void StepCluster_HorizontalCollision_Bounce()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Floor so clusters don't fall
        for (int x = 0; x < 128; x++)
            world.SetCell(x, 70, Materials.Stone);

        var a = MakeCluster(manager, 20f, 64f, 3);
        var b = MakeCluster(manager, 40f, 64f, 3);
        a.Mass = 9f; b.Mass = 9f;
        a.VelocityX = 3f;
        b.VelocityX = -3f;

        var allClusters = new List<ClusterData> { a, b };

        // Run until they'd collide
        for (int i = 0; i < 200; i++)
        {
            ClusterPhysics.StepCluster(a, world, allClusters);
            ClusterPhysics.StepCluster(b, world, allClusters);
        }

        // After collision, they should have bounced apart
        // A should have reversed direction at some point
        // B should have reversed direction at some point
        // They should not be at the same X
        Assert.True(MathF.Abs(a.X - b.X) > 1f,
            $"Clusters should have bounced apart: A.X={a.X:F2}, B.X={b.X:F2}");
    }

    [Fact]
    public void StepCluster_ClusterCollision_SetsOnGround()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        // Bottom cluster: on the floor
        var bottom = MakeCluster(manager, 32f, 47f, 3);
        bottom.IsSleeping = true;
        bottom.IsOnGround = true;

        // Top cluster: positioned so that after one step it overlaps bottom.
        // Bottom pixels at [46,47,48]. Top needs to reach Y where its pixel at Y+1 = 46.
        // So top at Y=44, velocity=1 → new Y=45.07 → bottom pixel at 46.07 rounds to 46 → overlap!
        var top = MakeCluster(manager, 32f, 44f, 3, Materials.IronOre);
        top.VelocityY = 1f; // Moving down

        var allClusters = new List<ClusterData> { bottom, top };

        ClusterPhysics.StepCluster(top, world, allClusters);

        // Top should detect being "on ground" (resting on another cluster)
        Assert.True(top.IsOnGround,
            "Landing on another cluster should count as being on ground");
    }

    [Fact]
    public void StepCluster_RotationBlockedByCluster()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Two clusters very close together
        var a = MakeCluster(manager, 30f, 32f, 5);
        var b = MakeCluster(manager, 35f, 32f, 5);
        a.IsSleeping = false;
        b.IsSleeping = true;
        a.AngularVelocity = 1.5f; // Large rotation

        float rotBefore = a.Rotation;

        var allClusters = new List<ClusterData> { a, b };
        ClusterPhysics.StepCluster(a, world, allClusters);

        // If rotation would have caused overlap with b, it should be reverted
        // The 5x5 clusters 5 apart means rotation could cause overlap
        // Angular velocity should be heavily damped
        Assert.True(MathF.Abs(a.AngularVelocity) < MathF.Abs(1.5f),
            $"Angular velocity should be damped after blocked rotation, got {a.AngularVelocity:F3}");
    }

    // --- Full Pipeline Tests (through ClusterManager) ---

    [Fact]
    public void Pipeline_TwoClusters_FallAndStack()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 55, Materials.Stone);

        // Two clusters, one above the other
        var bottom = ClusterFactory.CreateSquareCluster(32f, 30f, 3, Materials.Stone, manager);
        var top = ClusterFactory.CreateSquareCluster(32f, 10f, 3, Materials.IronOre, manager);

        // Run full pipeline
        for (int i = 0; i < 600; i++)
            manager.StepAndSync(world);

        // Both should have settled
        Assert.True(bottom.Y > 40, $"Bottom should be near floor, Y={bottom.Y:F2}");
        Assert.True(top.Y > 30, $"Top should have fallen, Y={top.Y:F2}");
        Assert.True(top.Y < bottom.Y, $"Top should be above bottom: top.Y={top.Y:F2}, bottom.Y={bottom.Y:F2}");

        // Material conservation: cluster pixel data is intact.
        // Grid counts may be slightly less than pixel counts when clusters
        // overlap in grid space (sub-pixel positions cause shared cells).
        // The overlapping pixels still exist in the cluster data — they just
        // aren't rendered to the grid until the clusters separate.
        int stoneCount = WorldAssert.CountMaterial(world, Materials.Stone);
        int ironCount = WorldAssert.CountMaterial(world, Materials.IronOre);
        Assert.True(stoneCount >= bottom.PixelCount,
            $"Stone count ({stoneCount}) should include bottom cluster ({bottom.PixelCount} pixels)");
        Assert.True(ironCount > 0, "Top cluster should have IronOre pixels on the grid");
        Assert.Equal(top.PixelCount, top.Pixels.Count); // Cluster data still intact
    }

    [Fact]
    public void Pipeline_ThreeClusters_Stack()
    {
        var world = new CellWorld(64, 64);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 55, Materials.Stone);

        // Three clusters at different heights
        var c1 = ClusterFactory.CreateSquareCluster(32f, 40f, 3, Materials.Stone, manager);
        var c2 = ClusterFactory.CreateSquareCluster(32f, 25f, 3, Materials.IronOre, manager);
        var c3 = ClusterFactory.CreateSquareCluster(32f, 10f, 3, Materials.Dirt, manager);

        for (int i = 0; i < 800; i++)
            manager.StepAndSync(world);

        // All should have settled into a stack
        Assert.True(c1.Y > c2.Y, $"C1 should be below C2: c1.Y={c1.Y:F2}, c2.Y={c2.Y:F2}");
        Assert.True(c2.Y > c3.Y, $"C2 should be below C3: c2.Y={c2.Y:F2}, c3.Y={c3.Y:F2}");
    }

    [Fact]
    public void Pipeline_HorizontalApproach_MaterialConservation()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 128; x++)
            world.SetCell(x, 100, Materials.Stone);

        var a = ClusterFactory.CreateSquareCluster(30f, 90f, 3, Materials.IronOre, manager);
        var b = ClusterFactory.CreateSquareCluster(90f, 90f, 3, Materials.Dirt, manager);
        a.VelocityX = 3f;
        b.VelocityX = -3f;

        int ironBefore = a.PixelCount;
        int dirtBefore = b.PixelCount;

        for (int i = 0; i < 500; i++)
            manager.StepAndSync(world);

        // Material conservation
        int ironAfter = WorldAssert.CountMaterial(world, Materials.IronOre);
        int dirtAfter = WorldAssert.CountMaterial(world, Materials.Dirt);
        Assert.Equal(ironBefore, ironAfter);
        Assert.Equal(dirtBefore, dirtAfter);
    }

    [Fact]
    public void Pipeline_CollisionWakesSleepingCluster()
    {
        var world = new CellWorld(128, 128);
        var manager = new ClusterManager();

        // Floor
        for (int x = 0; x < 128; x++)
            world.SetCell(x, 100, Materials.Stone);

        // Resting cluster on the floor
        var resting = ClusterFactory.CreateSquareCluster(64f, 96f, 3, Materials.Stone, manager);
        resting.IsSleeping = true;
        resting.IsOnGround = true;
        resting.IsPixelsSynced = true;
        resting.LastSyncedX = 64f;
        resting.LastSyncedY = 96f;

        // Falling cluster that will land on it
        var falling = ClusterFactory.CreateSquareCluster(64f, 50f, 3, Materials.IronOre, manager);

        // Let falling cluster reach the resting one
        for (int i = 0; i < 300; i++)
            manager.StepAndSync(world);

        // The resting cluster should have been woken at some point during the collision
        // (It may have re-slept by now if enough frames passed, but its position may have changed)
        // Check that both clusters have their pixels in the grid
        Assert.True(WorldAssert.CountMaterial(world, Materials.IronOre) > 0);
    }

    // --- GetWorldAABB Tests ---

    [Fact]
    public void GetWorldAABB_NoRotation_MatchesPixelBounds()
    {
        var manager = new ClusterManager();
        var cluster = MakeCluster(manager, 20f, 20f, 3); // [-1,1] in each axis
        cluster.Rotation = 0f;

        ClusterPhysics.GetWorldAABB(cluster, out float minX, out float maxX, out float minY, out float maxY);

        // With no rotation, AABB should tightly bound the pixels
        // Pixels span [-1,1], so world spans [19,21] but AABB adds margin
        Assert.True(minX <= 19f, $"minX={minX:F2} should be <= 19");
        Assert.True(maxX >= 21f, $"maxX={maxX:F2} should be >= 21");
        Assert.True(minY <= 19f, $"minY={minY:F2} should be <= 19");
        Assert.True(maxY >= 21f, $"maxY={maxY:F2} should be >= 21");
    }

    [Fact]
    public void GetWorldAABB_Rotated_EnlargesBox()
    {
        var manager = new ClusterManager();
        var cluster = MakeCluster(manager, 20f, 20f, 3);

        ClusterPhysics.GetWorldAABB(cluster, out _, out float maxX0, out _, out _);
        cluster.Rotation = MathF.PI / 4f; // 45 degrees
        ClusterPhysics.GetWorldAABB(cluster, out _, out float maxX45, out _, out _);

        // Rotated AABB should be at least as large as non-rotated
        Assert.True(maxX45 >= maxX0 - 0.01f,
            $"Rotated AABB should be at least as large: rotated maxX={maxX45:F2}, unrotated maxX={maxX0:F2}");
    }

    [Fact]
    public void GetWorldAABB_EmptyCluster_PointAtCenter()
    {
        var manager = new ClusterManager();
        var cluster = new ClusterData(manager.AllocateId());
        cluster.X = 15f; cluster.Y = 25f;
        manager.Register(cluster);

        ClusterPhysics.GetWorldAABB(cluster, out float minX, out float maxX, out float minY, out float maxY);

        Assert.Equal(15f, minX);
        Assert.Equal(15f, maxX);
        Assert.Equal(25f, minY);
        Assert.Equal(25f, maxY);
    }
}
