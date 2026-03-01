using ParticularLLM;
using ParticularLLM.Tests.Helpers;

namespace ParticularLLM.Tests.SimulationTests;

/// <summary>
/// Tests for powder horizontal velocity decay (air drag).
///
/// English rules:
///
/// 1. Powder with horizontal velocity gradually loses speed due to air drag.
///    Each frame, there is an airDrag/256 probability of losing 1 unit of velocityX.
/// 2. Higher airDrag values (lighter materials like ash) decay faster than lower values
///    (heavier materials like iron ore).
/// 3. Powder at rest (velocityX = 0) is unaffected by air drag.
/// 4. Air drag only affects horizontal velocity — vertical is governed by gravity.
/// 5. Material conservation is preserved through all drag operations.
/// 6. After enough frames, horizontal velocity should decay to zero and the
///    material should settle rather than flying sideways indefinitely.
/// </summary>
public class PowderAirDragTests
{
    // ===== BASIC DRAG BEHAVIOR =====

    [Fact]
    public void Sand_HorizontalVelocity_DecaysOverTime()
    {
        // Sand launched horizontally should eventually lose its horizontal velocity
        // and settle, rather than flying sideways forever.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Sand launched horizontally with vx=8 should lose its horizontal velocity to air drag and settle on the floor.";
        sim.Fill(0, 120, 128, 8, Materials.Stone); // Floor

        // Place sand with high horizontal velocity in open air
        sim.SetWithVelocity(20, 60, Materials.Sand, 8, 0);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(200, counts);

        // Sand should have settled on the floor (not still flying sideways)
        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        Assert.True(pos[0].y >= 115,
            $"Sand should settle on floor after drag, but is at y={pos[0].y}");
    }

    [Fact]
    public void Sand_HorizontalSpread_IsFinite()
    {
        // Without air drag, sand with vx=8 would fly 8 cells per frame forever.
        // With drag, the total horizontal displacement should be bounded.
        using var sim = new SimulationFixture(256, 128);
        sim.Description = "Sand launched horizontally should have bounded total horizontal displacement due to air drag, not flying to the edge of the world.";
        sim.Fill(0, 120, 256, 8, Materials.Stone); // Wide floor

        sim.SetWithVelocity(30, 60, Materials.Sand, 8, 0);
        int startX = 30;

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        int displacement = pos[0].x - startX;

        // Without drag, 8 cells/frame * 60 frames falling ≈ 480 cells.
        // With drag, displacement is bounded. The exact value is stochastic but
        // includes multiple bounces off the floor (restitution adds scatter velocity).
        Assert.True(displacement < 250,
            $"Horizontal displacement should be bounded by drag, got {displacement} cells");
        Assert.True(displacement > 0,
            $"Sand should have moved at least somewhat rightward, got {displacement}");
    }

    // ===== MATERIAL-DEPENDENT DRAG =====

    [Fact]
    public void Ash_DecaysFaster_ThanSand()
    {
        // Ash (airDrag=50) should lose horizontal velocity faster than sand (airDrag=25).
        // Both launched with same horizontal velocity, ash should travel less far.
        using var sim = new SimulationFixture(256, 128);
        sim.Description = "Ash with higher air drag should travel less far horizontally than sand when both are launched at the same speed.";
        sim.Fill(0, 120, 256, 8, Materials.Stone);

        // Launch both with same horizontal velocity, same height
        sim.SetWithVelocity(30, 60, Materials.Sand, 8, 0);
        sim.SetWithVelocity(30, 40, Materials.Ash, 8, 0);  // Different y to avoid collision

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var sandPos = sim.FindMaterial(Materials.Sand);
        var ashPos = sim.FindMaterial(Materials.Ash);
        Assert.Single(sandPos);
        Assert.Single(ashPos);

        int sandDisplacement = sandPos[0].x - 30;
        int ashDisplacement = ashPos[0].x - 30;

        // Ash should travel less far due to higher drag
        Assert.True(ashDisplacement < sandDisplacement,
            $"Ash (drag=50) should travel less than sand (drag=25). " +
            $"Ash: {ashDisplacement}, Sand: {sandDisplacement}");
    }

    [Fact]
    public void HigherDrag_DecaysFaster_SameRestitution()
    {
        // Test drag directly: two sand grains with the same initial velocity.
        // One in a world with default airDrag, one with artificially high drag.
        // We can't easily change per-material drag in a test, so instead:
        // Compare ash (airDrag=50) vs coal (airDrag=25) which have similar restitution
        // (ash=26, coal=40) — close enough that drag difference dominates.
        // Both launched horizontally at the same speed.
        using var sim = new SimulationFixture(256, 128);
        sim.Description = "Ash (airDrag=50) launched at the same speed as coal (airDrag=25) should travel less far, since higher drag decays velocity faster.";
        sim.Fill(0, 120, 256, 8, Materials.Stone);

        // Place at different Y to avoid interaction, same vx
        sim.SetWithVelocity(30, 50, Materials.Coal, 8, 0);  // airDrag=25
        sim.SetWithVelocity(30, 70, Materials.Ash, 8, 0);   // airDrag=50

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        var coalPos = sim.FindMaterial(Materials.Coal);
        var ashPos = sim.FindMaterial(Materials.Ash);
        Assert.Single(coalPos);
        Assert.Single(ashPos);

        int coalDisplacement = coalPos[0].x - 30;
        int ashDisplacement = ashPos[0].x - 30;

        // Ash (higher drag) should travel less far than coal (lower drag)
        Assert.True(ashDisplacement < coalDisplacement,
            $"Ash (drag=50) should travel less than coal (drag=25). " +
            $"Ash: {ashDisplacement}, Coal: {coalDisplacement}");
    }

