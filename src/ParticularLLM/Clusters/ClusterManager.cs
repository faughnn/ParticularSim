namespace ParticularLLM;

/// <summary>
/// Manages all clusters in the world. Orchestrates the 3-step pipeline:
///   1. CLEAR — remove cluster pixels from grid so clusters can move freely
///   2. PHYSICS — update cluster positions/velocities (gravity, collision with static terrain)
///   3. SYNC — write cluster pixels to grid at new positions, displacing movable materials
///
/// Displacement uses physics-based push (not BFS search):
///   - Cluster into air → place pixel, no conflict
///   - Cluster into powder/liquid → push cell in cluster's movement direction,
///     transfer velocity scaled by density ratio
///   - Cluster into gas → trivially displaced, negligible resistance
///   - Cluster into static → should not happen (physics prevents overlap),
///     but as safety net, skip placing cluster pixel
/// </summary>
public class ClusterManager
{
    private readonly Dictionary<ushort, ClusterData> _clusters = new();
    private readonly Queue<ushort> _freeIds = new();
    private ushort _nextId = 1;

    /// <summary>Momentum transfer factor when displacing cells (0-1).</summary>
    public float DisplacementMomentumFactor { get; set; } = 0.5f;

    // Stats
    public int ActiveCount => _clusters.Count;
    public int DisplacementsThisFrame { get; private set; }
    public int SleepingCount { get; private set; }
    public int SkippedSyncCount { get; private set; }

    public IEnumerable<ClusterData> AllClusters => _clusters.Values;

    /// <summary>
    /// Allocate a unique cluster ID. ID 0 is reserved for "no owner".
    /// </summary>
    public ushort AllocateId()
    {
        if (_freeIds.Count > 0)
            return _freeIds.Dequeue();
        return _nextId++;
    }

    /// <summary>Release a cluster ID for reuse.</summary>
    public void ReleaseId(ushort id)
    {
        if (id != 0)
            _freeIds.Enqueue(id);
    }

    /// <summary>Register a cluster with the manager.</summary>
    public void Register(ClusterData cluster)
    {
        if (cluster.Id == 0) return;
        _clusters[cluster.Id] = cluster;
    }

    /// <summary>Unregister a cluster (does NOT clear its pixels from the grid).</summary>
    /// <param name="releaseId">If true, the cluster's ID is returned to the free pool for reuse.</param>
    public void Unregister(ClusterData cluster, bool releaseId = true)
    {
        if (_clusters.Remove(cluster.Id) && releaseId)
            ReleaseId(cluster.Id);
    }

    /// <summary>Remove a cluster completely: clear pixels from grid, unregister.</summary>
    /// <param name="releaseId">If false, the ID is NOT recycled (use during fracture to prevent sub-cluster ID collision).</param>
    public void RemoveCluster(ClusterData cluster, CellWorld world, bool releaseId = true)
    {
        ClearClusterPixels(cluster, world);
        Unregister(cluster, releaseId);
    }

    /// <summary>Get a cluster by ID, or null if not found.</summary>
    public ClusterData? GetCluster(ushort id)
    {
        _clusters.TryGetValue(id, out var cluster);
        return cluster;
    }

    /// <summary>
    /// Step cluster physics and sync to world grid.
    /// Call before cell simulation each frame.
    /// </summary>
    public void StepAndSync(CellWorld world)
    {
        DisplacementsThisFrame = 0;
        SkippedSyncCount = 0;
        SleepingCount = 0;

        // Count sleeping clusters
        foreach (var cluster in _clusters.Values)
        {
            if (cluster.IsSleeping)
                SleepingCount++;
        }

        // STEP 1: Clear old cluster pixels from grid
        foreach (var cluster in _clusters.Values)
        {
            if (cluster.ShouldSkipSync())
                continue;

            cluster.IsPixelsSynced = false;
            ClearClusterPixels(cluster, world);
        }

        // STEP 2: Step physics for each cluster
        foreach (var cluster in _clusters.Values)
        {
            ClusterPhysics.StepCluster(cluster, world);
        }

        // STEP 3: Sync cluster pixels to grid at new positions
        foreach (var cluster in _clusters.Values)
        {
            if (cluster.ShouldSkipSync())
            {
                SkippedSyncCount++;
                continue;
            }

            SyncClusterToWorld(cluster, world);

            cluster.IsPixelsSynced = true;
            cluster.LastSyncedX = cluster.X;
            cluster.LastSyncedY = cluster.Y;
            cluster.LastSyncedRotation = cluster.Rotation;
        }
    }

    /// <summary>
    /// Clear a cluster's pixels from the world grid.
    /// Only clears cells whose ownerId matches this cluster.
    /// </summary>
    private void ClearClusterPixels(ClusterData cluster, CellWorld world)
    {
        ushort clusterId = cluster.Id;

        cluster.ForEachWorldCell(world.width, world.height, (cx, cy, mat) =>
        {
            int index = cy * world.width + cx;
            Cell cell = world.cells[index];
            if (cell.ownerId == clusterId)
            {
                cell.materialId = Materials.Air;
                cell.ownerId = 0;
                cell.velocityX = 0;
                cell.velocityY = 0;
                world.cells[index] = cell;
            }
        });
    }

