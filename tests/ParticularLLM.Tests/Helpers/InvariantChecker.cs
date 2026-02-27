using ParticularLLM;
using Xunit;

namespace ParticularLLM.Tests.Helpers;

/// <summary>
/// Layer 1 global invariants — physics rules that must always hold.
///
/// Per-step invariants: valid even mid-simulation.
/// Settled-state invariants: only valid after the sim has reached rest.
/// </summary>
public static class InvariantChecker
{
    // ===== PER-STEP INVARIANTS =====

    /// <summary>
    /// Asserts that material counts haven't changed since a snapshot.
    /// Per-step invariant: valid at any point during simulation.
    /// </summary>
    public static void AssertMaterialConservation(CellWorld world, Dictionary<byte, int> expectedCounts)
    {
        var actual = SnapshotMaterialCounts(world);

        foreach (var (matId, expectedCount) in expectedCounts)
        {
            if (matId == Materials.Air) continue; // Don't track air

            actual.TryGetValue(matId, out int actualCount);
            Assert.True(actualCount == expectedCount,
                $"Material conservation violated for material {matId}: " +
                $"expected {expectedCount}, got {actualCount} (delta {actualCount - expectedCount})");
        }

        // Check for unexpected materials that weren't in the snapshot
        foreach (var (matId, actualCount) in actual)
        {
            if (matId == Materials.Air) continue;
            if (!expectedCounts.ContainsKey(matId) && actualCount > 0)
            {
                Assert.Fail(
                    $"Material conservation violated: unexpected material {matId} appeared with count {actualCount}");
            }
        }
    }

    /// <summary>
    /// Asserts no two non-air materials occupy the same cell.
    /// Per-step invariant. (This is structurally guaranteed by the array layout,
    /// but verifies no MoveCell bug creates duplicates.)
    /// </summary>
    public static void AssertNoDuplication(CellWorld world, Dictionary<byte, int> expectedCounts)
    {
        // In a flat array, duplication means a material count increased while another decreased.
        // This is already caught by AssertMaterialConservation, but we provide a separate
        // check for clarity: total non-air cell count must match expected.
        int expectedTotal = 0;
        foreach (var (matId, count) in expectedCounts)
        {
            if (matId != Materials.Air) expectedTotal += count;
        }

        int actualTotal = 0;
        for (int i = 0; i < world.cells.Length; i++)
        {
            if (world.cells[i].materialId != Materials.Air)
                actualTotal++;
        }

        Assert.True(actualTotal == expectedTotal,
            $"Duplication check failed: expected {expectedTotal} non-air cells, got {actualTotal}");
    }

    // ===== SETTLED-STATE INVARIANTS =====

    /// <summary>
    /// Asserts no powder cell has air directly below it, unless:
    /// - The cell has upward velocity (velocityY &lt; 0)
    /// - The cell is inside an active (non-ghost) lift zone
    /// Settled-state invariant: only valid after sim reaches rest.
    /// </summary>
    public static void AssertNoFloatingPowder(CellWorld world, LiftTile[]? liftTiles = null)
    {
        var materials = world.materials;

        for (int y = 0; y < world.height - 1; y++) // Skip bottom row (nothing below)
        {
            for (int x = 0; x < world.width; x++)
            {
                int idx = y * world.width + x;
                Cell cell = world.cells[idx];

                if (cell.materialId == Materials.Air) continue;
                if (cell.ownerId != 0) continue; // Cluster-owned

                MaterialDef mat = materials[cell.materialId];
                if (mat.behaviour != BehaviourType.Powder) continue;

                // Check if cell below is air
                int belowIdx = (y + 1) * world.width + x;
                if (world.cells[belowIdx].materialId != Materials.Air) continue;

                // Exception: upward velocity
                if (cell.velocityY < 0) continue;

                // Exception: inside active lift
                if (liftTiles != null)
                {
                    var lt = liftTiles[idx];
                    if (lt.liftId != 0 && !lt.isGhost) continue;
                }

                // Exception: below cell is passable structure (lift material)
                MaterialDef belowMat = materials[world.cells[belowIdx].materialId];
                if ((belowMat.flags & MaterialFlags.Passable) != 0) continue;

                Assert.Fail(
                    $"Floating powder at ({x},{y}): material {cell.materialId} has air below " +
                    $"with no upward velocity and not in lift.\n" +
                    WorldDump.DumpRegion(world, x - 3, y - 3, 7, 7));
            }
        }
    }

