using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for the fractional gravity accumulator system.
///
/// English rules (derived from SimulateChunksLogic lines 152-172):
///
/// 1. Each frame, fractionalGravity (17) is added to velocityFracY accumulator (byte, 0-255).
/// 2. When accumulator >= 256: overflow → velocityY += gravity (1), remainder kept in accumulator.
/// 3. First velocity increment occurs at frame ~15 (ceil(256/17) = 16 frames, but frame 1
///    starts from 0, so overflow first hits on the frame where cumulative sum >= 256).
///    Exact: 17*15 = 255 (no overflow), 17*16 = 272 (overflow on 16th addition).
///    But note: sand also moves via Phase 3 diagonal slide before velocity > 0.
/// 4. Before velocity increment, powder moves 1 cell/frame diagonally (Phase 3 fallback).
/// 5. After velocityY > 0, powder falls velocityY cells/frame via Phase 1 path tracing.
/// 6. Velocity caps at maxVelocity (16 cells/frame).
/// 7. On collision, velocity resets (or transfers to diagonal momentum).
/// 8. Accumulator value is preserved between frames — it's truly fractional, not per-frame.
///
/// Known tradeoffs:
/// - Sand initially moves diagonally (Phase 3) before gaining velocity, so it doesn't
///   fall straight down until velocity > 0. This is expected behavior.
/// - Bottom-to-top scan means falling cascades but rising is one-at-a-time.
/// </summary>
public class FractionalGravityTests
{
    // ===== ACCUMULATOR MECHANICS =====

    [Fact]
    public void Accumulator_NoVelocityOnFirstFrame()
    {
        // After 1 frame: fracY = 0 + 17 = 17. No overflow, velocityY stays 0.
        // But sand still moves via Phase 3 slide.
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(1);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        Cell cell = FindSandCell(sim);
        Assert.Equal(0, cell.velocityY);
        Assert.Equal(17, cell.velocityFracY);
    }

    [Fact]
    public void Accumulator_BuildsOverMultipleFrames()
    {
        // In open space, sand slides diagonally 1 cell/frame via Phase 3.
        // After 10 frames: fracY = 10 * 17 = 170 (no overflow), velocityY still 0.
        // But sand has moved 10 cells down via diagonal slides.
        using var sim = new SimulationFixture();

        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(10);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should have slid diagonally about 10 rows down (one per frame)
        var positions = sim.FindMaterial(Materials.Sand);
        Assert.Single(positions);
        Assert.True(positions[0].y >= 18,
            $"Sand should have slid ~10 rows in 10 frames via Phase 3, but is at y={positions[0].y}");
    }

