using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Edge case tests for processing order.
///
/// English rules:
///
/// 1. frameUpdated prevents double-processing within same frame.
///    When a cell moves to a new position, its frameUpdated is set to currentFrame.
///    SimulateCell skips cells where frameUpdated == currentFrame.
/// 2. Bottom-to-top scan means falling cascades (multiple grains move per frame)
///    but rising is one-at-a-time.
/// 3. Alternating X direction per row ensures no systematic horizontal bias.
/// 4. 4-pass and flat modes produce different but both valid results.
///    Both must conserve materials and be individually deterministic.
/// </summary>
public class ProcessingOrderEdgeCases
{
    // ===== FRAME UPDATED =====

    [Fact]
    public void FrameUpdated_PreventsDoubleProcessing()
    {
        // A sand grain that moves from position A to position B in the same frame
        // should NOT be processed again at position B.
        // We verify this by checking that sand doesn't fall more than maxVelocity
        // cells per frame.
        using var sim = new SimulationFixture(64, 512);
        sim.Description = "A single sand grain should not fall more than its velocity allows per frame, verifying that frameUpdated prevents double-processing after a cell moves.";
        sim.Fill(0, 511, 64, 1, Materials.Stone);
        sim.Set(32, 0, Materials.Sand);

        // After 1 frame, sand should move at most 1 cell (velocity starts at 0,
        // zero-velocity pull moves 1 cell)
        sim.Step(1);
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y <= 1,
            $"Sand should move at most 1 cell in first frame, but at y={pos[0].y}");

        // After 50 more frames, even with acceleration, sand should not have
        // fallen more than sum of velocities (max 16 cells/frame)
        sim.Step(50);
        pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Rough upper bound: 1 + 1 + 1 + ... + 4 (velocity grows slowly via fractional accumulator)
        // In 51 total frames: velocity grows to ~3 at most, total distance < 150
        Assert.True(pos[0].y < 200,
            $"Sand fell too far ({pos[0].y}) — possible double-processing");
    }

    // ===== DETERMINISM =====

    [Fact]
    public void FlatMode_Deterministic()
    {
        // Same initial state should produce same result in flat mode.
        int y1 = RunAndGetFinalY(useFourPass: false);
        int y2 = RunAndGetFinalY(useFourPass: false);
        Assert.Equal(y1, y2);
    }

    [Fact]
    public void FourPassMode_Deterministic()
    {
        int y1 = RunAndGetFinalY(useFourPass: true);
        int y2 = RunAndGetFinalY(useFourPass: true);
        Assert.Equal(y1, y2);
    }

    [Fact]
    public void BothModes_ConserveMaterial()
    {
        // Both modes must conserve material even if final positions differ.
        for (int mode = 0; mode < 2; mode++)
        {
            using var sim = new SimulationFixture(128, 128);
            sim.Description = $"A row of 28 sand grains should be fully conserved after settling in {(mode == 1 ? "4-pass" : "flat")} processing mode.";
            sim.Simulator.UseFourPassGrouping = mode == 1;

            sim.Fill(0, 127, 128, 1, Materials.Stone);
            for (int x = 50; x < 78; x++)
                sim.Set(x, 10, Materials.Sand);

            var counts = sim.SnapshotMaterialCounts();
            sim.StepWithInvariants(300, counts);

            Assert.Equal(28, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        }
    }

    // ===== FALLING CASCADE (BOTTOM-TO-TOP) =====

    [Fact]
    public void BottomToTop_FallingCascades()
    {
        // With bottom-to-top scan, a column of sand should cascade:
        // bottom grain falls first, then next grain falls into vacated spot.
        // After 1 frame, all grains in a column should advance by 1.
        using var sim = new SimulationFixture(64, 64);
        sim.Description = "A vertical column of 5 sand grains should all cascade down by 1 cell in a single frame due to bottom-to-top processing.";
        sim.Fill(0, 63, 64, 1, Materials.Stone);

        // Stack 5 sand vertically with air below
        for (int i = 0; i < 5; i++)
            sim.Set(32, 20 + i, Materials.Sand);

        sim.Step(1);

        // After 1 frame, all 5 grains should have moved down by 1
        // (bottom grain at y=24 falls to y=25, freeing y=24 for the next, etc.)
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));

        // All sand should be in y=21..25 (shifted down by 1)
        var positions = sim.FindMaterial(Materials.Sand);
        foreach (var (x, y) in positions)
        {
            Assert.True(y >= 21 && y <= 25,
                $"Sand should cascade down by 1, but found at y={y}");
        }
    }

    // ===== CONSERVATION WITH COMPLEX SCENARIOS =====

    [Fact]
    public void Conservation_MixedMaterials_BothModes()
    {
        for (int mode = 0; mode < 2; mode++)
        {
            using var sim = new SimulationFixture(128, 128);
            sim.Description = $"Mixed sand and water should both be fully conserved after settling in {(mode == 1 ? "4-pass" : "flat")} processing mode.";
            sim.Simulator.UseFourPassGrouping = mode == 1;

            sim.Fill(0, 120, 128, 8, Materials.Stone);
            sim.Fill(50, 50, 10, 5, Materials.Sand);
            sim.Fill(50, 60, 10, 5, Materials.Water);

            var counts = sim.SnapshotMaterialCounts();
            sim.StepWithInvariants(300, counts);

            Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Sand));
            Assert.Equal(50, WorldAssert.CountMaterial(sim.World, Materials.Water));
        }
    }

    // ===== HELPERS =====

    private static int RunAndGetFinalY(bool useFourPass)
    {
        using var sim = new SimulationFixture(64, 128);
        sim.Description = $"A single sand grain dropped from y=10 should settle to the same final Y position every run in {(useFourPass ? "4-pass" : "flat")} mode.";
        sim.Simulator.UseFourPassGrouping = useFourPass;
        sim.Fill(0, 127, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);
        sim.Step(100);
        var pos = sim.FindMaterial(Materials.Sand);
        return pos[0].y;
    }
}