    // ===== AT-REST BEHAVIOR =====

    [Fact]
    public void Sand_AtRest_UnaffectedByDrag()
    {
        // Sand sitting on a surface with no horizontal velocity should not be
        // affected by drag. It should remain stationary.
        using var sim = new SimulationFixture(64, 64);
        sim.Description = "Sand resting on a surface with zero horizontal velocity should not drift, since air drag only acts on moving particles.";
        sim.Fill(0, 50, 64, 14, Materials.Stone);
        sim.Set(32, 49, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(100, counts);

        var pos = sim.FindMaterial(Materials.Sand);
        Assert.Single(pos);
        // Sand should still be at or near x=32 (only diagonal sliding might shift it slightly)
        Assert.True(Math.Abs(pos[0].x - 32) <= 1,
            $"Resting sand should not drift, but moved to x={pos[0].x}");
    }

    // ===== CONSERVATION =====

    [Fact]
    public void AirDrag_PreservesMaterialConservation()
    {
        // Multiple powder types with horizontal velocity should all be conserved.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "5 each of sand, ash, and coal launched horizontally at varying speeds should all be conserved through air drag operations.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        // Launch various powders horizontally
        for (int i = 0; i < 5; i++)
        {
            sim.SetWithVelocity(20 + i * 2, 40, Materials.Sand, (sbyte)(3 + i), 0);
            sim.SetWithVelocity(20 + i * 2, 50, Materials.Ash, (sbyte)(3 + i), 0);
            sim.SetWithVelocity(20 + i * 2, 60, Materials.Coal, (sbyte)(3 + i), 0);
        }

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Sand));
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Ash));
        Assert.Equal(5, WorldAssert.CountMaterial(sim.World, Materials.Coal));
    }

    // ===== TRAJECTORY SHAPE =====

    [Fact]
    public void Sand_HighVelocityScatter_FormsArc()
    {
        // Sand dropped from height onto a surface should scatter and form
        // a bounded pile — not fly to the edges of the world.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "10 sand grains dropped from height should scatter on impact but form a bounded pile due to air drag, not spread to the world edges.";
        sim.Fill(0, 100, 128, 28, Materials.Stone);

        // Drop a column of sand from height to generate high-speed impact scatter
        for (int y = 10; y < 20; y++)
            sim.Set(64, y, Materials.Sand);

        var counts = sim.SnapshotMaterialCounts();
        sim.StepWithInvariants(500, counts);

        // All sand should settle. Check that the pile is bounded, not spread to edges.
        var positions = sim.FindMaterial(Materials.Sand);
        Assert.Equal(10, positions.Count);

        int minX = positions.Min(p => p.x);
        int maxX = positions.Max(p => p.x);
        int spread = maxX - minX;

        // With drag, spread should be bounded. Without drag, sand could fly 60+ cells.
        // With drag, expect a reasonable pile width.
        Assert.True(spread < 60,
            $"Sand pile spread should be bounded by drag, got {spread} cells wide " +
            $"(min={minX}, max={maxX})");
    }

    // ===== VERTICAL VELOCITY UNAFFECTED =====

    [Fact]
    public void AirDrag_DoesNotAffectVerticalVelocity()
    {
        // Air drag should only affect horizontal velocity.
        // Two sand grains at the same height — one with vx=0, one with vx=8 —
        // should reach the floor at approximately the same time.
        using var sim = new SimulationFixture(128, 128);
        sim.Description = "Two sand grains at the same height, one stationary and one with high horizontal velocity, should reach the floor at approximately the same time since drag only affects horizontal speed.";
        sim.Fill(0, 120, 128, 8, Materials.Stone);

        sim.SetWithVelocity(30, 60, Materials.Sand, 0, 0);  // No horizontal velocity
        sim.SetWithVelocity(80, 60, Materials.Sand, 8, 0);  // High horizontal velocity

        // Run frame by frame and check when each reaches the floor
        int straightLandFrame = -1;
        int angledLandFrame = -1;

        for (int frame = 0; frame < 200; frame++)
        {
            sim.Step(1);

            if (straightLandFrame < 0)
            {
                var pos30 = sim.FindMaterial(Materials.Sand)
                    .Where(p => p.x <= 40).ToList();
                if (pos30.Count > 0 && pos30[0].y >= 118)
                    straightLandFrame = frame;
            }
            if (angledLandFrame < 0)
            {
                var pos80 = sim.FindMaterial(Materials.Sand)
                    .Where(p => p.x > 40).ToList();
                if (pos80.Count > 0 && pos80[0].y >= 118)
                    angledLandFrame = frame;
            }

            if (straightLandFrame >= 0 && angledLandFrame >= 0)
                break;
        }

        Assert.True(straightLandFrame > 0, "Straight-falling sand should reach floor");
        Assert.True(angledLandFrame > 0, "Angled sand should reach floor");

        // Both should land within a few frames of each other
        // (Bresenham diagonal movement might cause small timing differences)
        int timeDiff = Math.Abs(straightLandFrame - angledLandFrame);
        Assert.True(timeDiff <= 10,
            $"Drag should not significantly affect fall time. " +
            $"Straight: frame {straightLandFrame}, Angled: frame {angledLandFrame}, diff: {timeDiff}");
    }
}
