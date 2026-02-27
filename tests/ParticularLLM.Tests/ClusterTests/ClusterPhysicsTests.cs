using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.ClusterTests;

/// <summary>
/// Tests for the cluster rigid body physics solver.
///
/// English rules (derived from ClusterPhysics.cs):
///
/// GRAVITY:
/// - Each frame, VelocityY increases by Gravity (17/256 ≈ 0.0664 cells/frame²)
/// - A cluster in free-fall accelerates downward
/// - After ~15 frames, velocity reaches ~1 cell/frame (matching cell sim timing)
///
/// COLLISION WITH STATIC TERRAIN:
/// - Cluster pixels that overlap static materials (Stone, Wall) trigger collision
/// - X and Y axes are resolved separately (separated axis approach)
/// - On collision, position is reverted and velocity is reflected with restitution
/// - When landing (moving down and hitting ground), friction is applied to X velocity
///
/// ROTATION:
/// - Angular velocity updates rotation each frame
/// - If rotation causes overlap with statics, it's reverted
/// - Angular velocity has damping (0.98× per frame) and heavy damping on collision
///
/// SLEEP:
/// - When speed < threshold AND cluster is on ground, LowVelocityFrames increments
/// - After 30 consecutive low-velocity frames → force sleep
/// - Sleep zeroes all velocities
/// - Machine parts and force-affected clusters never auto-sleep
/// - Sleeping clusters skip physics step entirely
///
/// WORLD BOUNDARY:
/// - Out-of-bounds positions are treated as solid (collision with world edge)
/// </summary>
public class ClusterPhysicsTests
{
    private (CellWorld world, ClusterData cluster) CreateTestSetup(
        int worldSize = 64, float clusterX = 32, float clusterY = 10)
    {
        var world = new CellWorld(worldSize, worldSize);
        var cluster = new ClusterData(1);
        cluster.X = clusterX;
        cluster.Y = clusterY;

        // Simple 3x3 square cluster
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                cluster.AddPixel(new ClusterPixel((short)x, (short)y, Materials.Stone));

        return (world, cluster);
    }

    [Fact]
    public void Gravity_IncreasesVelocityY()
    {
        var (world, cluster) = CreateTestSetup();

        float velBefore = cluster.VelocityY;
        ClusterPhysics.StepCluster(cluster, world);
        float velAfter = cluster.VelocityY;

        Assert.True(velAfter > velBefore);
        Assert.True(MathF.Abs(velAfter - velBefore - ClusterPhysics.Gravity) < 0.01f,
            $"Velocity increment should be ~{ClusterPhysics.Gravity}, was {velAfter - velBefore}");
    }

    [Fact]
    public void Gravity_AccumulatesOverFrames()
    {
        var (world, cluster) = CreateTestSetup();

        int frames = 15;
        for (int i = 0; i < frames; i++)
            ClusterPhysics.StepCluster(cluster, world);

        float expectedVel = ClusterPhysics.Gravity * frames;
        Assert.True(MathF.Abs(cluster.VelocityY - expectedVel) < 0.1f,
            $"After {frames} frames, velocity should be ~{expectedVel:F3}, was {cluster.VelocityY:F3}");
    }

    [Fact]
    public void FreeFall_MovesDownward()
    {
        var (world, cluster) = CreateTestSetup();

        float startY = cluster.Y;
        for (int i = 0; i < 30; i++)
            ClusterPhysics.StepCluster(cluster, world);

        Assert.True(cluster.Y > startY, $"Cluster should fall down: started at {startY}, now at {cluster.Y}");
    }

    [Fact]
    public void Collision_LandsOnStoneFloor()
    {
        var (world, cluster) = CreateTestSetup();

        // Place stone floor at y=50
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        // Let cluster fall
        for (int i = 0; i < 500; i++)
            ClusterPhysics.StepCluster(cluster, world);

        // Cluster should be resting above the floor
        // Cluster bottom pixel is at Y + 1, so cluster.Y + 1 < 50
        Assert.True(cluster.Y < 50, $"Cluster Y ({cluster.Y:F2}) should be above floor at 50");
        Assert.True(cluster.Y > 40, $"Cluster should have fallen near the floor, not stuck at {cluster.Y:F2}");
    }

