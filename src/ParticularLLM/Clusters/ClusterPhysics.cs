namespace ParticularLLM;

/// <summary>
/// Simple deterministic rigid body solver for clusters.
/// Replaces Unity's Physics2D with separated-axis collision against static terrain.
///
/// Physics step per cluster:
/// 1. Apply gravity to velocity
/// 2. Integrate position (X then Y separately for collision resolution)
/// 3. Detect overlap with static terrain (pixel-level check)
/// 4. If overlap: undo movement, reflect velocity with restitution, apply friction
/// 5. Integrate rotation (undo if overlap)
/// 6. Sleep detection: consecutive low-velocity frames with ground contact → force sleep
/// </summary>
public static class ClusterPhysics
{
    /// <summary>
    /// Cluster gravity in cells/frame², matching cell simulation's fractional gravity.
    /// Cell sim: fractionalGravity=17, meaning 17/256 ≈ 0.0664 cells/frame².
    /// </summary>
    public const float Gravity = 17f / 256f;

    /// <summary>Speed below which sleep counter increments.</summary>
    public const float SleepSpeedThreshold = 0.1f;

    /// <summary>Consecutive low-speed frames before forcing sleep.</summary>
    public const int SleepFrameThreshold = 30;

    /// <summary>
    /// Step physics for a single cluster. Call after clearing cluster pixels from the grid.
    /// </summary>
    public static void StepCluster(ClusterData cluster, CellWorld world)
    {
        if (cluster.IsSleeping) return;
        if (cluster.PixelCount == 0) return;

        // Apply gravity
        cluster.VelocityY += Gravity;

        float oldX = cluster.X;
        float oldY = cluster.Y;
        float oldRot = cluster.Rotation;

        // Integrate X
        cluster.X += cluster.VelocityX;
        if (OverlapsStatic(cluster, world))
        {
            cluster.X = oldX;
            cluster.VelocityX *= -cluster.Restitution;
        }

        // Integrate Y
        cluster.Y += cluster.VelocityY;
        bool hitGround = false;
        if (OverlapsStatic(cluster, world))
        {
            cluster.Y = oldY;
            hitGround = cluster.VelocityY > 0; // Was moving down
            cluster.VelocityY *= -cluster.Restitution;

            // Apply friction when landing
            if (hitGround)
                cluster.VelocityX *= (1f - cluster.Friction);
        }

        cluster.IsOnGround = hitGround;

        // Integrate rotation
        if (MathF.Abs(cluster.AngularVelocity) > 0.001f)
        {
            cluster.Rotation += cluster.AngularVelocity;
            if (OverlapsStatic(cluster, world))
            {
                cluster.Rotation = oldRot;
                cluster.AngularVelocity *= -0.1f; // Heavy angular damping
            }
            else
            {
                // Angular damping
                cluster.AngularVelocity *= 0.98f;
            }
        }

        // Sleep detection
        float speed = MathF.Sqrt(
            cluster.VelocityX * cluster.VelocityX +
            cluster.VelocityY * cluster.VelocityY);

        if (speed < SleepSpeedThreshold && cluster.IsOnGround)
        {
            cluster.LowVelocityFrames++;
            if (cluster.LowVelocityFrames > SleepFrameThreshold &&
                cluster.ActiveForceCount == 0 &&
                !cluster.IsMachinePart)
            {
                cluster.IsSleeping = true;
                cluster.VelocityX = 0;
                cluster.VelocityY = 0;
                cluster.AngularVelocity = 0;
            }
        }
        else
        {
            cluster.LowVelocityFrames = 0;
        }
    }

    /// <summary>
    /// Check if any cluster pixel at its current position overlaps a static cell or is out of bounds.
    /// Uses direct pixel iteration (not ForEachWorldCell) so out-of-bounds positions are detected.
    /// </summary>
    public static bool OverlapsStatic(ClusterData cluster, CellWorld world)
    {
        cluster.BuildPixelLookup();
        if (cluster.PixelCount == 0) return false;

        float cos = MathF.Cos(cluster.Rotation);
        float sin = MathF.Sin(cluster.Rotation);

        // Iterate each pixel and compute its world position
        foreach (var pixel in cluster.Pixels)
        {
            float worldXf = cluster.X + pixel.localX * cos - pixel.localY * sin;
            float worldYf = cluster.Y + pixel.localX * sin + pixel.localY * cos;

            int wx = (int)MathF.Round(worldXf);
            int wy = (int)MathF.Round(worldYf);

            // Out of bounds = collision with world boundary
            if (!world.IsInBounds(wx, wy)) return true;

            byte mat = world.cells[wy * world.width + wx].materialId;
            if (mat != Materials.Air && world.materials[mat].behaviour == BehaviourType.Static)
                return true;
        }

        return false;
    }
}
