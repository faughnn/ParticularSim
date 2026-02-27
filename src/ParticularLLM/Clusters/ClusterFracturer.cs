namespace ParticularLLM;

/// <summary>
/// Handles fracture logic for clusters under compression.
///
/// Fracture process:
/// 1. Detect opposing contact forces on a cluster (sustained compression)
/// 2. After N frames of sustained pressure, fracture the cluster
/// 3. Generate 1-3 random crack lines through the cluster
/// 4. Partition pixels by signed distance to crack lines
/// 5. Small groups (< 3 pixels) merge into largest group (material conservation)
/// 6. Create sub-clusters with inherited velocity
///
/// Uses seeded RNG for deterministic results in tests.
/// </summary>
public static class ClusterFracturer
{
    public const int MinPixelsToFracture = 3;
    public const int CrushFrameThreshold = 30;

    /// <summary>
    /// Check all clusters for compression and fracture those that qualify.
    /// In our simple solver, compression is detected when a cluster has been
    /// on the ground with very low velocity for many frames AND has CrushPressureFrames > threshold.
    /// External systems (like pistons) increment CrushPressureFrames.
    /// </summary>
    public static void CheckAndFracture(ClusterManager manager, CellWorld world)
    {
        var toFracture = new List<ClusterData>();

        foreach (var cluster in manager.AllClusters)
        {
            if (cluster.IsSleeping) continue;
            if (cluster.IsMachinePart) continue;
            if (cluster.PixelCount < MinPixelsToFracture * 2) continue;

            if (cluster.CrushPressureFrames > CrushFrameThreshold)
                toFracture.Add(cluster);
        }

        foreach (var cluster in toFracture)
        {
            FractureCluster(cluster, manager, world);
        }
    }

    /// <summary>
    /// Fracture a cluster into smaller pieces using crack-line partitioning.
    /// No pixels are removed — small groups merge into the largest to preserve all material.
    /// </summary>
    /// <param name="seed">RNG seed for deterministic crack placement. Uses cluster ID if 0.</param>
    public static void FractureCluster(
        ClusterData cluster,
        ClusterManager manager,
        CellWorld world,
        int seed = 0)
    {
        var pixels = cluster.Pixels;
        int pixelCount = pixels.Count;

        if (pixelCount < MinPixelsToFracture * 2) return;

        // Seeded RNG for determinism
        if (seed == 0) seed = cluster.Id * 7919 + 31;
        var rng = new Random(seed);

        // Compute local bounding box
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var p in pixels)
        {
            if (p.localX < minX) minX = p.localX;
            if (p.localX > maxX) maxX = p.localX;
            if (p.localY < minY) minY = p.localY;
            if (p.localY > maxY) maxY = p.localY;
        }

        // Generate crack lines
        int numCracks = pixelCount < 20 ? 1 : rng.Next(1, 4); // 1 to 3 cracks
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;
        float extentX = (maxX - minX + 1) * 0.3f;
        float extentY = (maxY - minY + 1) * 0.3f;

        float[] crackPtX = new float[numCracks];
        float[] crackPtY = new float[numCracks];
        float[] crackNormX = new float[numCracks];
        float[] crackNormY = new float[numCracks];

        for (int i = 0; i < numCracks; i++)
        {
            crackPtX[i] = centerX + (float)(rng.NextDouble() * 2 - 1) * extentX;
            crackPtY[i] = centerY + (float)(rng.NextDouble() * 2 - 1) * extentY;
            float angle = (float)(rng.NextDouble() * MathF.PI);
            crackNormX[i] = MathF.Cos(angle);
            crackNormY[i] = MathF.Sin(angle);
        }

        // Partition pixels by signed distance to each crack line
        int maxGroups = 1 << numCracks;
        int[] groupCounts = new int[maxGroups];
        int[] pixelGroups = new int[pixelCount];

        for (int pi = 0; pi < pixelCount; pi++)
        {
            var p = pixels[pi];
            int group = 0;
            for (int ci = 0; ci < numCracks; ci++)
            {
                float dx = p.localX - crackPtX[ci];
                float dy = p.localY - crackPtY[ci];
                float signedDist = dx * crackNormX[ci] + dy * crackNormY[ci];
                if (signedDist >= 0f)
                    group |= (1 << ci);
            }
            pixelGroups[pi] = group;
            groupCounts[group]++;
        }

        // Merge small groups into the largest
        int largestGroup = 0;
        for (int g = 1; g < maxGroups; g++)
        {
            if (groupCounts[g] > groupCounts[largestGroup])
                largestGroup = g;
        }

        for (int g = 0; g < maxGroups; g++)
        {
            if (g == largestGroup) continue;
            if (groupCounts[g] > 0 && groupCounts[g] < MinPixelsToFracture)
            {
                groupCounts[largestGroup] += groupCounts[g];
                groupCounts[g] = 0;
                for (int pi = 0; pi < pixelCount; pi++)
                {
                    if (pixelGroups[pi] == g)
                        pixelGroups[pi] = largestGroup;
                }
            }
        }

        // Viability check: need at least 2 non-empty groups
        int viableGroups = 0;
        for (int g = 0; g < maxGroups; g++)
        {
            if (groupCounts[g] >= MinPixelsToFracture)
                viableGroups++;
        }
        if (viableGroups < 2) return;

        // Save original state
        float origVelX = cluster.VelocityX;
        float origVelY = cluster.VelocityY;
        float origAngVel = cluster.AngularVelocity;
        float origRot = cluster.Rotation;
        float cos = MathF.Cos(origRot);
        float sin = MathF.Sin(origRot);

        // Clear original cluster from grid before creating sub-clusters.
        // Don't release ID — prevents sub-cluster from reusing the original's ID.
        manager.RemoveCluster(cluster, world, releaseId: false);

        // Create sub-clusters for each non-empty group
        for (int g = 0; g < maxGroups; g++)
        {
            if (groupCounts[g] == 0) continue;

            var groupPixels = new List<ClusterPixel>(groupCounts[g]);
            float sumLX = 0, sumLY = 0;

            for (int pi = 0; pi < pixelCount; pi++)
            {
                if (pixelGroups[pi] != g) continue;
                var p = pixels[pi];
                groupPixels.Add(p);
                sumLX += p.localX;
                sumLY += p.localY;
            }

            // Compute centroid of this group in local space
            float centroidLX = sumLX / groupPixels.Count;
            float centroidLY = sumLY / groupPixels.Count;

            // Transform centroid to world cell space
            float rotCX = centroidLX * cos - centroidLY * sin;
            float rotCY = centroidLX * sin + centroidLY * cos;
            float worldCentroidX = cluster.X + rotCX;
            float worldCentroidY = cluster.Y + rotCY;

            // Re-center pixel offsets around the group centroid
            var subPixels = new List<ClusterPixel>(groupPixels.Count);
            foreach (var p in groupPixels)
            {
                short newLX = (short)MathF.Round(p.localX - centroidLX);
                short newLY = (short)MathF.Round(p.localY - centroidLY);
                subPixels.Add(new ClusterPixel(newLX, newLY, p.materialId));
            }

            var subCluster = ClusterFactory.CreateCluster(subPixels, worldCentroidX, worldCentroidY, manager);
            if (subCluster != null)
            {
                subCluster.VelocityX = origVelX;
                subCluster.VelocityY = origVelY;
                subCluster.AngularVelocity = origAngVel;
                subCluster.Rotation = origRot;
            }
        }
    }
}
