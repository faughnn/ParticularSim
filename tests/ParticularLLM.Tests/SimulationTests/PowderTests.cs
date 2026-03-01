using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// English rules for powder simulation (derived from SimulateChunksLogic.SimulatePowder):
///
/// 1. Powder falls downward due to gravity. Even at zero velocity, a "gravity pull" always
///    attempts to move the cell down one row if the space is open (CanMoveTo with density check).
/// 2. When powder can't fall straight down, it slides diagonally down-left or down-right
///    (TryPowderSlide). Direction is randomized per cell/frame to avoid bias.
/// 3. Stability resists sliding: a hash-based probability check means higher stability → steeper piles.
///    Dirt (stability=50) piles steeper than sand (stability=0).
/// 4. On collision, impact energy converts to scatter velocity (restitution-scaled diagonal bounce).
/// 5. Powder displaces lighter materials via density comparison — sand (128) sinks through water (64).
/// 6. Material conservation: MoveCell swaps source and target cells; no material is created or destroyed.
/// 7. Powder stops when both diagonal-down positions are blocked and there's no velocity.
/// 8. Known tradeoff: upward movement is slower than downward due to bottom-to-top scan order.
/// </summary>
public class PowderTests
{
    [Fact]
    public void Sand_MovesDownward_FirstFrame()
    {
        // Rule 1: powder falls due to gravity pull (tries y+1 even at zero velocity)
        using var sim = new SimulationFixture();
        sim.Description = "A single sand grain placed in open air should fall exactly one row downward on the first simulation frame.";
        sim.Set(32, 10, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1, counts);
        WorldAssert.IsAir(sim.World, 32, 10);
        int sandOnRow11 = WorldAssert.CountMaterial(sim.World, 0, 11, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow11);
    }

    [Fact]
    public void Sand_EventuallyFalls_After20Frames()
    {
        // Rule 1: gravity continuously pulls powder downward
        using var sim = new SimulationFixture();
        sim.Description = "A single sand grain should vacate its starting position after 20 frames of gravitational pull.";
        sim.Set(32, 10, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(20, counts);
        WorldAssert.IsAir(sim.World, 32, 10);
    }

    [Fact]
    public void Sand_FallsToBottomRow()
    {
        // Rule 1+7: sand falls until it hits the world boundary (bottom row)
        using var sim = new SimulationFixture();
        sim.Description = "Sand placed near the top should fall all the way to the bottom row of the world when there are no obstacles.";
        sim.Set(32, 0, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
        int sandOnBottom = WorldAssert.CountMaterial(sim.World, 0, 63, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnBottom);
    }

    [Fact]
    public void Sand_StopsAboveStone()
    {
        // Rule 7: sand stops when blocked below by static material (stone)
        using var sim = new SimulationFixture();
        sim.Description = "Sand falling onto a horizontal stone floor should come to rest on the row directly above the stone.";
        sim.Fill(0, 50, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
        int sandOnRow49 = WorldAssert.CountMaterial(sim.World, 0, 49, 64, 1, Materials.Sand);
        Assert.Equal(1, sandOnRow49);
    }

    [Fact]
    public void Sand_PilesDiagonally()
    {
        // Rule 2: when blocked below, sand slides diagonally, forming a spread pile
        using var sim = new SimulationFixture();
        sim.Description = "Five sand grains dropped in a column onto a stone floor should spread diagonally rather than stacking in a single column.";
        sim.Fill(0, 60, 64, 4, Materials.Stone);
        for (int i = 0; i < 5; i++)
            sim.Set(32, i, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        int sandInCol32 = WorldAssert.CountMaterial(sim.World, 32, 0, 1, 60, Materials.Sand);
        Assert.True(sandInCol32 < 5, "Sand should spread diagonally, not stack in one column");
    }

    [Fact]
    public void Sand_MaterialConservation_LargeAmount()
    {
        // Rule 6: per-frame material conservation with 240 sand cells
        using var sim = new SimulationFixture();
        sim.Description = "240 sand cells falling onto a stone floor should all be conserved with no material lost or duplicated on any frame.";
        sim.Fill(0, 60, 64, 4, Materials.Stone);
        int placed = 0;
        for (int x = 20; x < 44; x++)
            for (int y = 0; y < 10; y++)
            {
                sim.Set(x, y, Materials.Sand);
                placed++;
            }
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
        Assert.Equal(placed, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void Sand_DoesNotMoveWhenSupported()
    {
        // Rule 7: powder at rest with blocked diagonal stays put
        using var sim = new SimulationFixture();
        sim.Description = "Sand resting on a stone floor with no open diagonals should remain stationary and not move.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 62, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(50, counts);
        WorldAssert.CellIs(sim.World, 32, 62, Materials.Sand);
    }

    [Fact]
    public void Dirt_PilesSteeper_HigherSlideResistance()
    {
        // Rule 3: dirt (stability=50) resists diagonal sliding more than sand (stability=0)
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "30 dirt grains dropped in a column should form a steep pile with most grains near the center, due to dirt's high slide resistance.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);
        for (int y = 0; y < 30; y++)
            sim.Set(64, y, Materials.Dirt);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);
        Assert.Equal(30, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
        int nearCenter = WorldAssert.CountMaterial(sim.World, 60, 0, 9, 120, Materials.Dirt);
        Assert.True(nearCenter > 15, $"Dirt should pile steeply near center, but only {nearCenter}/30 in 9-wide zone");
    }
}
