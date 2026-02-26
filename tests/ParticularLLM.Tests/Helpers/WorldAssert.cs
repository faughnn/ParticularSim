using ParticularLLM;
using Xunit;

namespace ParticularLLM.Tests.Helpers;

public static class WorldAssert
{
    public static void CellIs(CellWorld world, int x, int y, byte expectedMaterial)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual == expectedMaterial,
            $"Expected material {expectedMaterial} at ({x},{y}), got {actual}.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    public static void IsAir(CellWorld world, int x, int y) => CellIs(world, x, y, Materials.Air);

    public static void IsNotAir(CellWorld world, int x, int y)
    {
        byte actual = world.GetCell(x, y);
        Assert.True(actual != Materials.Air,
            $"Expected non-air at ({x},{y}), but got Air.\n{DumpRegion(world, x - 3, y - 3, 7, 7)}");
    }

    public static int CountMaterial(CellWorld world, int x, int y, int w, int h, byte materialId)
    {
        int count = 0;
        for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
                if (world.GetCell(x + dx, y + dy) == materialId)
                    count++;
        return count;
    }

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
                sb.Append(MaterialChar(mat));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static char MaterialChar(byte mat) => mat switch
    {
        Materials.Air => '.',
        Materials.Stone => '#',
        Materials.Sand => ':',
        Materials.Water => '~',
        Materials.Steam => '^',
        Materials.Oil => 'o',
        Materials.Dirt => '!',
        Materials.Ground => 'G',
        Materials.Wall => 'W',
        Materials.LiftUp or Materials.LiftUpLight => '|',
        _ when Materials.IsBelt(mat) => '=',
        _ => '?',
    };
}