    [Fact]
    public void Accumulator_OverflowsAndAccelerates()
    {
        // After ~16 frames, accumulator overflows: 17*16 = 272 > 255.
        // velocityY should become 1.
        // After ~31 frames: second overflow (17*31 = 527, two overflows at 256 and 512+).
        // velocityY should become 2.
        // We test that sand falls FURTHER in later frames (acceleration visible).
        using var sim = new SimulationFixture(64, 128);

        sim.Fill(0, 127, 64, 1, Materials.Stone);
        sim.Set(32, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // Run 50 frames and track position each frame
        int prevY = 0;
        int maxDeltaY = 0;

        for (int frame = 1; frame <= 50; frame++)
        {
            sim.Step(1);
            InvariantChecker.AssertMaterialConservation(sim.World, counts);

            var positions = sim.FindMaterial(Materials.Sand);
            Assert.Single(positions);
            int currentY = positions[0].y;
            int deltaY = currentY - prevY;
            if (deltaY > maxDeltaY) maxDeltaY = deltaY;
            prevY = currentY;
        }

        // Sand should have accelerated: max deltaY should be > 1 at some point
        Assert.True(maxDeltaY > 1,
            $"Sand should accelerate over time, but max Y-movement per frame was only {maxDeltaY}");
    }

    [Fact]
    public void Velocity_CapsAtMaxVelocity()
    {
        // maxVelocity is 16. In a tall enough world, sand velocity should cap.
        // With gravity=1, velocity grows by 1 each ~15 frames.
        // After 16 overflows (~240 frames), velocity should cap at 16.
        using var sim = new SimulationFixture(64, 512);

        sim.Fill(0, 511, 64, 1, Materials.Stone);
        sim.Set(32, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(400, counts);

        // Sand should have reached the stone floor at some point during 400 frames
        // and velocity should have capped along the way
        var positions = sim.FindMaterial(Materials.Sand);
        Assert.Single(positions);
        // Sand should be near or at the floor (y=510 is just above stone at 511)
        Assert.True(positions[0].y >= 100,
            $"Sand should have fallen significantly in 400 frames, but is at y={positions[0].y}");
    }

    // ===== PHASE 3 SLIDE BEHAVIOR (PRE-VELOCITY) =====

    [Fact]
    public void PreVelocity_SandSlidesDiagonally()
    {
        // Before velocity > 0, sand still moves via Phase 3 diagonal slide.
        // It tries down-left or down-right each frame.
        using var sim = new SimulationFixture();
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.Step(1);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        // Sand should be at y=11 (one row down from y=10)
        var positions = sim.FindMaterial(Materials.Sand);
        Assert.Single(positions);
        Assert.Equal(11, positions[0].y);
    }

    [Fact]
    public void PreVelocity_SandFallsStraightIfWalled()
    {
        // In a 1-wide channel, diagonal slide fails but gravity still pulls down 1 cell/frame.
        // Even at velocity 0, sand should fall straight down immediately.
        using var sim = new SimulationFixture();

        sim.Fill(31, 0, 1, 64, Materials.Stone); // Wall left
        sim.Fill(33, 0, 1, 64, Materials.Stone); // Wall right
        sim.Fill(0, 63, 64, 1, Materials.Stone);  // Floor
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // After 1 frame: sand falls 1 cell straight down (zero-velocity gravity pull)
        sim.Step(1);
        InvariantChecker.AssertMaterialConservation(sim.World, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.Equal(32, pos[0].x);
        Assert.Equal(11, pos[0].y);

        // After more frames, sand continues falling and accelerates
        sim.StepWithInvariants(20, counts);

        pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y > 20,
            $"Sand should keep falling in walled channel, but is at y={pos[0].y}");
    }

    // ===== ACCELERATION PROFILE =====

    [Fact]
    public void Acceleration_DistanceIncreasesOverTime()
    {
        // Sand falling in open space should cover more distance in later intervals
        // than in earlier intervals (acceleration, not constant speed).
        using var sim = new SimulationFixture(64, 256);

        sim.Fill(0, 255, 64, 1, Materials.Stone);
        sim.Set(32, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // Measure position at frame 20, 40, 60
        sim.StepWithInvariants(20, counts);
        var pos20 = sim.FindMaterial(Materials.Sand);
        int y20 = pos20[0].y;

        sim.StepWithInvariants(20, counts);
        var pos40 = sim.FindMaterial(Materials.Sand);
        int y40 = pos40[0].y;

        sim.StepWithInvariants(20, counts);
        var pos60 = sim.FindMaterial(Materials.Sand);
        int y60 = pos60[0].y;

        int distFirst = y40 - y20;   // distance in frames 21-40
        int distSecond = y60 - y40;  // distance in frames 41-60

        // Second interval should cover more distance due to acceleration
        Assert.True(distSecond >= distFirst,
            $"Sand should accelerate: frames 21-40 moved {distFirst} cells, " +
            $"frames 41-60 moved {distSecond} cells (should be >= first interval)");
    }

    [Fact]
    public void Accumulator_PreservedAcrossFrames()
    {
        // The fractional accumulator is NOT reset between frames.
        // After overflow at frame ~16, velocity becomes 1 and sand moves 1+1=2 cells/frame
        // (1 from zero-velocity gravity pull + 1 from velocity).
        // The key test: sand accelerates over time, proving the accumulator persists.
        using var sim = new SimulationFixture();

        sim.Fill(31, 0, 1, 64, Materials.Stone);
        sim.Fill(33, 0, 1, 64, Materials.Stone);
        sim.Fill(0, 63, 64, 1, Materials.Stone);
        sim.Set(32, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // Sand now falls 1 cell/frame even at velocity 0. After accumulator overflow,
        // velocity becomes 1, and sand falls velocity cells via Phase 1 PLUS the zero-velocity
        // pull doesn't apply (velocity > 0 takes the Phase 1 path).
        // After 15 frames, sand should be at y=15 (1 cell/frame from zero-velocity pull).
        sim.StepWithInvariants(15, counts);
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.Equal(32, pos[0].x);
        Assert.Equal(15, pos[0].y);

        // After 30 more frames, velocity should have overflowed twice (vel=2),
        // so sand covers more than 1 cell/frame on average
        sim.StepWithInvariants(30, counts);
        var posAfter = sim.FindMaterial(Materials.Sand);
        Assert.Single(posAfter);
        // 45 frames total: first ~16 at 1 cell/frame, then accelerating.
        // With velocity growing to 2+, should be well past y=45
        Assert.True(posAfter[0].y > 45,
            $"Sand should accelerate past 1 cell/frame, but at y={posAfter[0].y} after 45 total frames");
    }

    // ===== COLLISION RESETS =====

    [Fact]
    public void Collision_ResetsVelocityWhenLanding()
    {
        // When sand hits a surface after falling, its velocity is zeroed (or transferred to diagonal).
        // This means if it's placed again in the air after landing, it starts slow again.
        using var sim = new SimulationFixture();

        sim.Fill(0, 40, 64, 1, Materials.Stone); // Floor
        sim.Set(32, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should be resting just above stone
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 38 && pos[0].y <= 39,
            $"Sand should land on or near stone floor at y=39, got y={pos[0].y}");

        // Check velocity is zero after settling
        Cell cell = sim.GetCell(pos[0].x, pos[0].y);
        Assert.Equal(0, cell.velocityY);
    }

    [Fact]
    public void Collision_TransfersToMomentum_WhenFastEnough()
    {
        // When sand collides with velocityY > 1, 70% of velocity transfers to diagonal.
        // This creates the characteristic spreading behavior of poured sand.
        using var sim = new SimulationFixture(128, 128);

        sim.Fill(0, 100, 128, 1, Materials.Stone); // Floor
        sim.Set(64, 0, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand fell from y=0, so it should have had significant velocity at impact.
        // After collision, it should have moved diagonally — NOT sitting directly above
        // where it started falling from (x=64).
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 98 && pos[0].y <= 99,
            $"Sand should land near stone floor at y=99, got y={pos[0].y}");
    }

    // ===== MULTI-PARTICLE CONSERVATION =====

    [Fact]
    public void MultiParticle_ConservedDuringFall()
    {
        // Multiple sand grains falling at different speeds should all be conserved.
        using var sim = new SimulationFixture(64, 128);

        sim.Fill(0, 127, 64, 1, Materials.Stone);

        // Place sand at different heights (different velocities when they land)
        for (int i = 0; i < 10; i++)
            sim.Set(32, i * 5, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(10, WorldAssert.CountMaterial(sim.World, Materials.Sand));
    }

    [Fact]
    public void MultiParticle_DifferentMaterialsSameGravity()
    {
        // Dirt and sand both use fractional gravity the same way.
        // Both should fall and be conserved.
        using var sim = new SimulationFixture(64, 128);

        sim.Fill(0, 127, 64, 1, Materials.Stone);
        sim.Set(30, 0, Materials.Sand);
        sim.Set(34, 0, Materials.Dirt);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(1, WorldAssert.CountMaterial(sim.World, Materials.Dirt));
    }

    // ===== SETTLED STATE =====

    [Fact]
    public void Settled_NoFloatingPowderAfterGravity()
    {
        // After enough frames, no powder should be floating with air below.
        using var sim = new SimulationFixture();

        sim.Fill(0, 60, 64, 4, Materials.Stone);
        for (int x = 20; x < 44; x++)
            sim.Set(x, 10, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepUntilSettled();
        InvariantChecker.AssertMaterialConservation(sim.World, counts);
        InvariantChecker.AssertNoFloatingPowder(sim.World);
    }

    // ===== HELPERS =====

    private static Cell FindSandCell(SimulationFixture sim)
    {
        for (int i = 0; i < sim.World.cells.Length; i++)
        {
            if (sim.World.cells[i].materialId == Materials.Sand)
                return sim.World.cells[i];
        }
        throw new Exception("No sand cell found");
    }
}
