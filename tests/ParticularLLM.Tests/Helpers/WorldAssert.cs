using ParticularLLM;
using Xunit;

namespace ParticularLLM.Tests.Helpers;

/// <summary>
/// Layer 2 assertion helpers for scenario tests.
/// Provides cell-level checks, material counting, region dumps, and spatial assertions.
/// </summary>
public static class WorldAssert
{
    /// <summary>
    /// Asserts that the cell at (x,y) contains the expected material.
    /// On failure, dumps a 7x7 region around the cell for context.
    /// </summary>
    public static void CellIs(CellWorld world, int x, int y, byte expectedMaterial)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual == expectedMaterial,
            $"Expected material {expectedMaterial} at ({x},{y}), got {actual}.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    /// <summary>Asserts that the cell at (x,y) is Air.</summary>
    public static void IsAir(CellWorld world, int x, int y) => CellIs(world, x, y, Materials.Air);

    /// <summary>Asserts that the cell at (x,y) is not Air.</summary>
    public static void IsNotAir(CellWorld world, int x, int y)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual != Materials.Air,
            $"Expected non-air at ({x},{y}), but got Air.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    /// <summary>Counts cells of a given material within a rectangular region.</summary>
    public static int CountMaterial(CellWorld world, int x, int y, int w, int h, byte materialId)
    {
        int count = 0;
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                if (world.GetCell(x + dx, y + dy) == materialId)
                    count++;
        return count;
    }

    /// <summary>Counts all cells of a given material in the entire world.</summary>
    public static int CountMaterial(CellWorld world, byte materialId)
    {
        int count = 0;
        for (int i = 0; i < world.cells.Length; i++)
            if (world.cells[i].materialId == materialId)
                count++;
        return count;
    }

    public static string DumpRegion(CellWorld world, int x, int y, int w, int h)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  Region ({x},{y}) to ({x+w-1},{y+h-1}):");
        for (int dy = 0; dy < h; dy++)
        {
            sb.Append("  ");
            for (int dx = 0; dx < w; dx++)
            {
                byte mat = world.GetCell(x + dx, y + dy);
                sb.Append(WorldDump.MaterialChar(mat));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Asserts that materialA's center-of-mass Y is above (lower Y value) materialB's.
    /// "A is above B" in screen coords means A has lower Y.
    /// </summary>
    public static void MaterialAbove(CellWorld world, byte matAbove, byte matBelow)
    {
        double aboveSumY = 0, aboveCount = 0;
        double belowSumY = 0, belowCount = 0;

        for (int i = 0; i < world.cells.Length; i++)
        {
            byte mat = world.cells[i].materialId;
            int y = i / world.width;

            if (mat == matAbove) { aboveSumY += y; aboveCount++; }
            else if (mat == matBelow) { belowSumY += y; belowCount++; }
        }

        Assert.True(aboveCount > 0, $"No cells of material {matAbove} found");
        Assert.True(belowCount > 0, $"No cells of material {matBelow} found");

        double aboveCenterY = aboveSumY / aboveCount;
        double belowCenterY = belowSumY / belowCount;

        Assert.True(aboveCenterY < belowCenterY,
            $"Expected material {matAbove} (center Y={aboveCenterY:F1}) to be above " +
            $"material {matBelow} (center Y={belowCenterY:F1})");
    }

    /// <summary>
    /// Asserts that no cells of the given material exist in the specified region.
    /// </summary>
    public static void NoMaterialInRegion(CellWorld world, byte materialId,
        int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        {
            for (int dx = 0; dx < w; dx++)
            {
                int cx = x + dx;
                int cy = y + dy;
                if (world.GetCell(cx, cy) == materialId)
                {
                    Assert.Fail(
                        $"Found material {materialId} at ({cx},{cy}) but expected none in " +
                        $"region ({x},{y})-({x+w-1},{y+h-1}).\n" +
                        WorldDump.DumpRegion(world, x, y, w, h));
                }
            }
        }
    }

    /// <summary>
    /// Asserts that a material is spread roughly symmetrically around a center X.
    /// Measures how many cells are left vs right of center; the ratio must be within maxRatio.
    /// maxRatio of 2.0 means one side can have at most 2x the cells of the other.
    /// </summary>
    public static void SymmetricSpread(CellWorld world, byte materialId, int centerX, double maxRatio)
    {
        int leftCount = 0, rightCount = 0;

        for (int i = 0; i < world.cells.Length; i++)
        {
            if (world.cells[i].materialId != materialId) continue;
            int x = i % world.width;

            if (x < centerX) leftCount++;
            else if (x > centerX) rightCount++;
            // x == centerX doesn't count for either side
        }

        if (leftCount == 0 && rightCount == 0) return; // No material found

        // Allow asymmetry up to maxRatio
        int min = Math.Min(leftCount, rightCount);
        int max = Math.Max(leftCount, rightCount);

        if (min == 0)
        {
            Assert.Fail(
                $"Material {materialId} is entirely on one side of center X={centerX}: " +
                $"left={leftCount}, right={rightCount}");
        }

        double ratio = (double)max / min;
        Assert.True(ratio <= maxRatio,
            $"Material {materialId} spread asymmetry around X={centerX}: " +
            $"left={leftCount}, right={rightCount}, ratio={ratio:F2} exceeds max {maxRatio:F2}");
    }
}