    [Fact]
    public void Collision_StopsVerticalVelocity()
    {
        var (world, cluster) = CreateTestSetup();

        // Place stone floor at y=50
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 50, Materials.Stone);

        // Let it fall and settle
        for (int i = 0; i < 500; i++)
            ClusterPhysics.StepCluster(cluster, world);

        Assert.True(MathF.Abs(cluster.VelocityY) < 0.1f,
            $"Vertical velocity should be near zero after landing, was {cluster.VelocityY:F3}");
    }

    [Fact]
    public void Collision_WallBlocksHorizontal()
    {
        var (world, cluster) = CreateTestSetup(clusterX: 10, clusterY: 32);

        // Place stone wall at x=5
        for (int y = 0; y < 64; y++)
            world.SetCell(5, y, Materials.Stone);

        cluster.VelocityX = -5f; // Moving left toward wall

        for (int i = 0; i < 100; i++)
            ClusterPhysics.StepCluster(cluster, world);

        // Cluster should not have passed through the wall
        Assert.True(cluster.X > 5, $"Cluster X ({cluster.X:F2}) should be to the right of wall at x=5");
    }

    [Fact]
    public void Collision_Restitution_ReducesBounceVelocity()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.Restitution = 0.5f;
        cluster.VelocityY = 5f; // Falling fast

        // Place floor
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 20, Materials.Stone);

        // Cluster at y=10 with bottom pixel at y=11, floor at y=20
        // Give it enough velocity to reach floor
        cluster.Y = 14f;

        ClusterPhysics.StepCluster(cluster, world);

        // After collision, velocity should be reversed and reduced
        // velY was 5 + gravity ≈ 5.066, after bounce: -5.066 * 0.5 ≈ -2.53
        Assert.True(cluster.VelocityY < 0, $"Should bounce upward, velocity = {cluster.VelocityY:F3}");
    }

    [Fact]
    public void Collision_Friction_ReducesHorizontalVelocity()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.Friction = 0.5f;
        cluster.VelocityX = 4f;
        cluster.VelocityY = 5f; // Falling

        // Place floor just below
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 14, Materials.Stone);

        cluster.Y = 10f;

        ClusterPhysics.StepCluster(cluster, world);

        // After landing, horizontal velocity should be reduced by friction
        Assert.True(MathF.Abs(cluster.VelocityX) < 4f,
            $"Horizontal velocity should be reduced by friction, was {cluster.VelocityX:F3}");
    }

    [Fact]
    public void Sleep_AfterLanding()
    {
        var (world, cluster) = CreateTestSetup();

        // Place floor close below
        for (int x = 0; x < 64; x++)
            world.SetCell(x, 20, Materials.Stone);

        // Run many frames to let it fall and settle
        for (int i = 0; i < 200; i++)
            ClusterPhysics.StepCluster(cluster, world);

        Assert.True(cluster.IsSleeping, "Cluster should be sleeping after settling on floor");
        Assert.Equal(0f, cluster.VelocityX);
        Assert.Equal(0f, cluster.VelocityY);
        Assert.Equal(0f, cluster.AngularVelocity);
    }

    [Fact]
    public void Sleep_Skips_PhysicsStep()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.IsSleeping = true;
        cluster.Y = 10f;
        cluster.VelocityY = 0;

        ClusterPhysics.StepCluster(cluster, world);

        // Position should not change
        Assert.Equal(10f, cluster.Y);
        Assert.Equal(0f, cluster.VelocityY);
    }

    [Fact]
    public void Sleep_MachinePart_NeverSleeps()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.IsMachinePart = true;

        for (int x = 0; x < 64; x++)
            world.SetCell(x, 20, Materials.Stone);

        for (int i = 0; i < 200; i++)
            ClusterPhysics.StepCluster(cluster, world);

        Assert.False(cluster.IsSleeping, "Machine parts should never auto-sleep");
    }

    [Fact]
    public void Sleep_ActiveForce_PreventsSleep()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.ActiveForceCount = 1; // External force active

        for (int x = 0; x < 64; x++)
            world.SetCell(x, 20, Materials.Stone);

        for (int i = 0; i < 200; i++)
            ClusterPhysics.StepCluster(cluster, world);

        Assert.False(cluster.IsSleeping, "Clusters with active forces should not auto-sleep");
    }

    [Fact]
    public void WorldBoundary_BottomEdge_IsCollision()
    {
        var world = new CellWorld(64, 64);
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.X = 32f;
        cluster.Y = 60f; // Near bottom
        cluster.VelocityY = 10f; // Fast downward

        ClusterPhysics.StepCluster(cluster, world);

        // Should not go past world boundary
        Assert.True(cluster.Y < 64, $"Cluster should not leave world, Y={cluster.Y:F2}");
    }

    [Fact]
    public void WorldBoundary_LeftEdge_IsCollision()
    {
        var world = new CellWorld(64, 64);
        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.X = 2f;
        cluster.Y = 32f;
        cluster.VelocityX = -5f; // Fast leftward

        ClusterPhysics.StepCluster(cluster, world);

        Assert.True(cluster.X >= 0, $"Cluster should not leave world, X={cluster.X:F2}");
    }

    [Fact]
    public void OverlapsStatic_DetectsStone()
    {
        var world = new CellWorld(64, 64);
        world.SetCell(32, 32, Materials.Stone);

        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.X = 32f;
        cluster.Y = 32f;

        Assert.True(ClusterPhysics.OverlapsStatic(cluster, world));
    }

    [Fact]
    public void OverlapsStatic_IgnoresMovableMaterials()
    {
        var world = new CellWorld(64, 64);
        world.SetCell(32, 32, Materials.Sand); // Powder, not static

        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.X = 32f;
        cluster.Y = 32f;

        Assert.False(ClusterPhysics.OverlapsStatic(cluster, world),
            "Sand is not static — cluster should not detect collision");
    }

    [Fact]
    public void OverlapsStatic_DetectsWallMaterial()
    {
        var world = new CellWorld(64, 64);
        world.SetCell(32, 32, Materials.Wall);

        var cluster = new ClusterData(1);
        cluster.AddPixel(new ClusterPixel(0, 0, Materials.Stone));
        cluster.X = 32f;
        cluster.Y = 32f;

        Assert.True(ClusterPhysics.OverlapsStatic(cluster, world));
    }

    [Fact]
    public void Rotation_Updates()
    {
        var (world, cluster) = CreateTestSetup();
        cluster.AngularVelocity = 0.1f;

        float rotBefore = cluster.Rotation;
        ClusterPhysics.StepCluster(cluster, world);

        Assert.NotEqual(rotBefore, cluster.Rotation);
    }

    [Fact]
    public void Rotation_CollisionReverts()
    {
        var (world, cluster) = CreateTestSetup(clusterX: 5, clusterY: 32);
        // Place wall very close
        for (int y = 0; y < 64; y++)
            world.SetCell(2, y, Materials.Stone);

        // Large angular velocity might cause overlap
        cluster.AngularVelocity = 1.5f;
        float rotBefore = cluster.Rotation;

        ClusterPhysics.StepCluster(cluster, world);

        // If rotation caused overlap with the stone wall, rotation should have been reverted.
        // The cluster should either have reverted to rotBefore or found a valid rotation.
        // Either way, it should NOT overlap with static material.
        bool overlaps = ClusterPhysics.OverlapsStatic(cluster, world);
        Assert.False(overlaps, "After rotation collision, cluster should not overlap static material");
    }
}