    /// <summary>
    /// Asserts no liquid cell is floating with air below AND air on both sides.
    /// Settled-state invariant: only valid after sim reaches rest.
    /// </summary>
    public static void AssertNoFloatingLiquid(CellWorld world, LiftTile[]? liftTiles = null)
    {
        var materials = world.materials;

        for (int y = 0; y < world.height - 1; y++)
        {
            for (int x = 0; x < world.width; x++)
            {
                int idx = y * world.width + x;
                Cell cell = world.cells[idx];

                if (cell.materialId == Materials.Air) continue;
                if (cell.ownerId != 0) continue;

                MaterialDef mat = materials[cell.materialId];
                if (mat.behaviour != BehaviourType.Liquid) continue;

                // Check air below
                if (world.GetCell(x, y + 1) != Materials.Air) continue;

                // Check air on both sides
                bool airLeft = (x == 0) || world.GetCell(x - 1, y) == Materials.Air;
                bool airRight = (x == world.width - 1) || world.GetCell(x + 1, y) == Materials.Air;

                if (!airLeft || !airRight) continue;

                // Exception: upward velocity (in lift)
                if (cell.velocityY < 0) continue;

                // Exception: inside active lift
                if (liftTiles != null)
                {
                    var lt = liftTiles[idx];
                    if (lt.liftId != 0 && !lt.isGhost) continue;
                }

                Assert.Fail(
                    $"Floating liquid at ({x},{y}): material {cell.materialId} has air below " +
                    $"and air on both sides.\n" +
                    WorldDump.DumpRegion(world, x - 3, y - 3, 7, 7));
            }
        }
    }

    /// <summary>
    /// Asserts that in a given region, the heavier material's center of mass is below
    /// the lighter material's center of mass (i.e. heavier stuff sinks).
    /// Settled-state invariant.
    /// </summary>
    public static void AssertDensityLayering(CellWorld world, byte heavyMat, byte lightMat,
        int regionX, int regionY, int regionW, int regionH)
    {
        double heavySumY = 0, heavyCount = 0;
        double lightSumY = 0, lightCount = 0;

        for (int dy = 0; dy < regionH; dy++)
        {
            for (int dx = 0; dx < regionW; dx++)
            {
                int x = regionX + dx;
                int y = regionY + dy;
                byte mat = world.GetCell(x, y);

                if (mat == heavyMat) { heavySumY += y; heavyCount++; }
                else if (mat == lightMat) { lightSumY += y; lightCount++; }
            }
        }

        if (heavyCount == 0 || lightCount == 0)
            return; // Can't assert layering without both materials

        double heavyCenterY = heavySumY / heavyCount;
        double lightCenterY = lightSumY / lightCount;

        // In our coordinate system, higher Y = lower on screen (bottom)
        Assert.True(heavyCenterY > lightCenterY,
            $"Density layering violated: heavy material {heavyMat} center-of-mass Y={heavyCenterY:F1} " +
            $"should be below (higher Y than) light material {lightMat} center-of-mass Y={lightCenterY:F1}\n" +
            WorldDump.DumpRegion(world, regionX, regionY, regionW, regionH));
    }

    // ===== HELPERS =====

    /// <summary>
    /// Takes a snapshot of all material counts in the world.
    /// </summary>
    public static Dictionary<byte, int> SnapshotMaterialCounts(CellWorld world)
    {
        var counts = new Dictionary<byte, int>();
        for (int i = 0; i < world.cells.Length; i++)
        {
            byte matId = world.cells[i].materialId;
            if (matId == Materials.Air) continue;
            counts.TryGetValue(matId, out int count);
            counts[matId] = count + 1;
        }
        return counts;
    }
}
