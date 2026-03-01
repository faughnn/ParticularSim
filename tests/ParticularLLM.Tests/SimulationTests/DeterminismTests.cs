using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Meta-property: Simulation determinism.
/// Given identical initial state, the simulation must produce byte-identical output
/// regardless of how many times it's run. This is required because:
/// - Chunk processing order is fixed (bottom-to-top, alternating X direction)
/// - Hash functions use position + frame, not random seeds
/// - No floating-point non-determinism (all physics uses integer/fixed-point math)
///
/// These tests verify full cell state (materialId, velocity, temperature, etc.),
/// not just material positions.
/// </summary>
public class DeterminismTests
{
    [Fact]
    public void SameSetup_ProducesIdenticalState()
    {
        byte[] state1 = RunScenario();
        byte[] state2 = RunScenario();

        Assert.Equal(state1.Length, state2.Length);
        for (int i = 0; i < state1.Length; i++)
            Assert.True(state1[i] == state2[i],
                $"States diverged at cell index {i / 11} (byte offset {i % 11})");
    }

    [Fact]
    public void ComplexScenario_Deterministic()
    {
        byte[] state1 = RunComplexScenario();
        byte[] state2 = RunComplexScenario();

        Assert.Equal(state1.Length, state2.Length);
        for (int i = 0; i < state1.Length; i++)
            Assert.True(state1[i] == state2[i],
                $"Complex scenario diverged at cell index {i / 11} (byte offset {i % 11})");
    }

    [Fact]
    public void Determinism_MultipleRuns_AllIdentical()
    {
        // Run the same scenario 5 times and verify all produce identical results
        byte[][] states = new byte[5][];
        for (int run = 0; run < 5; run++)
            states[run] = RunScenario();

        for (int run = 1; run < 5; run++)
        {
            Assert.Equal(states[0].Length, states[run].Length);
            for (int i = 0; i < states[0].Length; i++)
                Assert.True(states[0][i] == states[run][i],
                    $"Run {run} diverged from run 0 at cell index {i / 11} (byte offset {i % 11})");
        }
    }

    private static byte[] RunScenario()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand and water blocks dropped onto a stone floor should produce byte-identical final cell state on every run.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        for (int x = 30; x < 50; x++)
            for (int y = 0; y < 10; y++)
                sim.Set(x, y, Materials.Sand);

        for (int x = 60; x < 80; x++)
            for (int y = 0; y < 5; y++)
                sim.Set(x, y, Materials.Water);

        sim.Step(300);
        return SnapshotCells(sim.World);
    }

    private static byte[] RunComplexScenario()
    {
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "A complex scene with stone barriers, alternating sand/water layers, and steam should produce byte-identical final state on every run.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);
        sim.Fill(30, 100, 1, 20, Materials.Stone);
        sim.Fill(60, 80, 30, 1, Materials.Stone);

        for (int x = 40; x < 55; x++)
            for (int y = 0; y < 8; y++)
                sim.Set(x, y, (y % 2 == 0) ? Materials.Sand : Materials.Water);

        sim.Set(50, 30, Materials.Steam);
        sim.Set(51, 30, Materials.Steam);
        sim.Set(52, 30, Materials.Steam);

        sim.Step(500);
        return SnapshotCells(sim.World);
    }

    private static byte[] SnapshotCells(CellWorld world)
    {
        int cellSize = 11;
        byte[] snapshot = new byte[world.cells.Length * cellSize];
        for (int i = 0; i < world.cells.Length; i++)
        {
            var cell = world.cells[i];
            int offset = i * cellSize;
            snapshot[offset + 0] = cell.materialId;
            snapshot[offset + 1] = cell.flags;
            snapshot[offset + 2] = (byte)cell.velocityX;
            snapshot[offset + 3] = (byte)cell.velocityY;
            snapshot[offset + 4] = cell.temperature;
            snapshot[offset + 5] = cell.structureId;
            snapshot[offset + 6] = (byte)(cell.ownerId & 0xFF);
            snapshot[offset + 7] = (byte)(cell.ownerId >> 8);
            snapshot[offset + 8] = cell.velocityFracX;
            snapshot[offset + 9] = cell.velocityFracY;
            snapshot[offset + 10] = cell.frameUpdated;
        }
        return snapshot;
    }
}
