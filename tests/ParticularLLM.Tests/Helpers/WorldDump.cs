using ParticularLLM;
using System.Text;

namespace ParticularLLM.Tests.Helpers;

/// <summary>
/// ASCII dump utilities for Layer 3 (LLM review) tests.
/// Produces readable grid representations of the world state.
/// </summary>
public static class WorldDump
{
    /// <summary>
    /// Dumps the entire world as an ASCII grid.
    /// For large worlds, consider using the region variant instead.
    /// </summary>
    public static string DumpWorld(CellWorld world)
    {
        return DumpRegion(world, 0, 0, world.width, world.height);
    }

    /// <summary>
    /// Dumps a rectangular region of the world as an ASCII grid with coordinate labels.
    /// Out-of-bounds cells are shown as 'X'.
    /// </summary>
    public static string DumpRegion(CellWorld world, int x, int y, int w, int h)
    {
        var sb = new StringBuilder();

        // Header with X coordinates (ones digit)
        sb.Append("     ");
        for (int dx = 0; dx < w; dx++)
        {
            int cx = x + dx;
            if (cx >= 0 && cx < world.width)
                sb.Append((cx % 10).ToString());
            else
                sb.Append(' ');
        }
        sb.AppendLine();

        // Grid rows
        for (int dy = 0; dy < h; dy++)
        {
            int cy = y + dy;
            sb.Append($"{cy,4} ");

            for (int dx = 0; dx < w; dx++)
            {
                int cx = x + dx;
                if (cx < 0 || cx >= world.width || cy < 0 || cy >= world.height)
                {
                    sb.Append('X');
                }
                else
                {
                    sb.Append(MaterialChar(world.GetCell(cx, cy)));
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Dumps a region showing velocity information instead of material type.
    /// Useful for debugging physics behavior.
    /// Format: '.' for air, 'v' for falling, '^' for rising, '>' for right, '&lt;' for left, 'o' for stationary non-air.
    /// </summary>
    public static string DumpVelocity(CellWorld world, int x, int y, int w, int h)
    {
        var sb = new StringBuilder();

        sb.Append("     ");
        for (int dx = 0; dx < w; dx++)
        {
            int cx = x + dx;
            if (cx >= 0 && cx < world.width)
                sb.Append((cx % 10).ToString());
            else
                sb.Append(' ');
        }
        sb.AppendLine();

        for (int dy = 0; dy < h; dy++)
        {
            int cy = y + dy;
            sb.Append($"{cy,4} ");

            for (int dx = 0; dx < w; dx++)
            {
                int cx = x + dx;
                if (cx < 0 || cx >= world.width || cy < 0 || cy >= world.height)
                {
                    sb.Append('X');
                    continue;
                }

                int idx = cy * world.width + cx;
                Cell cell = world.cells[idx];

                if (cell.materialId == Materials.Air)
                {
                    sb.Append('.');
                }
                else if (cell.velocityY > 0)
                {
                    sb.Append('v'); // falling
                }
                else if (cell.velocityY < 0)
                {
                    sb.Append('^'); // rising
                }
                else if (cell.velocityX > 0)
                {
                    sb.Append('>'); // moving right
                }
                else if (cell.velocityX < 0)
                {
                    sb.Append('<'); // moving left
                }
                else
                {
                    sb.Append('o'); // stationary
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Material-to-character mapping shared with WorldAssert.
    /// </summary>
    internal static char MaterialChar(byte mat) => mat switch
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
        Materials.IronOre => 'I',
        Materials.MoltenIron => 'M',
        Materials.Iron => 'F',
        Materials.Coal => 'C',
        Materials.Ash => 'a',
        Materials.Smoke => 's',
        Materials.LiftUp or Materials.LiftUpLight => '|',
        _ when Materials.IsBelt(mat) => '=',
        _ => '?',
    };
}
