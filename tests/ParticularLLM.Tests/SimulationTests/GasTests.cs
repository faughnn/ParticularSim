using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// English rules for gas simulation (derived from SimulateChunksLogic.SimulateGas):
///
/// 1. Gas rises upward — gravity is inverted via fractional accumulation that builds
///    negative velocity. Each overflow of the fractional accumulator decrements velocityY.
/// 2. Gas traces upward along its velocity vector; blocked cells stop the trace early.
/// 3. If blocked directly above, gas tries diagonal upward (up-left or up-right, randomized).
/// 4. If diagonal is also blocked, gas spreads horizontally up to 4 cells.
/// 5. Gas displaces lighter materials via density comparison (steam density=4, smoke=2).
/// 6. Gas stops below solid ceilings (static materials block CanMoveTo).
/// 7. Material conservation: no gas is created or destroyed during movement.
/// 8. Known tradeoff: gas rises one cell at a time due to bottom-to-top scan order
///    (no cascade effect like falling powder gets).
/// </summary>
public class GasTests
{
    [Fact]
    public void Steam_RisesUp()
    {
        // Rule 1: gas rises away from starting position
        using var sim = new SimulationFixture();
        sim.Set(32, 50, Materials.Steam);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);
        WorldAssert.IsAir(sim.World, 32, 50);
    }

    [Fact]
    public void Steam_RisesToTop()
    {
        // Rule 1+6: gas rises to top row (world boundary acts as ceiling)
        using var sim = new SimulationFixture();
        sim.Set(32, 60, Materials.Steam);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);
        int steamOnTopRow = WorldAssert.CountMaterial(sim.World, 0, 0, 64, 1, Materials.Steam);
        Assert.Equal(1, steamOnTopRow);
    }

    [Fact]
    public void Steam_MaterialConservation()
    {
        // Rule 7: per-frame conservation with 5 steam cells
        using var sim = new SimulationFixture();
        int placed = 5;
        for (int i = 0; i < placed; i++)
            sim.Set(30 + i, 50, Materials.Steam);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        int remaining = WorldAssert.CountMaterial(sim.World, Materials.Steam);
        Assert.Equal(placed, remaining);
    }

    [Fact]
    public void Steam_LeavesOriginalPosition()
    {
        // Rule 1: gas moves away from start within a few frames
        using var sim = new SimulationFixture();
        sim.Set(32, 32, Materials.Steam);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(30, counts);
        WorldAssert.IsAir(sim.World, 32, 32);
    }

    [Fact]
    public void Steam_StopsBelowCeiling()
    {
        // Rule 6: gas stops below static ceiling
        using var sim = new SimulationFixture();
        sim.Fill(0, 10, 64, 1, Materials.Stone);
        sim.Set(32, 50, Materials.Steam);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        int steamOnRow11 = WorldAssert.CountMaterial(sim.World, 0, 11, 64, 1, Materials.Steam);
        Assert.Equal(1, steamOnRow11);
    }
}
