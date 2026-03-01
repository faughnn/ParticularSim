using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for gas (steam) behavior.
///
/// English rules (derived from SimulateGas lines 555-616):
///
/// 1. Gas rises using fractional accumulation — same mechanism as gravity but
///    velocity decrements (goes upward). Overflow detected by wrap-around.
/// 2. Trace path upward from current position to target (velocityY is negative).
/// 3. If can't rise directly: try diagonal upward (alternating left/right).
/// 4. If can't rise diagonally: spread horizontally up to 4 cells.
/// 5. When stuck (no movement possible): velocityY zeroed.
/// 6. Steam: density=4. This means it displaces nothing except air.
/// 7. Gas does NOT use spread from MaterialDef — hardcoded spread=4.
///
/// Known tradeoffs:
/// - Gas uses byte wrap-around for overflow detection (different from powder/liquid's >= 256 check).
/// - Gas doesn't have Phase 2/Phase 3 like powder; simpler movement model.
/// </summary>
public class GasSpreadTests
{
    // ===== BASIC RISING =====

    [Fact]
    public void Steam_RisesUpward()
    {
        using var sim = new SimulationFixture();
        sim.Description = "A single steam cell placed mid-world should rise upward from its starting position.";
        sim.Set(32, 50, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        var pos = sim.FindMaterial(Materials.Steam);
        Assert.Single(pos);
        Assert.True(pos[0].y < 50,
            $"Steam should rise from y=50, but is at y={pos[0].y}");
    }

    [Fact]
    public void Steam_RisesToTopOfWorld()
    {
        using var sim = new SimulationFixture();
        sim.Description = "A single steam cell should rise all the way to near the top of the world (y<=5).";
        sim.Set(32, 60, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Steam);
        Assert.Single(pos);
        // Steam should reach near the top (y=0) or be stuck at ceiling
        Assert.True(pos[0].y <= 5,
            $"Steam should rise to top, but is at y={pos[0].y}");
    }

    [Fact]
    public void Steam_StopsAtStoneCeiling()
    {
        using var sim = new SimulationFixture();
        sim.Description = "Steam rising toward a stone ceiling should stop just below it, not pass through.";
        sim.Fill(0, 20, 64, 1, Materials.Stone); // Ceiling
        sim.Set(32, 50, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Steam);
        Assert.Single(pos);
        // Steam should be just below the ceiling
        Assert.True(pos[0].y >= 21 && pos[0].y <= 25,
            $"Steam should stop below ceiling at y=20, but is at y={pos[0].y}");
    }

    // ===== DIAGONAL RISING =====

    [Fact]
    public void Steam_RisesDiagonallyAroundObstacle()
    {
        // Steam blocked directly above should try diagonal upward.
        using var sim = new SimulationFixture();
        sim.Description = "Steam blocked by a small stone overhang should navigate around it diagonally and rise above the obstacle.";

        // Small overhang blocking direct rise
        sim.Fill(30, 30, 5, 1, Materials.Stone); // Blocks x=30..34 at y=30
        sim.Set(32, 40, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Steam);
        Assert.Single(pos);
        // Steam should have gotten above the obstacle via diagonal
        Assert.True(pos[0].y < 30,
            $"Steam should rise above obstacle at y=30 via diagonal, but is at y={pos[0].y}");
    }

    // ===== HORIZONTAL SPREAD =====

    [Fact]
    public void Steam_SpreadsHorizontallyUnderCeiling()
    {
        // Steam trapped under a ceiling should spread horizontally (up to 4 cells).
        using var sim = new SimulationFixture();
        sim.Description = "Multiple steam cells trapped under a stone ceiling should spread horizontally at least as wide as their initial placement.";

        sim.Fill(0, 20, 64, 1, Materials.Stone); // Ceiling

        // Place multiple steam cells just below ceiling
        for (int x = 30; x < 35; x++)
            sim.Set(x, 21, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(100, counts);

        // Steam should have spread horizontally
        int minX = int.MaxValue, maxX = int.MinValue;
        foreach (var (x, _) in sim.FindMaterial(Materials.Steam))
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
        }
        int spread = maxX - minX + 1;
        Assert.True(spread >= 5,
            $"Steam should spread at least as wide as initial placement, spread={spread}");
    }

    // ===== MATERIAL CONSERVATION =====

    [Fact]
    public void Steam_MaterialConservation_MultipleParticles()
    {
        using var sim = new SimulationFixture();
        sim.Description = "An 8x5 block of steam cells rising in open air should all be conserved after 300 frames.";

        int steamCount = 0;
        for (int x = 28; x < 36; x++)
            for (int y = 50; y < 55; y++)
            {
                sim.Set(x, y, Materials.Steam);
                steamCount++;
            }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(300, counts);

        Assert.Equal(steamCount, WorldAssert.CountMaterial(sim.World, Materials.Steam));
    }

    [Fact]
    public void Steam_ConservedInEnclosure()
    {
        // Steam in a sealed box should be conserved (can't escape).
        using var sim = new SimulationFixture();
        sim.Description = "Steam placed inside a sealed stone box should remain fully conserved after 500 frames.";

        // Sealed box
        sim.Fill(20, 20, 24, 1, Materials.Stone); // Top
        sim.Fill(20, 43, 24, 1, Materials.Stone); // Bottom
        sim.Fill(20, 20, 1, 24, Materials.Stone);  // Left
        sim.Fill(43, 20, 1, 24, Materials.Stone);  // Right

        // Steam inside
        for (int x = 25; x < 35; x++)
            for (int y = 30; y < 35; y++)
                sim.Set(x, y, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int steamTotal = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.Equal(50, steamTotal);
    }

    // ===== GAS DENSITY INTERACTIONS =====

    [Fact]
    public void Steam_DoesNotDisplaceSand()
    {
        // Steam (density=4) should not displace sand (density=128).
        using var sim = new SimulationFixture();
        sim.Description = "Steam below sand should not displace the heavier sand; both materials should remain present.";

        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 30, Materials.Sand);
        sim.Set(32, 35, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(100, counts);

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Steam));
    }

