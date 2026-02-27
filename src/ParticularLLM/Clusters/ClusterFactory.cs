namespace ParticularLLM;

/// <summary>
/// Factory for creating clusters from pixel lists or world regions.
/// Handles physics property calculation (mass, center of mass, moment of inertia).
/// </summary>
public static class ClusterFactory
{
    /// <summary>
    /// Create a cluster from a list of pixels at a position in cell space.
    /// Registers with the manager and calculates physics properties.
    /// </summary>
    public static ClusterData CreateCluster(
        List<ClusterPixel> pixels,
        float cellX, float cellY,
        ClusterManager manager)
    {
        if (pixels == null || pixels.Count == 0)
            return null!;

        ushort id = manager.AllocateId();
        var cluster = new ClusterData(id);
        cluster.X = cellX;
        cluster.Y = cellY;
        cluster.SetPixels(pixels);

        CalculatePhysicsProperties(cluster);
        manager.Register(cluster);

        return cluster;
    }

    /// <summary>
    /// Create a cluster by extracting non-air cells from a world region.
    /// Extracted cells are cleared from the world (they become part of the cluster).
    /// </summary>
    public static ClusterData? CreateClusterFromRegion(
        CellWorld world,
        int startX, int startY,
        int regionWidth, int regionHeight,
        ClusterManager manager)
    {
        var pixels = new List<ClusterPixel>();

        // Center of region in cell coordinates
        float centerX = startX + regionWidth / 2f;
        float centerY = startY + regionHeight / 2f;

        // Extract non-air cells
        for (int y = startY; y < startY + regionHeight; y++)
        {
            for (int x = startX; x < startX + regionWidth; x++)
            {
                if (!world.IsInBounds(x, y)) continue;

                int index = y * world.width + x;
                Cell cell = world.cells[index];

                if (cell.materialId != Materials.Air && cell.ownerId == 0)
                {
                    // Local coordinates relative to center (cell space, Y+ = down)
                    short localX = (short)(x - (int)MathF.Round(centerX));
                    short localY = (short)(y - (int)MathF.Round(centerY));

                    pixels.Add(new ClusterPixel(localX, localY, cell.materialId));

                    // Clear from world
                    cell.materialId = Materials.Air;
                    cell.ownerId = 0;
                    world.cells[index] = cell;
                    world.MarkDirty(x, y);
                }
            }
        }

        if (pixels.Count == 0) return null;

        return CreateCluster(pixels, centerX, centerY, manager);
    }

    /// <summary>
    /// Calculate physics properties (mass, center of mass, moment of inertia) from pixels.
    /// Each pixel contributes 1 unit of mass (density-weighted mass could be added later).
    /// </summary>
    public static void CalculatePhysicsProperties(ClusterData cluster)
    {
        if (cluster.PixelCount == 0) return;

        float totalMass = cluster.PixelCount;
        float sumX = 0, sumY = 0;

        foreach (var p in cluster.Pixels)
        {
            sumX += p.localX;
            sumY += p.localY;
        }

        float comX = sumX / totalMass;
        float comY = sumY / totalMass;

        // Moment of inertia around center of mass
        float moi = 0;
        foreach (var p in cluster.Pixels)
        {
            float dx = p.localX - comX;
            float dy = p.localY - comY;
            moi += dx * dx + dy * dy;
        }

        cluster.Mass = totalMass;
        cluster.MomentOfInertia = MathF.Max(moi, 0.1f);
    }

    /// <summary>Create a test cluster with a square shape centered at (0,0) local.</summary>
    public static ClusterData CreateSquareCluster(
        float cellX, float cellY, int size, byte materialId,
        ClusterManager manager)
    {
        var pixels = new List<ClusterPixel>();
        int half = size / 2;

        for (int y = -half; y <= half; y++)
            for (int x = -half; x <= half; x++)
                pixels.Add(new ClusterPixel((short)x, (short)y, materialId));

        return CreateCluster(pixels, cellX, cellY, manager);
    }

    /// <summary>Create a test cluster with a circle shape.</summary>
    public static ClusterData CreateCircleCluster(
        float cellX, float cellY, int radius, byte materialId,
        ClusterManager manager)
    {
        var pixels = new List<ClusterPixel>();

        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
                if (x * x + y * y <= radius * radius)
                    pixels.Add(new ClusterPixel((short)x, (short)y, materialId));

        return CreateCluster(pixels, cellX, cellY, manager);
    }

    /// <summary>Create a test cluster with an L-shape.</summary>
    public static ClusterData CreateLShapeCluster(
        float cellX, float cellY, int size, byte materialId,
        ClusterManager manager)
    {
        var pixels = new List<ClusterPixel>();
        int half = size / 2;

        // Vertical bar (left side)
        for (int y = -half; y <= half; y++)
            for (int x = -half; x <= -half + size / 3; x++)
                pixels.Add(new ClusterPixel((short)x, (short)y, materialId));

        // Horizontal bar (bottom)
        for (int x = -half; x <= half; x++)
            for (int y = half - size / 3; y <= half; y++)
                if (!pixels.Exists(p => p.localX == x && p.localY == y))
                    pixels.Add(new ClusterPixel((short)x, (short)y, materialId));

        return CreateCluster(pixels, cellX, cellY, manager);
    }
}