    /// <summary>
    /// Sync a cluster's pixels to the world grid, displacing any movable materials.
    /// </summary>
    private void SyncClusterToWorld(ClusterData cluster, CellWorld world)
    {
        ushort clusterId = cluster.Id;

        cluster.ForEachWorldCell(world.width, world.height, (cx, cy, pixelMat) =>
        {
            int index = cy * world.width + cx;
            Cell existing = world.cells[index];

            // Displace existing non-air, non-owned cells
            if (existing.materialId != Materials.Air && existing.ownerId == 0)
            {
                DisplaceCell(world, cx, cy, existing, cluster);
            }

            // Place cluster pixel
            Cell newCell = new Cell
            {
                materialId = pixelMat,
                ownerId = clusterId,
                velocityX = 0,
                velocityY = 0,
                temperature = 20,
                structureId = 0,
            };
            world.cells[index] = newCell;
            world.MarkDirty(cx, cy);
        });
    }

    /// <summary>
    /// Displace a cell using physics-based push.
    /// Moves the cell one step in the cluster's movement direction and gives it velocity.
    /// Velocity is proportional to cluster speed and inversely proportional to material density.
    /// </summary>
    private void DisplaceCell(CellWorld world, int cx, int cy, Cell cell, ClusterData cluster)
    {
        var matDef = world.materials[cell.materialId];

        // Static materials should not be displaced — physics should prevent overlap.
        // If this happens, it's a collision resolution edge case. Skip placement.
        if (matDef.behaviour == BehaviourType.Static)
            return;

        // Calculate push direction from cluster velocity
        float velX = cluster.VelocityX;
        float velY = cluster.VelocityY;
        float speed = MathF.Sqrt(velX * velX + velY * velY);

        // Default push direction: downward (gravity)
        float dirX = 0, dirY = 1;
        if (speed > 0.01f)
        {
            dirX = velX / speed;
            dirY = velY / speed;
        }

        // Calculate push velocity (inversely proportional to material density)
        float densityFactor = 128f / Math.Max((int)matDef.density, 1);
        sbyte pushVelX = (sbyte)Math.Clamp(
            (int)(velX * DisplacementMomentumFactor * densityFactor),
            -PhysicsSettings.MaxVelocity, PhysicsSettings.MaxVelocity);
        sbyte pushVelY = (sbyte)Math.Clamp(
            (int)(velY * DisplacementMomentumFactor * densityFactor),
            -PhysicsSettings.MaxVelocity, PhysicsSettings.MaxVelocity);

        cell.velocityX = pushVelX;
        cell.velocityY = pushVelY;
        cell.ownerId = 0;

        // Try to place cell in push direction (1 step), then fallbacks
        int pushX = cx + (int)MathF.Round(dirX);
        int pushY = cy + (int)MathF.Round(dirY);

        // Try: push direction, then left/right, then up/down
        ReadOnlySpan<int> tryDx = stackalloc int[] { pushX - cx, -1, 1, 0, 0 };
        ReadOnlySpan<int> tryDy = stackalloc int[] { pushY - cy, 0, 0, -1, 1 };

        for (int i = 0; i < tryDx.Length; i++)
        {
            int tx = cx + tryDx[i];
            int ty = cy + tryDy[i];
            if (tx == cx && ty == cy) continue;
            if (!world.IsInBounds(tx, ty)) continue;

            int tidx = ty * world.width + tx;
            if (world.cells[tidx].materialId == Materials.Air)
            {
                world.cells[tidx] = cell;
                world.MarkDirty(tx, ty);
                DisplacementsThisFrame++;
                return;
            }
        }

        // Fallback: scan upward for empty cell (material conservation guarantee)
        for (int y = cy - 1; y >= 0; y--)
        {
            int idx = y * world.width + cx;
            if (world.cells[idx].materialId == Materials.Air)
            {
                world.cells[idx] = cell;
                world.MarkDirty(cx, y);
                DisplacementsThisFrame++;
                return;
            }
        }

        // Last resort: scan downward
        for (int y = cy + 1; y < world.height; y++)
        {
            int idx = y * world.width + cx;
            if (world.cells[idx].materialId == Materials.Air)
            {
                world.cells[idx] = cell;
                world.MarkDirty(cx, y);
                DisplacementsThisFrame++;
                return;
            }
        }

        // Absolute last resort: try any column
        for (int x = 0; x < world.width; x++)
        {
            for (int y = 0; y < world.height; y++)
            {
                int idx = y * world.width + x;
                if (world.cells[idx].materialId == Materials.Air)
                {
                    world.cells[idx] = cell;
                    world.MarkDirty(x, y);
                    DisplacementsThisFrame++;
                    return;
                }
            }
        }

        // World is completely full — material is lost (should never happen in practice)
    }
}
