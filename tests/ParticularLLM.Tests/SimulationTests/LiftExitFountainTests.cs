using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for lift exit and fountain behavior.
///
/// English rules (derived from SimulateChunksLogic lines 141-256):
///
/// 1. Inside a lift, net force = gravity(17) - liftForce(20) = -3 per frame (upward).
/// 2. Fractional accumulator underflows at frame ~86 (ceil(256/3)), giving velocityY = -1.
///    But zero-velocity gravity pull also moves material up 1 cell/frame inside lift
///    (because CanMoveTo checks density displacement into lift tiles which are Passable).
///    Actually, zero-velocity pull moves DOWN, so in a lift the upward movement relies on
///    the fractional accumulator eventually giving negative velocity.
///    Net -3/frame: underflow at frame ~86 (256/3 = 85.3), then velocityY = -1.
///    Wait — the accumulator starts at 0, adds -3 each frame. -3*86 = -258 < -256.
///    Actually: newFracY = cell.velocityFracY + netForce. netForce = 17-20 = -3.
///    Frame 1: newFracY = 0 + (-3) = -3 → underflow → velocityFracY = 253, velocityY = -1
///    So velocity becomes -1 on the VERY FIRST FRAME inside a lift!
///
/// 3. At the exit row (top of lift, where row above has no lift tile):
///    lateralSign = (2 * localX - 7) — maps localX 0..7 to -7,-5,-3,-1,+1,+3,+5,+7
///    lateralForceValue = lateralSign * liftExitLateralForce (120)
///    This creates a fountain pattern: left side pushes left, right side pushes right.
///
/// 4. After exiting the lift, material should spread horizontally via velocityX.
///    BUG: Phase 1 vertical movement always returns after moving, so velocityX is never
///    consumed during free-fall. Material exits in a straight column and falls back in.
///
/// 5. MoveCell restores lift material when a cell exits a lift tile position.
///
/// 6. Material conservation must hold throughout lift operations.
///
/// Known bugs being tested:
/// - "Material falls back into lift" — oscillation loop, no horizontal dispersal
/// - "Lift fountain lateral force no effect" — velocityX set but never consumed in free-fall
/// </summary>
public class LiftExitFountainTests
{
    // ===== BASIC LIFT FORCE =====

    [Fact]
    public void Lift_NetUpwardForce_FirstFrame()
    {
        // Net force = gravity(17) - liftForce(20) = -3
        // Frame 1: fracY = 0 + (-3) = -3 → underflow → velocityY = -1 immediately
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 80); // rows 80-87
        sim.Simulator.SetLiftManager(lifts);

        // Place sand inside lift
        sim.Set(36, 84, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1, counts);

