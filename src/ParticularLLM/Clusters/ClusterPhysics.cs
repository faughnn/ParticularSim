namespace ParticularLLM;

/// <summary>
/// Simple deterministic rigid body solver for clusters.
/// Handles collision with static terrain AND other clusters.
///
/// Physics step per cluster:
/// 1. Apply gravity to velocity
/// 2. Integrate position (X then Y separately for collision resolution)
/// 3. Detect overlap with static terrain (pixel-level check)
/// 4. Detect overlap with other clusters (AABB pre-filter + pixel-level check)
/// 5. If overlap: undo movement, apply collision response
///    - Static terrain: reflect velocity with restitution
///    - Other cluster: 1D momentum exchange (conservation of momentum with restitution)
/// 6. Integrate rotation (undo if overlap with statics or clusters)
/// 7. Sleep detection: consecutive low-velocity frames with ground contact → force sleep
///
/// Cluster-cluster collision uses separated-axis approach matching static collision:
/// - After moving along each axis, check for overlap with all other clusters
/// - On collision: undo movement, exchange momentum based on masses
/// - Heavier clusters transfer more momentum to lighter ones
/// - Collision wakes sleeping clusters
/// - Landing on another cluster counts as "on ground" for sleep detection
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

    /// <summary>Speed below which embedded material provides full normal force (cancels gravity).</summary>
    public const float SoftGroundSpeedThreshold = 0.5f;

    /// <summary>Continuous velocity damping factor per displaced cell (between discrete displacement events).</summary>
    public const float ContinuousDragFactor = 0.15f;

    /// <summary>
    /// Step physics for a single cluster (no cluster-cluster collision).
    /// Backward-compatible overload for tests that only need static collision.
    /// </summary>
    public static void StepCluster(ClusterData cluster, CellWorld world)
    {
        StepCluster(cluster, world, Array.Empty<ClusterData>());
    }

    /// <summary>
    /// Step physics for a single cluster with full collision detection.
    /// Checks both static terrain and other clusters.
    /// Call after clearing cluster pixels from the grid.
    /// </summary>
    public static void StepCluster(ClusterData cluster, CellWorld world, IReadOnlyList<ClusterData> allClusters)
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
        else
        {
            var hitCluster = FindOverlappingCluster(cluster, allClusters);
            if (hitCluster != null)
            {
                cluster.X = oldX;
                ResolveCollision1D(cluster, hitCluster, isXAxis: true);
            }
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
        else
        {
            var hitCluster = FindOverlappingCluster(cluster, allClusters);
            if (hitCluster != null)
            {
                cluster.Y = oldY;
                hitGround = cluster.VelocityY > 0; // Was moving down (landing on another cluster)
                ResolveCollision1D(cluster, hitCluster, isXAxis: false);

                // Apply friction when landing on another cluster
                if (hitGround)
                    cluster.VelocityX *= (1f - cluster.Friction);
            }
        }

        cluster.IsOnGround = hitGround;

        // Soft ground support: displaced materials resist cluster movement
        if (!hitGround && cluster.DisplacedCellsLastSync > 0)
        {
            float sgSpeed = MathF.Sqrt(cluster.VelocityX * cluster.VelocityX +
                                        cluster.VelocityY * cluster.VelocityY);

            if (cluster.VelocityY > 0 && sgSpeed < SoftGroundSpeedThreshold)
            {
                // Slow enough: material provides normal force (like resting on surface)
                cluster.Y = oldY; // Undo downward drift
                cluster.VelocityY = 0f;
                cluster.VelocityX *= (1f - cluster.Friction);
                cluster.IsOnGround = true;
            }
            else if (sgSpeed > 0.01f)
            {
                // Fast: continuous damping between discrete displacement events
                float dampFactor = ContinuousDragFactor * cluster.DisplacedCellsLastSync / cluster.Mass;
                dampFactor = MathF.Min(dampFactor, 0.5f);
                cluster.VelocityX *= (1f - dampFactor);
                cluster.VelocityY *= (1f - dampFactor);
            }
        }

        // Integrate rotation
        if (MathF.Abs(cluster.AngularVelocity) > 0.001f)
        {
            cluster.Rotation += cluster.AngularVelocity;
            bool rotOverlap = OverlapsStatic(cluster, world);
            if (!rotOverlap)
                rotOverlap = FindOverlappingCluster(cluster, allClusters) != null;

            if (rotOverlap)
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
    /// 1D collision response between two clusters along one axis.
    /// Uses conservation of momentum with coefficient of restitution.
    ///
    /// Formulas (standard 1D inelastic collision):
    ///   v1' = (m1*v1 + m2*v2 - m2*e*(v1 - v2)) / (m1 + m2)
    ///   v2' = (m1*v1 + m2*v2 + m1*e*(v1 - v2)) / (m1 + m2)
    ///
    /// Properties:
    ///   - Conserves momentum: m1*v1' + m2*v2' = m1*v1 + m2*v2
    ///   - e=1 (elastic): equal-mass clusters swap velocities
    ///   - e=0 (inelastic): both end up at center-of-mass velocity
    ///   - Heavy cluster barely affected, light cluster gets most of the impulse
    /// </summary>
    public static void ResolveCollision1D(ClusterData a, ClusterData b, bool isXAxis)
    {
        float mA = a.Mass;
        float mB = b.Mass;
        float e = (a.Restitution + b.Restitution) * 0.5f;

        float vA = isXAxis ? a.VelocityX : a.VelocityY;
        float vB = isXAxis ? b.VelocityX : b.VelocityY;

        float totalMass = mA + mB;
        float newVA = (mA * vA + mB * vB - mB * e * (vA - vB)) / totalMass;
        float newVB = (mA * vA + mB * vB + mA * e * (vA - vB)) / totalMass;

        if (isXAxis)
        {
            a.VelocityX = newVA;
            b.VelocityX = newVB;
        }
        else
        {
            a.VelocityY = newVA;
            b.VelocityY = newVB;
        }

        // Wake the other cluster if sleeping
        if (b.IsSleeping)
            b.Wake();
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

    /// <summary>
    /// Find the first cluster that overlaps with this cluster at their current positions.
    /// Uses AABB pre-filtering for performance, then pixel-level overlap check.
    /// Returns null if no overlap.
    /// </summary>
    public static ClusterData? FindOverlappingCluster(ClusterData cluster, IReadOnlyList<ClusterData> allClusters)
    {
        if (allClusters.Count == 0) return null;

        cluster.BuildPixelLookup();
        if (cluster.PixelCount == 0) return null;

        float cosA = MathF.Cos(cluster.Rotation);
        float sinA = MathF.Sin(cluster.Rotation);

        cluster.GetWorldAABB(out float aMinX, out float aMaxX, out float aMinY, out float aMaxY);

        for (int i = 0; i < allClusters.Count; i++)
        {
            var other = allClusters[i];
            if (other.Id == cluster.Id) continue;
            if (other.PixelCount == 0) continue;

            // AABB pre-filter
            other.GetWorldAABB(out float bMinX, out float bMaxX, out float bMinY, out float bMaxY);
            if (aMaxX < bMinX || aMinX > bMaxX || aMaxY < bMinY || aMinY > bMaxY)
                continue;

            // Pixel-level overlap: for each pixel in A, check if it maps to a pixel in B
            other.BuildPixelLookup();
            float cosB = MathF.Cos(other.Rotation);
            float sinB = MathF.Sin(other.Rotation);

            foreach (var pixel in cluster.Pixels)
            {
                // Pixel → world
                float worldX = cluster.X + pixel.localX * cosA - pixel.localY * sinA;
                float worldY = cluster.Y + pixel.localX * sinA + pixel.localY * cosA;

                // World → other's local space (inverse rotation)
                float dx = worldX - other.X;
                float dy = worldY - other.Y;
                float localX = dx * cosB + dy * sinB;
                float localY = -dx * sinB + dy * cosB;

                int lx = (int)MathF.Round(localX);
                int ly = (int)MathF.Round(localY);

                if (other.GetPixelMaterialAt(lx, ly) != Materials.Air)
                    return other;
            }
        }

        return null;
    }

    /// <summary>
    /// Compute world-space AABB for a cluster at its current position and rotation.
    /// Delegates to ClusterData.GetWorldAABB (single source of truth for extent math).
    /// </summary>
    public static void GetWorldAABB(ClusterData cluster, out float minX, out float maxX, out float minY, out float maxY)
    {
        cluster.GetWorldAABB(out minX, out maxX, out minY, out maxY);
    }
}