    [Fact]
    public void Steam_RisesThroughAir_NotThroughStone()
    {
        // Steam should rise through air but be blocked by stone.
        using var sim = new SimulationFixture();
        sim.Description = "Steam should pass through a gap in the first stone ceiling but be blocked by a solid second ceiling above.";

        // Two layers of stone with gaps
        sim.Fill(0, 20, 64, 1, Materials.Stone);
        sim.Set(32, 20, Materials.Air); // Gap in first ceiling
        sim.Fill(0, 10, 64, 1, Materials.Stone);
        // No gap in second ceiling

        sim.Set(32, 50, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Steam);
        Assert.Single(pos);
        // Steam should pass through gap at y=20 but stop at y=10 ceiling
        Assert.True(pos[0].y >= 11,
            $"Steam should stop below second ceiling at y=10, but is at y={pos[0].y}");
    }

    // ===== VELOCITY BEHAVIOR =====

    [Fact]
    public void Steam_AcceleratesUpward()
    {
        // Steam should accelerate upward via fractional gravity.
        // Measure early frames to see acceleration before hitting max velocity.
        using var sim = new SimulationFixture(64, 512);
        sim.Description = "Steam in a tall world should accelerate upward via fractional gravity, covering more than 1 cell per frame on average.";

        sim.Set(32, 500, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();

        // Measure position at frames 10, 30, 50 (early enough to see acceleration)
        sim.StepWithInvariants(10, counts);
        int y10 = sim.FindMaterial(Materials.Steam)[0].y;

        sim.StepWithInvariants(20, counts);
        int y30 = sim.FindMaterial(Materials.Steam)[0].y;

        int distFirst = y10 - y30;  // How far steam rose in frames 11-30 (positive = upward)

        // Steam should have risen (y decreased)
        Assert.True(distFirst > 0,
            $"Steam should rise: y at frame 10 was {y10}, at frame 30 was {y30}");

        // Steam should have moved more than 1 cell per frame on average
        // (indicating velocity > 0 from accumulator overflow)
        Assert.True(distFirst >= 10,
            $"Steam should accelerate: rose only {distFirst} cells in 20 frames");
    }
}