        Cell cell = sim.World.cells[0]; // placeholder
        // Find the sand cell
        for (int i = 0; i < sim.World.cells.Length; i++)
        {
            if (sim.World.cells[i].materialId == Materials.Sand)
            {
                cell = sim.World.cells[i];
                break;
            }
        }
        // After 1 frame, velocity should be -1 (upward)
        Assert.Equal(-1, cell.velocityY);
    }

    [Fact]
    public void Lift_SandRisesToTopAndExits()
    {
        // Sand placed at bottom of lift should rise to the top and exit via lateral force.
        // After exiting, sand moves horizontally and then falls in open space.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 64); // rows 64-71
        lifts.PlaceLift(32, 72); // rows 72-79
        lifts.PlaceLift(32, 80); // rows 80-87
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(36, 85, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();

        // Check early (20 frames): sand should be rising inside the lift
        sim.StepWithInvariants(20, counts);
        var earlyPos = sim.FindMaterial(Materials.Sand);
        Assert.Single(earlyPos);
        Assert.True(earlyPos[0].y < 85,
            $"Sand should be rising in lift after 20 frames, but at y={earlyPos[0].y}");

        // Run longer: sand should exit lift and land on floor
        sim.StepWithInvariants(480, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should have exited the lift column (x=32..39) via lateral force
        bool outsideLiftColumn = pos[0].x < 32 || pos[0].x > 39;
        Assert.True(outsideLiftColumn || pos[0].y >= 110,
            $"Sand should exit lift horizontally or settle on floor, but at ({pos[0].x},{pos[0].y})\n" +
            WorldDump.DumpRegion(sim.World, 20, 55, 40, 75));
    }

    // ===== LATERAL EXIT FORCE =====

    [Fact]
    public void Lift_ExitRow_SetsLateralVelocity()
    {
        // At exit row, lateralForceValue is applied to velocityFracX.
        // For localX=0: lateralSign = (2*0-7) = -7, force = -7*120 = -840
        // newFracX = 0 + (-840) = -840 → underflows multiple times
        // For localX=7: lateralSign = (2*7-7) = +7, force = +7*120 = +840
        // These are large forces that should give significant velocityX.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(32, 64); // rows 64-71
        sim.Simulator.SetLiftManager(lifts);

        // Place sand at exit row (y=64 is top of lift, row above y=63 has no lift)
        sim.Set(32, 64, Materials.Sand); // localX = 0, leftmost → should push left
        var counts = sim.SnapshotMaterialCounts();

        sim.StepWithInvariants(1, counts);

        // Find sand and check velocityX
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);

        Cell cell = sim.GetCell(pos[0].x, pos[0].y);
        // localX=0: force = -7*120 = -840. This causes multiple underflows:
        // -840 / 256 = 3.28, so velocityX should be at least -3
        Assert.True(cell.velocityX < 0,
            $"Sand at left side of lift exit should have negative velocityX, got {cell.velocityX}");
    }

    // ===== FOUNTAIN BEHAVIOR (BUG TESTS) =====

    [Fact]
    public void Lift_FountainEffect_MaterialSpreadsHorizontally()
    {
        // After exiting the lift, material should spread horizontally due to lateral force.
        // BUG: Currently, velocityX is set but never consumed during free-fall.
        // Material should NOT fall straight back into the lift.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        // 4-block tall lift
        lifts.PlaceLift(48, 64);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        // Floor to catch material
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand at bottom of lift
        sim.Set(50, 90, Materials.Sand);
        sim.Set(54, 90, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        // After exiting lift, sand should have spread horizontally,
        // NOT be stuck oscillating inside the lift
        var positions = sim.FindMaterial(Materials.Sand);

        // At least one sand grain should be outside the lift column (x=48..55)
        bool anyOutside = false;
        foreach (var (x, y) in positions)
        {
            if (x < 48 || x > 55)
            {
                anyOutside = true;
                break;
            }
        }
        Assert.True(anyOutside,
            $"Sand should spread horizontally after exiting lift, but all sand is in lift column\n" +
            WorldDump.DumpRegion(sim.World, 40, 55, 30, 75));
    }

    [Fact]
    public void Lift_NoOscillationLoop()
    {
        // Material should NOT bounce up and down inside a lift forever.
        // After enough frames, material should exit the lift and settle.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(52, 86, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);

        // Sand should have settled somewhere — NOT still oscillating in the lift
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);

        // Sand should be resting on the stone floor, NOT inside the lift zone
        Assert.True(pos[0].y >= 110,
            $"Sand should settle on floor after exiting lift, but is at y={pos[0].y}\n" +
            WorldDump.DumpRegion(sim.World, 40, 60, 30, 70));
    }

    [Fact]
    public void Lift_FountainSymmetry_LeftAndRight()
    {
        // The fountain should push material left on the left side and right on the right side.
        // lateralSign for localX=0..3 is negative (left), for localX=4..7 is positive (right).
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand on both sides of the lift center
        sim.Set(49, 86, Materials.Sand);  // Left side (localX=1, pushes left)
        sim.Set(54, 86, Materials.Sand);  // Right side (localX=6, pushes right)

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var positions = sim.FindMaterial(Materials.Sand);
        Assert.Equal(2, positions.Count);

        // Sort by x to identify left and right grains
        positions.Sort((a, b) => a.x.CompareTo(b.x));

        int liftCenterX = 51; // center of 48..55
        // Left grain should have moved left of lift center
        Assert.True(positions[0].x < liftCenterX,
            $"Left-side sand should be pushed left of center ({liftCenterX}), but is at x={positions[0].x}");
        // Right grain should have moved right of lift center
        Assert.True(positions[1].x > liftCenterX,
            $"Right-side sand should be pushed right of center ({liftCenterX}), but is at x={positions[1].x}");
    }

    // ===== VELOCITYX CONSUMPTION DURING FREE-FALL =====

    [Fact]
    public void Lift_VelocityX_ConsumedDuringRise()
    {
        // When material rises with both velocityY < 0 and velocityX != 0,
        // the horizontal velocity should be applied — material should move diagonally up,
        // not straight up.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        // Place sand at exit row
        sim.Set(48, 72, Materials.Sand); // leftmost (localX=0), strong push left
        var counts = sim.SnapshotMaterialCounts();

        // Run enough frames for sand to exit and rise above lift
        sim.StepWithInvariants(30, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);

        // Sand should have moved left (velocityX < 0) during its rise above the lift
        Assert.True(pos[0].x < 48,
            $"Sand exiting left side of lift should move left during rise, but is at x={pos[0].x}\n" +
            WorldDump.DumpRegion(sim.World, 35, 55, 30, 30));
    }

    // ===== MULTI-PARTICLE FOUNTAIN =====

    [Fact]
    public void Lift_MultipleSand_AllExit()
    {
        // Multiple sand grains should all eventually exit the lift.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 64);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place 5 sand grains at various heights in the lift
        sim.Set(50, 90, Materials.Sand);
        sim.Set(51, 85, Materials.Sand);
        sim.Set(52, 80, Materials.Sand);
        sim.Set(53, 75, Materials.Sand);
        sim.Set(54, 70, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(1000, counts);

        // All sand should have settled on the floor, not stuck in the lift
        var positions = sim.FindMaterial(Materials.Sand);
        foreach (var (x, y) in positions)
        {
            Assert.True(y >= 110,
                $"All sand should exit lift and reach floor, but found sand at ({x},{y})");
        }
    }

    // ===== LIQUID THROUGH LIFT =====

    [Fact]
    public void Lift_WaterExitsAndSpreads()
    {
        // Water should exit the lift and spread horizontally on landing.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.Set(52, 86, Materials.Water);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        // Water should have exited the lift (not stuck inside)
        var pos = sim.FindMaterial(Materials.Water);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 100,
            $"Water should exit lift and reach floor area, but is at y={pos[0].y}\n" +
            WorldDump.DumpRegion(sim.World, 40, 60, 30, 70));
    }

    // ===== LIFT TILE RESTORATION =====

    [Fact]
    public void Lift_TilesRestoredAfterMaterialPasses()
    {
        // After sand passes through a lift, all lift tiles should be restored.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 80); // rows 80-87
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place sand inside the lift
        sim.Set(52, 84, Materials.Sand);
        var counts = sim.SnapshotMaterialCounts();

        sim.StepWithInvariants(500, counts);

        // Count lift material in the lift zone — should be fully restored
        int liftMaterialCount = 0;
        for (int dy = 0; dy < 8; dy++)
        {
            for (int dx = 0; dx < 8; dx++)
            {
                byte mat = sim.Get(48 + dx, 80 + dy);
                if (Materials.IsLift(mat))
                    liftMaterialCount++;
            }
        }
        // All 64 cells in the 8x8 block should have lift material
        Assert.Equal(64, liftMaterialCount);
    }

    // ===== MATERIAL CONSERVATION STRESS =====

    [Fact]
    public void Lift_ConservationStress_ManySandThroughLift()
    {
        // Pour many sand grains through a lift. All should be conserved.
        using var sim = new SimulationFixture(128, 128);
        var lifts = new LiftManager(sim.World);
        lifts.PlaceLift(48, 64);
        lifts.PlaceLift(48, 72);
        lifts.PlaceLift(48, 80);
        lifts.PlaceLift(48, 88);
        sim.Simulator.SetLiftManager(lifts);

        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Place a block of sand below the lift (it will fall into the lift and rise)
        int sandCount = 0;
        for (int x = 48; x < 56; x++)
        {
            for (int y = 96; y < 100; y++)
            {
                sim.Set(x, y, Materials.Sand);
                sandCount++;
            }
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(2000, counts);
    }
}
